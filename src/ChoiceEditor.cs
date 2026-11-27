using System;
using System.Collections.Generic;
using BlueEyes.Dialogues;
using HarmonyLib;
using Il2CppInterop.Runtime;
using UnityEngine;
using NB = BlueEyes.NarrativeBrain;

namespace Road96Toolkit
{
    // How answers work in Road 96: every answer in a conversation can carry
    //   a karma impact (a KarmaImpact asset, its Impact value is added to your karma),
    //   a world impact (a WorldGaugeImpact asset: which of the three election gauges it pushes and by how much),
    //   and odds (a NarrativeProbability asset: base value plus bonuses from your karma and energy, clamped,
    //   then the game rolls a dice against it).
    // Those assets are shared between many answers, so edits here swap in a private copy for just that answer.
    // The odds switch hooks the dice roll itself, so it works for every roll in the game, answers or not.
    internal static class ChoiceEditor
    {
        // ---------- names ----------

        public static string GaugeName(NB.GaugeType t)
        {
            switch (t)
            {
                case NB.GaugeType.Moderate: return "Florres";
                case NB.GaugeType.Individualist: return "Tyrak";
                case NB.GaugeType.Revolutionary: return "Revolution";
                default: return t.ToString();
            }
        }

        public static string GaugeLong(NB.GaugeType t) => GaugeName(t) + " (" + t + ")";

        // ---------- odds ----------

        static readonly Dictionary<IntPtr, float> _custom = new Dictionary<IntPtr, float>(); // our per answer odds

        public static void Patch(Harmony h)
        {
            try
            {
                var post = new HarmonyMethod(typeof(ChoiceEditor), nameof(ProbPostfix));
                h.Patch(AccessTools.Method(typeof(NarrativeProbability), nameof(NarrativeProbability.GetProbability),
                    new[] { typeof(NB.NarrativeContext), typeof(NB.NarrativeSettings) }), postfix: post);
                h.Patch(AccessTools.Method(typeof(NarrativeProbability), nameof(NarrativeProbability.GetProbability),
                    new[] { typeof(float), typeof(float), typeof(NB.NarrativeSettings), typeof(NB.NarrativeContext) }), postfix: post);
                h.Patch(AccessTools.Method(typeof(NarrativeProbability), nameof(NarrativeProbability.RollDice)),
                    postfix: new HarmonyMethod(typeof(ChoiceEditor), nameof(RollPostfix)));
            }
            catch (Exception e) { Plugin.Log.LogWarning("Odds hook failed: " + e.Message); }
        }

        // the chance we want instead of the game's one, or null to leave it
        static float? Forced(NarrativeProbability p)
        {
            if (p == null) return null;
            if (_custom.TryGetValue(p.Pointer, out var v)) return v;
            int mode = Plugin.OddsMode.Value;
            if (mode == 0) return null;
            bool good = true;
            try { good = p._isPositiveForThePlayer; } catch { }
            float want = mode == 1 ? 1f : mode == 2 ? 0f : Mathf.Clamp01(Plugin.OddsPercent.Value / 100f);
            // for rolls where "it happens" is bad for you, winning means it does not happen
            return good ? want : 1f - want;
        }

        static void ProbPostfix(NarrativeProbability __instance, ref float __result)
        {
            try { var f = Forced(__instance); if (f != null) __result = f.Value; } catch { }
        }

        static void RollPostfix(NarrativeProbability __instance, ref bool __result)
        {
            try
            {
                var f = Forced(__instance);
                if (f == null) return;
                __result = f.Value >= 0.999f || (f.Value > 0.001f && UnityEngine.Random.value < f.Value);
                Plugin.Log.LogInfo($"Dice roll forced to {(__result ? "yes" : "no")} ({f.Value * 100f:0}%)");
            }
            catch { }
        }

        public static string OddsModeText()
        {
            switch (Plugin.OddsMode.Value)
            {
                case 1: return "always win";
                case 2: return "always fail";
                case 3: return $"{Plugin.OddsPercent.Value:0}% to win";
                default: return "normal";
            }
        }

        public static void DrawOddsSwitch()
        {
            Gui.Header("Dice rolls (every chance in the game)   now: " + OddsModeText());
            var r = Gui.Row(4);
            string[] names = { "Normal", "Always win", "Always fail", "Custom %" };
            for (int i = 0; i < 4; i++)
                if (Gui.Pick(r[i], names[i], Plugin.OddsMode.Value == i, Gui.TabKind.Choice)) { Plugin.OddsMode.Value = i; ChoiceHints.Reread(); }
            if (Plugin.OddsMode.Value == 3)
            {
                var c = Gui.Row(2f, 0.7f, 0.7f, 0.7f, 0.7f);
                GUI.Label(c[0], $"Chance to win: {Plugin.OddsPercent.Value:0}%");
                float v = Plugin.OddsPercent.Value;
                if (GUI.Button(c[1], "-10")) v -= 10;
                if (GUI.Button(c[2], "-1")) v -= 1;
                if (GUI.Button(c[3], "+1")) v += 1;
                if (GUI.Button(c[4], "+10")) v += 10;
                v = Mathf.Clamp(v, 0, 100);
                if (Math.Abs(v - Plugin.OddsPercent.Value) > 0.01f) { Plugin.OddsPercent.Value = v; ChoiceHints.Reread(); }
            }
        }

        // ---------- per answer edits ----------

        static readonly HashSet<IntPtr> _ours = new HashSet<IntPtr>();

        static T Make<T>() where T : ScriptableObject
        {
            var so = ScriptableObject.CreateInstance(Il2CppType.Of<T>()).Cast<T>();
            so.hideFlags = HideFlags.HideAndDontSave;
            so.name = "Road96Toolkit edit";
            _ours.Add(so.Pointer);
            return so;
        }

        static bool Ours(UnityEngine.Object o) => o != null && _ours.Contains(o.Pointer);

        static float KarmaOf(DialogueChoiceNode.Choice c) { try { return c.impact != null ? c.impact.Impact : 0f; } catch { return 0f; } }

        static void SetKarma(DialogueChoiceNode.Choice c, float v)
        {
            KarmaImpactValue k = null;
            if (Ours(c.impact)) k = c.impact.TryCast<KarmaImpactValue>();
            if (k == null) { k = Make<KarmaImpactValue>(); c.impact = k; }
            k._impact = v;
        }

        static WorldGaugeImpact IconsFor(NB.GaugeType t)
        {
            try
            {
                foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<WorldGaugeImpact>()))
                {
                    var w = o.TryCast<WorldGaugeImpact>();
                    if (w != null && !Ours(w) && w._gaugeType == t && w._impactIcon != null) return w;
                }
            }
            catch { }
            return null;
        }

        static WorldGaugeImpact OwnWorld(DialogueChoiceNode.Choice c)
        {
            if (Ours(c.worldImpact)) return c.worldImpact;
            var old = c.worldImpact;
            var w = Make<WorldGaugeImpact>();
            if (old != null) { w._gaugeType = old._gaugeType; w._impact = old._impact; w._impactIcon = old._impactIcon; w._interactionIcon = old._interactionIcon; }
            else { w._gaugeType = NB.GaugeType.Moderate; w._impact = 0f; }
            c.worldImpact = w;
            return w;
        }

        static void SetGauge(DialogueChoiceNode.Choice c, NB.GaugeType t)
        {
            var w = OwnWorld(c);
            w._gaugeType = t;
            var src = IconsFor(t);
            if (src != null) { w._impactIcon = src._impactIcon; w._interactionIcon = src._interactionIcon; }
        }

        static readonly Dictionary<IntPtr, NarrativeProbability> _origOdds = new Dictionary<IntPtr, NarrativeProbability>();

        static void SetOdds(DialogueChoiceNode.Choice c, float? chance)
        {
            if (chance == null)
            {
                if (c.probability != null) _custom.Remove(c.probability.Pointer);
                if (_origOdds.TryGetValue(c.Pointer, out var orig)) { c.probability = orig; _origOdds.Remove(c.Pointer); }
                return;
            }
            // keep the game's own odds asset if it has one (it may be shared, so the value is stored per asset here)
            var p = c.probability;
            if (p == null || !Ours(p))
            {
                if (!_origOdds.ContainsKey(c.Pointer)) _origOdds[c.Pointer] = p;
                var mine = Make<NarrativeProbability>();
                if (p != null) { mine._baseValue = p._baseValue; mine._clamp = p._clamp; mine._isPositiveForThePlayer = p._isPositiveForThePlayer; }
                else { mine._isPositiveForThePlayer = true; mine._clamp = new Vector2(0f, 1f); }
                c.probability = mine;
                p = mine;
            }
            bool good = true;
            try { good = p._isPositiveForThePlayer; } catch { }
            _custom[p.Pointer] = good ? chance.Value : 1f - chance.Value;
        }

        static float Step(float v) => Math.Abs(v) >= 2f ? 1f : 0.1f;

        public static void DrawAnswers()
        {
            DrawOddsSwitch();
            Gui.Space(4);

            var node = ChoiceHints.Current;
            List<DialogueChoiceNode.Choice> choices = null;
            try
            {
                if (node?.availableChoices != null)
                {
                    choices = new List<DialogueChoiceNode.Choice>();
                    for (int i = 0; i < node.availableChoices.Count; i++) choices.Add(node.availableChoices[i]);
                }
            }
            catch { choices = null; }

            if (choices == null || choices.Count == 0)
            {
                Gui.Header("Answers");
                Gui.Label("Open this tab while the answers of a conversation are on screen.");
                Gui.Label("You can then change what each answer does before you pick it:");
                Gui.Label("its karma, which candidate it helps and by how much, and its odds.");
                Gui.Label("Candidates: Florres is the Moderate gauge, Tyrak the Individualist one,");
                Gui.Label("and the Revolutionary gauge is the revolution (no candidate).");
                return;
            }

            var ctx = Game.Context;
            var settings = Game.Settings;
            Gui.Header("Answers on screen (changes last until the game is closed)");
            for (int i = 0; i < choices.Count; i++)
            {
                var c = choices[i];
                if (c == null || !ChoiceHints.IsVisible(i)) continue;
                if (!Gui.HasRoom(Gui.LineH * 4)) { Gui.Label("(more answers below, not enough room)"); break; }
                Gui.Label($"{i + 1}. {ChoiceHints.TextOf(i)}");

                float k = KarmaOf(c);
                var r = Gui.Row(2.2f, 0.7f, 0.7f, 0.7f, 0.7f, 0.9f);
                GUI.Label(r[0], $"   Karma {(k > 0 ? "+" : "")}{k:0.##}");
                if (GUI.Button(r[1], "--")) Edit(() => SetKarma(c, k - 0.5f));
                if (GUI.Button(r[2], "-")) Edit(() => SetKarma(c, k - 0.1f));
                if (GUI.Button(r[3], "+")) Edit(() => SetKarma(c, k + 0.1f));
                if (GUI.Button(r[4], "++")) Edit(() => SetKarma(c, k + 0.5f));
                if (GUI.Button(r[5], "None")) Edit(() => SetKarma(c, 0f));

                WorldGaugeImpact w = null;
                try { w = c.worldImpact; } catch { }
                float amount = 0f; NB.GaugeType type = NB.GaugeType.Moderate;
                try { if (w != null) { amount = w._impact; type = w._gaugeType; } } catch { }
                r = Gui.Row(1.3f, 0.9f, 0.6f, 0.6f, 0.6f, 0.6f, 0.9f);
                GUI.Label(r[0], "   Helps");
                if (GUI.Button(r[1], w == null ? "nobody" : GaugeName(type)))
                    Edit(() => SetGauge(c, w == null ? NB.GaugeType.Moderate : (NB.GaugeType)(((int)type + 1) % 3)));
                GUI.Label(r[2], $" {(amount > 0 ? "+" : "")}{amount:0.##}");
                float st = Step(amount);
                if (GUI.Button(r[3], "-")) Edit(() => OwnWorld(c)._impact = amount - st);
                if (GUI.Button(r[4], "+")) Edit(() => OwnWorld(c)._impact = amount + st);
                if (GUI.Button(r[5], "x2")) Edit(() => OwnWorld(c)._impact = amount == 0 ? 1f : amount * 2f);
                if (GUI.Button(r[6], "None")) Edit(() => { if (c.worldImpact != null) OwnWorld(c)._impact = 0f; });

                string odds = "no dice roll";
                try
                {
                    if (c.probability != null && ctx != null && settings != null)
                    {
                        float p = c.probability.GetProbability(ctx, settings);
                        bool good = c.probability._isPositiveForThePlayer;
                        odds = $"{(good ? p : 1f - p) * 100f:0}% to win";
                    }
                }
                catch { }
                r = Gui.Row(2.2f, 0.8f, 0.8f, 0.8f, 0.9f);
                GUI.Label(r[0], "   Odds: " + odds);
                if (GUI.Button(r[1], "Win")) Edit(() => SetOdds(c, 1f));
                if (GUI.Button(r[2], "50%")) Edit(() => SetOdds(c, 0.5f));
                if (GUI.Button(r[3], "Fail")) Edit(() => SetOdds(c, 0f));
                if (GUI.Button(r[4], "Game")) Edit(() => SetOdds(c, null));
                Gui.Space(3);
            }
        }

        static void Edit(Action a)
        {
            if (Game.Try("Edit answer", a)) ChoiceHints.Reread();
        }
    }
}

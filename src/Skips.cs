using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using NB = BlueEyes.NarrativeBrain;

namespace Road96Toolkit
{
    // Skip tab: leave the scenario you are in, always offer the bus or the taxi, and a hate list.
    // A hated scenario is never picked: when the game's picker lands on one, a random allowed scenario
    // of the same travel type (on foot, in a car...) is picked instead. If one starts anyway it is skipped.
    internal static class Skips
    {
        static ConfigEntry<bool> _alwaysBus, _alwaysTaxi, _autoSkip;
        static readonly HashSet<string> _hated = new HashSet<string>(StringComparer.Ordinal);
        static string FilePath => Path.Combine(BepInEx.Paths.ConfigPath, "Road96Toolkit", "hatelist.txt");
        static readonly System.Random _rng = new System.Random();

        public static void Init(ConfigFile cfg)
        {
            _alwaysBus = cfg.Bind("Skip", "AlwaysAllowBus", false, "Every scenario lets you leave by bus");
            _alwaysTaxi = cfg.Bind("Skip", "AlwaysAllowTaxi", false, "Every scenario lets you leave by taxi");
            _autoSkip = cfg.Bind("Skip", "SkipHatedScenarios", true, "Never play the scenarios on your hate list, a random one is picked instead");
            try
            {
                if (File.Exists(FilePath))
                    foreach (var l in File.ReadAllLines(FilePath)) if (l.Trim().Length > 0) _hated.Add(l.Trim());
            }
            catch { }
        }

        static void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllLines(FilePath, _hated.OrderBy(x => x));
            }
            catch (Exception e) { Plugin.Log.LogWarning("Hate list save failed: " + e.Message); }
        }

        public static bool IsHated(string id) => id != null && _hated.Contains(id);

        // ---------- hooks ----------

        public static void Patch(Harmony h)
        {
            try
            {
                h.Patch(AccessTools.Method(typeof(NB.NarrativeBrain), nameof(NB.NarrativeBrain.PickNextElement)),
                    postfix: new HarmonyMethod(typeof(Skips), nameof(PickPostfix)));
            }
            catch (Exception e) { Plugin.Log.LogWarning("Hate list hook failed: " + e.Message); }
            try
            {
                var post = new HarmonyMethod(typeof(Skips), nameof(ExitPostfix));
                h.Patch(AccessTools.Method(typeof(NB.NarrativeController), nameof(NB.NarrativeController.ShouldExitBeAvailable)), postfix: post);
                h.Patch(AccessTools.Method(typeof(NB.NarrativeContext), nameof(NB.NarrativeContext.ShouldExitBeAvailable)),
                    postfix: new HarmonyMethod(typeof(Skips), nameof(ExitPostfix3)));
            }
            catch (Exception e) { Plugin.Log.LogWarning("Bus hook failed: " + e.Message); }
            try
            {
                // bus stops and taxi signs in the level switch on and off by themselves too
                h.Patch(AccessTools.Method(typeof(BlueEyes.State.ExitAvailableDependantObject), nameof(BlueEyes.State.ExitAvailableDependantObject.CheckState)),
                    postfix: new HarmonyMethod(typeof(Skips), nameof(StopPostfix)));
            }
            catch (Exception e) { Plugin.Log.LogWarning("Bus stop hook failed: " + e.Message); }
        }

        static void StopPostfix(BlueEyes.State.ExitAvailableDependantObject __instance, ref bool __result)
        {
            try { if (Forced(__instance._exit)) __result = !__instance._invert; } catch { }
        }

        static bool Forced(NB.PlayerState exit) =>
            (exit == NB.PlayerState.Bus && _alwaysBus.Value) || (exit == NB.PlayerState.Taxi && _alwaysTaxi.Value);

        static void ExitPostfix(NB.PlayerState exit, ref bool __result) { if (!__result && Forced(exit)) __result = true; }
        static void ExitPostfix3(NB.PlayerState exitState, ref bool __result) { if (!__result && Forced(exitState)) __result = true; }

        static void PickPostfix(NB.NarrativeBrain __instance, NB.NarrativeContext context, ref NB.NarrativeElement __result)
        {
            try
            {
                if (!_autoSkip.Value || __result == null || !IsHated(__result.ID)) return;
                // something you queued yourself always goes through
                if (context != null && context._overrideNextBubble == __result.ID) return;
                var other = Other(__instance, __result);
                if (other == null) { Plugin.Log.LogInfo($"Hate list: {__result.ID} picked, nothing else could go there"); return; }
                Plugin.Log.LogInfo($"Hate list: {__result.ID} swapped for {other.ID}");
                ToolkitBehaviour.Toast($"Skipped {__result.ID}, playing {other.ID} instead");
                __result = other;
            }
            catch (Exception e) { Plugin.Log.LogWarning("Hate list pick: " + e.Message); }
        }

        // a random scenario the game also allows right now, of the same travel type when there is one
        static NB.NarrativeElement Other(NB.NarrativeBrain brain, NB.NarrativeElement not)
        {
            var all = brain?.Elements;
            if (all == null) return null;
            var ok = new List<NB.NarrativeElement>();
            NB.PlayerState type = NB.PlayerState.Any;
            try { type = not.RequiredPlayerState; } catch { }
            for (int i = 0; i < all.Length; i++)
            {
                var s = all[i];
                var el = s?._element;
                if (el == null || s._disabled || el.IsBorder || IsHated(el.ID) || el.Pointer == not.Pointer) continue;
                ok.Add(el);
            }
            var same = ok.Where(e => { try { return e.RequiredPlayerState == type; } catch { return false; } }).ToList();
            var pool = same.Count > 0 ? same : ok;
            return pool.Count == 0 ? null : pool[_rng.Next(pool.Count)];
        }

        // ---------- skipping ----------

        static string CurrentId()
        {
            try
            {
                var b = Game.Context?.CurrentRun?._bubbles;
                return b != null && b.Count > 0 ? b[b.Count - 1]?._bubbleId : null;
            }
            catch { return null; }
        }

        public static void SkipNow()
        {
            var ctrl = Game.Controller;
            if (ctrl == null) { ToolkitBehaviour.Toast("Start a trip first"); return; }
            string cur = CurrentId();
            Game.Try("Skip scenario", () =>
            {
                var ctx = ctrl.CurrentContext;
                // drop a queued pick of the scenario we are leaving
                if (ctx != null && ctx._overrideNextBubble == cur) ctx._overrideNextBubble = "";
                var next = ctrl.PickNextBubble();
                if (next != null && next.ID == cur)
                {
                    var other = Other(Game.Brain, next);
                    if (other != null) next = other;
                }
                if (next != null) ctrl._nextBubble = next;
                Plugin.Log.LogInfo($"Skip: {cur} -> {next?.ID}");
                ToolkitBehaviour.Toast("Skipping to " + (next?.ID ?? "the next scenario"));
                ctrl.GoToNextBubble(false, true);
            });
        }

        static string _lastSeen;
        static float _skipAt;

        // safety net: a hated scenario that starts anyway (picked before you hated it) is skipped once it has loaded
        public static void Update()
        {
            if (_autoSkip == null || !_autoSkip.Value || _hated.Count == 0) return;
            string cur = CurrentId();
            if (cur != _lastSeen)
            {
                _lastSeen = cur;
                _skipAt = IsHated(cur) && Game.Context?._overrideNextBubble != cur ? Time.unscaledTime + 4f : 0f;
            }
            if (_skipAt > 0f && Time.unscaledTime > _skipAt && Game.Player != null && GameStates.Has("GameplayState"))
            {
                _skipAt = 0f;
                ToolkitBehaviour.Toast(cur + " is on your hate list, skipping it");
                SkipNow();
            }
        }

        // ---------- the tab ----------

        static string _search = "", _lastSearch = null;
        static List<NB.NarrativeElement> _all, _shown = new List<NB.NarrativeElement>();
        static int _page, _filter, _lastFilter = -1;
        static bool _dirty;

        static void Read()
        {
            _all = new List<NB.NarrativeElement>();
            Game.Try("Scenario list", () =>
            {
                var arr = Game.Brain?._narrativeInput?.GetAllPossibleInputs();
                if (arr == null) return;
                for (int i = 0; i < arr.Length; i++) if (arr[i] != null && !arr[i].IsBorder) _all.Add(arr[i]);
            });
            _all.Sort((a, b) => string.CompareOrdinal(a.ID, b.ID));
        }

        static string Label(NB.NarrativeElement e)
        {
            string title = null;
            try { title = ItemsTab.Loc(e.Title); } catch { }
            return (IsHated(e.ID) ? "[hated] " : "") + e.ID + (string.IsNullOrEmpty(title) || title == e.Title ? "" : "   " + title);
        }

        public static void Draw()
        {
            Gui.Header("Leave this scenario");
            string cur = CurrentId();
            Gui.Label("You are in: " + (cur ?? "no scenario"));
            var r = Gui.Row(1f, 1f);
            if (GUI.Button(r[0], "Skip it now")) SkipNow();
            if (cur != null && GUI.Button(r[1], IsHated(cur) ? "Take it off the hate list" : "Hate it and skip"))
            {
                if (IsHated(cur)) { _hated.Remove(cur); Save(); }
                else { _hated.Add(cur); Save(); SkipNow(); }
                _dirty = true;
            }
            Gui.Label("Skipping drops you into the next scenario like the end of the level would.");

            Gui.Space(4);
            Gui.Header("Ways out");
            if (Gui.Toggle("Always allow taking the bus", _alwaysBus.Value)) _alwaysBus.Value = !_alwaysBus.Value;
            if (Gui.Toggle("Always allow calling a taxi", _alwaysTaxi.Value)) _alwaysTaxi.Value = !_alwaysTaxi.Value;
            Gui.Label("Counts from the next scenario on. The bus and taxi still cost money.");

            Gui.Space(4);
            Gui.Header($"Hate list ({_hated.Count})");
            if (Gui.Toggle("Never play hated scenarios, pick a random one instead", _autoSkip.Value)) _autoSkip.Value = !_autoSkip.Value;
            if (_all == null || _all.Count == 0) Read();
            _filter = Gui.Tabs(new[] { "All scenarios", "Hated" }, _filter, Gui.TabKind.Choice);
            _search = Gui.Search(_search);
            if (_dirty || _search != _lastSearch || _filter != _lastFilter)
            {
                if (_filter != _lastFilter || _search != _lastSearch) _page = 0;
                _dirty = false; _lastSearch = _search; _lastFilter = _filter;
                _shown = _all.Where(e => (_filter == 0 || IsHated(e.ID)) && Gui.Contains(Label(e), _search)).ToList();
            }
            Gui.Label("Click a scenario to hate it, click again to forgive it.");
            int rows = Math.Max(3, (int)((Gui.Bottom - Gui.Y) / (Gui.LineH + 4)) - 2);
            int hit = Gui.List(_shown, ref _page, Label, rows);
            if (hit >= 0)
            {
                var id = _shown[hit].ID;
                if (!_hated.Remove(id)) _hated.Add(id);
                Save();
                _dirty = true;
                ToolkitBehaviour.Toast(IsHated(id) ? id + " is on your hate list" : id + " forgiven");
            }
        }
    }
}

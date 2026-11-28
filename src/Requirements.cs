using System;
using System.Collections.Generic;
using System.Linq;
using BlueEyes.Dialogues;
using BlueEyes.Interactions;
using BlueEyes.Inventory;
using BlueEyes.Phone;
using Il2CppInterop.Runtime;
using NodeCanvas.DialogueTrees;
using NodeCanvas.Framework;
using UnityEngine;
using NB = BlueEyes.NarrativeBrain;

namespace Road96Toolkit
{
    // Phone calls, answers and branches in a conversation only open up when the teen has the right
    // facts, items or abilities. This reads those checks out of the dialogue and can give the
    // current teen whatever is missing, so a tip or answer becomes available.
    internal static class Requirements
    {
        internal class Need
        {
            public string Text;
            public Func<bool?> Met;   // null when we can't tell
            public Action Give;       // null when we can't give it
        }

        internal class Group
        {
            public string Title;
            public bool AnyOf;
            public int Choice = -1;
            public DialogueChoiceNode Node;
            public readonly List<Need> Needs = new List<Need>();

            public bool? IsMet()
            {
                var res = Needs.Select(n => { try { return n.Met(); } catch { return (bool?)null; } }).ToList();
                if (res.Count == 0) return true;
                if (AnyOf) return res.Any(r => r == true) ? true : res.All(r => r == false) ? false : (bool?)null;
                return res.Any(r => r == false) ? false : res.All(r => r == true) ? true : (bool?)null;
            }

            public bool CanGive => Needs.Any(n => n.Give != null);
        }

        // ---------- reading the checks ----------

        static string Line(Node n)
        {
            try
            {
                var l = n?.TryCast<DialogLineNode>();
                var st = l?.statement;
                if (st == null) return null;
                string key = !string.IsNullOrEmpty(st._i2) ? st._i2 : st._text;
                var s = ItemsTab.Loc(key);
                return string.IsNullOrEmpty(s) ? null : s.Replace("\n", " ");
            }
            catch { return null; }
        }

        static string After(Node n, int i)
        {
            try
            {
                var outs = n.outConnections;
                if (outs == null || i >= outs.Count) return null;
                var next = outs[i]?.targetNode;
                // skip a few nodes that say nothing to reach the first spoken line
                for (int hop = 0; hop < 4 && next != null; hop++)
                {
                    var s = Line(next);
                    if (s != null) return s;
                    var o = next.outConnections;
                    next = o != null && o.Count > 0 ? o[0]?.targetNode : null;
                }
            }
            catch { }
            return null;
        }

        static string Short(string s, int n) => s == null ? "" : s.Length <= n ? s : s.Substring(0, n - 3) + "...";

        public static List<Group> Read(Graph g)
        {
            var groups = new List<Group>();
            if (g == null) return groups;
            Game.Try("Read requirements", () =>
            {
                var nodes = g.allNodes;
                if (nodes == null) return;
                for (int k = 0; k < nodes.Count; k++)
                {
                    var n = nodes[k];
                    if (n == null) continue;

                    var choice = n.TryCast<DialogueChoiceNode>();
                    if (choice != null)
                    {
                        var list = choice.availableChoices;
                        if (list == null) continue;
                        for (int i = 0; i < list.Count; i++)
                        {
                            var c = list[i];
                            if (c?.condition == null) continue;
                            string text = null;
                            try { var st = c.statement; text = ItemsTab.Loc(st == null ? null : (!string.IsNullOrEmpty(st.i2) ? st.i2 : st.text)); } catch { }
                            var gr = new Group { Title = "Answer: " + Short(text ?? ("answer " + (i + 1)), 70), Choice = i, Node = choice };
                            Add(gr, c.condition, false);
                            if (gr.Needs.Count > 0) groups.Add(gr);
                        }
                        continue;
                    }

                    var cn = n.TryCast<NodeCanvas.DialogueTrees.ConditionNode>();
                    if (cn != null)
                    {
                        var gr = new Group { Title = "Line: " + Short(After(cn, 0) ?? "(a branch with no line)", 70) };
                        try { Add(gr, cn.condition, false); } catch { }
                        if (gr.Needs.Count > 0) groups.Add(gr);
                        continue;
                    }

                    var mc = n.TryCast<MultipleConditionNode>();
                    if (mc?.conditions != null)
                    {
                        for (int i = 0; i < mc.conditions.Count; i++)
                        {
                            var gr = new Group { Title = "Line: " + Short(After(mc, i) ?? $"(branch {i + 1})", 70) };
                            Add(gr, mc.conditions[i], false);
                            if (gr.Needs.Count > 0) groups.Add(gr);
                        }
                    }
                }
            });
            return groups;
        }

        static void Add(Group gr, ConditionTask c, bool neg)
        {
            if (c == null) return;
            bool inv = neg;
            try { inv ^= c._invert; } catch { }

            var list = c.TryCast<ConditionList>();
            if (list != null)
            {
                bool all = true;
                try { all = list.allTrueRequired; } catch { }
                // "not all of them" means "any of them is false", and the other way round
                if (all == inv) gr.AnyOf = true;
                if (list.conditions != null) for (int i = 0; i < list.conditions.Count; i++) Add(gr, list.conditions[i], inv);
                return;
            }

            var tag = c.TryCast<NB.NodeCanvasCheckTag>();
            if (tag != null) { AddTag(gr, tag._tag, !inv); return; }
            var tw = c.TryCast<NB.NodeCanvasCheckTagWrapper>();
            if (tw != null) { AddTag(gr, tw._tag?._tag, !inv); return; }

            var item = c.TryCast<NodeCanvasCheckItem>();
            if (item != null) { AddItem(gr, item._value, !inv); return; }

            var ab = c.TryCast<NodeCanvasCheckAbility>();
            if (ab != null) { AddAbility(gr, ab._value, !inv); return; }

            var ws = c.TryCast<NB.NodeCanvasCheckWorldState>();
            if (ws != null) { AddWorld(gr, ws._state, inv); return; }

            var gen = c.TryCast<NB.NodeCanvasCheckGender>();
            if (gen != null)
            {
                string want = gen._gender.ToString();
                gr.Needs.Add(new Need
                {
                    Text = (inv ? "Teen is not " : "Teen is ") + want.ToLowerInvariant(),
                    Met = () => { var r = Game.Context?.CurrentRun; return r == null ? (bool?)null : (r._playerGender.ToString() == want) != inv; },
                });
                return;
            }

            string name = "a check";
            try { name = c.GetIl2CppType().Name.Replace("NodeCanvas", "").Replace("Check", "check "); } catch { }
            gr.Needs.Add(new Need { Text = (inv ? "Not " : "") + name.Trim(), Met = () => null });
        }

        static void AddTag(Group gr, string tag, bool want)
        {
            if (string.IsNullOrEmpty(tag)) return;
            string about = FactsEditor.TextOf(tag);
            gr.Needs.Add(new Need
            {
                Text = (want ? "Fact " : "No fact ") + tag + (about != null ? "  (" + Short(about, 50) + ")" : ""),
                Met = () => FactsEditor.Has(tag) == want,
                Give = () => FactsEditor.Set(tag, want),
            });
        }

        static void AddItem(Group gr, InventoryItem it, bool want)
        {
            if (it == null) return;
            gr.Needs.Add(new Need
            {
                Text = (want ? "Item " : "Without item ") + ItemsTab.NameOf(it),
                Met = () => { var ctx = Game.Context; return ctx == null ? (bool?)null : ctx.HasItem(it) == want; },
                Give = () =>
                {
                    var ctx = Game.Context;
                    if (ctx == null) return;
                    if (want && !ctx.HasItem(it)) ctx.AddItem(it);
                    if (!want && ctx.HasItem(it)) ctx.RemoveItem(it);
                },
            });
        }

        static void AddAbility(Group gr, BlueEyes.Abilities.Ability a, bool want)
        {
            if (a == null) return;
            string id = null;
            try { id = a.ID; } catch { }
            if (string.IsNullOrEmpty(id)) return;
            gr.Needs.Add(new Need
            {
                Text = (want ? "Ability " : "Without ability ") + a.name,
                Met = () => { var ctx = Game.Context; return ctx == null ? (bool?)null : ctx.HasAbility(id) == want; },
                Give = () =>
                {
                    var ctx = Game.Context;
                    if (ctx == null) return;
                    if (want && !ctx.HasAbility(id)) ctx.AddAbility(id);
                    if (!want && ctx.HasAbility(id)) ctx.RemoveAbility(id);
                },
            });
        }

        // ---------- world states ----------
        // Most phone calls check a world state asset: who you met, which scenario you saw, the act of the
        // story, the trip number, an election gauge, or a fact. Facts inside it are given for real. Anything
        // else can't be given, so the check in the conversation is made to pass instead, until the game closes.

        static readonly Dictionary<IntPtr, bool> _forced = new Dictionary<IntPtr, bool>();

        public static void Patch(HarmonyLib.Harmony h)
        {
            try
            {
                h.Patch(HarmonyLib.AccessTools.Method(typeof(NB.NodeCanvasCheckWorldState), "OnCheck"),
                    postfix: new HarmonyLib.HarmonyMethod(typeof(Requirements), nameof(WorldPostfix)));
            }
            catch (Exception e) { Plugin.Log.LogWarning("World state hook failed: " + e.Message); }
        }

        static void WorldPostfix(NB.NodeCanvasCheckWorldState __instance, ref bool __result)
        {
            try
            {
                var s = __instance._state;
                if (s != null && _forced.TryGetValue(s.Pointer, out var want)) __result = want;
            }
            catch { }
        }

        static string TagOf(TagWSC t)
        {
            string tag = null;
            try { tag = t.tagWrapper != null ? t.tagWrapper._tag : null; } catch { }
            return string.IsNullOrEmpty(tag) ? t._tag : tag;
        }

        static string Describe(WorldStateCondition w, int depth = 0)
        {
            if (w == null) return "nothing";
            try
            {
                var t = w.TryCast<TagWSC>();
                if (t != null) return (t._isPresent ? "fact " : "no fact ") + TagOf(t);
                var seen = w.TryCast<CharacterSeenWSC>();
                if (seen != null) return "met " + seen._npc + (seen._distance == CharacterSeenWSC.Distance.RUN ? " this trip" : seen._distance == CharacterSeenWSC.Distance.PREVIOUS_BUBBLE ? " just before" : " in any trip");
                var bub = w.TryCast<LastBubbleSeenWSC>();
                if (bub != null) return "played " + bub._bubbleID + " (" + bub._bubbleSawAt.ToString().ToLowerInvariant().Replace('_', ' ') + ")";
                var cur = w.TryCast<CurrentBubbleWSC>();
                if (cur != null) return "in scenario " + cur._bubbleID;
                var act = w.TryCast<StoryActWSC>();
                if (act != null) return "story act " + act._overOrEqual + " to " + act._underOrEqual;
                var run = w.TryCast<RunIDWSC>();
                if (run != null) return "trip " + run._currentRunRequired;
                var g = w.TryCast<WorldGaugeWSC>();
                if (g != null) return ChoiceEditor.GaugeName(g._gauge) + (g._comparator == WorldGaugeWSC.Comparator.OVER_OR_EQUAL ? " at least " : " at most ") + g._value.ToString("0.##");
                var gh = w.TryCast<WorldGaugeHigherWSC>();
                if (gh != null) return ChoiceEditor.GaugeName(gh._higherTarget) + (gh._comparator == WorldGaugeHigherWSC.Comparator.HIGHER ? " leads" : " is behind");
                var dist = w.TryCast<DistanceRemainingWSC>();
                if (dist != null) return (dist._under ? "under " : "over ") + dist._distanceMax + " km to the border";
                var st = w.TryCast<WorldState>();
                if (st != null && depth < 2)
                {
                    var reqs = st._anyRequirements;
                    if (reqs == null || reqs.Length == 0) return "always";
                    var ways = new List<string>();
                    for (int i = 0; i < reqs.Length && i < 3; i++)
                    {
                        var parts = new List<string>();
                        var r = reqs[i];
                        if (r._requiredConditions != null) foreach (var c in r._requiredConditions) parts.Add(Describe(c, depth + 1));
                        if (r._unwantedConditions != null) foreach (var c in r._unwantedConditions) parts.Add("not " + Describe(c, depth + 1));
                        ways.Add(parts.Count == 0 ? "always" : string.Join(" and ", parts));
                    }
                    if (reqs.Length > 3) ways.Add($"{reqs.Length - 3} more ways");
                    return ways.Count == 1 ? ways[0] : "(" + string.Join(") or (", ways) + ")";
                }
            }
            catch { }
            return w.name;
        }

        // gives the facts a world state asks for, using the first way it can be met
        static void GiveFacts(WorldStateCondition w, bool want)
        {
            var t = w.TryCast<TagWSC>();
            if (t != null) { var tag = TagOf(t); if (!string.IsNullOrEmpty(tag)) FactsEditor.Set(tag, t._isPresent == want); return; }
            var st = w.TryCast<WorldState>();
            if (st == null || !want) return;
            var reqs = st._anyRequirements;
            if (reqs == null || reqs.Length == 0) return;
            var r = reqs[0];
            if (r._requiredConditions != null) foreach (var c in r._requiredConditions) if (c != null) GiveFacts(c, true);
            if (r._unwantedConditions != null) foreach (var c in r._unwantedConditions) if (c != null) GiveFacts(c, false);
        }

        static void AddWorld(Group gr, WorldStateCondition w, bool inv)
        {
            if (w == null) return;
            var t = w.TryCast<TagWSC>();
            if (t != null) { AddTag(gr, TagOf(t), t._isPresent != inv); return; }

            bool want = !inv;
            var ptr = w.Pointer;
            gr.Needs.Add(new Need
            {
                Text = (inv ? "Not: " : "") + Short(Describe(w), 110),
                Met = () =>
                {
                    if (_forced.TryGetValue(ptr, out var f)) return f == want;
                    var ctx = Game.Context;
                    return ctx == null ? (bool?)null : w.IsConditionValid(ctx) == want;
                },
                Give = () =>
                {
                    var ctx = Game.Context;
                    Game.Try("World state facts", () => GiveFacts(w, want));
                    bool ok = false;
                    try { ok = ctx != null && w.IsConditionValid(ctx) == want; } catch { }
                    if (ok) _forced.Remove(ptr);
                    else _forced[ptr] = want;
                    Plugin.Log.LogInfo($"Unlock: {Describe(w)} -> {(ok ? "facts given" : "check forced to " + want)}");
                },
            });
        }

        public static int ForcedCount => _forced.Count;
        public static void ClearForced() { _forced.Clear(); ChoiceHints.Reread(); }

        // gives what is missing; for "any of" only the first thing we can give
        public static int Give(Group g)
        {
            int n = 0;
            foreach (var need in g.Needs)
            {
                bool? met = null;
                try { met = need.Met(); } catch { }
                if (met == true || need.Give == null) { if (g.AnyOf && met == true) return n; continue; }
                if (Game.Try("Give requirement", need.Give)) { n++; Plugin.Log.LogInfo("Unlock gave: " + need.Text); }
                if (g.AnyOf) break;
            }
            ChoiceHints.Reread();
            return n;
        }

        // ---------- phone numbers ----------

        class Number
        {
            public int Digits;
            public string Who;
            public PhoneNumber Asset;
            public List<Group> Groups;
        }

        static readonly List<Number> _numbers = new List<Number>();
        static int _numPage, _groupPage;
        static Number _sel;
        static float _nextScan;
        static int _view; // 0 this conversation, 1 phone numbers

        static void ScanNumbers()
        {
            _numbers.Clear();
            var wrong = new HashSet<IntPtr>();
            Game.Try("Phone numbers", () =>
            {
                foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<PhoneDirectory>()))
                {
                    var d = o.TryCast<PhoneDirectory>();
                    if (d?._wrongNumber != null) wrong.Add(d._wrongNumber.Pointer);
                }
                var seen = new HashSet<int>();
                foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<PhoneNumber>()))
                {
                    var p = o.TryCast<PhoneNumber>();
                    if (p == null || wrong.Contains(p.Pointer) || !seen.Add(p._numbers)) continue;
                    string who = null;
                    try
                    {
                        var actors = p._actorNames;
                        if (actors != null && actors.Length > 0) who = ItemsTab.Loc(actors[0]._displayedActorName) ?? actors[0]._dialogActorName;
                    }
                    catch { }
                    if (string.IsNullOrEmpty(who)) who = p.name;
                    _numbers.Add(new Number { Digits = p._numbers, Who = who, Asset = p });
                }
            });
            _numbers.Sort((a, b) => a.Digits.CompareTo(b.Digits));
        }

        // ---------- the page ----------

        static string Mark(bool? met) => met == true ? "[ok]" : met == false ? "[missing]" : "[?]";

        public static void Draw()
        {
            _view = Gui.Tabs(new[] { "This conversation", "Phone numbers", "Puzzles" }, _view, Gui.TabKind.Choice);
            if (_view == 2) { Puzzles.Draw(); return; }
            if (_forced.Count > 0)
            {
                var r = Gui.Row(2f, 1f);
                GUI.Label(r[0], $"{_forced.Count} checks forced open until the game closes");
                if (GUI.Button(r[1], "Stop forcing")) { ClearForced(); ToolkitBehaviour.Toast("Checks back to normal"); }
            }
            if (_view == 0) DrawCurrent(); else DrawPhones();
        }

        static IntPtr _curTree;
        static List<Group> _curGroups = new List<Group>();

        static void DrawCurrent()
        {
            DialogueTree dt = null;
            try { dt = DialogueTree.currentDialogue; } catch { }
            if (dt == null)
            {
                Gui.Label("No conversation is running right now.");
                Gui.Label("Start talking to someone (or call a number) and open this again,");
                Gui.Label("or pick a number under Phone numbers to get ready before you call.");
                _curTree = IntPtr.Zero;
                return;
            }
            if (dt.Pointer != _curTree) { _curTree = dt.Pointer; _curGroups = Read(dt); _groupPage = 0; }
            if (Gui.Button("Read the conversation again")) _curGroups = Read(dt);
            DrawGroups(_curGroups, ChoiceHints.Current);
        }

        static void DrawPhones()
        {
            if (Time.unscaledTime > _nextScan || _numbers.Count == 0) { _nextScan = Time.unscaledTime + 10f; ScanNumbers(); }
            if (_numbers.Count == 0)
            {
                Gui.Label("No phone numbers are loaded. They load with a level that has a payphone.");
                return;
            }
            Gui.Label("Pick a number to see what the call checks, then give the teen what is missing.");
            int hit = Gui.List(_numbers, ref _numPage, n =>
            {
                string state = "";
                if (n.Groups != null)
                {
                    int open = n.Groups.Count(g => g.IsMet() == true);
                    state = $"   {open}/{n.Groups.Count} open";
                }
                return $"{n.Digits}   {Short(n.Who, 34)}{state}";
            }, 6, n => n == _sel);
            if (hit >= 0)
            {
                _sel = _numbers[hit];
                _sel.Groups = Read(_sel.Asset._dialogue);
                _groupPage = 0;
            }
            if (_sel == null) return;
            Gui.Header($"Call {_sel.Digits} ({_sel.Who})");
            if (_sel.Asset._dialogue == null) { Gui.Label("This number has no conversation."); return; }
            if (_sel.Groups.Count == 0) { Gui.Label("This call checks nothing, everyone can make it."); return; }
            var r = Gui.Row(1f, 1f);
            if (GUI.Button(r[0], "Give everything this call needs"))
            {
                int n = 0;
                foreach (var g in _sel.Groups) if (g.IsMet() != true) n += Give(g);
                ToolkitBehaviour.Toast(n > 0 ? $"Gave the teen {n} things" : "Nothing left to give");
            }
            GUI.Label(r[1], "  (some checks may rule each other out)");
            DrawGroups(_sel.Groups, null);
        }

        static void DrawGroups(List<Group> groups, DialogueChoiceNode onScreen)
        {
            if (groups.Count == 0) { Gui.Label("Nothing in this conversation checks the teen."); return; }

            // one group needs about two lines plus one per check
            int start = Math.Min(_groupPage, Math.Max(0, groups.Count - 1));
            int shown = 0;
            for (int i = start; i < groups.Count; i++)
            {
                var g = groups[i];
                float need = Gui.LineH * (2 + g.Needs.Count) + 12;
                if (!Gui.HasRoom(need + Gui.LineH + 6)) break;
                shown++;
                bool? met = g.IsMet();
                Gui.Label($"{Mark(met)} {g.Title}" + (g.AnyOf && g.Needs.Count > 1 ? "   (any one of these)" : ""));
                foreach (var n in g.Needs)
                {
                    bool? m = null;
                    try { m = n.Met(); } catch { }
                    Gui.Label($"      {Mark(m)} {n.Text}");
                }

                bool hidden = onScreen != null && g.Node != null && g.Node.Pointer == onScreen.Pointer && !ChoiceHints.IsVisible(g.Choice);
                var r = Gui.Row(1f, 1f);
                if (met != true && g.CanGive && GUI.Button(r[0], "Give the teen what this needs"))
                {
                    int got = Give(g);
                    ToolkitBehaviour.Toast(got > 0 ? $"Gave {got}: " + Short(g.Title, 40) : "Could not give it");
                }
                if (hidden && GUI.Button(r[1], "Give it and pick this answer"))
                {
                    Give(g);
                    PickHidden(onScreen, g.Choice);
                }
                Gui.Space(4);
            }
            if (shown == 0) shown = 1;
            var nav = Gui.Row(1f, 2f, 1f);
            if (GUI.Button(nav[0], "< Prev") && _groupPage > 0) _groupPage = Math.Max(0, _groupPage - shown);
            GUI.Label(nav[1], $"  {start + 1} to {Math.Min(groups.Count, start + shown)} of {groups.Count} checks");
            if (GUI.Button(nav[2], "Next >") && start + shown < groups.Count) _groupPage = start + shown;
        }

        // the game already left this answer out, so we choose it ourselves and clear the buttons
        static void PickHidden(DialogueChoiceNode node, int index)
        {
            Game.Try("Pick locked answer", () =>
            {
                node.OnOptionSelected(index);
                foreach (var d in UnityEngine.Object.FindObjectsOfType<DialogInteraction>())
                    if (d?._currentChoices != null && d._currentChoices.Count > 0) d.ResetAllDialogChoices();
                ToolkitBehaviour.Toast("Picked: " + ChoiceHints.TextOf(index));
            });
        }

        // for the hints panel
        public static int LockedOnScreen()
        {
            var node = ChoiceHints.Current;
            if (node?.availableChoices == null) return 0;
            int n = 0;
            try
            {
                for (int i = 0; i < node.availableChoices.Count; i++)
                    if (!ChoiceHints.IsVisible(i) && node.availableChoices[i]?.condition != null) n++;
            }
            catch { }
            return n;
        }
    }
}

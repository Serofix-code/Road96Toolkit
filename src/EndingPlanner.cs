using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using NB = BlueEyes.NarrativeBrain;

namespace Road96Toolkit
{
    // Pick an ending (a way a scenario can end, or a candidate winning the election) and see the
    // road to it: what facts it needs, which earlier scenarios give those facts, and what you have.
    // Built from the game's own data: every scenario lists its possible exits, and every exit lists
    // the facts it adds, removes and needs.
    internal static class EndingPlanner
    {
        class Exit
        {
            public Scn S;
            public int Index;
            public string Synopsis = "";
            public List<string> Add = new List<string>(), Remove = new List<string>(), Want = new List<string>(), Unwant = new List<string>();
            public bool GameOver;
            public readonly Dictionary<NB.GaugeType, float> Gauges = new Dictionary<NB.GaugeType, float>();
        }

        class Scn
        {
            public NB.NarrativeElement El;
            public string Id, Npc = "", Arc = "", Synopsis = "";
            public string[] Need = Array.Empty<string>(), Block = Array.Empty<string>();
            public float KMin, KMax;
            public readonly List<Exit> Exits = new List<Exit>();
        }

        class Step
        {
            public int Depth;
            public string Tag;
            public bool Want, Ok;
            public Exit Via;
            public Scn ViaScn;
        }

        static readonly List<Scn> _all = new List<Scn>();
        static readonly Dictionary<string, Scn> _byId = new Dictionary<string, Scn>(StringComparer.Ordinal);
        static readonly Dictionary<string, List<Exit>> _adds = new Dictionary<string, List<Exit>>(StringComparer.Ordinal);
        static readonly Dictionary<string, List<Exit>> _removes = new Dictionary<string, List<Exit>>(StringComparer.Ordinal);
        static List<string> _npcs = new List<string>();

        static int _mode;            // 0 character endings, 1 election
        static int _npc, _scnPage, _exitPage, _stepPage, _votePage;
        static Scn _goal;
        static Exit _goalExit;       // null means any way of finishing it
        static bool _planning;
        static List<Step> _steps = new List<Step>();
        static float _nextPlan;
        static NB.GaugeType _gauge = NB.GaugeType.Moderate;
        static List<Exit> _votes = new List<Exit>();

        static void Load()
        {
            _all.Clear(); _byId.Clear(); _adds.Clear(); _removes.Clear();
            var input = Game.Brain?._narrativeInput;
            if (input == null) return;
            Game.Try("Planner scan", () =>
            {
                foreach (var el in input.GetAllPossibleInputs())
                {
                    if (el == null || string.IsNullOrEmpty(el.ID) || _byId.ContainsKey(el.ID)) continue;
                    var s = new Scn { El = el, Id = el.ID };
                    try { s.Npc = el.NPC ?? ""; s.Arc = el.Arc ?? ""; s.Synopsis = el.Synopsis ?? ""; } catch { }
                    try { if (el.RequiredTags != null) s.Need = el.RequiredTags.Where(t => !string.IsNullOrEmpty(t)).ToArray(); } catch { }
                    try { if (el.UnwantedTags != null) s.Block = el.UnwantedTags.Where(t => !string.IsNullOrEmpty(t)).ToArray(); } catch { }
                    try { s.KMin = el.EntryKarmaLevelMin; s.KMax = el.EntryKarmaLevelMax; } catch { }
                    try
                    {
                        var imps = el.NarrativeImpacts;
                        if (imps != null)
                            for (int i = 0; i < imps.Count; i++)
                            {
                                var im = imps[i];
                                var e = new Exit { S = s, Index = i };
                                try { e.Synopsis = (im._synopsis ?? "").Replace("\n", " ").Trim(); } catch { }
                                try { e.GameOver = im._isGameOver; } catch { }
                                Copy(im._addedTags, e.Add);
                                Copy(im._removedTags, e.Remove);
                                Copy(im._wantedTags, e.Want);
                                Copy(im._unwantedTags, e.Unwant);
                                try
                                {
                                    var g = im._worldGaugeImpacts;
                                    if (g != null)
                                        for (int j = 0; j < g.Count; j++)
                                        {
                                            var w = g[j];
                                            if (w == null) continue;
                                            e.Gauges.TryGetValue(w._gaugeType, out var v);
                                            e.Gauges[w._gaugeType] = v + w._impact;
                                        }
                                }
                                catch { }
                                s.Exits.Add(e);
                                foreach (var t in e.Add) Map(_adds, t, e);
                                foreach (var t in e.Remove) Map(_removes, t, e);
                            }
                    }
                    catch { }
                    _all.Add(s);
                    _byId[s.Id] = s;
                }
            });
            _all.Sort((a, b) => string.Compare(a.Id, b.Id, StringComparison.OrdinalIgnoreCase));
            _npcs = _all.Select(s => s.Npc).Where(n => n.Length > 0).Distinct().OrderBy(n => n).ToList();
            Plugin.Log.LogInfo($"Planner: {_all.Count} scenarios, {_all.Sum(s => s.Exits.Count)} exits, {_adds.Count} facts with a source");
        }

        static void Copy(Il2CppSystem.Collections.Generic.List<string> from, List<string> to)
        {
            try { if (from != null) for (int i = 0; i < from.Count; i++) if (!string.IsNullOrEmpty(from[i])) to.Add(from[i]); } catch { }
        }

        static void Map(Dictionary<string, List<Exit>> d, string t, Exit e)
        {
            if (!d.TryGetValue(t, out var l)) d[t] = l = new List<Exit>();
            l.Add(e);
        }

        static bool Has(string t) => FactsEditor.Has(t);
        static bool Played(Scn s) => Has(s.Id);
        static string Cut(string s, int n) => string.IsNullOrEmpty(s) ? "" : s.Length <= n ? s : s.Substring(0, n) + "...";
        static string ExitText(Exit e) => e.Synopsis.Length > 0 ? e.Synopsis : (e.Add.Count > 0 ? "gives " + string.Join(", ", e.Add) : "way " + (e.Index + 1));

        // where a fact comes from: finishing that scenario, or the best exit that adds it
        static Exit SourceOf(string tag, bool want, out Scn scn)
        {
            scn = null;
            if (want && _byId.TryGetValue(tag, out var direct)) { scn = direct; return null; }
            var d = want ? _adds : _removes;
            if (!d.TryGetValue(tag, out var l) || l.Count == 0) return null;
            // prefer a scenario you have not played, that you could play now, with the fewest needs
            var e = l.OrderBy(x => Played(x.S)).ThenBy(x => Missing(x.S)).ThenBy(x => x.S.Need.Length).First();
            scn = e.S;
            return e;
        }

        static int Missing(Scn s) => s.Need.Count(t => !Has(t)) + s.Block.Count(Has);

        static void Plan()
        {
            _steps = new List<Step>();
            if (_goal == null) return;
            var seen = new HashSet<string>();
            void Req(string tag, bool want, int depth)
            {
                if (!seen.Add((want ? "+" : "-") + tag)) return;
                var st = new Step { Depth = depth, Tag = tag, Want = want, Ok = Has(tag) == want };
                st.Via = SourceOf(tag, want, out st.ViaScn);
                _steps.Add(st);
                if (st.Ok || depth >= 3 || st.ViaScn == null || st.ViaScn == _goal) return;
                foreach (var t in st.ViaScn.Need) Req(t, true, depth + 1);
                foreach (var t in st.ViaScn.Block) Req(t, false, depth + 1);
                if (st.Via != null)
                {
                    foreach (var t in st.Via.Want) Req(t, true, depth + 1);
                    foreach (var t in st.Via.Unwant) Req(t, false, depth + 1);
                }
            }
            foreach (var t in _goal.Need) Req(t, true, 0);
            foreach (var t in _goal.Block) Req(t, false, 0);
            if (_goalExit != null)
            {
                foreach (var t in _goalExit.Want) Req(t, true, 0);
                foreach (var t in _goalExit.Unwant) Req(t, false, 0);
            }
        }

        public static void Draw(NB.NarrativeController ctrl)
        {
            var ctx = Game.Context;
            if (ctx == null) { Gui.Label("Load a trip first."); return; }
            if (_all.Count == 0) Load();
            if (_all.Count == 0) { Gui.Label("Could not read the scenarios yet."); return; }

            if (_planning && _goal != null) { DrawPlan(ctrl, ctx); return; }

            int m = Gui.Tabs(new[] { "Character endings", "Election" }, _mode, Gui.TabKind.Choice);
            if (m != _mode) { _mode = m; _scnPage = 0; }
            if (_mode == 1) { DrawElection(ctrl); return; }

            if (_npcs.Count == 0) { Gui.Label("No characters found."); return; }
            if (_npc >= _npcs.Count) _npc = 0;
            var r = Gui.Row(3f, 1f);
            if (GUI.Button(r[0], "Character: " + _npcs[_npc] + "   (click for the next one)")) { _npc = (_npc + 1) % _npcs.Count; _scnPage = 0; _goal = null; }
            if (GUI.Button(r[1], "Reload")) { Load(); _goal = null; }

            var list = _all.Where(s => s.Npc == _npcs[_npc]).ToList();
            Gui.Label("Pick the scenario you want to reach:");
            int pick = Gui.List(list, ref _scnPage, s =>
                (Played(s) ? "[x] " : "[  ] ") + s.Id + (s.Arc.Length > 0 ? "  arc " + s.Arc : "") +
                (Missing(s) == 0 ? "   can play now" : $"   {Missing(s)} missing") + "   " + Cut(s.Synopsis, 50), 7, s => s == _goal);
            if (pick >= 0) { _goal = list[pick]; _goalExit = null; _exitPage = 0; }

            if (_goal == null) return;
            Gui.Space(4);
            Gui.Label($"How should {_goal.Id} end? Each way below is one of its endings in the game's data.");
            var exits = new List<Exit> { null };
            exits.AddRange(_goal.Exits);
            int ep = Gui.List(exits, ref _exitPage, e => e == null ? "Any way, I just want to play it" :
                Cut(ExitText(e), 80) + (e.GameOver ? "   (ends the trip)" : "") + (e.Want.Count + e.Unwant.Count > 0 ? "   needs more" : ""), 5, e => e == _goalExit);
            if (ep >= 0)
            {
                _goalExit = exits[ep];
                _planning = true;
                _stepPage = 0;
                Plan();
            }
        }

        static void DrawPlan(NB.NarrativeController ctrl, NB.NarrativeContext ctx)
        {
            if (Time.unscaledTime > _nextPlan) { _nextPlan = Time.unscaledTime + 2f; Plan(); }
            var r = Gui.Row(1f, 3f);
            if (GUI.Button(r[0], "< Change goal")) { _planning = false; return; }
            GUI.Label(r[1], "  Goal: " + _goal.Id + (_goal.Npc.Length > 0 ? " (" + _goal.Npc + ")" : "") + (Played(_goal) ? "   already played once" : ""));
            if (_goalExit != null) Gui.Label("Ending: " + Cut(ExitText(_goalExit), 130));
            if (_goalExit != null && _goalExit.Add.Count > 0) Gui.Label("It gives: " + Cut(string.Join(", ", _goalExit.Add), 130));

            int missing = _steps.Count(s => !s.Ok && s.Depth == 0);
            float karma = 0f;
            try { karma = ctx._currentKarma; } catch { }
            bool karmaOk = _goal.KMin >= _goal.KMax || (karma >= _goal.KMin && karma <= _goal.KMax);
            Gui.Label(missing == 0
                ? "You have everything it needs." + (karmaOk ? "" : $"  Karma has to be between {_goal.KMin:0.##} and {_goal.KMax:0.##}.")
                : $"{missing} things missing. The lines indented under one show how to get it.");

            int pick = -1;
            var steps = _steps;
            int per = 10, pages = Math.Max(1, (steps.Count + per - 1) / per);
            if (_stepPage >= pages) _stepPage = pages - 1;
            for (int i = _stepPage * per; i < Math.Min(steps.Count, (_stepPage + 1) * per); i++)
            {
                var s = steps[i];
                var row = Gui.Row(5f, 1f, 1f);
                string pad = new string(' ', s.Depth * 4);
                string what = s.Want ? s.Tag : "not " + s.Tag;
                string from = s.Ok ? "" : s.ViaScn == null ? "   (no scenario gives it, give it yourself)"
                    : s.Via == null ? "   <- play " + s.ViaScn.Id
                    : "   <- " + s.ViaScn.Id + ": " + Cut(ExitText(s.Via), 45);
                GUI.Label(row[0], pad + (s.Ok ? "[x] " : "[  ] ") + what + from);
                if (!s.Ok && GUI.Button(row[1], s.Want ? "Give" : "Remove")) pick = i;
                if (!s.Ok && s.ViaScn != null && s.ViaScn != _goal && GUI.Button(row[2], "Queue")) Queue(ctrl, ctx, s.ViaScn);
            }
            var nav = Gui.Row(1f, 2f, 1f);
            if (GUI.Button(nav[0], "< Prev") && _stepPage > 0) _stepPage--;
            GUI.Label(nav[1], $"  page {_stepPage + 1} / {pages}   ({steps.Count} steps)");
            if (GUI.Button(nav[2], "Next >") && _stepPage < pages - 1) _stepPage++;
            if (pick >= 0) { FactsEditor.Set(steps[pick].Tag, steps[pick].Want); Plan(); }

            var b = Gui.Row(3);
            if (GUI.Button(b[0], "Give me all it needs"))
            {
                foreach (var s in _steps.Where(s => s.Depth == 0 && !s.Ok)) FactsEditor.Set(s.Tag, s.Want);
                Plan();
                ToolkitBehaviour.Toast("Gave everything " + _goal.Id + " needs");
            }
            if (GUI.Button(b[1], "Queue it next")) Queue(ctrl, ctx, _goal);
            if (GUI.Button(b[2], "Play it now"))
                Game.Try("Play goal", () =>
                {
                    ctx._overrideNextBubble = _goal.Id;
                    ctrl._nextBubble = _goal.El;
                    ctrl.GoToNextBubble(false, true);
                    ToolkitBehaviour.Toast("Loading " + _goal.Id);
                });
            if (Gui.HasRoom()) Gui.Label("Giving facts is the quick way. Playing the scenarios on the left of each arrow is the real way.");
        }

        static void Queue(NB.NarrativeController ctrl, NB.NarrativeContext ctx, Scn s) =>
            Game.Try("Queue", () => { ctx._overrideNextBubble = s.Id; ctrl._nextBubble = s.El; ToolkitBehaviour.Toast("Queued " + s.Id); });

        static void DrawElection(NB.NarrativeController ctrl)
        {
            var ctx = Game.Context;
            var types = new[] { NB.GaugeType.Moderate, NB.GaugeType.Individualist, NB.GaugeType.Revolutionary };
            int cur = Array.IndexOf(types, _gauge);
            int n = Gui.Tabs(types.Select(t => ChoiceEditor.GaugeName(t) + " wins").ToArray(), cur, Gui.TabKind.Choice);
            if (n != cur || _votes.Count == 0)
            {
                _gauge = types[n];
                _votePage = 0;
                _votes = _all.SelectMany(s => s.Exits).Where(e => e.Gauges.TryGetValue(_gauge, out var v) && v > 0)
                    .OrderByDescending(e => e.Gauges[_gauge]).ToList();
            }
            Gui.Label($"Scenario endings that help {ChoiceEditor.GaugeLong(_gauge)} the most. Ones you have not played come first.");
            var list = _votes.OrderBy(e => Played(e.S)).ThenByDescending(e => e.Gauges[_gauge]).ToList();
            int pick = Gui.List(list, ref _votePage, e =>
                (Played(e.S) ? "[x] " : "[  ] ") + $"+{e.Gauges[_gauge]:0.##}  {e.S.Id}" + (e.S.Npc.Length > 0 ? " (" + e.S.Npc + ")" : "") +
                (Missing(e.S) == 0 ? "  ready" : "") + "   " + Cut(ExitText(e), 55), 11);
            if (pick >= 0) { _goal = list[pick].S; _goalExit = list[pick]; _planning = true; _stepPage = 0; Plan(); }
            Gui.Label("Pick one to plan it. Story > Election can also set the gauges straight away.");
        }
    }
}

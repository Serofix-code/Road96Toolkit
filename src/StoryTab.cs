using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using NB = BlueEyes.NarrativeBrain;

namespace Road96Toolkit
{
    // Scenario (bubble) picker, run endings and the election gauges.
    internal static class StoryTab
    {
        class Entry
        {
            public NB.NarrativeElement Element;
            public string Id, Npc, Title, State;
            public bool Border;
        }

        static List<Entry> _all = new List<Entry>();
        static List<Entry> _filtered = new List<Entry>();
        static List<string> _npcs = new List<string>();
        static int _npcFilter = -1;
        static string _search = "";
        static string _lastSearch = null;
        static int _lastNpc = -2;
        static int _page;
        static Entry _selected;
        static int _section;

        static readonly string[] Sections = { "Scenarios", "Endings", "Election" };

        public static void Draw()
        {
            var ctrl = Game.Controller;
            if (ctrl == null)
            {
                Gui.Label("Story systems are not loaded yet. Start or continue a trip first.");
                return;
            }

            var row = Gui.Row(3);
            for (int i = 0; i < Sections.Length; i++)
                if (GUI.Button(row[i], i == _section ? "[ " + Sections[i] + " ]" : Sections[i])) _section = i;
            Gui.Space(4);

            switch (_section)
            {
                case 0: DrawScenarios(ctrl); break;
                case 1: DrawEndings(); break;
                case 2: DrawElection(); break;
            }
        }

        static void Refresh()
        {
            _all.Clear();
            _npcs.Clear();
            var brain = Game.Brain;
            if (brain == null) return;
            var input = brain._narrativeInput;
            if (input == null) return;
            var arr = input.GetAllPossibleInputs();
            var seen = new HashSet<string>();
            for (int i = 0; i < arr.Length; i++)
            {
                var el = arr[i];
                if (el == null) continue;
                var e = new Entry { Element = el };
                Game.Try("Read scenario", () =>
                {
                    e.Id = el.ID;
                    e.Npc = string.IsNullOrEmpty(el.NPC) ? "" : el.NPC;
                    e.Title = el.Title;
                    e.State = el.RequiredPlayerState.ToString();
                    e.Border = el.IsBorder;
                });
                if (e.Id == null || !seen.Add(e.Id)) continue;
                _all.Add(e);
                if (e.Npc.Length > 0 && !_npcs.Contains(e.Npc)) _npcs.Add(e.Npc);
            }
            _all.Sort((a, b) => string.Compare(a.Npc + a.Id, b.Npc + b.Id, StringComparison.OrdinalIgnoreCase));
            _npcs.Sort(StringComparer.OrdinalIgnoreCase);
            _lastSearch = null;
            Plugin.Log.LogInfo($"Scenario list: {_all.Count} entries, {_npcs.Count} characters");
        }

        static void DrawScenarios(NB.NarrativeController ctrl)
        {
            if (_all.Count == 0) Refresh();

            var ctx = Game.Context;
            string current = "?";
            Game.Try("Read current bubble", () => current = ctx?.CurrentBubbleID ?? "none");
            Gui.Label("Current scenario: " + current);

            var r = Gui.Row(2f, 1f);
            string npcLabel = _npcFilter < 0 ? "All characters" : _npcs[_npcFilter];
            if (GUI.Button(r[0], "Character: " + npcLabel + "  (click to cycle)"))
            {
                _npcFilter++;
                if (_npcFilter >= _npcs.Count) _npcFilter = -1;
            }
            if (GUI.Button(r[1], "Reload list")) Refresh();

            _search = Gui.Search(_search);

            if (_search != _lastSearch || _npcFilter != _lastNpc)
            {
                _lastSearch = _search;
                _lastNpc = _npcFilter;
                _filtered = _all.Where(e =>
                    (_npcFilter < 0 || e.Npc == _npcs[_npcFilter]) &&
                    (Gui.Contains(e.Id, _search) || Gui.Contains(e.Title, _search) || Gui.Contains(e.Npc, _search))).ToList();
                _page = 0;
            }

            int picked = Gui.List(_filtered, ref _page,
                e => $"{e.Id}   {(e.Npc.Length > 0 ? "(" + e.Npc + ")" : "")}{(e.Border ? " [border]" : "")}",
                14, e => e == _selected);
            if (picked >= 0) _selected = _filtered[picked];

            Gui.Space();
            if (_selected == null)
            {
                Gui.Label("Pick a scenario above.");
                return;
            }

            Gui.Label($"Selected: {_selected.Id}");
            Gui.Label($"Character: {(_selected.Npc.Length > 0 ? _selected.Npc : "none")}   Starts as: {_selected.State}");
            if (!string.IsNullOrEmpty(_selected.Title) && _selected.Title != _selected.Id) Gui.Label("Title: " + _selected.Title);

            var b = Gui.Row(2);
            if (GUI.Button(b[0], "Queue as next scenario"))
            {
                if (Queue(ctrl, _selected)) ToolkitBehaviour.Toast("Queued " + _selected.Id + ", it plays after this one");
            }
            if (GUI.Button(b[1], "Play it now"))
            {
                if (Queue(ctrl, _selected))
                {
                    Game.Try("Jump to scenario", () => ctrl.GoToNextBubble(false, true));
                    ToolkitBehaviour.Toast("Loading " + _selected.Id);
                }
            }
        }

        static bool Queue(NB.NarrativeController ctrl, Entry e)
        {
            return Game.Try("Queue scenario", () =>
            {
                var ctx = ctrl.CurrentContext;
                if (ctx != null) ctx._overrideNextBubble = e.Id;
                ctrl._nextBubble = e.Element;
            });
        }

        static void DrawEndings()
        {
            Gui.Label("End the current trip right now with the ending you pick.");
            Gui.Label("This plays the normal end of trip sequence and saves the result.");
            Gui.Space();

            var ritual = Game.Ritual;
            if (ritual == null)
            {
                Gui.Label("The end of trip sequence is not loaded here. Try during a trip.");
            }
            else
            {
                foreach (EndRunRitual.RunEnding ending in Enum.GetValues(typeof(EndRunRitual.RunEnding)))
                {
                    if (Gui.Button(Pretty(ending.ToString())))
                    {
                        var en = ending;
                        Game.Try("Start ending", () => ritual.StartRitual(en));
                        ToolkitBehaviour.Toast("Ending: " + Pretty(en.ToString()));
                    }
                }
            }

            Gui.Space();
            Gui.Header("Border crossing");
            var ctrl = Game.Controller;
            var brain = Game.Brain;
            if (ctrl == null || brain == null) return;
            var input = brain._narrativeInput;
            if (input == null) return;

            void Jump(string label, NB.NarrativeElement el)
            {
                if (el == null) return;
                if (Gui.Button(label + "  (" + el.ID + ")"))
                {
                    Game.Try("Jump", () =>
                    {
                        var ctx = ctrl.CurrentContext;
                        if (ctx != null) ctx._overrideNextBubble = el.ID;
                        ctrl._nextBubble = el;
                        ctrl.GoToNextBubble(false, true);
                    });
                    ToolkitBehaviour.Toast("Loading " + el.ID);
                }
            }

            Game.Try("Read border scenes", () =>
            {
                Jump("Go to the border", input._borderBubble);
                Jump("Go to the border with Zoe", input._borderZoeBubble);
                Jump("Final border", input._borderFinal);
                Jump("Final road", input._finalRoadBubble);
                Jump("Final road with Zoe", input._finalRoadZoeBubble);
            });
        }

        static void DrawElection()
        {
            var ctx = Game.Context;
            if (ctx == null)
            {
                Gui.Label("No story context yet.");
                return;
            }

            string act = "?";
            Game.Try("Read act", () => act = ctx.StoryAct.ToString());
            Gui.Label("Story act: " + act);
            Gui.Label("The election ending leans on these three world gauges.");
            Gui.Label("Push one up to steer which ending you get at the end of the game.");
            Gui.Space();

            var gauges = ctx._worldGauges;
            if (gauges == null)
            {
                Gui.Label("No gauges found.");
                return;
            }

            for (int i = 0; i < gauges.Count; i++)
            {
                var g = gauges[i];
                if (g == null) continue;
                var r = Gui.Row(3f, 1f, 1f, 1.6f);
                GUI.Label(r[0], $"{g._type}: {g._value:0.00}");
                if (GUI.Button(r[1], "-")) g._value -= Step(g._value);
                if (GUI.Button(r[2], "+")) g._value += Step(g._value);
                if (GUI.Button(r[3], "Make it win"))
                {
                    float max = 0;
                    for (int j = 0; j < gauges.Count; j++)
                        if (j != i && gauges[j] != null) max = Math.Max(max, gauges[j]._value);
                    g._value = Math.Max(g._value, max + Math.Max(1f, Math.Abs(max) * 0.5f));
                    ToolkitBehaviour.Toast(g._type + " now leads");
                }
            }

            Gui.Space();
            if (Gui.Button("Reset gauges to zero"))
            {
                for (int i = 0; i < gauges.Count; i++)
                    if (gauges[i] != null) gauges[i]._value = 0f;
            }
        }

        static float Step(float v) => Math.Abs(v) > 5f ? 1f : 0.1f;

        static string Pretty(string s) => s.Replace('_', ' ');
    }
}

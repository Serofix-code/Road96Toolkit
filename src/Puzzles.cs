using System;
using System.Collections.Generic;
using System.Linq;
using Il2CppInterop.Runtime;
using NodeCanvas.Framework;
using UnityEngine;

namespace Road96Toolkit
{
    // Dial puzzles (like the calendar in Stan and Mitch's hideout, where you set the date of the
    // killer's attack) and the three clues of the Stan and Mitch investigation.
    //
    // A dial is a StateHolder with the states "0" to "9". The scenario's flow graph checks every dial
    // with a FlowCanvasCheckStateNode, one after the other, and sends an event like CORRECT_DATE when
    // all of them match. Reading those checks gives the answer, and setting the dials solves it.
    internal static class Puzzles
    {
        class Dial { public StateHolder Holder; public string Want; }
        class Puzzle { public string Name; public readonly List<Dial> Dials = new List<Dial>(); }

        static readonly List<Puzzle> _found = new List<Puzzle>();
        static float _nextScan;

        static bool IsDial(StateHolder h)
        {
            try
            {
                var ps = h._possibleStates;
                if (ps == null || ps.Count < 2 || ps.Count > 12) return false;
                for (int i = 0; i < ps.Count; i++) if (ps[i] == null || ps[i].Length != 1 || !char.IsDigit(ps[i][0])) return false;
                return true;
            }
            catch { return false; }
        }

        static void Scan()
        {
            _found.Clear();
            Game.Try("Dial puzzles", () =>
            {
                var done = new HashSet<IntPtr>();
                foreach (var o in UnityEngine.Object.FindObjectsOfType(Il2CppType.Of<GraphOwner>()))
                {
                    var owner = o.TryCast<GraphOwner>();
                    Graph g = null;
                    try { g = owner?.graph; } catch { }
                    if (g == null || !done.Add(g.Pointer)) continue;
                    var nodes = g.allNodes;
                    if (nodes == null) continue;

                    string ev = null;
                    var dials = new List<Dial>();
                    for (int i = 0; i < nodes.Count; i++)
                    {
                        var n = nodes[i];
                        if (n == null) continue;
                        var e = n.TryCast<BlueEyes.FlowCanvasUtils.FlowCanvasTriggeredEventNode>();
                        if (e != null)
                        {
                            string name = null;
                            try { name = e._eventName; } catch { }
                            if (ev == null && name != null && name.StartsWith("CORRECT", StringComparison.OrdinalIgnoreCase)) ev = name;
                            continue;
                        }
                        var c = n.TryCast<FlowCanvasCheckStateNode>();
                        var sv = c?._state;
                        var h = sv?._holder;
                        if (h == null || !IsDial(h) || string.IsNullOrEmpty(sv._currentState)) continue;
                        if (dials.Any(d => d.Holder.Pointer == h.Pointer)) continue;
                        dials.Add(new Dial { Holder = h, Want = sv._currentState });
                    }
                    if (ev == null || dials.Count < 2) continue;
                    var p = new Puzzle { Name = ev.Substring(ev.IndexOf('_') + 1).ToLowerInvariant().Replace('_', ' ') };
                    p.Dials.AddRange(dials);
                    _found.Add(p);
                }
            });
        }

        static string Answer(Puzzle p)
        {
            var s = string.Concat(p.Dials.Select(d => d.Want));
            return p.Dials.Count == 4 && p.Name.Contains("date") ? s.Substring(0, 2) + "/" + s.Substring(2) : s;
        }

        static string Now(Puzzle p)
        {
            try { return string.Concat(p.Dials.Select(d => d.Holder.CurrentState)); } catch { return "?"; }
        }

        static void Solve(Puzzle p)
        {
            Game.Try("Set dials", () =>
            {
                foreach (var d in p.Dials) if (d.Holder.CurrentState != d.Want) d.Holder.SetState(d.Want);
            });
            Plugin.Log.LogInfo($"Dials set for {p.Name}: {Answer(p)}");
            ToolkitBehaviour.Toast("Dials set to " + Answer(p) + ", now confirm it in the game");
        }

        // ---------- Stan and Mitch ----------

        class Clue { public string What, Known, Unknown; }

        static readonly Clue[] Clues =
        {
            new Clue { What = "What the killer looks like", Known = "JarodExposedWHO", Unknown = "STANMITCH3_JarodsApparenceNotKnown" },
            new Clue { What = "Where the killer will attack", Known = "JarodExposedWHERE", Unknown = "STANMITCH6_JarodsPlanWhereNotKnown" },
            new Clue { What = "When the killer will attack", Known = "JarodExposedWHEN", Unknown = "STANMITCH9_JarodsPlanWhenNotKnown" },
        };

        static Dictionary<string, string> _realTag;

        // the facts are named in the game's text table as TAG_<name>, the real fact can differ a little
        static string Tag(string name)
        {
            if (_realTag == null)
            {
                _realTag = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                Game.Try("Clue facts", () =>
                {
                    foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<NarrativeTagWrapper>()))
                    {
                        var w = o.TryCast<NarrativeTagWrapper>();
                        if (w == null || string.IsNullOrEmpty(w._tag) || string.IsNullOrEmpty(w._bigChoiceText)) continue;
                        var key = w._bigChoiceText.StartsWith("TAG_") ? w._bigChoiceText.Substring(4) : w._bigChoiceText;
                        _realTag[key] = w._tag;
                    }
                });
                if (_realTag.Count == 0) _realTag = null;
            }
            return _realTag != null && _realTag.TryGetValue(name, out var t) ? t : name;
        }

        // for the hints panel, next to the keypad codes
        public static IEnumerable<string> CodeLines()
        {
            if (Time.unscaledTime > _nextScan) { _nextScan = Time.unscaledTime + 10f; Scan(); }
            return _found.Select(p => "Dials (" + p.Name + "): " + Answer(p)).ToList();
        }

        public static void Draw()
        {
            if (Time.unscaledTime > _nextScan) { _nextScan = Time.unscaledTime + 2f; Scan(); }

            Gui.Header("Dial puzzles here");
            if (_found.Count == 0)
            {
                Gui.Label("None in this place. In Stan and Mitch's hideout this shows the date for the calendar.");
            }
            foreach (var p in _found)
            {
                string now = Now(p), want = string.Concat(p.Dials.Select(d => d.Want));
                Gui.Label($"Right {p.Name}: {Answer(p)}      dials now: {now}" + (now == want ? "  (already right)" : ""));
                if (Gui.Button("Set the dials to " + Answer(p))) Solve(p);
            }

            Gui.Space(6);
            Gui.Header("Stan and Mitch investigation");
            Gui.Label("Gives the clue as if you found it yourself.");
            foreach (var c in Clues)
            {
                string known = Tag(c.Known), unknown = Tag(c.Unknown);
                bool has = FactsEditor.Has(known);
                var r = Gui.Row(2.4f, 1f);
                GUI.Label(r[0], (has ? "[known] " : "[not known] ") + c.What);
                if (!has && GUI.Button(r[1], "Give it"))
                {
                    FactsEditor.Set(known, true);
                    FactsEditor.Set(unknown, false);
                    ToolkitBehaviour.Toast(c.What + ": known");
                }
                else if (has && GUI.Button(r[1], "Take it away"))
                {
                    FactsEditor.Set(known, false);
                }
            }
        }
    }
}

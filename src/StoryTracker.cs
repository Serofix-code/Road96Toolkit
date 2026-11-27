using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using NB = BlueEyes.NarrativeBrain;

namespace Road96Toolkit
{
    // Checklist of every scenario: which ones you played (in any trip), which are left,
    // and what each one needs before the game will pick it.
    internal static class StoryTracker
    {
        class Item
        {
            public NB.NarrativeElement El;
            public string Id, Npc, Title;
            public string[] Need = Array.Empty<string>(), Block = Array.Empty<string>();
            public bool Generic, Border;
        }

        static readonly List<Item> _all = new List<Item>();
        static List<Item> _shown = new List<Item>();
        static readonly HashSet<string> _played = new HashSet<string>();
        static readonly HashSet<string> _thisTrip = new HashSet<string>();
        static int _filter; // 0 all, 1 left, 2 played, 3 can play now
        static int _npc = -1;
        static List<string> _npcs = new List<string>();
        static string _search = "", _lastSearch = null;
        static int _lastFilter = -1, _lastNpc = -2, _page;
        static Item _sel;
        static float _nextRefresh;

        static void Load()
        {
            _all.Clear();
            var input = Game.Brain?._narrativeInput;
            if (input == null) return;
            Game.Try("Tracker scan", () =>
            {
                var seen = new HashSet<string>();
                foreach (var el in input.GetAllPossibleInputs())
                {
                    if (el == null || string.IsNullOrEmpty(el.ID) || !seen.Add(el.ID)) continue;
                    var it = new Item { El = el, Id = el.ID, Npc = el.NPC ?? "", Generic = el.Generic, Border = el.IsBorder };
                    try { it.Title = ItemsTab.Loc(el.Title); } catch { }
                    try { if (el.RequiredTags != null) it.Need = el.RequiredTags.ToArray(); } catch { }
                    try { if (el.UnwantedTags != null) it.Block = el.UnwantedTags.ToArray(); } catch { }
                    _all.Add(it);
                }
            });
            _all.Sort((a, b) => string.Compare(a.Npc + a.Id, b.Npc + b.Id, StringComparison.OrdinalIgnoreCase));
            _npcs = _all.Select(i => i.Npc).Where(n => n.Length > 0).Distinct().OrderBy(n => n).ToList();
            _lastSearch = null;
        }

        static void ReadPlayed(NB.NarrativeContext ctx)
        {
            _played.Clear();
            _thisTrip.Clear();
            Game.Try("Tracker played", () =>
            {
                void Runs(Il2CppSystem.Collections.Generic.List<NB.Run> runs, bool current)
                {
                    if (runs == null) return;
                    for (int i = 0; i < runs.Count; i++)
                    {
                        var r = runs[i];
                        if (r?._bubbles == null) continue;
                        for (int j = 0; j < r._bubbles.Count; j++)
                        {
                            var id = r._bubbles[j]?._bubbleId;
                            if (string.IsNullOrEmpty(id)) continue;
                            _played.Add(id);
                            if (current && i == runs.Count - 1) _thisTrip.Add(id);
                        }
                    }
                }
                Runs(ctx._allTimeRuns, false);
                Runs(ctx._runs, true);
                var prev = ctx._previousValidatedNarrativeElementsID;
                if (prev != null) for (int i = 0; i < prev.Count; i++) _played.Add(prev[i]);
                // finished scenarios also leave their id as a tag
                foreach (var it in _all) if (ctx.HasTag(it.Id)) _played.Add(it.Id);
            });
        }

        static bool HasTag(NB.NarrativeContext ctx, string t) { try { return ctx.HasTag(t); } catch { return false; } }

        static bool CanPlay(NB.NarrativeContext ctx, Item it) =>
            it.Need.All(t => HasTag(ctx, t)) && !it.Block.Any(t => HasTag(ctx, t));

        public static void Draw(NB.NarrativeController ctrl)
        {
            var ctx = Game.Context;
            if (ctx == null) { Gui.Label("Load a trip first."); return; }
            if (_all.Count == 0) Load();
            if (Time.unscaledTime > _nextRefresh) { _nextRefresh = Time.unscaledTime + 3f; ReadPlayed(ctx); }

            int done = _all.Count(i => _played.Contains(i.Id));
            Gui.Label($"Played {done} of {_all.Count} scenarios ({(_all.Count == 0 ? 0 : done * 100 / _all.Count)}%).  This trip: {_thisTrip.Count}");

            // a line per character with their progress
            var per = _all.Where(i => i.Npc.Length > 0).GroupBy(i => i.Npc)
                .Select(g => $"{g.Key} {g.Count(i => _played.Contains(i.Id))}/{g.Count()}");
            Gui.Label(string.Join("   ", per));

            var r = Gui.Row(4);
            string[] f = { "All", "Not played", "Played", "Can play now" };
            for (int i = 0; i < 4; i++)
                if (Gui.Pick(r[i], f[i], _filter == i, Gui.TabKind.Choice)) _filter = i;
            r = Gui.Row(2f, 1f);
            if (GUI.Button(r[0], "Character: " + (_npc < 0 ? "all" : _npcs[_npc]) + "  (click to cycle)")) { _npc++; if (_npc >= _npcs.Count) _npc = -1; }
            if (GUI.Button(r[1], "Reload")) { Load(); ReadPlayed(ctx); }

            _search = Gui.Search(_search);
            if (_search != _lastSearch || _filter != _lastFilter || _npc != _lastNpc || Event.current?.type == EventType.Layout && _shown.Count == 0)
            {
                _lastSearch = _search; _lastFilter = _filter; _lastNpc = _npc;
                _shown = _all.Where(i =>
                    (_npc < 0 || i.Npc == _npcs[_npc]) &&
                    (Gui.Contains(i.Id, _search) || Gui.Contains(i.Title, _search) || Gui.Contains(i.Npc, _search)) &&
                    (_filter == 0 || (_filter == 1 && !_played.Contains(i.Id)) || (_filter == 2 && _played.Contains(i.Id)) || (_filter == 3 && CanPlay(ctx, i)))).ToList();
                _page = 0;
            }

            int pick = Gui.List(_shown, ref _page, i =>
                (_played.Contains(i.Id) ? "[x] " : "[  ] ") + i.Id + (i.Npc.Length > 0 ? "  (" + i.Npc + ")" : "") + (CanPlay(ctx, i) && !_played.Contains(i.Id) ? "   ready" : ""), 11, i => i == _sel);
            if (pick >= 0) _sel = _shown[pick];

            if (_sel == null) { Gui.Label("Pick a scenario to see what it needs."); return; }
            Gui.Space(4);
            Gui.Label(_sel.Id + (string.IsNullOrEmpty(_sel.Title) || _sel.Title == _sel.Id ? "" : "   " + _sel.Title));
            Gui.Label((_played.Contains(_sel.Id) ? "Played" : "Not played yet") + (_sel.Generic ? "   generic scene" : "") + (_sel.Border ? "   border" : ""));
            if (_sel.Need.Length > 0)
                Gui.Label("Needs: " + string.Join(", ", _sel.Need.Select(t => (HasTag(ctx, t) ? "[x] " : "[  ] ") + t)));
            else Gui.Label("Needs nothing special");
            if (_sel.Block.Length > 0)
                Gui.Label("Blocked by: " + string.Join(", ", _sel.Block.Select(t => (HasTag(ctx, t) ? "[!] " : "") + t)));

            r = Gui.Row(2);
            if (GUI.Button(r[0], "Queue as next"))
                Game.Try("Queue", () => { ctx._overrideNextBubble = _sel.Id; ctrl._nextBubble = _sel.El; ToolkitBehaviour.Toast("Queued " + _sel.Id); });
            if (GUI.Button(r[1], "Give me what it needs"))
                Game.Try("Give tags", () =>
                {
                    foreach (var t in _sel.Need) if (!HasTag(ctx, t)) ctx.AddTag(t);
                    foreach (var t in _sel.Block) if (HasTag(ctx, t)) ctx.RemoveTag(t);
                    ToolkitBehaviour.Toast("Story flags set for " + _sel.Id);
                });
        }
    }
}

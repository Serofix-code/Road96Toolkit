using System;
using System.Collections.Generic;
using System.Linq;
using Il2CppInterop.Runtime;
using UnityEngine;
using NB = BlueEyes.NarrativeBrain;

namespace Road96Toolkit
{
    // Story facts: the game remembers everything you did as tags ("met Zoe", "helped Fanny", a finished
    // scenario...). Scenarios, answers and world objects check them, so adding or removing one changes
    // what the game thinks happened.
    internal static class FactsEditor
    {
        class Fact
        {
            public string Tag, Text = "";
            public readonly List<string> NeededBy = new List<string>(), BlockedBy = new List<string>();
            public bool Scenario, Asset;
        }

        static readonly Dictionary<string, Fact> _all = new Dictionary<string, Fact>(StringComparer.Ordinal);
        static List<Fact> _shown = new List<Fact>();
        static readonly HashSet<string> _have = new HashSet<string>(StringComparer.Ordinal);
        static int _filter; // 0 all, 1 have, 2 do not have
        static string _search = "", _lastSearch = null, _newFact = "";
        static int _lastFilter = -1, _page, _lastCount = -1;
        static Fact _sel;
        static float _nextRead;
        static bool _backedUp;

        static Fact Get(string tag)
        {
            if (!_all.TryGetValue(tag, out var f)) { f = new Fact { Tag = tag }; _all[tag] = f; }
            return f;
        }

        static void Scan(NB.NarrativeContext ctx)
        {
            _all.Clear();
            Game.Try("Facts scan", () =>
            {
                var input = Game.Brain?._narrativeInput;
                if (input != null)
                    foreach (var el in input.GetAllPossibleInputs())
                    {
                        if (el == null || string.IsNullOrEmpty(el.ID)) continue;
                        Get(el.ID).Scenario = true;
                        try { if (el.RequiredTags != null) foreach (var t in el.RequiredTags) if (!string.IsNullOrEmpty(t) && !Get(t).NeededBy.Contains(el.ID)) Get(t).NeededBy.Add(el.ID); } catch { }
                        try { if (el.UnwantedTags != null) foreach (var t in el.UnwantedTags) if (!string.IsNullOrEmpty(t) && !Get(t).BlockedBy.Contains(el.ID)) Get(t).BlockedBy.Add(el.ID); } catch { }
                    }
            });
            Game.Try("Facts assets", () =>
            {
                foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<NarrativeTagWrapper>()))
                {
                    var w = o.TryCast<NarrativeTagWrapper>();
                    if (w == null || string.IsNullOrEmpty(w._tag)) continue;
                    var f = Get(w._tag);
                    f.Asset = true;
                    if (f.Text.Length == 0 && !string.IsNullOrEmpty(w._bigChoiceText))
                    {
                        var s = ItemsTab.Loc(w._bigChoiceText);
                        if (!string.IsNullOrEmpty(s) && s != w._bigChoiceText) f.Text = s.Replace("\n", " ");
                    }
                }
            });
            Game.Try("Facts world", () =>
            {
                foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<TagWSC>()))
                {
                    var c = o.TryCast<TagWSC>();
                    if (c == null) continue;
                    string t = null;
                    try { t = c.tagWrapper != null ? c.tagWrapper._tag : null; } catch { }
                    if (string.IsNullOrEmpty(t)) t = c._tag;
                    if (!string.IsNullOrEmpty(t)) Get(t).Asset = true;
                }
            });
            ReadHave(ctx);
            _lastSearch = null;
        }

        static void ReadHave(NB.NarrativeContext ctx)
        {
            _have.Clear();
            Game.Try("Facts read", () =>
            {
                var l = ctx._currentTags;
                if (l == null) return;
                for (int i = 0; i < l.Count; i++)
                {
                    var t = l[i];
                    if (string.IsNullOrEmpty(t)) continue;
                    _have.Add(t);
                    Get(t);
                }
            });
        }

        public static bool Has(string tag)
        {
            var ctx = Game.Context;
            if (ctx == null) return false;
            try { return ctx.HasTag(tag); } catch { return false; }
        }

        public static bool Set(string tag, bool on)
        {
            var ctx = Game.Context;
            if (ctx == null || string.IsNullOrWhiteSpace(tag)) return false;
            tag = tag.Trim();
            if (!_backedUp) { _backedUp = true; Game.Try("Facts backup", () => SavesTab.SnapshotNow("before facts")); }
            bool ok = Game.Try(on ? "Add fact" : "Remove fact", () =>
            {
                if (on) { if (!ctx.HasTag(tag)) ctx.AddTag(tag); }
                else while (ctx.HasTag(tag)) ctx.RemoveTag(tag);
            });
            if (on) _have.Add(tag); else _have.Remove(tag);
            Get(tag);
            _lastSearch = null;
            ChoiceHints.Reread();
            return ok;
        }

        // finds a fact by exact name first, then by the start or any part of it
        public static string Find(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            text = text.Trim();
            if (_all.Count == 0 && Game.Context != null) Scan(Game.Context);
            if (_all.ContainsKey(text)) return text;
            var keys = _all.Keys.ToList();
            return keys.FirstOrDefault(k => k.Equals(text, StringComparison.OrdinalIgnoreCase))
                ?? keys.FirstOrDefault(k => k.StartsWith(text, StringComparison.OrdinalIgnoreCase))
                ?? keys.FirstOrDefault(k => k.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        public static IEnumerable<string> Matching(string text)
        {
            if (Game.Context != null && _all.Count == 0) Scan(Game.Context);
            return _all.Keys.Where(k => Gui.Contains(k, text)).OrderBy(k => k);
        }

        static string Line(Fact f) =>
            (_have.Contains(f.Tag) ? "[x] " : "[  ] ") + f.Tag +
            (f.Text.Length > 0 ? "   " + Cut(f.Text, 60) : "") +
            (f.Scenario ? "   (scenario done)" : "") +
            (f.NeededBy.Count > 0 ? $"   needed by {f.NeededBy.Count}" : "");

        static string Cut(string s, int n) => s.Length <= n ? s : s.Substring(0, n) + "...";

        public static void Draw()
        {
            var ctx = Game.Context;
            if (ctx == null) { Gui.Label("Load a trip first."); return; }
            if (_all.Count == 0) Scan(ctx);
            if (Time.unscaledTime > _nextRead)
            {
                _nextRead = Time.unscaledTime + 2f;
                int before = _have.Count;
                ReadHave(ctx);
                if (_have.Count != before) _lastSearch = null;
            }

            Gui.Label($"The game remembers what you did as facts. You have {_have.Count}, {_all.Count} are known. Click one to switch it.");
            Gui.Label("Scenarios, answers and places check these, so a change takes effect the next time the game looks.");

            var r = Gui.Row(1f, 1f, 1f, 1f);
            string[] f = { "All", "I have", "I do not have" };
            for (int i = 0; i < 3; i++)
                if (GUI.Button(r[i], _filter == i ? "[ " + f[i] + " ]" : f[i])) _filter = i;
            if (GUI.Button(r[3], "Rescan")) Scan(ctx);

            _search = Gui.Search(_search);
            if (_search != _lastSearch || _filter != _lastFilter || _all.Count != _lastCount)
            {
                _lastSearch = _search; _lastFilter = _filter; _lastCount = _all.Count;
                _shown = _all.Values.Where(x =>
                        (Gui.Contains(x.Tag, _search) || Gui.Contains(x.Text, _search)) &&
                        (_filter == 0 || (_filter == 1) == _have.Contains(x.Tag)))
                    .OrderBy(x => x.Scenario).ThenBy(x => x.Tag, StringComparer.OrdinalIgnoreCase).ToList();
            }

            int pick = Gui.List(_shown, ref _page, Line, 12, x => x == _sel);
            if (pick >= 0)
            {
                var x = _shown[pick];
                _sel = x;
                bool on = !_have.Contains(x.Tag);
                Set(x.Tag, on);
                ToolkitBehaviour.Toast((on ? "Added " : "Removed ") + x.Tag);
            }

            r = Gui.Row(1f, 3f, 1f);
            GUI.Label(r[0], "New fact");
            _newFact = Gui.TextField(r[1], _newFact);
            if (GUI.Button(r[2], "Add") && !string.IsNullOrWhiteSpace(_newFact))
            {
                var t = _newFact.Trim();
                Set(t, true);
                ToolkitBehaviour.Toast("Added " + t);
                _newFact = "";
            }

            if (_sel == null) return;
            Gui.Space(4);
            Gui.Label(_sel.Tag + (_have.Contains(_sel.Tag) ? "   you have it" : "   you do not have it") + (_sel.Scenario ? "   (left when this scenario is finished)" : ""));
            if (_sel.Text.Length > 0) Gui.Label(Cut(_sel.Text, 140));
            if (_sel.NeededBy.Count > 0 && Gui.HasRoom()) Gui.Label("Needed by: " + Cut(string.Join(", ", _sel.NeededBy), 140));
            if (_sel.BlockedBy.Count > 0 && Gui.HasRoom()) Gui.Label("Stops: " + Cut(string.Join(", ", _sel.BlockedBy), 140));
            if (_sel.NeededBy.Count == 0 && _sel.BlockedBy.Count == 0 && !_sel.Scenario && Gui.HasRoom())
                Gui.Label("No scenario checks this one directly, answers or places in a level might.");
        }
    }
}

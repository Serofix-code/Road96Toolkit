using System;
using System.Collections.Generic;
using System.Linq;
using BlueEyes.Inventory;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace Road96Toolkit
{
    // Collectible giver (the cassette tapes) and every other inventory item the game knows.
    internal static class ItemsTab
    {
        class Entry
        {
            public InventoryItem Item;
            public string Name;
            public bool Tape;
        }

        static int _section; // 0 tapes, 1 every item
        static readonly List<Entry> _all = new List<Entry>();
        static List<Entry> _shown = new List<Entry>();
        static string _search = "", _lastSearch = null;
        static int _lastSection = -1, _page;

        internal static string NameOf(InventoryItem it)
        {
            try
            {
                var tape = it.TryCast<Collectible>();
                if (tape != null && !string.IsNullOrEmpty(tape._trackName))
                {
                    var title = Loc(tape._trackName);
                    var author = Loc(tape._trackAuthor);
                    return string.IsNullOrEmpty(author) ? title : $"{title}  by {author}";
                }
            }
            catch { }
            return it.name;
        }

        internal static string Loc(string key)
        {
            if (string.IsNullOrEmpty(key)) return key;
            try
            {
                var s = BlueEyes.Localization.LocalizationManager.Localize(key, false);
                if (!string.IsNullOrEmpty(s)) return s;
            }
            catch { }
            return key;
        }

        static void Scan()
        {
            _all.Clear();
            Game.Try("Scan items", () =>
            {
                var seen = new HashSet<IntPtr>();
                foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<InventoryItem>()))
                {
                    var it = o.TryCast<InventoryItem>();
                    if (it == null || !seen.Add(it.Pointer)) continue;
                    _all.Add(new Entry { Item = it, Name = NameOf(it), Tape = it.TryCast<Collectible>() != null });
                }
                // the manager keeps the full tape list even for tapes not loaded yet
                var mgr = UnityEngine.Object.FindObjectOfType<CollectibleManager>();
                if (mgr != null && mgr._allCollectibles != null)
                    foreach (var c in mgr._allCollectibles)
                        if (c != null && seen.Add(c.Pointer)) _all.Add(new Entry { Item = c, Name = NameOf(c), Tape = true });
            });
            _all.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            _lastSearch = null;
            Plugin.Log.LogInfo($"Items: {_all.Count} items, {_all.Count(e => e.Tape)} tapes");
        }

        static bool Has(BlueEyes.NarrativeBrain.NarrativeContext ctx, InventoryItem it)
        {
            try { return ctx.HasItem(it); } catch { return false; }
        }

        static void Refresh()
        {
            Game.Try("Refresh tapes", () =>
            {
                var mgr = UnityEngine.Object.FindObjectOfType<CollectibleManager>();
                mgr?.UpdateAvailableCollectibles();
            });
        }

        public static void Draw()
        {
            var ctx = Game.Context;
            if (ctx == null) { Gui.Label("Load into the game first."); return; }
            if (_all.Count == 0) Scan();

            var row = Gui.Row(1f, 1f, 0.6f);
            if (Gui.Pick(row[0], "Collectible tapes", _section == 0, Gui.TabKind.Section)) _section = 0;
            if (Gui.Pick(row[1], "Every item", _section == 1, Gui.TabKind.Section)) _section = 1;
            if (GUI.Button(row[2], "Rescan")) Scan();
            Gui.Divider(Gui.TabKind.Section);

            if (_search != _lastSearch || _section != _lastSection)
            {
                _lastSearch = _search;
                _lastSection = _section;
                _shown = _all.Where(e => (_section == 1 || e.Tape) && Gui.Contains(e.Name, _search)).ToList();
                _page = 0;
            }

            int owned = _shown.Count(e => Has(ctx, e.Item));
            Gui.Label(_section == 0
                ? $"You have {owned} of {_shown.Count} tapes. They play in the walkman and count for the collection."
                : $"{_shown.Count} items. Vouchers, tapes, quest items and more. Some only make sense in their own level.");

            var a = Gui.Row(2);
            if (GUI.Button(a[0], _section == 0 ? "Give me every tape" : "Give me everything shown"))
            {
                int n = 0;
                foreach (var e in _shown)
                    if (!Has(ctx, e.Item)) { Game.Try("Give item", () => ctx.AddItem(e.Item)); n++; }
                Refresh();
                ToolkitBehaviour.Toast($"Gave {n} item(s)");
            }
            if (GUI.Button(a[1], "Take away everything shown"))
            {
                int n = 0;
                foreach (var e in _shown)
                    if (Has(ctx, e.Item)) { Game.Try("Take item", () => ctx.RemoveItem(e.Item)); n++; }
                Refresh();
                ToolkitBehaviour.Toast($"Took {n} item(s)");
            }

            _search = Gui.Search(_search);
            int pick = Gui.List(_shown, ref _page, e => (Has(ctx, e.Item) ? "[have]  " : "[  ]   ") + e.Name, 18);
            if (pick >= 0)
            {
                var e = _shown[pick];
                bool had = Has(ctx, e.Item);
                Game.Try(had ? "Take item" : "Give item", () => { if (had) ctx.RemoveItem(e.Item); else ctx.AddItem(e.Item); });
                Refresh();
                ToolkitBehaviour.Toast((had ? "Took " : "Gave ") + e.Name);
            }
            Gui.Label("Click an item to give it or take it away.");
        }
    }
}

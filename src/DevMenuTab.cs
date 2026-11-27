using System;
using System.Collections.Generic;
using UnityEngine;
using Elem = Tool7.DebugMenu.DebugMenuElements.DebugMenuElement;

namespace Road96Toolkit
{
    // The developers left their debug menu in the game. This tab lists whatever it has registered
    // in the current scene and runs the entries for you, so you do not need their gamepad combo.
    internal static class DevMenuTab
    {
        static readonly List<Elem> _path = new List<Elem>();
        static int _page;
        static bool _loggedRoot;

        public static void Draw()
        {
            Gui.Label("Game state: " + GameStates.Describe());
            Gui.Label("The game's own debug screen is broken in the retail build, so its entries are listed here.");
            var r = Gui.Row(1f, 1f);
            if (GUI.Button(r[0], "Back to teen view")) GameStates.BackToTeenView();
            if (GUI.Button(r[1], "Write info to log")) GameStates.DumpInfo();
            Gui.Space();

            Tool7.DebugMenu.DebugMenu dm = null;
            Game.Try("Find dev menu", () =>
            {
                dm = Tool7.DebugMenu.DebugMenu.Instance;
                if (dm == null) dm = null;
            });
            Elem root = null;
            if (dm != null) Game.Try("Dev menu root", () => root = dm.Root);
            if (root != null && !_loggedRoot)
            {
                _loggedRoot = true;
                Game.Try("Log dev root", () => Plugin.Log.LogInfo("Dev menu root has " + (root._childs?.Count ?? 0) + " entries"));
            }
            if (root == null)
            {
                Gui.Label("The developer menu has nothing loaded right now.");
                Gui.Label("It fills up once you are in a trip, try again in game.");
                return;
            }

            // drop anything that got destroyed since last frame
            _path.RemoveAll(e => e == null);
            var current = _path.Count > 0 ? _path[_path.Count - 1] : root;

            var nav = Gui.Row(1f, 3f);
            if (GUI.Button(nav[0], "< Up") && _path.Count > 0) { _path.RemoveAt(_path.Count - 1); _page = 0; }
            string where = "Developer menu";
            foreach (var e in _path) where += " / " + SafeName(e);
            GUI.Label(nav[1], where);

            var kids = new List<Elem>();
            Game.Try("Dev menu children", () =>
            {
                var list = current._childs;
                if (list == null) return;
                for (int i = 0; i < list.Count; i++)
                    if (list[i] != null) kids.Add(list[i]);
            });

            if (kids.Count == 0)
            {
                Gui.Label("(empty)");
                return;
            }

            const int rows = 18;
            int pages = Math.Max(1, (kids.Count + rows - 1) / rows);
            if (_page >= pages) _page = pages - 1;
            for (int i = _page * rows; i < Math.Min(kids.Count, (_page + 1) * rows); i++)
            {
                var e = kids[i];
                bool leaf = true;
                string value = "";
                Game.Try("Dev entry", () =>
                {
                    leaf = e.IsLeaf;
                    if (leaf) value = e.GetUserStringSafe();
                });
                string label = leaf ? SafeName(e) + (string.IsNullOrEmpty(value) || value == SafeName(e) ? "" : "   " + value) : SafeName(e) + "  >";
                if (GUI.Button(new Rect(Gui.X, Gui.Y, Gui.W, Gui.LineH), label))
                {
                    if (leaf)
                    {
                        Game.Try("Run dev entry", () => e.OnElementSelectedSafe());
                        Plugin.Log.LogInfo("Dev menu: ran " + SafeName(e));
                        ToolkitBehaviour.Toast("Ran " + SafeName(e));
                    }
                    else
                    {
                        _path.Add(e);
                        _page = 0;
                        return;
                    }
                }
                Gui.Y += Gui.LineH + Gui.Gap;
            }
            var pr = Gui.Row(1f, 2f, 1f);
            if (GUI.Button(pr[0], "< Prev") && _page > 0) _page--;
            GUI.Label(pr[1], $"  page {_page + 1} / {pages}");
            if (GUI.Button(pr[2], "Next >") && _page < pages - 1) _page++;
            Gui.Label("Entries that need a value may do nothing from here.");
        }

        static string SafeName(Elem e)
        {
            try { return e._elementName ?? "?"; } catch { return "?"; }
        }
    }
}

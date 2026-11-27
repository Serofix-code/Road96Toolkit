using System;
using System.Collections.Generic;
using UnityEngine;

namespace Road96Toolkit
{
    // IL2CPP stripped most of GUILayout in this game, so everything is laid out by hand
    // with plain GUI.Label / GUI.Button calls and a moving cursor.
    internal static class Gui
    {
        public const float LineH = 22f;
        public const float Gap = 3f;

        public static float X, Y, W, Bottom;

        public static void Begin(Rect area)
        {
            X = area.x;
            Y = area.y;
            W = area.width;
            Bottom = area.yMax;
        }

        public static bool HasRoom(float h = LineH) => Y + h <= Bottom;

        public static void Space(float h = 6f) => Y += h;

        public static void Label(string text, float h = LineH)
        {
            GUI.Label(new Rect(X, Y, W, h), text);
            Y += h;
        }

        public static void Header(string text)
        {
            GUI.Box(new Rect(X, Y, W, LineH), text);
            Y += LineH + Gap;
        }

        public static bool Button(string text)
        {
            bool r = GUI.Button(new Rect(X, Y, W, LineH), text);
            Y += LineH + Gap;
            return r;
        }

        public static bool Toggle(string text, bool on) => Button((on ? "[ON]  " : "[off]  ") + text);

        // Main tabs are blue, the sections inside a tab orange and small "pick one" choices green,
        // so they never look like a button that does something.
        public enum TabKind { Main, Section, Choice }

        public static Color ColorOf(TabKind k) =>
            k == TabKind.Main ? new Color(0.45f, 0.75f, 1f) : k == TabKind.Section ? new Color(1f, 0.7f, 0.3f) : new Color(0.5f, 1f, 0.55f);

        public static bool Pick(Rect r, string text, bool on, TabKind kind)
        {
            var col = ColorOf(kind);
            var old = GUI.color;
            GUI.color = on ? col : Color.Lerp(col, new Color(0.6f, 0.6f, 0.6f, 1f), 0.5f);
            bool hit = GUI.Button(r, on ? "[ " + text + " ]" : text);
            if (on) GUI.DrawTexture(new Rect(r.x + 3, r.yMax - 3, r.width - 6, 3), Texture2D.whiteTexture);
            GUI.color = old;
            return hit;
        }

        // a coloured line under a row of tabs, so it is clear what sits inside them
        public static void Divider(TabKind kind)
        {
            var old = GUI.color;
            var c = ColorOf(kind); c.a = 0.8f;
            GUI.color = c;
            GUI.DrawTexture(new Rect(X, Y, W, 2), Texture2D.whiteTexture);
            GUI.color = old;
            Y += 6;
        }

        public static int Tabs(string[] names, int sel, TabKind kind, int perRow = 0)
        {
            if (perRow <= 0) perRow = names.Length;
            for (int s = 0; s < names.Length; s += perRow)
            {
                var r = Row(perRow);
                for (int i = s; i < Math.Min(names.Length, s + perRow); i++)
                    if (Pick(r[i - s], names[i], i == sel, kind)) sel = i;
            }
            if (kind != TabKind.Choice) Divider(kind);
            return sel;
        }

        // Splits the current line into equal cells. Call Next() after drawing the row.
        public static Rect[] Row(int cells, float h = LineH)
        {
            var res = new Rect[cells];
            float cw = (W - Gap * (cells - 1)) / cells;
            for (int i = 0; i < cells; i++)
                res[i] = new Rect(X + i * (cw + Gap), Y, cw, h);
            Y += h + Gap;
            return res;
        }

        // A row with weights, for example label wide and buttons narrow.
        public static Rect[] Row(params float[] weights)
        {
            float total = 0;
            foreach (var w in weights) total += w;
            float avail = W - Gap * (weights.Length - 1);
            var res = new Rect[weights.Length];
            float x = X;
            for (int i = 0; i < weights.Length; i++)
            {
                float cw = avail * weights[i] / total;
                res[i] = new Rect(x, Y, cw, LineH);
                x += cw + Gap;
            }
            Y += LineH + Gap;
            return res;
        }

        static bool _textFieldBroken;

        public static string TextField(Rect r, string value)
        {
            if (_textFieldBroken)
            {
                GUI.Label(r, "(typing not supported)");
                return value;
            }
            try
            {
                return GUI.TextField(r, value ?? "");
            }
            catch (Exception e)
            {
                _textFieldBroken = true;
                Plugin.Log.LogWarning("GUI.TextField unavailable: " + e.Message);
                return value;
            }
        }

        public static string Search(string value)
        {
            var r = Row(1f, 4f);
            GUI.Label(r[0], "Search");
            return TextField(r[1], value);
        }

        // Paged list helper. Returns the index clicked, or -1.
        // index of the list entry under the mouse after the last List call, or -1
        public static int Hovered = -1;

        public static int List<T>(IList<T> items, ref int page, Func<T, string> label, int rows, Func<T, bool> highlight = null)
        {
            int clicked = -1;
            Hovered = -1;
            var ev = Event.current;
            int pages = Math.Max(1, (items.Count + rows - 1) / rows);
            if (page >= pages) page = pages - 1;
            if (page < 0) page = 0;

            int start = page * rows;
            for (int i = start; i < Math.Min(items.Count, start + rows); i++)
            {
                string text = label(items[i]);
                if (highlight != null && highlight(items[i])) text = "> " + text;
                var br = new Rect(X, Y, W, LineH);
                if (ev != null && br.Contains(ev.mousePosition)) Hovered = i;
                if (GUI.Button(br, text)) clicked = i;
                Y += LineH + Gap;
            }

            var nav = Row(1f, 2f, 1f);
            if (GUI.Button(nav[0], "< Prev") && page > 0) page--;
            GUI.Label(nav[1], $"  page {page + 1} / {pages}   ({items.Count} items)");
            if (GUI.Button(nav[2], "Next >") && page < pages - 1) page++;
            return clicked;
        }

        public static bool Contains(string hay, string needle)
        {
            if (string.IsNullOrEmpty(needle)) return true;
            if (hay == null) return false;
            return hay.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}

using System.Collections.Generic;
using UnityEngine;

namespace Road96Toolkit
{
    // A small note in the top right every time the game gives or takes a story fact.
    internal static class FactPopups
    {
        class Note { public string Text; public bool Gained; public float Until; }

        static readonly List<Note> _notes = new List<Note>();
        const float Life = 6f;
        const int MaxShown = 6;

        public static void Add(string tag, bool gained)
        {
            Plugin.Log.LogInfo((gained ? "Fact gained: " : "Fact lost: ") + tag);
            if (!Plugin.FactPopups.Value) return;
            var desc = FactsEditor.TextOf(tag);
            var text = (gained ? "+ " : "- ") + tag + (string.IsNullOrEmpty(desc) ? "" : "   " + (desc.Length > 50 ? desc.Substring(0, 50) + "..." : desc));
            _notes.Add(new Note { Text = text, Gained = gained, Until = Time.unscaledTime + Life });
            if (_notes.Count > 30) _notes.RemoveAt(0);
        }

        public static void Draw(float scale)
        {
            if (_notes.Count == 0) return;
            float now = Time.unscaledTime;
            _notes.RemoveAll(n => n.Until < now);
            if (_notes.Count == 0) return;

            var old = GUI.color;
            float w = 380, h = 24, x = Screen.width / scale - w - 12, y = 60;
            int start = Mathf.Max(0, _notes.Count - MaxShown);
            if (start > 0)
            {
                GUI.color = new Color(1f, 1f, 1f, 0.8f);
                GUI.Box(new Rect(x, y, w, h), $"{start} more facts changed");
                y += h + 3;
            }
            for (int i = start; i < _notes.Count; i++)
            {
                var n = _notes[i];
                float a = Mathf.Clamp01((n.Until - now) / 1f);
                var c = n.Gained ? new Color(0.55f, 1f, 0.6f) : new Color(1f, 0.55f, 0.5f);
                c.a = a;
                GUI.color = c;
                var r = new Rect(x, y, w, h);
                GUI.Box(r, "");
                GUI.Box(r, "");
                GUI.Label(new Rect(r.x + 8, r.y + 2, r.width - 12, r.height), n.Text);
                y += h + 3;
            }
            GUI.color = old;
        }
    }
}

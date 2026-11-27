using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using BlueEyes.Interactions;
using UnityEngine;

namespace Road96Toolkit
{
    // Marks everything you can pick up: cassette tapes you do not have yet, and anything the game
    // offers a Take or Pick up prompt for. Can also show every interaction the game has available.
    internal static class ItemEsp
    {
        class Mark
        {
            public Vector3 Pos;
            public Transform Anchor;
            public string Label;
            public int Kind; // 0 tape, 1 item, 2 other interaction
        }

        static readonly List<Mark> _marks = new List<Mark>();
        static float _nextScan;
        static int _tapes, _items, _other;
        static readonly Regex PickupWords = new Regex(@"\b(take|pick|grab|steal|collect|get|loot|keep|pocket)\b|^(take|pick|grab|steal|collect)", RegexOptions.IgnoreCase);
        static readonly Color[] Colors = { new Color(1f, 0.85f, 0.1f), new Color(0.2f, 0.9f, 1f), new Color(0.75f, 0.75f, 0.75f) };

        public static int Count => _marks.Count;

        static string Clean(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace("(Clone)", "").Replace("(toolkit)", "").Replace('_', ' ').Trim();
            return s.Length > 40 ? s.Substring(0, 40) : s;
        }

        static void Scan()
        {
            _marks.Clear();
            _tapes = _items = _other = 0;
            var ctx = Game.Context;

            Game.Try("Item ESP tapes", () =>
            {
                foreach (var tape in UnityEngine.Object.FindObjectsOfType<CollectibleTape>())
                {
                    if (tape == null || !tape.isActiveAndEnabled) continue;
                    var r = tape._tapeRenderer;
                    if (r != null && (!r.enabled || !r.gameObject.activeInHierarchy)) continue;
                    Collectible c = null;
                    try { c = tape._correspondingCollectible?.CurrentCollectible; } catch { }
                    if (c != null && ctx != null && ctx.HasItem(c)) continue; // already yours
                    string name = "Tape";
                    try { if (c != null) name = "Tape: " + ItemsTab.Loc(c._trackName) + " (" + ItemsTab.Loc(c._trackAuthor) + ")"; } catch { }
                    _marks.Add(new Mark { Anchor = tape.transform, Label = name, Kind = 0 });
                    _tapes++;
                }
            });

            Game.Try("Item ESP interactions", () =>
            {
                foreach (var i in UnityEngine.Object.FindObjectsOfType<Interaction>())
                {
                    if (i == null || !i.isActiveAndEnabled) continue;
                    if (i.TryCast<DialogChoiceInteraction>() != null) continue;
                    bool available;
                    try { available = i.IsAvailable(); } catch { available = false; }
                    if (!available) continue;

                    string raw = null, text = null;
                    try { raw = i._content; } catch { }
                    if (!string.IsNullOrEmpty(raw)) { try { text = ItemsTab.Loc(raw); } catch { } }
                    if (string.IsNullOrEmpty(text)) text = raw ?? "";
                    bool pickup = PickupWords.IsMatch(text) || PickupWords.IsMatch(raw ?? "");
                    if (!pickup && !Plugin.ItemEspAll.Value) continue;

                    string owner = null;
                    try { owner = i._provider != null ? i._provider.gameObject.name : null; } catch { }
                    owner ??= i.gameObject.name;
                    Transform anchor = null;
                    try { anchor = i.WorldAnchor; } catch { }
                    anchor ??= i.transform;

                    string label = (text.Length > 0 ? text + ": " : "") + Clean(owner);
                    _marks.Add(new Mark { Anchor = anchor, Label = label, Kind = pickup ? 1 : 2 });
                    if (pickup) _items++; else _other++;
                }
            });
        }

        public static void Update()
        {
            if (!Plugin.ItemEsp.Value) { if (_marks.Count > 0) _marks.Clear(); return; }
            if (Time.unscaledTime < _nextScan) return;
            _nextScan = Time.unscaledTime + 1f;
            Scan();
        }

        public static void Toggle()
        {
            Plugin.ItemEsp.Value = !Plugin.ItemEsp.Value;
            _nextScan = 0f;
            ToolkitBehaviour.Toast("Item ESP " + (Plugin.ItemEsp.Value ? "on" : "off"));
        }

        static void Shadowed(Rect r, string text, Color c)
        {
            GUI.color = new Color(0, 0, 0, 0.85f);
            GUI.Label(new Rect(r.x + 1, r.y + 1, r.width, r.height), text);
            GUI.color = c;
            GUI.Label(r, text);
        }

        public static void Draw()
        {
            if (!Plugin.ItemEsp.Value) return;
            var cam = Game.MainCamera;
            if (cam == null) return;
            var old = GUI.color;
            var camPos = cam.transform.position;
            float range = Mathf.Max(5f, Plugin.ItemEspRange.Value);

            foreach (var m in _marks)
            {
                if (m.Anchor != null) { try { m.Pos = m.Anchor.position; } catch { m.Anchor = null; } }
                float d = Vector3.Distance(camPos, m.Pos);
                if (d > range) continue;
                var sp = cam.WorldToScreenPoint(m.Pos);
                if (sp.z <= 0.05f) continue;
                float x = sp.x, y = Screen.height - sp.y;
                // closer things get a bigger box
                float half = Mathf.Clamp(60f / Mathf.Max(1f, d), 5f, 24f);
                var c = Colors[m.Kind];
                GUI.color = c;
                var tex = Texture2D.whiteTexture;
                float t = 2f;
                GUI.DrawTexture(new Rect(x - half, y - half, half * 2, t), tex);
                GUI.DrawTexture(new Rect(x - half, y + half - t, half * 2, t), tex);
                GUI.DrawTexture(new Rect(x - half, y - half, t, half * 2), tex);
                GUI.DrawTexture(new Rect(x + half - t, y - half, t, half * 2), tex);
                Shadowed(new Rect(x + half + 4, y - 11, 420, 22), $"{m.Label}  {d:0} m", c);
            }

            string sum = $"Item ESP: {_tapes} tapes, {_items} things to pick up" + (Plugin.ItemEspAll.Value ? $", {_other} other" : "") + $"  (within {range:0} m)";
            Shadowed(new Rect(10, 8, 600, 22), sum, Color.white);
            GUI.color = old;
        }
    }
}

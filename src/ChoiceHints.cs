using System;
using System.Collections.Generic;
using System.Text;
using BlueEyes.Dialogues;
using BlueEyes.Digicode;
using Il2CppInterop.Runtime;
using NodeCanvas.DialogueTrees;
using UnityEngine;
using NB = BlueEyes.NarrativeBrain;

namespace Road96Toolkit
{
    // Shows what every dialogue choice really does (karma, vote gauges, money, items, odds,
    // how the scene ends) and the code of any keypad in the level.
    internal static class ChoiceHints
    {
        class Hint
        {
            public string Text;
            public string Effects;
            public float Score;
            public bool Done;
        }

        static IntPtr _node;
        static readonly List<Hint> _hints = new List<Hint>();
        static int _best = -1;
        static readonly List<string> _codes = new List<string>();
        static float _nextCodeScan;

        public static void Update()
        {
            if (!Plugin.ChoiceHints.Value) { _hints.Clear(); _node = IntPtr.Zero; return; }

            DialogueChoiceNode node = null;
            try
            {
                var dt = DialogueTree.currentDialogue;
                node = dt?.currentNode?.TryCast<DialogueChoiceNode>();
            }
            catch { }

            if (node == null) { if (_node != IntPtr.Zero) { _hints.Clear(); _node = IntPtr.Zero; } }
            else if (node.Pointer != _node)
            {
                _node = node.Pointer;
                Read(node);
            }

            if (Time.unscaledTime > _nextCodeScan)
            {
                _nextCodeScan = Time.unscaledTime + 3f;
                ScanCodes();
            }
        }

        static void Read(DialogueChoiceNode node)
        {
            _hints.Clear();
            _best = -1;
            var ctx = Game.Context;
            var settings = Game.Settings;
            Game.Try("Read choices", () =>
            {
                var list = node.availableChoices;
                if (list == null) return;
                for (int i = 0; i < list.Count; i++)
                {
                    var c = list[i];
                    if (c == null) continue;
                    var h = new Hint();
                    var sb = new StringBuilder();
                    float score = 0;

                    try
                    {
                        var st = c.statement;
                        string key = st == null ? null : (!string.IsNullOrEmpty(st.i2) ? st.i2 : st.text);
                        h.Text = ItemsTab.Loc(key) ?? "?";
                    }
                    catch { h.Text = "choice " + (i + 1); }

                    try
                    {
                        if (c.impact != null && Math.Abs(c.impact.Impact) > 0.001f)
                        {
                            sb.Append($"Karma {(c.impact.Impact > 0 ? "+" : "")}{c.impact.Impact:0.##}   ");
                            score += c.impact.Impact * 3f;
                        }
                    }
                    catch { }
                    try
                    {
                        var w = c.worldImpact;
                        if (w != null && Math.Abs(w._impact) > 0.001f)
                            sb.Append($"{w._gaugeType} vote {(w._impact > 0 ? "+" : "")}{w._impact:0.##}   ");
                    }
                    catch { }
                    try { if (c.moneyRequired > 0) { sb.Append($"Costs ${c.moneyRequired}   "); score -= 0.2f; } } catch { }
                    try
                    {
                        if (c.item != null)
                            sb.Append((c.takeItem ? "Uses up " : "Needs ") + c.item.name + (ctx != null && !ctx.HasItem(c.item) ? " (you don't have it)" : "") + "   ");
                    }
                    catch { }
                    try
                    {
                        if (c.probability != null && ctx != null && settings != null)
                        {
                            float p = c.probability.GetProbability(ctx, settings);
                            sb.Append($"{p * 100f:0}% chance   ");
                            score += (p - 0.5f) * (c.probability._isPositiveForThePlayer ? 2f : -2f);
                        }
                    }
                    catch { }
                    try
                    {
                        if (c.isExit)
                        {
                            var ex = c.exitType;
                            sb.Append("Ends the scene" + (ex != NB.PlayerState.Any ? " (" + ex + ")" : "") + "   ");
                            if (ex == NB.PlayerState.FailedBorder) score -= 10f;
                            if (ex == NB.PlayerState.CrossedBorder) score += 10f;
                        }
                    }
                    catch { }
                    try { h.Done = c.onlyOnce && c.IsDone; } catch { }

                    h.Effects = sb.Length > 0 ? sb.ToString().TrimEnd() : "No hidden effect";
                    h.Score = h.Done ? -100f : score;
                    _hints.Add(h);
                }
            });

            float bestScore = 0.05f;
            for (int i = 0; i < _hints.Count; i++)
                if (_hints[i].Score > bestScore) { bestScore = _hints[i].Score; _best = i; }
            if (_hints.Count > 0) Plugin.Log.LogInfo($"Choice hints: {_hints.Count} choices, best {_best}");
        }

        static void ScanCodes()
        {
            _codes.Clear();
            try
            {
                foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<DigicodeController>()))
                {
                    var d = o.TryCast<DigicodeController>();
                    if (d == null || !d.gameObject.scene.IsValid() || d._correctCodes == null) continue;
                    int digits = 4;
                    try { digits = Math.Max(1, DigicodeController.MAX_DIAL); } catch { }
                    var parts = new List<string>();
                    foreach (var code in d._correctCodes) parts.Add(code.ToString("D" + digits));
                    if (parts.Count > 0) _codes.Add(d.gameObject.name + ": " + string.Join(" or ", parts));
                }
            }
            catch { }
        }

        public static void Draw()
        {
            if (!Plugin.ChoiceHints.Value) return;
            if (_hints.Count == 0 && _codes.Count == 0) return;

            float w = 470;
            float h = 26 + _hints.Count * 42 + (_codes.Count > 0 ? 22 + _codes.Count * 20 : 0);
            var r = new Rect(Screen.width - w - 16, Screen.height * 0.3f, w, h);
            GUI.Box(r, "");
            GUI.Box(r, "");
            float y = r.y + 4;
            if (_hints.Count > 0)
            {
                GUI.Label(new Rect(r.x + 8, y, w - 16, 20), "Choice hints (" + Plugin.Name + ")");
                y += 22;
                for (int i = 0; i < _hints.Count; i++)
                {
                    var hi = _hints[i];
                    string mark = i == _best ? "BEST  " : hi.Done ? "(used)  " : "";
                    GUI.Label(new Rect(r.x + 8, y, w - 16, 20), $"{i + 1}. {mark}{Short(hi.Text, 60)}");
                    GUI.Label(new Rect(r.x + 26, y + 18, w - 34, 20), hi.Effects);
                    y += 42;
                }
            }
            if (_codes.Count > 0)
            {
                GUI.Label(new Rect(r.x + 8, y, w - 16, 20), "Keypad codes in this level");
                y += 22;
                foreach (var c in _codes)
                {
                    GUI.Label(new Rect(r.x + 16, y, w - 24, 20), c);
                    y += 20;
                }
            }
        }

        static string Short(string s, int n) => s == null ? "" : s.Length <= n ? s : s.Substring(0, n - 3) + "...";
    }
}

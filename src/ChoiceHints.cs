using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BlueEyes.Interactions;
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
            public int Index;
            public bool Visible = true;
        }

        static DialogueChoiceNode _cur;
        static float _readAt;
        static bool _visibleChecked, _picked;
        public static bool HasChoices => _hints.Count > 0;

        static IntPtr _node;
        static readonly List<Hint> _hints = new List<Hint>();
        static int _best = -1;
        static readonly List<string> _codes = new List<string>();
        static float _nextCodeScan;

        public static void Update()
        {
            if (!Plugin.ChoiceHints.Value && !Plugin.AutoPickBest.Value) { _hints.Clear(); _node = IntPtr.Zero; return; }

            DialogueChoiceNode node = null;
            try
            {
                var dt = DialogueTree.currentDialogue;
                node = dt?.currentNode?.TryCast<DialogueChoiceNode>();
            }
            catch { }

            if (node == null) { if (_node != IntPtr.Zero) { _hints.Clear(); _node = IntPtr.Zero; _cur = null; } }
            else if (node.Pointer != _node)
            {
                _node = node.Pointer;
                _cur = node;
                _readAt = Time.unscaledTime;
                _visibleChecked = _picked = false;
                Read(node);
            }

            // the answer buttons show up a moment after the node starts
            if (_cur != null && !_visibleChecked && Time.unscaledTime - _readAt > 0.4f)
            {
                _visibleChecked = true;
                MarkVisible();
            }
            if (_cur != null && _visibleChecked && !_picked && Plugin.AutoPickBest.Value && _best >= 0 &&
                Time.unscaledTime - _readAt > Mathf.Max(0.5f, Plugin.AutoPickDelay.Value))
            {
                _picked = true;
                PickBest();
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
                    var h = new Hint { Index = i };
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

        static List<DialogChoiceInteraction> ShownButtons()
        {
            var res = new List<DialogChoiceInteraction>();
            try
            {
                foreach (var d in UnityEngine.Object.FindObjectsOfType<DialogInteraction>())
                {
                    var list = d?._currentChoices;
                    if (list == null) continue;
                    for (int i = 0; i < list.Count; i++) if (list[i] != null) res.Add(list[i]);
                }
            }
            catch { }
            return res;
        }

        static string Clean(string s) => (s ?? "").Trim().ToLowerInvariant();

        // only answers the game is really offering count (some are hidden by conditions)
        static void MarkVisible()
        {
            var shown = ShownButtons();
            if (shown.Count == 0) return;
            var texts = new HashSet<string>();
            foreach (var b in shown) { try { texts.Add(Clean(b.Content)); } catch { } }
            if (!_hints.Any(h => texts.Contains(Clean(h.Text)))) return; // texts did not match, keep them all
            foreach (var h in _hints) h.Visible = texts.Contains(Clean(h.Text));
            _best = -1;
            float bestScore = 0.05f;
            for (int i = 0; i < _hints.Count; i++)
                if (_hints[i].Visible && _hints[i].Score > bestScore) { bestScore = _hints[i].Score; _best = i; }
        }

        static void PickBest()
        {
            var h = _hints[_best];
            Game.Try("Auto pick", () =>
            {
                // press the real answer button when we can find it, so the game cleans up its UI
                foreach (var b in ShownButtons())
                {
                    string text = null;
                    try { text = b.Content; } catch { }
                    if (Clean(text) == Clean(h.Text)) { b.OnInteractionBegin(); ToolkitBehaviour.Toast("Picked: " + h.Text); return; }
                }
                _cur.OnOptionSelected(h.Index);
                ToolkitBehaviour.Toast("Picked: " + h.Text);
            });
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
            int shownCount = _hints.Count(x => x.Visible);

            float w = 470;
            float h = 26 + shownCount * 42 + (_codes.Count > 0 ? 22 + _codes.Count * 20 : 0);
            var r = new Rect(Screen.width - w - 16, Screen.height * 0.3f, w, h);
            GUI.Box(r, "");
            GUI.Box(r, "");
            float y = r.y + 4;
            if (_hints.Count > 0)
            {
                GUI.Label(new Rect(r.x + 8, y, w - 16, 20), "Choice hints" + (Plugin.AutoPickBest.Value ? "   (auto pick is on)" : ""));
                y += 22;
                for (int i = 0; i < _hints.Count; i++)
                {
                    var hi = _hints[i];
                    if (!hi.Visible) continue;
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

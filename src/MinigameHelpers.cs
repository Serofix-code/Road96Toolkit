using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace Road96Toolkit
{
    // Help for the other minigames: Connect 4 best move (from the game's own AI), metal detector
    // markers, air hockey goals, endless lives and ammo in the arcade shooter, a god mode for the
    // shooting scenes, and the odds on scratch tickets.
    internal static class MinigameHelpers
    {
        static float _nextScan;
        static Connect4 _c4;
        static Connect4Minimax _c4ai;
        static Il2CppSystem.Threading.Tasks.Task<int> _c4task;
        static int _c4board = -1, _c4best = -1;
        static readonly List<MetalDetectorTarget> _metal = new List<MetalDetectorTarget>();
        static AirHockeyMinigame _hockey;
        static readonly Dictionary<IntPtr, (int life, int ammo)> _arcadeStart = new Dictionary<IntPtr, (int, int)>();
        static readonly List<string> _tickets = new List<string>();

        public static bool AirHockeyActive { get { try { return _hockey != null && _hockey._gameIsOn; } catch { return false; } } }
        public static bool Connect4Found => _c4 != null;
        public static int Connect4Best => _c4best;

        static void Scan()
        {
            Game.Try("Minigame scan", () =>
            {
                _c4 = UnityEngine.Object.FindObjectOfType<Connect4>();
                _hockey = UnityEngine.Object.FindObjectOfType<AirHockeyMinigame>();
                _metal.Clear();
                foreach (var t in UnityEngine.Object.FindObjectsOfType<MetalDetectorTarget>()) if (t != null) _metal.Add(t);
                _tickets.Clear();
                foreach (var t in UnityEngine.Object.FindObjectsOfType<ScratchTicketInteraction>())
                {
                    if (t?._potentialGains == null) continue;
                    var parts = new List<string>();
                    for (int i = 0; i < t._potentialGains.Count; i++)
                    {
                        var g = t._potentialGains[i];
                        if (g == null) continue;
                        string what = g._item != null ? g._item.name : "$" + g._gain;
                        float pr = g._probability <= 1f ? g._probability * 100f : g._probability;
                        parts.Add($"{what} {pr:0}%");
                    }
                    if (parts.Count > 0) _tickets.Add(string.Join(", ", parts));
                }
            });
        }

        static int BoardKey()
        {
            int key = 17;
            try
            {
                var cols = _c4._tokens;
                if (cols == null) return -1;
                for (int x = 0; x < cols.Length; x++)
                {
                    var col = cols[x];
                    if (col == null) continue;
                    for (int y = 0; y < col.Length; y++) key = key * 31 + (col[y] == null ? 0 : col[y].Pointer.GetHashCode() % 7 + 1);
                }
            }
            catch { return -1; }
            return key;
        }

        public static void Update()
        {
            if (!Plugin.MinigameHelpers.Value) return;
            if (Time.unscaledTime > _nextScan) { _nextScan = Time.unscaledTime + 2f; Scan(); }

            // Connect 4: ask the game's own AI what it would play for us whenever the board changes
            bool c4Playing = false;
            try { c4Playing = _c4 != null && _c4._currentPlayToken != null; } catch { }
            if (c4Playing)
            {
                try
                {
                    int key = BoardKey();
                    if (key != _c4board && _c4task == null)
                    {
                        _c4board = key;
                        _c4ai ??= new Connect4Minimax(_c4, Connect4Minimax.Connect4Difficulty.HARD);
                        _c4task = _c4ai.GetBestPlay(true);
                    }
                    if (_c4task != null && _c4task.IsCompleted)
                    {
                        _c4best = _c4task.Result;
                        _c4task = null;
                    }
                }
                catch { _c4task = null; }
            }
            else { _c4ai = null; _c4best = -1; _c4task = null; _c4board = -1; }

            // arcade shooter: keep lives and ammo where they started
            if (Plugin.ArcadeGodMode.Value)
            {
                try
                {
                    foreach (var p in UnityEngine.Object.FindObjectsOfType<TSS_PlayerController>())
                    {
                        if (p == null) continue;
                        if (!_arcadeStart.TryGetValue(p.Pointer, out var start)) { start = (Math.Max(1, p._currentLife), Math.Max(1, p._currentAmmo)); _arcadeStart[p.Pointer] = start; }
                        if (p._currentLife < start.life) p._currentLife = start.life;
                        if (p._currentAmmo < start.ammo) p._currentAmmo = start.ammo;
                    }
                    // shooting scenes in the story
                    foreach (var l in UnityEngine.Object.FindObjectsOfType<PlayerLife>())
                        if (l != null && l._currentLife < l._life) l._currentLife = l._life;
                }
                catch { }
            }
        }

        public static void ScoreAirHockeyGoal()
        {
            if (_hockey == null) { ToolkitBehaviour.Toast("No air hockey game here"); return; }
            Game.Try("Air hockey goal", () => { _hockey.OnPuckScore(true); ToolkitBehaviour.Toast("Goal!"); });
        }

        public static void Draw()
        {
            if (!Plugin.MinigameHelpers.Value) return;
            var cam = Game.MainCamera;

            // metal detector: a marker on every hidden thing
            if (cam != null && _metal.Count > 0)
            {
                var old = GUI.color;
                foreach (var t in _metal)
                {
                    if (t == null || !t.isActiveAndEnabled) continue;
                    Vector3 pos;
                    try { pos = t.Position; } catch { continue; }
                    float d = Vector3.Distance(cam.transform.position, pos);
                    if (d > 60f) continue;
                    var sp = cam.WorldToScreenPoint(pos);
                    if (sp.z <= 0) continue;
                    GUI.color = new Color(0.3f, 1f, 0.4f);
                    var r = new Rect(sp.x - 9, Screen.height - sp.y - 9, 18, 18);
                    GUI.DrawTexture(r, Texture2D.whiteTexture);
                    GUI.color = Color.white;
                    GUI.Label(new Rect(r.xMax + 4, r.y, 160, 22), $"dig here  {d:0} m");
                }
                GUI.color = old;
            }

            var lines = new List<string>();
            if (_c4 != null && _c4best >= 0 && _c4task == null)
            {
                lines.Add($"Connect 4: drop your token in column {_c4best + 1} from the left (marked in green)");
                if (cam != null)
                {
                    try
                    {
                        var sp = cam.WorldToScreenPoint(_c4.GetPlayPosition(_c4best));
                        if (sp.z > 0)
                        {
                            var old = GUI.color;
                            GUI.color = new Color(0.3f, 1f, 0.4f);
                            GUI.DrawTexture(new Rect(sp.x - 12, Screen.height - sp.y - 40, 24, 24), Texture2D.whiteTexture);
                            GUI.color = old;
                            GUI.Label(new Rect(sp.x - 30, Screen.height - sp.y - 64, 120, 22), "best move");
                        }
                    }
                    catch { }
                }
            }
            if (AirHockeyActive) lines.Add("Air hockey: World tab has a button to score a goal");
            foreach (var t in _tickets) lines.Add("Scratch ticket odds: " + t);
            if (lines.Count == 0) return;
            float w = 560;
            var box = new Rect(Screen.width / 2f - w / 2f, Screen.height - 40 - lines.Count * 22, w, lines.Count * 22 + 8);
            GUI.Box(box, "");
            for (int i = 0; i < lines.Count; i++) GUI.Label(new Rect(box.x + 8, box.y + 4 + i * 22, w - 16, 22), lines[i]);
        }
    }
}

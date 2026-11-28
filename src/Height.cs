using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace Road96Toolkit
{
    // Make the teen taller or shorter. The whole player is scaled, so the body, the camera (your eyes)
    // and the collision capsule all grow together. Characters aim their eyes at a point on the player
    // with their IK look at, and that point moves with the player, so they keep looking you in the eyes.
    // The check below lists what the characters near you are really looking at.
    internal static class Height
    {
        static ConfigEntry<float> _height;
        static Vector3 _baseScale = Vector3.one;
        static IntPtr _player;
        static float _baseEye = -1f;

        public static void Init(ConfigFile cfg)
        {
            _height = cfg.Bind("Teen", "Height", 1f, "How tall the teen is, 1 is normal. 0.3 to 3");
        }

        static float Want => Mathf.Clamp(_height?.Value ?? 1f, 0.3f, 3f);

        public static void Update()
        {
            var p = Game.Player;
            if (p == null) return;
            try
            {
                var t = p.transform;
                if (p.Pointer != _player)
                {
                    // a new player object after a level load, remember how it was built
                    _player = p.Pointer;
                    _baseScale = t.localScale;
                    _baseEye = -1f;
                }
                var target = _baseScale * Want;
                if ((t.localScale - target).sqrMagnitude > 0.000001f) t.localScale = target;
                if (_baseEye < 0f && Math.Abs(Want - 1f) < 0.001f)
                {
                    var cam = Game.MainCamera;
                    if (cam != null && !Freecam.Active) _baseEye = cam.transform.position.y - t.position.y;
                }
                var c2 = Game.MainCamera;
                if (_eyes != null && c2 != null) _eyes.position = c2.transform.position;
                if (Time.unscaledTime > _nextFix) { _nextFix = Time.unscaledTime + 0.5f; FixLooks(); }
            }
            catch { }
        }

        public static void Set(float h)
        {
            _height.Value = Mathf.Clamp((float)Math.Round(h, 2), 0.3f, 3f);
            _nextCheck = 0f;
        }

        // ---------- checking what characters look at ----------

        static readonly List<string> _looks = new List<string>();
        static float _nextCheck;

        static bool UnderPlayer(Transform t, Transform player)
        {
            for (var x = t; x != null; x = x.parent) if (x == player) return true;
            return false;
        }

        // A point that sits right on your eyes. Characters whose look target is stuck on a spot of the
        // player that does not scale (some scenes give them a fixed point) are pointed at this instead.
        static Transform _eyes;
        static float _nextFix;
        static int _fixed;

        static Transform Eyes(Camera cam)
        {
            if (_eyes == null)
            {
                var go = new GameObject("ToolkitEyes");
                UnityEngine.Object.DontDestroyOnLoad(go);
                _eyes = go.transform;
            }
            _eyes.position = cam.transform.position;
            return _eyes;
        }

        // only touch targets that were meant for the player: near the player, but not on the
        // scaled body or the camera
        static void FixLooks()
        {
            var p = Game.Player;
            var cam = Game.MainCamera;
            if (p == null || cam == null || Freecam.Active || Math.Abs(Want - 1f) < 0.001f) return;
            var pt = p.transform;
            var eyes = Eyes(cam);
            foreach (var ik in UnityEngine.Object.FindObjectsOfType<BlueEyes.IK.IKManager>())
            {
                if (ik == null || Vector3.Distance(ik.transform.position, pt.position) > 15f) continue;
                if (UnderPlayer(ik.transform, pt)) continue;
                var solver = ik._lookAt?._lookAt;
                var target = solver?.target;
                if (target == null || target == eyes || UnderPlayer(target, pt) || UnderPlayer(target, cam.transform)) continue;
                var flat = target.position - pt.position; flat.y = 0f;
                if (flat.magnitude > 0.75f) continue;
                solver.target = eyes;
                _fixed++;
            }
        }

        static void Check()
        {
            _looks.Clear();
            var p = Game.Player;
            var cam = Game.MainCamera;
            if (p == null) return;
            var pt = p.transform;
            Game.Try("Look check", () =>
            {
                foreach (var ik in UnityEngine.Object.FindObjectsOfType<BlueEyes.IK.IKManager>())
                {
                    if (ik == null || Vector3.Distance(ik.transform.position, pt.position) > 25f) continue;
                    if (UnderPlayer(ik.transform, pt)) continue;
                    var solver = ik._lookAt?._lookAt;
                    var target = solver?.target;
                    if (target == null) continue;
                    string who = ik.transform.root.name;
                    string where;
                    if (target == _eyes) where = "your eyes (pointed there by the toolkit)";
                    else if (UnderPlayer(target, pt)) where = "you (" + target.name + "), moves with your height";
                    else if (cam != null && (target == cam.transform || UnderPlayer(target, cam.transform))) where = "your camera, moves with your height";
                    else continue;
                    string off = "";
                    if (cam != null && !Freecam.Active) off = $", {Vector3.Distance(solver.IKPosition, cam.transform.position):0.00} m from your eyes";
                    _looks.Add($"{who}: looks at {where}{off}");
                    if (_looks.Count >= 6) break;
                }
            });
        }

        public static void Draw()
        {
            Gui.Header($"Height: {Want:0.00}x");
            var r = Gui.Row(1f, 1f, 1f, 1f, 1f, 1f);
            float[] presets = { 0.5f, 0.75f, 1f, 1.25f, 1.5f, 2f };
            for (int i = 0; i < presets.Length; i++)
                if (Gui.Pick(r[i], presets[i] + "x", Math.Abs(Want - presets[i]) < 0.001f, Gui.TabKind.Choice)) Set(presets[i]);
            r = Gui.Row(1f, 1f, 1f, 1f);
            if (GUI.Button(r[0], "-0.1")) Set(Want - 0.1f);
            if (GUI.Button(r[1], "-0.01")) Set(Want - 0.01f);
            if (GUI.Button(r[2], "+0.01")) Set(Want + 0.01f);
            if (GUI.Button(r[3], "+0.1")) Set(Want + 0.1f);
            float v = GUI.HorizontalSlider(new Rect(Gui.X, Gui.Y + 4, Gui.W, Gui.LineH), Want, 0.3f, 3f);
            Gui.Y += Gui.LineH + Gui.Gap;
            if (Math.Abs(v - Want) > 0.005f) Set(v);

            var p = Game.Player;
            var cam = Game.MainCamera;
            if (p == null) { Gui.Label("Load into a level to see it."); return; }
            if (cam != null && !Freecam.Active)
            {
                float eye = cam.transform.position.y - p.transform.position.y;
                Gui.Label(_baseEye > 0f ? $"Eye height {eye:0.00} m (normal {_baseEye:0.00} m)" : $"Eye height {eye:0.00} m");
            }
            Gui.Label("Your body, your eyes and how big you are for doors and ledges all change.");
            Gui.Label("Walking speed stays the same. Cutscenes may put you back to normal for a moment.");

            Gui.Space(4);
            Gui.Header("Who looks at you");
            if (Time.unscaledTime > _nextCheck) { _nextCheck = Time.unscaledTime + 1f; Check(); }
            if (_looks.Count == 0) Gui.Label("Nobody near you is looking at you right now. Talk to someone to check.");
            if (_fixed > 0) Gui.Label($"Looks moved to your eyes so far: {_fixed}");
            foreach (var l in _looks) Gui.Label(l);
        }
    }
}

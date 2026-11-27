using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace Road96Toolkit
{
    // Shows which cup hides the ball in the shell game (the "bonneteau" with the guy who shuffles cans).
    internal static class CupEsp
    {
        static readonly List<BonneteauController> _games = new List<BonneteauController>();
        static float _nextScan;

        public static int Found => _games.Count;

        public static void Update()
        {
            if (!Plugin.CupEsp.Value) { _games.Clear(); return; }
            if (Time.unscaledTime < _nextScan) return;
            _nextScan = Time.unscaledTime + 1f;
            _games.Clear();
            try
            {
                foreach (var o in UnityEngine.Object.FindObjectsOfType(Il2CppType.Of<BonneteauController>()))
                {
                    var c = o.TryCast<BonneteauController>();
                    if (c != null && c.isActiveAndEnabled) _games.Add(c);
                }
            }
            catch { }
        }

        public static void Draw()
        {
            if (!Plugin.CupEsp.Value || _games.Count == 0) return;
            var cam = Game.MainCamera;
            if (cam == null) return;

            var old = GUI.color;
            foreach (var g in _games)
            {
                try
                {
                    if (g == null) continue;
                    var ball = g._cochonet;
                    if (ball == null) continue;
                    var can = ball._currentCan;

                    // the can is what you click, the ball itself is hidden under it
                    if (can != null) Mark(cam, can.transform.position, 0.12f, new Color(1f, 0.15f, 0.15f, 1f), "BALL HERE");
                    Mark(cam, ball.transform.position, 0.04f, new Color(1f, 0.9f, 0.1f, 1f), null);
                }
                catch { }
            }
            GUI.color = old;
        }

        static void Mark(Camera cam, Vector3 world, float radius, Color color, string label)
        {
            var sp = cam.WorldToScreenPoint(world);
            if (sp.z <= 0.05f) return;
            // size on screen from a world radius
            var edge = cam.WorldToScreenPoint(world + cam.transform.right * radius);
            float half = Mathf.Clamp(Mathf.Abs(edge.x - sp.x), 6f, 220f);
            float x = sp.x, y = Screen.height - sp.y;
            var r = new Rect(x - half, y - half, half * 2, half * 2);

            GUI.color = color;
            var tex = Texture2D.whiteTexture;
            float t = 3f;
            GUI.DrawTexture(new Rect(r.x, r.y, r.width, t), tex);
            GUI.DrawTexture(new Rect(r.x, r.yMax - t, r.width, t), tex);
            GUI.DrawTexture(new Rect(r.x, r.y, t, r.height), tex);
            GUI.DrawTexture(new Rect(r.xMax - t, r.y, t, r.height), tex);
            if (label != null)
                GUI.Label(new Rect(r.x, r.y - 22, 200, 22), label);
        }
    }
}

using System;
using BlueEyes.Entities;
using Cinemachine;
using UnityEngine;

namespace Road96Toolkit
{
    // Detaches the main camera from Cinemachine and flies it around.
    internal static class Freecam
    {
        public static bool Active { get; private set; }

        static Camera _cam;
        static CinemachineBrain _brain;
        static bool _brainWasEnabled;
        static Vector3 _pos;
        static float _yaw, _pitch, _roll;
        static float _speed = 6f;
        static float _fov = -1f, _origFov;
        static bool _frozePlayer;
        static bool _lockPlayer = true;

        public static Vector3 Position => _pos;
        public static Quaternion Rotation => Quaternion.Euler(_pitch, _yaw, _roll);

        public static void Toggle()
        {
            if (Active) Disable(); else Enable();
        }

        public static void Enable()
        {
            _cam = Game.MainCamera;
            if (_cam == null)
            {
                ToolkitBehaviour.Toast("No camera found");
                return;
            }

            Game.Try("Freecam brain", () =>
            {
                _brain = _cam.GetComponent<CinemachineBrain>();
                if (_brain != null)
                {
                    _brainWasEnabled = _brain.enabled;
                    _brain.enabled = false;
                }
            });

            var t = _cam.transform;
            _pos = t.position;
            var e = t.rotation.eulerAngles;
            _yaw = e.y;
            _pitch = e.x > 180 ? e.x - 360 : e.x;
            _roll = 0;
            _origFov = _cam.fieldOfView;
            if (_fov < 0) _fov = _origFov;

            var p = Game.Player;
            if (_lockPlayer && p != null)
            {
                Game.Try("Freeze player", () => p.Freeze(true, PlayerBlueEyes.FreezeOrigin.GodMode));
                _frozePlayer = true;
            }

            Active = true;
            ToolkitBehaviour.Toast("Freecam on");
        }

        public static void Disable()
        {
            if (!Active) return;
            Active = false;

            Game.Try("Freecam restore", () =>
            {
                if (_brain != null) _brain.enabled = _brainWasEnabled;
                if (_cam != null) _cam.fieldOfView = _origFov;
            });

            var p = Game.Player;
            if (_frozePlayer && p != null)
                Game.Try("Unfreeze player", () => p.Freeze(false, PlayerBlueEyes.FreezeOrigin.GodMode));
            _frozePlayer = false;
            ToolkitBehaviour.Toast("Freecam off");
        }

        public static void Update()
        {
            if (!Active) return;
            if (_cam == null) { Disable(); return; }

            float dt = Time.unscaledDeltaTime;

            float scroll = InputHelper.Scroll();
            if (Math.Abs(scroll) > 0.01f)
                _speed = Mathf.Clamp(_speed * (scroll > 0 ? 1.2f : 1f / 1.2f), 0.2f, 400f);

            // look: always while the toolkit window is closed, right mouse while it is open
            if (!ToolkitBehaviour.IsOpen || InputHelper.RightMouseHeld())
            {
                var d = InputHelper.MouseDelta();
                _yaw += d.x * 0.1f;
                _pitch = Mathf.Clamp(_pitch - d.y * 0.1f, -89f, 89f);
            }

            float sp = _speed;
            if (InputHelper.Held(KeyCode.LeftShift)) sp *= 4f;
            if (InputHelper.Held(KeyCode.LeftControl)) sp *= 0.25f;

            var rot = Rotation;
            Vector3 move = Vector3.zero;
            if (InputHelper.Held(KeyCode.W)) move += rot * Vector3.forward;
            if (InputHelper.Held(KeyCode.S)) move += rot * Vector3.back;
            if (InputHelper.Held(KeyCode.A)) move += rot * Vector3.left;
            if (InputHelper.Held(KeyCode.D)) move += rot * Vector3.right;
            if (InputHelper.Held(KeyCode.E) || InputHelper.Held(KeyCode.Space)) move += Vector3.up;
            if (InputHelper.Held(KeyCode.Q)) move += Vector3.down;
            _pos += move * sp * dt;
        }

        public static void LateUpdate()
        {
            if (!Active || _cam == null) return;
            // Cinemachine is off, but other scripts may still poke the camera, so we win the last word here
            var t = _cam.transform;
            t.position = _pos;
            t.rotation = Rotation;
            _cam.fieldOfView = _fov;
        }

        public static void Draw()
        {
            Gui.Label("Fly the camera anywhere. Your teen stays where they are.");
            if (Gui.Toggle("Freecam (" + Plugin.FreecamKey.Value + ")", Active)) Toggle();
            if (Gui.Toggle("Lock the player while flying", _lockPlayer)) _lockPlayer = !_lockPlayer;
            Gui.Space();

            Gui.Header("Controls");
            Gui.Label("W A S D move, Q down, E or Space up");
            Gui.Label("Shift is 4x faster, Ctrl is 4x slower, mouse wheel changes speed");
            Gui.Label("With this window open hold the right mouse button to look around");
            Gui.Space();

            var r = Gui.Row(2f, 1f, 1f);
            GUI.Label(r[0], $"Speed: {_speed:0.0} m/s");
            if (GUI.Button(r[1], "-")) _speed = Mathf.Max(0.2f, _speed / 1.5f);
            if (GUI.Button(r[2], "+")) _speed = Mathf.Min(400f, _speed * 1.5f);

            r = Gui.Row(2f, 1f, 1f, 1f);
            GUI.Label(r[0], $"Field of view: {(_fov < 0 ? 60 : _fov):0}");
            if (GUI.Button(r[1], "-")) _fov = Mathf.Clamp((_fov < 0 ? 60 : _fov) - 5, 5, 150);
            if (GUI.Button(r[2], "+")) _fov = Mathf.Clamp((_fov < 0 ? 60 : _fov) + 5, 5, 150);
            if (GUI.Button(r[3], "Reset")) _fov = _origFov > 0 ? _origFov : 60;

            r = Gui.Row(2f, 1f, 1f, 1f);
            GUI.Label(r[0], $"Roll: {_roll:0}");
            if (GUI.Button(r[1], "-")) _roll -= 5;
            if (GUI.Button(r[2], "+")) _roll += 5;
            if (GUI.Button(r[3], "Level")) _roll = 0;

            Gui.Space();
            if (Gui.Button("Teleport my teen to the camera"))
            {
                var p = Game.Player;
                if (p == null) ToolkitBehaviour.Toast("No player in this scene");
                else if (!Active) ToolkitBehaviour.Toast("Turn the freecam on first");
                else Game.Try("Teleport", () =>
                {
                    var ground = _pos;
                    if (Physics.Raycast(_pos, Vector3.down, out var hit, 200f)) ground = hit.point + Vector3.up * 0.05f;
                    p.Teleport(ground, Quaternion.Euler(0, _yaw, 0));
                    ToolkitBehaviour.Toast("Teleported");
                });
            }
            if (Gui.Button("Move the camera back to my teen"))
            {
                var p = Game.Player;
                if (p != null && Active)
                {
                    var t = p.transform;
                    _pos = t.position + Vector3.up * 1.6f;
                    _yaw = t.eulerAngles.y;
                    _pitch = 0;
                }
            }
        }
    }
}

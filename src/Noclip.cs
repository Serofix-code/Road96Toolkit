using System;
using BlueEyes.Entities;
using UnityEngine;

namespace Road96Toolkit
{
    // Lets the teen fly through walls. Unlike the freecam, you are the one moving,
    // so you can stop anywhere and keep playing from there.
    internal static class Noclip
    {
        public static bool Active { get; private set; }

        static PlayerBlueEyes _player;
        static CharacterController _cc;
        static float _speed = 5f;

        public static float Speed => _speed;

        public static void Toggle()
        {
            if (Active) Disable(); else Enable();
        }

        public static void Enable()
        {
            if (Freecam.Active) Freecam.Disable();
            var p = Game.Player;
            if (p == null)
            {
                ToolkitBehaviour.Toast("No teen to move in this scene");
                return;
            }
            _player = p;
            Game.Try("Noclip on", () =>
            {
                p.FreezePosition(true);
                _cc = p._controller;
                if (_cc != null) _cc.enabled = false;
            });
            Active = true;
            ToolkitBehaviour.Toast("Noclip on");
        }

        public static void Disable()
        {
            if (!Active) return;
            Active = false;
            var p = _player;
            Game.Try("Noclip off", () =>
            {
                if (_cc != null) _cc.enabled = true;
                if (p != null) p.FreezePosition(false);
            });
            _player = null;
            _cc = null;
            ToolkitBehaviour.Toast("Noclip off");
        }

        public static void Update()
        {
            if (!Active) return;
            if (_player == null || Game.Player == null || Game.Player.Pointer != _player.Pointer)
            {
                // new scene, new player object
                Active = false;
                _player = null;
                _cc = null;
                return;
            }
            if (ToolkitBehaviour.IsOpen) return;

            var cam = Game.MainCamera;
            if (cam == null) return;

            float scroll = InputHelper.Scroll();
            if (Math.Abs(scroll) > 0.01f)
                _speed = Mathf.Clamp(_speed * (scroll > 0 ? 1.2f : 1f / 1.2f), 0.5f, 200f);

            float sp = _speed;
            if (InputHelper.Held(KeyCode.LeftShift)) sp *= 3f;
            if (InputHelper.Held(KeyCode.LeftAlt)) sp *= 0.3f;

            var ct = cam.transform;
            Vector3 move = Vector3.zero;
            if (InputHelper.Held(KeyCode.W)) move += ct.forward;
            if (InputHelper.Held(KeyCode.S)) move -= ct.forward;
            if (InputHelper.Held(KeyCode.D)) move += ct.right;
            if (InputHelper.Held(KeyCode.A)) move -= ct.right;
            if (InputHelper.Held(KeyCode.Space)) move += Vector3.up;
            if (InputHelper.Held(KeyCode.LeftControl)) move += Vector3.down;
            if (move.sqrMagnitude > 1f) move.Normalize();

            _player.transform.position += move * sp * Time.unscaledDeltaTime;
        }
    }
}

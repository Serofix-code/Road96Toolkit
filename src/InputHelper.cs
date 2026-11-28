using System;
using UnityEngine;
using NewInput = UnityEngine.InputSystem;

namespace Road96Toolkit
{
    // Road 96 ships both the old input manager and the new input system.
    // We try the old one first and fall back if Unity says it is disabled.
    internal static class InputHelper
    {
        static int _mode = -1; // 0 old Input, 1 new InputSystem

        static void Detect()
        {
            if (_mode != -1) return;
            try
            {
                Input.GetKey(KeyCode.F1);
                _mode = 0;
            }
            catch (Exception)
            {
                _mode = 1;
            }
            Plugin.Log.LogInfo(_mode == 0 ? "Using legacy input" : "Using InputSystem");
        }

        static NewInput.Controls.KeyControl NewKey(KeyCode kc)
        {
            var kb = NewInput.Keyboard.current;
            if (kb == null) return null;
            NewInput.Key key;
            switch (kc)
            {
                case KeyCode.LeftControl: key = NewInput.Key.LeftCtrl; break;
                case KeyCode.RightControl: key = NewInput.Key.RightCtrl; break;
                case KeyCode.Return: key = NewInput.Key.Enter; break;
                case KeyCode.LeftBracket: key = NewInput.Key.LeftBracket; break;
                case KeyCode.RightBracket: key = NewInput.Key.RightBracket; break;
                default:
                    var name = kc.ToString();
                    if (name.StartsWith("Alpha")) name = "Digit" + name.Substring(5);
                    if (!Enum.TryParse(name, out key)) return null;
                    break;
            }
            return kb[key];
        }

        public static bool Down(KeyCode kc)
        {
            Detect();
            try
            {
                if (_mode == 0) return Input.GetKeyDown(kc);
                var k = NewKey(kc);
                return k != null && k.wasPressedThisFrame;
            }
            catch { return false; }
        }

        public static bool Held(KeyCode kc)
        {
            Detect();
            try
            {
                if (_mode == 0) return Input.GetKey(kc);
                var k = NewKey(kc);
                return k != null && k.isPressed;
            }
            catch { return false; }
        }

        static bool _axisBroken;

        public static Vector2 MouseDelta()
        {
            Detect();
            try
            {
                if (_mode == 0 && !_axisBroken)
                {
                    try
                    {
                        return new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y")) * 10f;
                    }
                    catch { _axisBroken = true; }
                }
                var m = NewInput.Mouse.current;
                if (m == null) return Vector2.zero;
                return m.delta.ReadValue();
            }
            catch { return Vector2.zero; }
        }

        public static float Scroll()
        {
            Detect();
            try
            {
                if (_mode == 0) return Input.mouseScrollDelta.y;
                var m = NewInput.Mouse.current;
                return m == null ? 0f : m.scroll.ReadValue().y / 120f;
            }
            catch { return 0f; }
        }

        public static bool LeftMouseDown()
        {
            Detect();
            try
            {
                if (_mode == 0) return Input.GetMouseButtonDown(0);
                var m = NewInput.Mouse.current;
                return m != null && m.leftButton.wasPressedThisFrame;
            }
            catch { return false; }
        }

        public static bool RightMouseDown()
        {
            Detect();
            try
            {
                if (_mode == 0) return Input.GetMouseButtonDown(1);
                var m = NewInput.Mouse.current;
                return m != null && m.rightButton.wasPressedThisFrame;
            }
            catch { return false; }
        }

        // bottom left origin, like Input.mousePosition
        public static Vector2 MousePosition()
        {
            Detect();
            try
            {
                if (_mode == 0) return Input.mousePosition;
                var m = NewInput.Mouse.current;
                return m == null ? Vector2.zero : m.position.ReadValue();
            }
            catch { return Vector2.zero; }
        }

        public static bool LeftMouseHeld()
        {
            Detect();
            try
            {
                if (_mode == 0) return Input.GetMouseButton(0);
                var m = NewInput.Mouse.current;
                return m != null && m.leftButton.isPressed;
            }
            catch { return false; }
        }

        public static bool RightMouseHeld()
        {
            Detect();
            try
            {
                if (_mode == 0) return Input.GetMouseButton(1);
                var m = NewInput.Mouse.current;
                return m != null && m.rightButton.isPressed;
            }
            catch { return false; }
        }
    }
}

namespace Road96Toolkit
{
    // A key with optional Ctrl / Shift / Alt, written in the config like "Ctrl+F2".
    internal sealed class Hotkey
    {
        public UnityEngine.KeyCode Key = UnityEngine.KeyCode.None;
        public bool Ctrl, Shift, Alt;

        public static Hotkey Parse(string text)
        {
            var h = new Hotkey();
            if (string.IsNullOrWhiteSpace(text)) return h;
            foreach (var raw in text.Split('+'))
            {
                var part = raw.Trim();
                switch (part.ToLowerInvariant())
                {
                    case "ctrl": case "control": h.Ctrl = true; break;
                    case "shift": h.Shift = true; break;
                    case "alt": h.Alt = true; break;
                    default:
                        if (System.Enum.TryParse(part, true, out UnityEngine.KeyCode k)) h.Key = k;
                        break;
                }
            }
            return h;
        }

        static bool CtrlHeld => InputHelper.Held(UnityEngine.KeyCode.LeftControl) || InputHelper.Held(UnityEngine.KeyCode.RightControl);
        static bool ShiftHeld => InputHelper.Held(UnityEngine.KeyCode.LeftShift) || InputHelper.Held(UnityEngine.KeyCode.RightShift);
        static bool AltHeld => InputHelper.Held(UnityEngine.KeyCode.LeftAlt) || InputHelper.Held(UnityEngine.KeyCode.RightAlt);

        public bool Pressed()
        {
            if (Key == UnityEngine.KeyCode.None) return false;
            if (!InputHelper.Down(Key)) return false;
            // modifiers must match exactly, so F2 alone does not fire Ctrl+F2 and the other way round
            return Ctrl == CtrlHeld && Shift == ShiftHeld && Alt == AltHeld;
        }

        public override string ToString()
        {
            if (Key == UnityEngine.KeyCode.None) return "(no key)";
            return (Ctrl ? "Ctrl+" : "") + (Shift ? "Shift+" : "") + (Alt ? "Alt+" : "") + Key;
        }
    }
}

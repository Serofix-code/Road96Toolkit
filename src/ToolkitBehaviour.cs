using System;
using BlueEyes.Entities;
using UnityEngine;

namespace Road96Toolkit
{
    public class ToolkitBehaviour : MonoBehaviour
    {
        public ToolkitBehaviour(IntPtr ptr) : base(ptr) { }

        internal static ToolkitBehaviour Instance;

        static readonly string[] TabNames = { "Story", "Teen", "Abilities", "World", "Freecam", "Spawner", "Scene editor" };
        int _tab;

        bool _open;
        public static bool IsOpen => Instance != null && Instance._open;
        bool _playerFrozenByUs;
        bool _prevCursorVisible;
        CursorLockMode _prevCursorLock;
        Rect _window = new Rect(40, 40, 560, 720);
        bool _dragging;
        Vector2 _dragOffset;

        static string _toast;
        static float _toastUntil;

        public static void Toast(string msg)
        {
            _toast = msg;
            _toastUntil = Time.unscaledTime + 3f;
        }

        void Awake()
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            gameObject.hideFlags = HideFlags.HideAndDontSave;
        }

        void Update()
        {
            if (InputHelper.Down(Plugin.MenuKey.Value)) SetOpen(!_open);
            if (InputHelper.Down(Plugin.FreecamKey.Value)) Freecam.Toggle();
            if (InputHelper.Down(Plugin.PickKey.Value)) SceneEditor.PickUnderCrosshair();
            if (InputHelper.Down(Plugin.HideUiKey.Value)) WorldTab.ToggleHud();

            Freecam.Update();
            SceneEditor.Update();
        }

        void LateUpdate()
        {
            Freecam.LateUpdate();
            if (_open)
            {
                Cursor.visible = true;
                Cursor.lockState = CursorLockMode.None;
            }
        }

        void SetOpen(bool open)
        {
            _open = open;
            var p = Game.Player;
            if (open)
            {
                _prevCursorVisible = Cursor.visible;
                _prevCursorLock = Cursor.lockState;
                if (Plugin.FreezeWithMenu.Value && p != null && !Freecam.Active)
                {
                    Game.Try("Freeze player", () => p.Freeze(true, PlayerBlueEyes.FreezeOrigin.Debug));
                    _playerFrozenByUs = true;
                }
            }
            else
            {
                if (_playerFrozenByUs && p != null && !Freecam.Active)
                    Game.Try("Unfreeze player", () => p.Freeze(false, PlayerBlueEyes.FreezeOrigin.Debug));
                _playerFrozenByUs = false;
                Cursor.visible = _prevCursorVisible;
                Cursor.lockState = _prevCursorLock;
            }
        }

        void OnGUI()
        {
            float s = Mathf.Clamp(Plugin.MenuScale.Value, 0.5f, 3f);
            if (Math.Abs(s - 1f) > 0.01f)
                GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));

            if (_toast != null && Time.unscaledTime < _toastUntil)
            {
                var tr = new Rect(Screen.width / s / 2f - 220, 20, 440, 26);
                GUI.Box(tr, "");
                GUI.Box(tr, _toast);
            }

            if (Freecam.Active && !_open)
                GUI.Label(new Rect(10, Screen.height / s - 26, 900, 24), "Freecam  |  WASD move  Q/E down/up  Shift fast  Ctrl slow  Scroll speed  " + Plugin.FreecamKey.Value + " exit");

            if (!_open) return;

            HandleDrag(s);

            // drawn twice so the background is less see through
            GUI.Box(_window, "");
            GUI.Box(_window, "");
            GUI.Box(new Rect(_window.x, _window.y, _window.width, Gui.LineH), $"{Plugin.Name} {Plugin.Version}   ({Plugin.MenuKey.Value} to close, drag here to move)");

            var inner = new Rect(_window.x + 8, _window.y + Gui.LineH + 6, _window.width - 16, _window.height - Gui.LineH - 14);
            Gui.Begin(inner);

            // two rows of tabs
            var r1 = Gui.Row(4);
            var r2 = Gui.Row(3);
            for (int i = 0; i < TabNames.Length; i++)
            {
                var rect = i < 4 ? r1[i] : r2[i - 4];
                string label = i == _tab ? "[ " + TabNames[i] + " ]" : TabNames[i];
                if (GUI.Button(rect, label)) _tab = i;
            }
            Gui.Space(4);

            try
            {
                switch (_tab)
                {
                    case 0: StoryTab.Draw(); break;
                    case 1: TeenTab.Draw(); break;
                    case 2: AbilitiesTab.Draw(); break;
                    case 3: WorldTab.Draw(); break;
                    case 4: Freecam.Draw(); break;
                    case 5: Spawner.Draw(); break;
                    case 6: SceneEditor.Draw(); break;
                }
            }
            catch (Exception e)
            {
                Gui.Label("This tab hit an error: " + e.GetType().Name);
                Gui.Label(e.Message);
            }
        }

        void HandleDrag(float s)
        {
            var e = Event.current;
            if (e == null) return;
            var title = new Rect(_window.x, _window.y, _window.width, Gui.LineH);
            var mouse = e.mousePosition;
            if (e.type == EventType.MouseDown && title.Contains(mouse))
            {
                _dragging = true;
                _dragOffset = mouse - new Vector2(_window.x, _window.y);
            }
            else if (e.type == EventType.MouseUp)
            {
                _dragging = false;
            }
            else if (e.type == EventType.MouseDrag && _dragging)
            {
                _window.x = Mathf.Clamp(mouse.x - _dragOffset.x, 0, Screen.width / s - 60);
                _window.y = Mathf.Clamp(mouse.y - _dragOffset.y, 0, Screen.height / s - 30);
            }
        }
    }
}

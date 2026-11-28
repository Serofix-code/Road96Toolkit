using System;
using BlueEyes.Entities;
using UnityEngine;

namespace Road96Toolkit
{
    public class ToolkitBehaviour : MonoBehaviour
    {
        public ToolkitBehaviour(IntPtr ptr) : base(ptr) { }

        internal static ToolkitBehaviour Instance;

        static readonly string[] TabNames = { "Story", "Levels", "Teen", "Abilities", "Items", "Saves", "World", "Travel", "Freecam", "Spawner", "Scene editor", "Dev menu", "Skip" };
        const int DevTab = 11;
        internal const int SceneEditorTab = 10;

        internal void CloseMenu() { if (_open) SetOpen(false); }
        internal void OpenTab(int tab) { _tab = tab; if (!_open) SetOpen(true); }
        int _tab;

        bool _open;
        public static bool IsOpen => Instance != null && Instance._open;
        bool _playerFrozenByUs;
        bool _prevCursorVisible;
        CursorLockMode _prevCursorLock;
        Rect _window = new Rect(40, 40, 580, 760);
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
            if (Plugin.CommandKey.Pressed()) CommandBar.Toggle();
            // typing in the command bar must not set off the other shortcuts
            if (CommandBar.Open) { GameStates.Update(); return; }
            if (Plugin.MenuKey.Pressed()) SetOpen(!_open);
            if (Plugin.ThirdPersonKey.Pressed()) ThirdPerson.Toggle();
            if (Plugin.ItemEspKey.Pressed()) ItemEsp.Toggle();
            if (Plugin.FreecamKey.Pressed()) Freecam.Toggle();
            if (Plugin.NoclipKey.Pressed()) Noclip.Toggle();
            if (Plugin.PickKey.Pressed())
            {
                // pressing it while holding something lets go of it, otherwise it picks
                if (!SceneEditor.DropHeld() && !DevView.DropHeld())
                {
                    SceneEditor.PickUnderCrosshair();
                    if (DevView.Active && SceneEditor.Selected != null) DevView.Select(SceneEditor.Selected);
                }
            }
            if (Plugin.DevViewKey.Pressed()) DevView.Toggle();
            if (Plugin.PhotoKey.Pressed()) PhotoMode.Toggle();
            if (Plugin.HideUiKey.Pressed()) WorldTab.ToggleHud();
            if (Plugin.DevMenuKey.Pressed())
            {
                if (_open && _tab == DevTab) SetOpen(false);
                else { _tab = DevTab; if (!_open) SetOpen(true); }
            }

            if (!_open && !Freecam.Active && InputHelper.Down(KeyCode.Backspace) && GameStates.Has("GodModeState"))
                GameStates.BackToTeenView();

            GameStates.Update();
            Freecam.Update();
            Noclip.Update();
            CupEsp.Update();
            SceneEditor.Update();
            CharacterCatalog.Update();
            NpcPreview.Update();
            IconSwapper.Update();
            LevelPopup.Update();
            ChoiceHints.Update();
            DevView.Update();
            SavesTab.Update();
            TripRewind.Update();
            PhotoMode.Update();
            LayoutShare.Update();
            MinigameHelpers.Update();
            ItemEsp.Update();
            TeenPicker.Update();
            ThirdPerson.Update();
            Skips.Update();
            Game.Try("Height", Height.Update);
            SavedObjects.Update();
            try { AssetBrowser.Update(); } catch (System.Exception e) { Plugin.Log.LogWarning("Asset export: " + e.Message); }
            CursorFix();
        }

        void LateUpdate()
        {
            Freecam.LateUpdate();
            ThirdPerson.LateUpdate();
            Poses.LateUpdate();
            if (_open || CommandBar.Open || DevView.Active || (PhotoMode.Active && PhotoMode.PanelVisible))
            {
                Cursor.visible = true;
                Cursor.lockState = CursorLockMode.None;
                _showingCursor = true;
            }
            else if (_showingCursor)
            {
                // dev view, photo mode or the command bar just closed: give the mouse back to the game
                _showingCursor = false;
                Game.Try("Cursor restore", () => GameStates.Stack?.ForceUpdateStateOverride());
                _cursorFixUntil = Time.unscaledTime + 1f;
            }
        }

        bool _showingCursor;

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
                // let the game put its own mouse setting back, and keep checking for a moment
                Game.Try("Cursor restore", () => GameStates.Stack?.ForceUpdateStateOverride());
                _cursorFixUntil = Time.unscaledTime + 1f;
            }
        }

        float _cursorFixUntil;

        // The game only locks the mouse when its state changes, so after our window closes we
        // copy its wish over for a short while in case something else showed the cursor.
        void CursorFix()
        {
            if (_open || Time.unscaledTime > _cursorFixUntil) return;
            try
            {
                var stack = GameStates.Stack;
                if (stack == null) return;
                bool lockIt = stack.LastOverride._lockMouse;
                Cursor.lockState = lockIt ? CursorLockMode.Locked : CursorLockMode.None;
                Cursor.visible = !lockIt;
            }
            catch { }
        }

        void OnGUI()
        {
            // clean frame for photos
            if (PhotoMode.HideEverything) return;
            CupEsp.Draw();
            ItemEsp.Draw();

            float s = Mathf.Clamp(Plugin.MenuScale.Value, 0.5f, 3f);
            if (Math.Abs(s - 1f) > 0.01f)
                GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));

            if (_toast != null && Time.unscaledTime < _toastUntil)
            {
                var tr = new Rect(Screen.width / s / 2f - 220, 20, 440, 26);
                GUI.Box(tr, "");
                GUI.Box(tr, _toast);
            }

            LevelPopup.Draw();
            ChoiceHints.Draw();
            FactPopups.Draw(s);
            MinigameHelpers.Draw();
            CommandBar.Draw(s);
            DevView.Draw(s);
            PhotoMode.Draw(s);

            if (Noclip.Active && !_open)
                GUI.Label(new Rect(10, Screen.height / s - 26, 900, 24), $"Noclip  |  WASD fly where you look  Space/E up  Q down  Shift fast  Alt slow  Scroll speed ({Noclip.Speed:0.0})  {Plugin.NoclipKey} to stop");

            if (Freecam.Active && !_open && !DevView.Active && !PhotoMode.Active)
                GUI.Label(new Rect(10, Screen.height / s - 26, 900, 24), "Freecam  |  WASD move  Q/E down/up  Shift fast  Ctrl slow  Scroll speed  " + Plugin.FreecamKey + " or Backspace for teen view");

            if (!_open) return;

            HandleDrag(s);

            // drawn twice so the background is less see through
            GUI.Box(_window, "");
            GUI.Box(_window, "");
            GUI.Box(new Rect(_window.x, _window.y, _window.width, Gui.LineH), $"{Plugin.Name} {Plugin.Version}   ({Plugin.MenuKey} to close, drag here to move)");

            var inner = new Rect(_window.x + 8, _window.y + Gui.LineH + 6, _window.width - 16, _window.height - Gui.LineH - 14);
            Gui.Begin(inner);

            // two rows of tabs
            _tab = Gui.Tabs(TabNames, _tab, Gui.TabKind.Main, 6);

            try
            {
                switch (_tab)
                {
                    case 0: StoryTab.Draw(); break;
                    case 1: LevelsTab.Draw(); break;
                    case 2: TeenTab.Draw(); break;
                    case 3: AbilitiesTab.Draw(); break;
                    case 4: ItemsTab.Draw(); break;
                    case 5: SavesTab.Draw(); break;
                    case 6: WorldTab.Draw(); break;
                    case 7: TravelTab.Draw(); break;
                    case 8: Freecam.Draw(); break;
                    case 9: Spawner.Draw(); break;
                    case 10: SceneEditor.Draw(); break;
                    case 11: DevMenuTab.Draw(); break;
                    case 12: Skips.Draw(); break;
                }
            }
            catch (Exception e)
            {
                Gui.Label("This tab hit an error: " + e.GetType().Name);
                Gui.Label(e.Message);
            }

            if (!DevView.Active) try { NpcPreview.Draw(_window); } catch { }
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

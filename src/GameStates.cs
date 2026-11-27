using System;
using System.Collections.Generic;
using System.Text;
using BlueEyes.Entities;
using Cinemachine;
using Il2CppInterop.Runtime;
using UnityEngine;
using AppStack = Tool7.ApplicationStack.ApplicationStack;
using AppState = Tool7.ApplicationStack.ApplicationState;
using DevMenu = Tool7.DebugMenu.DebugMenu;

namespace Road96Toolkit
{
    // Watches the game's application state stack, and talks to the developer tools left in the build
    // (the debug menu and the built in fly mode).
    internal static class GameStates
    {
        static float _nextPoll;
        static string _lastStates = "";

        public static AppStack Stack
        {
            get
            {
                try
                {
                    var s = AppStack.Instance;
                    return s == null ? null : s;
                }
                catch { return null; }
            }
        }

        public static List<AppState> States()
        {
            var res = new List<AppState>();
            var s = Stack;
            if (s == null) return res;
            try
            {
                var list = s._stateStack;
                if (list == null) return res;
                for (int i = 0; i < list.Count; i++)
                    if (list[i] != null) res.Add(list[i]);
            }
            catch { }
            return res;
        }

        public static string NameOf(AppState st)
        {
            try { return st.GetIl2CppType().Name; } catch { return "?"; }
        }

        public static bool Has(string typeName)
        {
            foreach (var st in States())
                if (NameOf(st) == typeName) return true;
            return false;
        }

        public static string Describe()
        {
            var sb = new StringBuilder();
            foreach (var st in States())
            {
                if (sb.Length > 0) sb.Append(" > ");
                sb.Append(NameOf(st));
            }
            return sb.Length == 0 ? "(none)" : sb.ToString();
        }

        public static void Update()
        {
            if (Time.unscaledTime < _nextPoll) return;
            _nextPoll = Time.unscaledTime + 0.5f;
            var now = Describe();
            if (Plugin.BlockGameFlyMode.Value && now.Contains("GodModeState"))
            {
                int n = StopGameFlyMode();
                Plugin.Log.LogInfo($"[states] the game's own fly mode switched on by itself, turned it off ({n})");
                ToolkitBehaviour.Toast("Blocked the game's built in fly mode");
            }
            if (!Plugin.LogStates.Value) return;
            if (now != _lastStates)
            {
                _lastStates = now;
                Plugin.Log.LogInfo("[states] " + now);
            }
        }

        // The game ships a developer fly mode (GodModeState). If it ever turns on, this switches it off.
        public static int StopGameFlyMode()
        {
            int removed = 0;
            var stack = Stack;
            if (stack == null) return 0;
            foreach (var st in States())
            {
                if (NameOf(st) != "GodModeState") continue;
                if (Game.Try("Stop fly mode", () => stack.RemoveState(st))) removed++;
            }
            Game.Try("Reset fly mode owner", () =>
            {
                foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<GodModeDebug>()))
                {
                    var g = o.TryCast<GodModeDebug>();
                    if (g != null) g._currentState = null;
                }
            });
            return removed;
        }

        // Puts the camera back behind the teen's eyes, whatever turned it loose.
        public static void BackToTeenView()
        {
            Freecam.Disable();
            Noclip.Disable();
            int fly = StopGameFlyMode();

            Game.Try("Camera brain", () =>
            {
                var cam = Game.MainCamera;
                if (cam == null) return;
                var brain = cam.GetComponent<CinemachineBrain>();
                if (brain != null) brain.enabled = true;
            });

            var p = Game.Player;
            if (p != null)
            {
                Game.Try("Unfreeze", () =>
                {
                    p.Freeze(false, PlayerBlueEyes.FreezeOrigin.GodMode);
                    if (!ToolkitBehaviour.IsOpen) p.Freeze(false, PlayerBlueEyes.FreezeOrigin.Debug);
                });
            }
            ToolkitBehaviour.Toast(fly > 0 ? "Back to teen view (game fly mode was on)" : "Back to teen view");
        }

        static DevMenu FindDevMenu()
        {
            try
            {
                var dm = DevMenu.Instance;
                if (dm != null) return dm;
            }
            catch { }
            try
            {
                foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<DevMenu>()))
                {
                    var dm = o.TryCast<DevMenu>();
                    if (dm != null && dm.gameObject.scene.IsValid()) return dm;
                }
            }
            catch { }
            return null;
        }

        public static void DumpInfo()
        {
            var log = Plugin.Log;
            log.LogInfo("==== Road96 Toolkit info ====");
            log.LogInfo("States: " + Describe());
            Game.Try("Info scenes", () =>
            {
                for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
                {
                    var sc = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                    log.LogInfo($"Scene {i}: {sc.name} loaded={sc.isLoaded}");
                }
            });
            Game.Try("Info dev menu", () =>
            {
                var all = Resources.FindObjectsOfTypeAll(Il2CppType.Of<DevMenu>());
                log.LogInfo($"DebugMenu objects: {all.Length}");
                foreach (var o in all)
                {
                    var dm = o.TryCast<DevMenu>();
                    if (dm == null) continue;
                    log.LogInfo($"  {dm.gameObject.name} scene={dm.gameObject.scene.name} active={dm.gameObject.activeInHierarchy} visible={dm.Visible}");
                    var root = dm.Root;
                    if (root != null) DumpElement(root, 1, 0);
                }
            });
            Game.Try("Info controllers", () =>
            {
                log.LogInfo($"DebugMenuController objects: {Resources.FindObjectsOfTypeAll(Il2CppType.Of<DebugMenuController>()).Length}");
                foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<GodModeDebug>()))
                {
                    var g = o.TryCast<GodModeDebug>();
                    if (g == null) continue;
                    log.LogInfo($"GodModeDebug {g.gameObject.name} scene={g.gameObject.scene.name} enabled={g.enabled} active={g.gameObject.activeInHierarchy} state={(g._currentState != null)}");
                }
            });
            log.LogInfo("==== end ====");
        }

        static int _dumped;

        static void DumpElement(Tool7.DebugMenu.DebugMenuElements.DebugMenuElement e, int depth, int index)
        {
            if (_dumped > 400 || depth > 6) return;
            _dumped++;
            string name = "?";
            Game.Try("Element name", () => name = e._elementName);
            Plugin.Log.LogInfo(new string(' ', depth * 2) + name);
            Game.Try("Element children", () =>
            {
                var kids = e._childs;
                if (kids == null) return;
                for (int i = 0; i < kids.Count; i++) DumpElement(kids[i], depth + 1, i);
            });
        }
    }
}

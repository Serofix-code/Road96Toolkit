using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using UnityEngine;

namespace Road96Toolkit
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInProcess("Road 96.exe")]
    public class Plugin : BasePlugin
    {
        public const string Guid = "serofix.road96toolkit";
        public const string Name = "Road96 Toolkit";
        public const string Version = "1.0.0";

        internal static new ManualLogSource Log;
        internal static Plugin Instance;

        internal static ConfigEntry<KeyCode> MenuKey;
        internal static ConfigEntry<KeyCode> FreecamKey;
        internal static ConfigEntry<KeyCode> PickKey;
        internal static ConfigEntry<KeyCode> HideUiKey;
        internal static ConfigEntry<bool> FreezeWithMenu;
        internal static ConfigEntry<float> MenuScale;

        public override void Load()
        {
            Instance = this;
            Log = base.Log;

            MenuKey = Config.Bind("Keys", "Menu", KeyCode.F1, "Open or close the toolkit window");
            FreecamKey = Config.Bind("Keys", "Freecam", KeyCode.F2, "Toggle the free camera");
            PickKey = Config.Bind("Keys", "PickObject", KeyCode.F3, "Select whatever is under the screen center for the scene editor");
            HideUiKey = Config.Bind("Keys", "HideHud", KeyCode.F4, "Hide or show the game HUD");
            FreezeWithMenu = Config.Bind("General", "FreezePlayerWhileMenuOpen", true, "Stop the player from walking and looking around while the window is open");
            MenuScale = Config.Bind("General", "MenuScale", 1f, "Scale of the window, 1 is normal, try 1.5 on 4K screens");

            AddComponent<ToolkitBehaviour>();
            Log.LogInfo($"{Name} {Version} loaded. Press {MenuKey.Value} in game.");
        }
    }
}

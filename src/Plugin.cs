using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;

namespace Road96Toolkit
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInProcess("Road 96.exe")]
    public class Plugin : BasePlugin
    {
        public const string Guid = "serofix.road96toolkit";
        public const string Name = "Road96 Toolkit";
        public const string Version = "1.3.0";

        internal static new ManualLogSource Log;
        internal static Plugin Instance;

        internal static Hotkey MenuKey, FreecamKey, NoclipKey, PickKey, HideUiKey, DevMenuKey;
        internal static ConfigEntry<bool> FreezeWithMenu;
        internal static ConfigEntry<float> MenuScale;
        internal static ConfigEntry<bool> LogStates;
        internal static ConfigEntry<bool> BlockGameFlyMode;
        internal static ConfigEntry<bool> CupEsp;

        static bool _upgradeKeys;

        static Hotkey BindKey(ConfigFile cfg, string name, string def, string desc, string oldDefault = null)
        {
            var entry = cfg.Bind("Shortcuts", name, def, desc + ". Examples: F1, Ctrl+F2, Shift+Alt+K. Leave empty to turn it off.");
            // older versions used other default keys, move people over unless they picked their own
            if (_upgradeKeys && oldDefault != null && string.Equals(entry.Value?.Trim(), oldDefault, StringComparison.OrdinalIgnoreCase))
                entry.Value = def;
            var key = Hotkey.Parse(entry.Value);
            entry.SettingChanged += (_, _) =>
            {
                var fresh = Hotkey.Parse(entry.Value);
                key.Key = fresh.Key; key.Ctrl = fresh.Ctrl; key.Shift = fresh.Shift; key.Alt = fresh.Alt;
            };
            return key;
        }

        public override void Load()
        {
            Instance = this;
            Log = base.Log;

            var cfgVersion = Config.Bind("General", "ConfigVersion", 1, "Used by the toolkit to update old settings, leave it alone");
            _upgradeKeys = cfgVersion.Value < 2;
            cfgVersion.Value = 2;

            MenuKey = BindKey(Config, "Menu", "F1", "Open or close the toolkit window");
            FreecamKey = BindKey(Config, "Freecam", "F2", "Turn the free camera on or off", "Ctrl+F2");
            NoclipKey = BindKey(Config, "Noclip", "F3", "Turn noclip on or off (fly the teen through walls)", "Ctrl+F3");
            PickKey = BindKey(Config, "PickObject", "F5", "Select whatever is in the middle of the screen for the scene editor", "F3");
            HideUiKey = BindKey(Config, "HideHud", "F4", "Hide or show the game HUD");
            DevMenuKey = BindKey(Config, "DeveloperMenu", "F6", "Open the game's own developer debug menu");

            FreezeWithMenu = Config.Bind("General", "FreezePlayerWhileMenuOpen", true, "Stop the player from walking and looking around while the window is open");
            MenuScale = Config.Bind("General", "MenuScale", 1f, "Scale of the window, 1 is normal, try 1.5 on 4K screens");
            LogStates = Config.Bind("General", "LogGameStates", true, "Write the game's state changes to the BepInEx log, handy when reporting bugs");

            BlockGameFlyMode = Config.Bind("General", "BlockGameFlyMode", true, "The game has a hidden developer fly mode that can switch on by itself. Keep this on to stop it, the toolkit freecam still works");

            CupEsp = Config.Bind("Cheats", "CupGameBallEsp", true, "Show which cup hides the ball in the shell game");

            AddComponent<ToolkitBehaviour>();
            Log.LogInfo($"{Name} {Version} loaded. Press {MenuKey} in game.");
        }
    }
}

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
        public const string Version = "1.6.0";

        internal static new ManualLogSource Log;
        internal static Plugin Instance;

        internal static Hotkey MenuKey, FreecamKey, NoclipKey, PickKey, HideUiKey, DevMenuKey;
        internal static ConfigEntry<bool> FreezeWithMenu;
        internal static ConfigEntry<float> MenuScale;
        internal static ConfigEntry<bool> LogStates;
        internal static ConfigEntry<bool> BlockGameFlyMode;
        internal static ConfigEntry<bool> CupEsp;
        internal static ConfigEntry<float> PreviewExposure;
        internal static ConfigEntry<bool> ChoiceHints;
        internal static ConfigEntry<bool> LevelNamePopup;
        internal static ConfigEntry<float> LevelNamePopupSeconds;
        internal static Hotkey DevViewKey;
        internal static ConfigEntry<bool> AutoBackup;
        internal static Hotkey PhotoKey;
        internal static ConfigEntry<bool> PhotoPause;
        internal static ConfigEntry<bool> PhotoRememberLast;
        internal static ConfigEntry<bool> PhotoLookMovesCamera;
        internal static ConfigEntry<int> AutoBackupCount;
        internal static ConfigEntry<bool> PreviewOnHover;

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
            PhotoKey = BindKey(Config, "PhotoMode", "F8", "Photo mode: pause, free camera, effects and screenshots");
            DevViewKey = BindKey(Config, "DevView", "F7", "Dev view: freecam with a spawn palette, click to place and edit characters");

            FreezeWithMenu = Config.Bind("General", "FreezePlayerWhileMenuOpen", true, "Stop the player from walking and looking around while the window is open");
            MenuScale = Config.Bind("General", "MenuScale", 1f, "Scale of the window, 1 is normal, try 1.5 on 4K screens");
            LogStates = Config.Bind("General", "LogGameStates", true, "Write the game's state changes to the BepInEx log, handy when reporting bugs");

            BlockGameFlyMode = Config.Bind("General", "BlockGameFlyMode", true, "The game has a hidden developer fly mode that can switch on by itself. Keep this on to stop it, the toolkit freecam still works");

            PreviewExposure = Config.Bind("Preview", "Exposure", 7f, "Exposure of the 3D character preview. Higher is darker");
            PreviewOnHover = Config.Bind("Preview", "ShowOnHover", true, "Show the 3D preview when the mouse rests on a character in the spawner list");
            ChoiceHints = Config.Bind("Cheats", "ChoiceHints", false, "Show what every dialogue choice does (karma, vote, money, odds), which one is best, and keypad codes");
            LevelNamePopup = Config.Bind("General", "LevelNamePopup", true, "Show the name of a level for a few seconds after it loads");
            LevelNamePopupSeconds = Config.Bind("General", "LevelNamePopupSeconds", 4f, "How long the level name stays on screen");
            PhotoRememberLast = Config.Bind("PhotoMode", "RememberLastOptions", true, "Open photo mode with the options you used last time");
            PhotoLookMovesCamera = Config.Bind("PhotoMode", "LooksSetFovAndRoll", false, "Loading a saved look also sets the field of view and roll");
            PhotoPause = Config.Bind("PhotoMode", "PauseGame", true, "Pause the game when photo mode opens");
            AutoBackup = Config.Bind("Saves", "AutoBackup", true, "Copy the save every time the game writes it, to BepInEx/config/Road96Toolkit/saves/auto");
            AutoBackupCount = Config.Bind("Saves", "AutoBackupCount", 15, "How many automatic backups to keep");
            SavesTab.ApplyPendingRestore();
            CupEsp = Config.Bind("Cheats", "CupGameBallEsp", true, "Show which cup hides the ball in the shell game");

            AddComponent<ToolkitBehaviour>();
            Log.LogInfo($"{Name} {Version} loaded. Press {MenuKey} in game.");
        }
    }
}

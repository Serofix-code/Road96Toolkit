using System;
using System.Collections.Generic;
using UnityEngine;
using NB = BlueEyes.NarrativeBrain;

namespace Road96Toolkit
{
    // Money, energy, time of day, area and a few other trip values.
    internal static class WorldTab
    {
        static bool _hudHidden;
        static readonly int[] Amounts = { 1, 5, 20, 100, 500, 1000 };
        static int _custom = 50;

        static int Step(int v, bool up)
        {
            int b = up ? v : v - 1;
            if (b < 10) return 1;
            if (b < 100) return 10;
            if (b < 1000) return 50;
            if (b < 10000) return 500;
            return 5000;
        }
        static readonly List<Canvas> _hidden = new List<Canvas>();

        public static void Draw()
        {
            var ctx = Game.Context;
            var settings = Game.Settings;
            if (ctx == null || settings == null)
            {
                Gui.Label("Load into the game first.");
                return;
            }

            int money = 0, days = 0, dist = 0;
            float energy = 0, karma = 0, maxEnergy = 0;
            string tod = "?", env = "?";
            Game.Try("Read world", () =>
            {
                money = ctx._currentMoney;
                energy = ctx._currentEnergy;
                maxEnergy = ctx.MaxEnergy(settings);
                karma = ctx._currentKarma;
                days = ctx._daysRemaining;
                dist = ctx._distanceRemaining;
                tod = ctx._currentTimeOfDay.ToString();
                env = ctx._currentEnvironment.ToString();
            });

            Gui.Header($"Money giver   (you have ${money})");
            var r = Gui.Row(1f, 1f, 1f, 1f, 1f, 1f);
            for (int i = 0; i < Amounts.Length; i++)
            {
                int amt = Amounts[i];
                if (GUI.Button(r[i], "+" + amt)) Game.Try("Money", () => ctx.AddMoney(amt));
            }
            r = Gui.Row(2f, 1f, 1f, 1.4f, 1.4f);
            GUI.Label(r[0], $"Custom amount: ${_custom}");
            if (GUI.Button(r[1], "-")) _custom = Math.Max(0, _custom - Step(_custom, false));
            if (GUI.Button(r[2], "+")) _custom = Math.Min(999999, _custom + Step(_custom, true));
            if (GUI.Button(r[3], "Give it")) Game.Try("Money", () => ctx.AddMoney(_custom));
            if (GUI.Button(r[4], "Set to it")) Game.Try("Money", () =>
            {
                int diff = _custom - ctx._currentMoney;
                if (diff > 0) ctx.AddMoney(diff); else if (diff < 0) ctx.RemoveMoney(-diff);
            });
            if (Gui.Button("Take all my money")) Game.Try("Money", () => ctx.RemoveMoney(ctx._currentMoney));

            Gui.Space();
            Gui.Header("Trip");

            r = Gui.Row(2f, 1f, 1f, 1f);
            GUI.Label(r[0], $"Energy: {energy:0.#} / {maxEnergy:0.#}");
            if (GUI.Button(r[1], "-1")) Game.Try("Energy", () => ctx.ImpactEnergy(-1f, settings, true));
            if (GUI.Button(r[2], "+1")) Game.Try("Energy", () => ctx.ImpactEnergy(1f, settings, true));
            if (GUI.Button(r[3], "Full")) Game.Try("Energy", () => ctx.ImpactEnergy(maxEnergy - ctx._currentEnergy, settings, true));

            r = Gui.Row(2f, 1f, 1f, 1f);
            GUI.Label(r[0], $"Karma: {karma:0.00}");
            if (GUI.Button(r[1], "-")) Game.Try("Karma", () => ctx.SetKarma(ctx._currentKarma - 0.1f));
            if (GUI.Button(r[2], "+")) Game.Try("Karma", () => ctx.SetKarma(ctx._currentKarma + 0.1f));
            if (GUI.Button(r[3], "Max")) Game.Try("Karma", () => ctx.SetKarma(1f));

            r = Gui.Row(2f, 1f, 1f, 1f);
            GUI.Label(r[0], $"Days left before vote: {days}");
            if (GUI.Button(r[1], "-1")) Game.Try("Days", () => ctx._daysRemaining = Math.Max(0, ctx._daysRemaining - 1));
            if (GUI.Button(r[2], "+1")) Game.Try("Days", () => ctx._daysRemaining = ctx._daysRemaining + 1);
            if (GUI.Button(r[3], "Pass a day")) Game.Try("Days", () => ctx.PassADay(true, 1));

            r = Gui.Row(2f, 1f, 1f, 1f);
            GUI.Label(r[0], $"Distance to border: {dist}");
            if (GUI.Button(r[1], "-100")) Game.Try("Distance", () => ctx.MoveDistance(100));
            if (GUI.Button(r[2], "+100")) Game.Try("Distance", () => ctx.MoveDistance(-100));
            if (GUI.Button(r[3], "At border")) Game.Try("Distance", () => ctx.MoveDistance(ctx._distanceRemaining));

            Gui.Space();
            Gui.Header("Time and place (used by the next scenario)");
            r = Gui.Row(1.4f, 1f, 1f, 1f);
            GUI.Label(r[0], "Time: " + tod);
            if (GUI.Button(r[1], "Day")) Game.Try("Time", () => ctx.SetTimeOfDay(NB.TimeOfDay.Day, true));
            if (GUI.Button(r[2], "Night")) Game.Try("Time", () => ctx.SetTimeOfDay(NB.TimeOfDay.Night, true));
            if (GUI.Button(r[3], "Any")) Game.Try("Time", () => ctx.SetTimeOfDay(NB.TimeOfDay.Anytime, true));

            r = Gui.Row(1.4f, 1f, 1f, 1f);
            GUI.Label(r[0], "Area: " + env);
            if (GUI.Button(r[1], "Desert")) Game.Try("Area", () => ctx.SetEnvironment(NB.Environment.Desert));
            if (GUI.Button(r[2], "Forest")) Game.Try("Area", () => ctx.SetEnvironment(NB.Environment.Forest));
            if (GUI.Button(r[3], "Any")) Game.Try("Area", () => ctx.SetEnvironment(NB.Environment.Any));

            Gui.Space();
            Gui.Header("Game");
            r = Gui.Row(4);
            GUI.Label(r[0], $"Speed: {Time.timeScale:0.##}x");
            if (GUI.Button(r[1], "Pause")) Time.timeScale = 0f;
            if (GUI.Button(r[2], "Normal")) { Time.timeScale = 1f; FixSound(false); }
            if (GUI.Button(r[3], "2x")) { Time.timeScale = 2f; FixSound(false); }

            Gui.Space();
            Gui.Header("Helpers");
            if (Gui.Toggle("Show the ball in the cup game" + (CupEsp.Found > 0 ? "  (game found)" : ""), Plugin.CupEsp.Value))
                Plugin.CupEsp.Value = !Plugin.CupEsp.Value;
            if (Gui.Toggle("Choice hints: what each answer does, the best one, keypad codes", Plugin.ChoiceHints.Value))
                Plugin.ChoiceHints.Value = !Plugin.ChoiceHints.Value;
            if (Gui.Toggle("Show the level name when a level loads", Plugin.LevelNamePopup.Value))
                Plugin.LevelNamePopup.Value = !Plugin.LevelNamePopup.Value;

            Gui.Space();
            Gui.Header("Sound");
            var snd = Gui.Row(1f, 1f);
            if (GUI.Button(snd[0], "Fix sound")) FixSound(true);
            if (GUI.Button(snd[1], "Sound info to log")) SoundInfo();

            if (Gui.Toggle("Hide game HUD (" + Plugin.HideUiKey + ")", _hudHidden)) ToggleHud();

            if (Gui.Button("Open the developer debug menu (if it exists in this build)"))
            {
                Game.Try("Debug menu", () =>
                {
                    var dm = Tool7.DebugMenu.DebugMenu.Instance;
                    if (dm == null) { ToolkitBehaviour.Toast("Debug menu is not in this build"); return; }
                    dm.ToggleVisible();
                });
            }
        }

        // The game pauses all audio while the game speed is 0, and does not always undo it.
        public static void FixSound(bool loud)
        {
            Game.Try("Fix sound", () =>
            {
                if (Time.timeScale <= 0f) Time.timeScale = 1f;
                AudioListener.pause = false;
                if (AudioListener.volume < 0.01f) AudioListener.volume = 1f;

                // there must be exactly one listener switched on
                var all = UnityEngine.Object.FindObjectsOfType<AudioListener>();
                int on = 0;
                foreach (var l in all) if (l != null && l.isActiveAndEnabled) on++;
                if (on == 0)
                {
                    var cam = Game.MainCamera;
                    var l = cam != null ? cam.GetComponent<AudioListener>() : null;
                    if (l == null && cam != null) l = cam.gameObject.AddComponent<AudioListener>();
                    if (l != null) l.enabled = true;
                }
                GameStates.Stack?.ForceUpdateStateOverride();
            });
            if (loud) { SoundInfo(); ToolkitBehaviour.Toast("Sound reset. Still silent? Check the Windows volume mixer for Road 96"); }
        }

        static void SoundInfo()
        {
            Game.Try("Sound info", () =>
            {
                var log = Plugin.Log;
                log.LogInfo($"[sound] timeScale={Time.timeScale} paused={AudioListener.pause} volume={AudioListener.volume} output={UnityEngine.AudioSettings.outputSampleRate}Hz speakers={UnityEngine.AudioSettings.speakerMode}");
                foreach (var l in UnityEngine.Object.FindObjectsOfType<AudioListener>())
                    log.LogInfo($"[sound] listener on {l.gameObject.name} enabled={l.isActiveAndEnabled}");
                var am = BlueEyes.Audio.AudioManager.Instance;
                if (am != null)
                {
                    log.LogInfo($"[sound] snapshots on stack: {am._snapshotStack?.Count}");
                    var mixer = am._mixer;
                    if (mixer != null)
                        foreach (var p in new[] { "MasterVolume", "Master", "MusicVolume", "SFXVolume", "VoiceVolume" })
                            if (mixer.GetFloat(p, out float v)) log.LogInfo($"[sound] mixer {p} = {v} dB");
                }
                log.LogInfo("[sound] game override: " + GameStates.Stack?.LastOverride.ToDebugString());
            });
        }

        public static void ToggleHud()
        {
            if (!_hudHidden)
            {
                _hidden.Clear();
                Game.Try("Hide HUD", () =>
                {
                    var all = UnityEngine.Object.FindObjectsOfType<Canvas>();
                    foreach (var c in all)
                    {
                        if (c != null && c.enabled && c.isRootCanvas)
                        {
                            c.enabled = false;
                            _hidden.Add(c);
                        }
                    }
                });
                _hudHidden = true;
            }
            else
            {
                foreach (var c in _hidden)
                    Game.Try("Show HUD", () => { if (c != null) c.enabled = true; });
                _hidden.Clear();
                _hudHidden = false;
            }
        }
    }
}

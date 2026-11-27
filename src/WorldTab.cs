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

            Gui.Header("Trip");
            var r = Gui.Row(2f, 1f, 1f, 1f);
            GUI.Label(r[0], $"Money: ${money}");
            if (GUI.Button(r[1], "+10")) Game.Try("Money", () => ctx.AddMoney(10));
            if (GUI.Button(r[2], "+100")) Game.Try("Money", () => ctx.AddMoney(100));
            if (GUI.Button(r[3], "Zero")) Game.Try("Money", () => ctx.RemoveMoney(ctx._currentMoney));

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
            if (GUI.Button(r[2], "Normal")) Time.timeScale = 1f;
            if (GUI.Button(r[3], "2x")) Time.timeScale = 2f;

            if (Gui.Toggle("Hide game HUD (" + Plugin.HideUiKey.Value + ")", _hudHidden)) ToggleHud();

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

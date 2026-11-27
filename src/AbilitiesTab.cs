using System;
using UnityEngine;

namespace Road96Toolkit
{
    // The skills you learn from the characters (lockpicking, hacking and so on).
    internal static class AbilitiesTab
    {
        static int _savedMax = -1;

        public static void Draw()
        {
            var ctx = Game.Context;
            var settings = Game.Settings;
            if (ctx == null || settings == null)
            {
                Gui.Label("Load into the game first.");
                return;
            }

            var all = settings._abilities;
            if (all == null)
            {
                Gui.Label("No ability list found.");
                return;
            }

            int max = 0;
            Game.Try("Read max abilities", () => max = settings._maxAbilitiesPerRun);
            Gui.Label($"Abilities allowed per trip: {max}");
            if (Gui.Toggle("No limit on abilities per trip", max >= 99))
            {
                Game.Try("Ability limit", () =>
                {
                    if (settings._maxAbilitiesPerRun >= 99)
                        settings._maxAbilitiesPerRun = _savedMax > 0 ? _savedMax : 3;
                    else
                    {
                        _savedMax = settings._maxAbilitiesPerRun;
                        settings._maxAbilitiesPerRun = 99;
                    }
                });
            }

            var row = Gui.Row(2);
            if (GUI.Button(row[0], "Give all"))
            {
                for (int i = 0; i < all.Length; i++)
                {
                    var a = all[i];
                    if (a == null) continue;
                    Game.Try("Give ability", () => { if (!ctx.HasAbility(a.ID)) ctx.AddAbility(a.ID); });
                }
                ToolkitBehaviour.Toast("All abilities unlocked");
            }
            if (GUI.Button(row[1], "Remove all"))
            {
                for (int i = 0; i < all.Length; i++)
                {
                    var a = all[i];
                    if (a == null) continue;
                    Game.Try("Remove ability", () => { if (ctx.HasAbility(a.ID)) ctx.RemoveAbility(a.ID); });
                }
            }
            Gui.Space();

            for (int i = 0; i < all.Length; i++)
            {
                var a = all[i];
                if (a == null) continue;
                string id = "?";
                bool has = false;
                Game.Try("Read ability", () => { id = a.ID; has = ctx.HasAbility(id); });
                if (Gui.Toggle(id, has))
                {
                    Game.Try("Toggle ability", () =>
                    {
                        if (has) ctx.RemoveAbility(id); else ctx.AddAbility(id);
                    });
                }
            }

            Gui.Space();
            Gui.Label("Abilities open extra options at the border and in some scenes.");
        }
    }
}

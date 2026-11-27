using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using NB = BlueEyes.NarrativeBrain;

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
            DrawSpecial(ctx, all);
            Gui.Space();
            Gui.Label("Abilities open extra options at the border and in some scenes.");
        }

        static List<string> _special;

        // Abilities that exist in the game but are not in the normal list, like the OMEN one
        // that is normally only given out through HP's OMEN Gaming Hub.
        static void DrawSpecial(NB.NarrativeContext ctx, Il2CppReferenceArray<BlueEyes.Inventory.InventoryItem> normal)
        {
            Gui.Header("Special abilities");
            if (_special == null || Gui.Button("Rescan special abilities"))
            {
                _special = new List<string>();
                var known = new HashSet<string>();
                for (int i = 0; i < normal.Length; i++)
                    if (normal[i] != null) Game.Try("Known ability", () => known.Add(normal[i].ID));
                Game.Try("Find abilities", () =>
                {
                    foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<BlueEyes.Abilities.Ability>()))
                    {
                        var a = o.TryCast<BlueEyes.Abilities.Ability>();
                        if (a == null) continue;
                        var id = a.ID;
                        if (!string.IsNullOrEmpty(id) && !known.Contains(id) && !_special.Contains(id)) _special.Add(id);
                    }
                });
                var settings = Game.Settings;
                Game.Try("Settings abilities", () =>
                {
                    var e = settings?._energyAbility;
                    if (!string.IsNullOrEmpty(e) && !known.Contains(e) && !_special.Contains(e)) _special.Add(e);
                    var np = settings?._narrativeProbabilityAbility;
                    if (np != null && !known.Contains(np.ID) && !_special.Contains(np.ID)) _special.Add(np.ID);
                });
                Plugin.Log.LogInfo("Special abilities: " + string.Join(", ", _special));
            }

            bool omen = false;
            Game.Try("Omen flag", () => omen = ctx._isOmenAbilityUnlock);
            if (Gui.Toggle("OMEN ability unlocked (normally needs HP OMEN Gaming Hub)", omen))
            {
                Game.Try("Omen flag", () =>
                {
                    ctx._isOmenAbilityUnlock = !omen;
                    NB.NarrativeContext.UNLOCK_OMEN_ABILITY_ASAP = !omen;
                });
            }

            if (_special.Count == 0)
            {
                Gui.Label("No extra abilities are loaded right now.");
                return;
            }
            if (Gui.Button("Give all special abilities"))
            {
                foreach (var id in _special)
                    Game.Try("Give special", () => { if (!ctx.HasAbility(id)) ctx.AddAbility(id); });
                Game.Try("Omen flag", () => ctx._isOmenAbilityUnlock = true);
                ToolkitBehaviour.Toast("Special abilities given");
            }
            foreach (var id in _special)
            {
                bool has = false;
                Game.Try("Read special", () => has = ctx.HasAbility(id));
                if (Gui.Toggle(id, has))
                    Game.Try("Toggle special", () => { if (has) ctx.RemoveAbility(id); else ctx.AddAbility(id); });
            }
        }
    }
}

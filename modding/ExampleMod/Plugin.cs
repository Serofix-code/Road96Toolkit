using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using UnityEngine;
using NB = BlueEyes.NarrativeBrain;

namespace ExampleMod
{
    // A small mod to start from:
    //   F8 gives you money
    //   F9 writes your current scenario, money and facts to the log
    //   every new fact is written to the log
    [BepInPlugin("yourname.road96examplemod", "Road96 Example Mod", "1.0.0")]
    [BepInProcess("Road 96.exe")]
    public class Plugin : BasePlugin
    {
        internal static new ManualLogSource Log;
        internal static ConfigEntry<int> MoneyAmount;

        public override void Load()
        {
            Log = base.Log;
            MoneyAmount = Config.Bind("General", "MoneyAmount", 50, "How much money F8 gives you");

            var harmony = new Harmony("yourname.road96examplemod");
            try
            {
                var t = typeof(NB.NarrativeContext);
                harmony.Patch(AccessTools.Method(t, nameof(NB.NarrativeContext.AddTag)),
                    prefix: new HarmonyMethod(typeof(Plugin), nameof(HadPrefix)),
                    postfix: new HarmonyMethod(typeof(Plugin), nameof(AddPostfix)));
            }
            catch (Exception e) { Log.LogWarning("Fact hook failed: " + e.Message); }

            // adding a MonoBehaviour from here also registers it with IL2CPP
            AddComponent<ExampleBehaviour>();
            Log.LogInfo("Example mod loaded");
        }

        static void HadPrefix(NB.NarrativeContext __instance, string __0, out bool __state)
        {
            __state = true;
            try { __state = __instance.HasTag(__0); } catch { }
        }

        static void AddPostfix(NB.NarrativeContext __instance, string __0, bool __state)
        {
            try
            {
                if (__state) return; // already had it
                var main = NB.NarrativeController.Instance?.CurrentContext;
                if (main == null || main.Pointer != __instance.Pointer) return; // a teen card's context, not yours
                Log.LogInfo("New fact: " + __0);
            }
            catch { }
        }
    }

    public class ExampleBehaviour : MonoBehaviour
    {
        // IL2CPP needs this constructor on every MonoBehaviour you make
        public ExampleBehaviour(IntPtr ptr) : base(ptr) { }

        void Update()
        {
            try
            {
                if (Input.GetKeyDown(KeyCode.F8)) GiveMoney();
                if (Input.GetKeyDown(KeyCode.F9)) PrintState();
            }
            catch (Exception e) { Plugin.Log.LogWarning(e.Message); }
        }

        static NB.NarrativeContext Context
        {
            get
            {
                var ctrl = NB.NarrativeController.Instance;
                return ctrl == null ? null : ctrl.CurrentContext;
            }
        }

        static void GiveMoney()
        {
            var ctx = Context;
            if (ctx == null) return;
            ctx.AddMoney(Plugin.MoneyAmount.Value);
            Plugin.Log.LogInfo($"Gave {Plugin.MoneyAmount.Value}, you have {ctx._currentMoney} now");
        }

        static void PrintState()
        {
            var ctx = Context;
            if (ctx == null) { Plugin.Log.LogInfo("No story loaded yet"); return; }

            Plugin.Log.LogInfo($"Trip {ctx.CurrentRuns}, scenario {ctx.CurrentBubbleID}");
            Plugin.Log.LogInfo($"Money {ctx._currentMoney}, karma {ctx._currentKarma:0.00}, days left {ctx._daysRemaining}");

            var tags = ctx._currentTags; // a game list, so loop with an index
            Plugin.Log.LogInfo($"{tags.Count} facts:");
            for (int i = 0; i < tags.Count; i++) Plugin.Log.LogInfo("  " + tags[i]);
        }
    }
}

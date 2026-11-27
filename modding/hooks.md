# Harmony hooks

BepInEx 6 comes with HarmonyX, and it can patch the game's methods even though they are native code. A few rules for IL2CPP:

* Patch with `harmony.Patch(AccessTools.Method(...), prefix: ..., postfix: ...)`. Attribute patches (`[HarmonyPatch]`) also work, but the manual way makes it easy to wrap each one in a `try/catch` so one broken hook does not stop your whole mod.
* Name parameters `__0`, `__1` and so on. The real names often do not match after interop.
* `__instance` and `__result` work as normal.
* If a method has more than one version (overloads), pass the parameter types: `AccessTools.Method(typeof(T), "Name", new[] { typeof(A), typeof(B) })`.
* A prefix that returns `false` skips the game's method.
* To pass something from prefix to postfix use `out T __state` in the prefix and `T __state` in the postfix.
* Hooks only fire when game code calls the method through IL2CPP. A method the compiler inlined will never hit your hook. If a hook does nothing, try the method that calls it.

## Hooks I know work

These are used in Road96 Toolkit and Road96 Mixtape. "Seen in game" means I saw it run in the log, the others build and patch with no errors but I have not watched every one fire.

| Method | Used for | Seen in game |
|---|---|---|
| `NarrativeProbability.RollDice(NarrativeContext, NarrativeSettings)` postfix | force dice rolls to pass or fail | yes |
| `NarrativeProbability.GetProbability(NarrativeContext, NarrativeSettings)` postfix | change the % shown and used | |
| `NarrativeProbability.GetProbability(float, float, NarrativeSettings, NarrativeContext)` postfix | same, other overload | |
| `CharacterSelectionMenu.OnCharaSelected()` postfix | know which teen card was picked | |
| `NarrativeContext.OnRunStarted(NarrativeSettings)` prefix and postfix | do something at the start of every trip | |
| `NarrativeContext.AddTag(string)` / `RemoveTag(string)` prefix and postfix | watch facts being gained and lost | |
| `LocalizationManager.Localize(string, bool)` prefix | add your own text keys | |
| `SaveManager.SaveContext(string, NarrativeContext, bool)` prefix and postfix | keep your own data out of the save | |

## Examples

Force every dice roll to pass:

```csharp
harmony.Patch(
    AccessTools.Method(typeof(NarrativeProbability), nameof(NarrativeProbability.RollDice)),
    postfix: new HarmonyMethod(typeof(MyPlugin), nameof(RollPostfix)));

static void RollPostfix(ref bool __result) => __result = true;
```

Log every new fact, only when it was really new:

```csharp
var t = typeof(NB.NarrativeContext);
harmony.Patch(AccessTools.Method(t, nameof(NB.NarrativeContext.AddTag)),
    prefix:  new HarmonyMethod(typeof(MyPlugin), nameof(HadPrefix)),
    postfix: new HarmonyMethod(typeof(MyPlugin), nameof(AddPostfix)));

static void HadPrefix(NB.NarrativeContext __instance, string __0, out bool __state)
{
    __state = __instance.HasTag(__0);
}

static void AddPostfix(NB.NarrativeContext __instance, string __0, bool __state)
{
    if (__state) return;                                               // already had it
    if (__instance.Pointer != NB.NarrativeController.Instance?.CurrentContext?.Pointer) return; // not the main context
    Log.LogInfo("New fact: " + __0);
}
```

Compare contexts with `.Pointer`. Two wrapper objects can point at the same game object, so `==` on them is not reliable.

# Making mods for Road 96

This folder is for anyone who wants to write their own Road 96 mod. It lists the game classes and calls I found while making Road96 Toolkit and Road96 Mixtape, with the exact names you need, plus a small example mod you can copy.

Everything here was read from the game's own code (the interop DLLs BepInEx makes), Steam version, Unity 2020.3, IL2CPP. If the game gets an update some names might change, so run the [TypeDump](TypeDump) tool to check.

## Pages

* [story.md](story.md): the story system. Facts, scenarios, endings, trips, money, karma, the election, dice rolls
* [items-tapes-audio.md](items-tapes-audio.md): items, cassette tapes, sound and game text
* [player-ui-saves.md](player-ui-saves.md): the player, pause menu, teen select screen, end of trip screen and save files
* [dialogue-phone.md](dialogue-phone.md): conversations, answers, the conditions that lock them, and payphone numbers
* [hooks.md](hooks.md): Harmony hooks that I know work, and how to write them for this game
* [ExampleMod](ExampleMod): a tiny mod that builds as is. Copy it to start your own
* [TypeDump](TypeDump): a small tool that lists every field and method of any game class, or dumps the whole game API to text files

## Setup

1. Install [BepInEx 6 IL2CPP](https://builds.bepinex.dev/projects/bepinex_be) (6.0.0-be.793 or newer, the `Unity.IL2CPP-win-x64` build) into the Road 96 folder.
2. Start the game once and wait for the main menu. BepInEx makes `BepInEx/interop`, which has a DLL for every game assembly. That is what you build against.
3. Install the .NET 6 SDK.
4. Copy `ExampleMod`, set `GameDir` in the csproj to your Road 96 folder and run `dotnet build -c Release`. The DLL goes straight into `BepInEx/plugins`.

## How IL2CPP mods differ from normal Unity mods

Road 96 is compiled to native code, so you do not touch the real C# classes. BepInEx makes wrapper classes with the same names, and these rules apply:

* **Every field is a property.** `ctx._currentMoney` reads and writes the real field in game memory, even private ones. That is why most of what you need is in fields with a `_` in front.
* **Game lists are not .NET lists.** `ctx._currentTags` is an `Il2CppSystem.Collections.Generic.List<string>`. Use `.Count`, `[i]`, `.Add`, `.Contains`. LINQ does not work on it directly, so loop with an index.
* **Casting.** Use `obj.TryCast<T>()` instead of `as` or `(T)`. It returns null when the type does not match.
* **Finding assets.** `Resources.FindObjectsOfTypeAll(Il2CppType.Of<T>())` finds every loaded ScriptableObject of a type, then `TryCast<T>()` each one. This is how you get every scenario, fact or tape in the game.
* **Your own MonoBehaviour** needs a constructor that takes an `IntPtr`. Add it with `AddComponent<T>()` from your plugin's `Load`, which registers it for you.
* **Unity null.** Destroyed objects are not C# null. Write `if (x == null)` with the Unity type, not `x is null`.
* **Strings** pass through fine both ways. Arrays like `Il2CppStringArray` work with `.Length` and `[i]`.

## Where things are

| What | Path |
|---|---|
| Your save | `%USERPROFILE%\AppData\LocalLow\DigixArt\Road 96\Saves\context.sav` (plain JSON) |
| Unity log | `%USERPROFILE%\AppData\LocalLow\DigixArt\Road 96\Player.log` |
| BepInEx log | `Road 96\BepInEx\LogOutput.log` |
| Interop DLLs | `Road 96\BepInEx\interop` |

## Where most classes live

| Assembly (in `BepInEx/interop`) | What is in it |
|---|---|
| `NarrativeBrain.dll` | the story: NarrativeController, NarrativeContext, NarrativeElement, Run, Bubble, NarrativeProbability, NarrativeTagWrapper |
| `CharacterController.dll` | PlayerBlueEyes (the player) |
| `InventoryAssembly.dll` | InventoryItem, ItemList |
| `CollectiblesAssembly.dll` | Collectible (tapes), CollectibleManager |
| `AudioAssembly.dll` | AudioDescriptor, AudioManager |
| `LocalizationAssembly.dll` | LocalizationManager (all game text) |
| `SaveAssembly.dll` | SaveManager |
| `PauseAssembly.dll` | PauseController |
| `StartMenuAssembly.dll` | CharacterSelectionMenu (the three teen cards) |
| `EndRunRitualAssembly.dll` | EndRunRitual (end of trip screen) |
| `AbilitiesAssembly.dll` | Ability |

Use `TypeDump find <word>` to find anything else.

## Tips

* Wrap game calls in `try/catch`. During loading a lot of things are null for a moment, and an exception in `Update` will spam the log every frame.
* Most singletons are `ManagedSingleton<T>` with a static `Instance`, for example `NarrativeController.Instance`, `SaveManager.Instance`, `PauseController.Instance`.
* Back up `context.sav` before testing anything that changes the story.
* The game cannot overwrite your DLL while it runs. Rename the old one to `.old` first, then copy the new one in, and it loads on the next start.
* Road96 Toolkit's source in [`../src`](../src) uses almost everything on these pages, so it is a good place to see real examples.

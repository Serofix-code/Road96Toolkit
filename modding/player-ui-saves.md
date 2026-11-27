# Player, menus and saves

## The player

`BlueEyes.Entities.PlayerBlueEyes` (CharacterController.dll). Only there when you walk around, null in cars, menus and loading.

```csharp
var p = BlueEyes.Entities.PlayerBlueEyes.Instance;
p.transform.position
p.Teleport(position, rotation);
p.InstantLookAt(point);
p.LookAtInDuration(point, 0.5f, 0f);
p.SetCrouch(true);  p._isCrouched
p.SetCanRun(true);
p.SetSpeedFactor(2f);                 // walk and run speed
p._walkingDefaultSpeed, p._runingDefaultSpeed
p.SetControllerState(false);          // turn player control off and on
p.Freeze(true, BlueEyes.FreezeOrigin.Debug);
p.FreezeMouse(true, BlueEyes.FreezeOrigin.Debug);
p._controller                         // Unity CharacterController
p._fps                                // FirstPersonController
p._virtualCamera                      // Cinemachine camera
p._defaultFov, p.ResetZoom();
```

`Freeze` takes an origin so different systems can freeze the player without undoing each other. Use your own origin (Debug or GodMode) and unfreeze with the same one.

`Camera.main` is the real game camera.

## Teen select screen

`CharacterSelectionMenu` (StartMenuAssembly.dll), the screen with the three teen cards at the start of a trip:

```csharp
var m = CharacterSelectionMenu.Instance;
m._uis                 // the three cards
m._uis[i]._associatedContext   // the NarrativeContext that card would start
m._currentSelection
m.OnCharaSelected()    // runs when the player really picks a card
```

## End of trip screen

`EndRunRitual` (EndRunRitualAssembly.dll), the "passed the border / arrested / died" screen:

```csharp
EndRunRitual.Instance.StartRitual(NB.RunEnding.Passed_Border);
EndRunRitual.Instance._currentEnding
```

## Pause menu

`BlueEyes.Pause.PauseController` (PauseAssembly.dll):

```csharp
var pc = BlueEyes.Pause.PauseController.Instance;
pc.Pause();  pc.RequestQuitPause();
pc._currentPauseState
pc._canPause               // set false to block the pause menu
pc._onPauseStart, pc._onPauseEnd
```

There is no direct "back to main menu" call on it, only the popup (`_backToMenuPopup`). `NarrativeController.Instance.GoToMainMenu()` exists, but it skips the game's own save step, so test it carefully.

## Saves

`SaveManager` (SaveAssembly.dll, no namespace).

```csharp
var sm = SaveManager.Instance;
sm.SavePath                                // get only
sm.SaveContext(ctx, false);
sm.SaveContext("context", ctx, false);     // the one the game itself calls with an id
sm.LoadContext();
sm.ForceLoadContext(ctx);
sm.DeleteSave("context");
```

The save is `%USERPROFILE%\AppData\LocalLow\DigixArt\Road 96\Saves\context.sav`. It is plain JSON of the NarrativeContext: money, facts in `_currentTags`, items as IDs in `_itemsID`, every trip in `_allTimeRuns` and so on. The game writes it after every scenario.

Safe way to swap a save: copy a `context.sav` in while you are on the main menu, then press Continue. Doing it mid trip does nothing until the game loads again, and the game may write over it at the next scenario.

If your mod adds things the base game does not know (items, tags), hook `SaveManager.SaveContext(string, NarrativeContext, bool)` with a prefix that takes them out and a postfix that puts them back, so the save still loads without your mod. Road96 Mixtape does this for its tapes.

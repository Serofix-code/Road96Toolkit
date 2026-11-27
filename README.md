# Road96 Toolkit

A BepInEx 6 plugin for Road 96 (Steam, PC) that adds an in game menu with a free camera, a scene editor, a character spawner and full control over the story: pick the scenario, the ending, which teen you play and their gender, and your abilities.

Single player only. It does not touch online features.

## Features

**Story**
* Browse every scenario in the game, filter by character or search by name
* Queue a scenario as the next one, or jump straight into it
* Jump to the border, the final border and the final road (with or without Zoe)
* End the current trip with any ending: passed the border, arrested at the border, died, arrested, died at the border
* Election gauges (Revolutionary, Individualist, Moderate): raise or lower them, or make one win to steer the final ending

**Teen**
* See all the teens with their portraits and play as any of them
* Switch gender (picks a matching teen so the portrait fits)
* Random teen button

**Abilities**
* Toggle every ability on or off, give all, remove all
* Remove the per trip ability limit

**World**
* Money, energy, karma, days left before the vote, distance to the border
* Day or night and desert or forest for the next scenario
* Pause, normal speed, 2x speed
* Hide the game HUD for clean screenshots

**Freecam**
* Fly anywhere with WASD, Q and E, Shift and Ctrl for speed, mouse wheel to tune it
* Field of view and roll for screenshots
* Teleport your teen to where the camera is

**Character spawner**
* Lists every character the current area has loaded (and any other prefab in memory)
* Spawns a copy in front of the camera, standing still or with its normal behaviour
* Edit or delete anything you spawned

**Scene editor**
* Browse the loaded scenes as a tree, or press F3 to grab whatever is in the middle of the screen
* Move, rotate and scale with fine or coarse steps, hide, duplicate, delete
* Carry an object with the camera, drop it to the ground, make it face you
* Save your changes per scene and load them again later

## Keys

| Key | What it does |
|-----|--------------|
| F1 | Open or close the menu |
| F2 | Freecam on or off |
| F3 | Select the object in the middle of the screen |
| F4 | Hide or show the HUD |

All keys can be changed in `BepInEx/config/serofix.road96toolkit.cfg` after the first launch.

## Install

1. Get the latest **BepInEx 6 IL2CPP win x64** bleeding edge build from https://builds.bepinex.dev/projects/bepinex_be (the file is called `BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.XXX.zip`). Road 96 is an IL2CPP game, so the normal BepInEx 5 or the Mono build will not work.
2. Unzip it into the Road 96 folder, next to `Road 96.exe`. On Steam right click Road 96, Manage, Browse local files.
3. Start the game once and wait until you reach the main menu. The first start takes a minute while BepInEx sets itself up. Close the game.
4. Download `Road96Toolkit.zip` from the [Releases](../../releases) page and unzip it into the same Road 96 folder. You should end up with `BepInEx/plugins/Road96Toolkit/Road96Toolkit.dll`.
5. Start the game and press F1.

If you also have MelonLoader installed, remove or rename its `version.dll` first. Two loaders at once will fight.

## Notes

* Teen and gender changes show on the trip map, the posters and the loading screens. You play in first person so you will not see yourself in game.
* Time of day and area changes are picked up by the next scenario, not the one you are in.
* Spawned characters are copies. They look and animate like the original but are not part of the story, so they will not talk to you.
* Ending a trip early saves it like a normal ending. Back up your saves first if you care about them: `%USERPROFILE%\AppData\LocalLow\DigixArt\Road 96`

## Building

You need the .NET SDK (6 or newer) and a Road 96 install with BepInEx 6 IL2CPP that has been started once (that creates `BepInEx/interop`).

```
dotnet build -c Release
```

If the game is not in the default Steam folder, pass it in:

```
dotnet build -c Release -p:GameDir="D:\SteamLibrary\steamapps\common\Road 96"
```

A Release build copies the DLL into `BepInEx/plugins/Road96Toolkit` for you. Close the game first, it locks the file.

## License

MIT, see LICENSE. Road 96 belongs to DigixArt. This project ships no game files.

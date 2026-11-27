# Road96 Toolkit

A BepInEx 6 plugin for Road 96 (Steam, PC) that adds an in game menu with a free camera, a photo mode, noclip, a scene editor, a character spawner with a 3D preview, a level and mission starter, a dev view editor, choice hints with auto pick, a story tracker, waypoints, a command bar, minigame helpers, a third person camera, a collectible giver, a save manager, the developers' debug menu and full control over the story: pick the scenario, the ending, which teen you play, their gender and age, your money and your abilities.


## Features

**Story**
* Browse every scenario in the game, filter by character or search by name
* Queue a scenario as the next one, or jump straight into it
* Jump to the border, the final border and the final road (with or without Zoe)
* End the current trip with any ending: passed the border, arrested at the border, died, arrested, died at the border
* Election gauges, shown with the candidate they help: Florres (Moderate), Tyrak (Individualist) and the revolution (Revolutionary). Raise or lower them, or make one win to steer the final ending
* Facts: the game remembers what you did as facts (met Zoe, helped someone, finished a scenario). See which ones you have, search every known one with what needs it, switch any of them on or off, or type in your own. Also `fact <name>` and `facts` in the command bar
* Answers: while a conversation shows its answers, change what each one does before you pick it: its karma, which candidate it helps and by how much, and its odds (win, 50% or fail)
* Dice rolls: make every chance in the game always win, always fail, or win at a percent you pick (Story > Answers, the World tab or `odds win` in the command bar)
* Story tracker: every scenario with a played or not played mark, progress per character, and a filter for the ones you can play right now. Pick one to see what it needs and what blocks it, queue it, or give yourself what it needs in one click

**Levels**
* Start any mission in the game straight away, from the first hitchhike to the last border
* Load any level on its own, including the developers' test and prototype levels left in the build
* Raw scenes: add any of the 178 scene files to the world, or unload them again

**Items**
* Collectible giver: every cassette tape in the game, see which ones you have, give or take any of them, or give them all at once
* Every other inventory item too (vouchers, quest items and so on)

**Saves**
* Snapshots: save a named copy of your trip whenever you want, with a summary (trip, last scenario, money, days, teen, tapes)
* Automatic backups every time the game saves, the last 15 are kept (change it in the config)
* Put any snapshot back from the main menu, or have it put back the next time you start the game
* The save that is replaced gets backed up first, so a restore can always be undone
* Snapshots live in `BepInEx/config/Road96Toolkit/saves`

**Teen**
* See all the teens with their portraits and play as any of them (portraits fixed in 1.3)
* Switch gender (picks a matching teen so the portrait fits)
* Random teen button
* Age chooser (one click ages from 12 to 21)
* Age setter for an exact age, or a youngest and oldest age for the game to roll between
* Teen picker: make your own teens with a face, a custom icon (PNG or JPG), starting money, energy, distance to the border, age, days missing and where they were last seen. Choose which of them show up on the three teen cards after Sonya's talk, or leave a card to the game. Your teens are kept in `BepInEx/config/Road96Toolkit/teens.json`
* HUD icon swapper: replace the teen head in the top left corner with another teen's icon, any picture the game has loaded, or your own PNG or JPG (put them in `BepInEx/config/Road96Toolkit/icons`). Hover a name to see the picture

**Abilities**
* Toggle every ability on or off, give all, remove all
* Remove the per trip ability limit
* Special abilities: the OMEN ability (normally only unlocked through HP's OMEN Gaming Hub) and any other ability the game has loaded that is not in the normal list

**World**
* Money giver: quick buttons from $1 to $1000, a custom amount to give or set, or take it all
* Energy, karma, days left before the vote, distance to the border
* Day or night and desert or forest for the next scenario
* Pause, normal speed, 2x speed
* Hide the game HUD for clean screenshots
* Fix sound button: the game mutes everything while the game speed is 0 and does not always unmute, this puts it back

**Helpers** (each one can be turned on or off in the World tab)
* Ball ESP for the cup game (the guy who shuffles the cans): a red box sits on the can that hides the ball and follows it through the whole shuffle
* Choice hints: while you pick an answer in a conversation, a panel lists what every answer really does (karma, which candidate it helps, money, items it needs, the odds of it working, how the scene ends) and marks the best one. It also shows the code of any keypad in the level. Off by default
* Level name: the name of the level, its story id and the character in it show up for a few seconds when a level loads. On by default
* Item ESP (F11): marks every cassette tape you do not have yet (yellow) and everything the game lets you pick up (blue) through walls, with its name and distance. Can also show every other thing you can interact with. Off by default
* Auto pick: answers dialogues for you with the best answer (after a short wait you can change in the config). Only answers the game is really showing count. Off by default
* Minigame help: the best Connect 4 move (worked out by the game's own AI) marked on the board, a marker on every hidden thing for the metal detector, the odds on scratch tickets, and a button that scores an air hockey goal
* Endless lives and ammo in the arcade shooter and the shooting scenes. Off by default

**Travel**
* Save waypoints where you stand (or where the freecam is) and jump back with one click. They are kept per level in `BepInEx/config/Road96Toolkit/waypoints.json`
* Teleport straight to any character in the level, closest first

**Command bar** (F9)
* Type instead of clicking: `money 100`, `setmoney 500`, `karma 1`, `energy full`, `tp diner`, `wp diner`, `mission SONYA_6`, `queue SONYA_6`, `level <name>`, `spawn <name>`, `tapes`, `teen 3`, `speed 2`, `snapshot before border`, `freecam`, `noclip`, `photo`, `devview`, `hints`, `autopick`, `esp`, `odds win`, `fact <name>`
* Up and Down go through what you typed before, `help` lists everything

**Third person** (F10, experimental)
* An over the shoulder camera that stops at walls. Change the distance with the - and = keys or in the Freecam tab
* The teen has no body in this game, so pick any character as your body and it walks with you

**Noclip**
* Fly your teen through walls and floors with WASD where you look, Space or E up, Q down, Shift fast, Alt slow
* Unlike the freecam you are the one moving, so you can land anywhere and keep playing

**Freecam**
* Only turns on from its shortcut (F2) or the button in the menu
* Fly anywhere with WASD, Q and E, Shift and Ctrl for speed, mouse wheel to tune it
* Field of view and roll for screenshots
* Back to teen view with one button or Backspace
* Teleport your teen to where the camera is
* The game hides a developer fly mode that can switch on by itself. The toolkit turns it off when that happens (you can allow it in the config)

**Photo mode** (F8)
* Pauses the game, hides the HUD and gives you the free camera. Hold the right mouse button to look around
* Field of view, roll and exposure
* Filters (black and white, warm, cold, vintage, dramatic, dreamy) plus contrast, saturation, warmth, hue, vignette and film grain
* Depth of field with a focus distance, a blur amount and an Aim button that focuses on whatever is in the middle of the screen
* Turn the sun and change its height
* Rule of thirds grid
* P takes a photo, H hides the panel for a clean view. The toolkit hides its own UI in the photo. Pick 1x, 2x or 4x size (2x and 4x depend on the game's renderer, use 1x if they come out wrong)
* Photos go to `Pictures/Road 96 Photos`
* Saved looks: save all the photo options (filter, colours, focus, sun, grid, size, and optionally field of view and roll) under a name and use them again with one click
* Photo mode opens with the options you used last time (can be turned off)

**Dev view** (F7)
* An editor mode built on the freecam. Hold the right mouse button to look around, the cursor stays free for clicking
* Spawn palette on the left with the characters of this level, characters copied from other levels, and props. Rest the mouse on a name for the 3D preview
* Click a name, then click in the world to place it. Shift+click places more, R turns it
* Click any character or object in the world to select it. A frame shows what is selected
* Inspector on the right: move it with the mouse (G), nudge it, turn it, scale it, drop it to the ground, make it face you, bring it to you
* For characters: make them stand still or go back to their routine, freeze or speed up their animation, mute them
* Poses: play any of the character's own animations, or any animation the game has loaded, then hold the pose for a photo
* Copy (Ctrl+D), delete (Del), hide, or open it in the scene editor. Moves are remembered for the layout saver

**Character spawner**
* Lists every character the current area has loaded (and any other prefab in memory)
* Other levels: pick any level and the toolkit copies its characters for you, so you can spawn people that are not in the area you are in. The level loads out of sight for a second and is unloaded again
* 3D preview: rest the mouse on a character in the list and a small window next to the menu shows it turning around, with zoom and darker or brighter buttons (hover preview can be turned off)
* Spawns a copy in front of the camera, standing still or with its normal behaviour
* Edit or delete anything you spawned

**Scene editor**
* Browse the loaded scenes as a tree, or press F5 to grab whatever is in the middle of the screen
* Move, rotate and scale with fine or coarse steps, hide, duplicate, delete
* Carry an object with the camera, drop it to the ground, make it face you
* Save your changes per scene and load them again later
* Share layouts: export everything you built (moved, hidden and deleted objects plus every character and prop you placed) as one `.r96layout` file and load files from friends. It can copy the characters a layout needs from their levels for you. Files go in `BepInEx/config/Road96Toolkit/shared`

**Developer menu**
* The developers left their debug menu in the game, but its screen is broken in the retail build. This tab lists everything it has registered in the current scene and runs the entries for you
* Writes the game's state and loaded scenes to the log, useful for bug reports

## Keys

| Key | What it does |
|-----|--------------|
| F1 | Open or close the menu |
| F2 | Freecam on or off |
| Backspace | Leave the freecam and go back to the teen's view |
| F3 | Noclip on or off |
| F5 | Select the object in the middle of the screen |
| F4 | Hide or show the HUD |
| F6 | Open the developer menu tab |
| F7 | Dev view on or off |
| F8 | Photo mode on or off (P photo, H hide panel) |
| F9 | Command bar |
| F10 | Third person camera on or off |
| F11 | Item ESP on or off |

All shortcuts can be changed in `BepInEx/config/serofix.road96toolkit.cfg` after the first launch. Modifiers work too, for example `Ctrl+Shift+F`. If you never changed the old Ctrl+F2 and Ctrl+F3 defaults, 1.3 moves them to F2 and F3 by itself.

## Install

1. Get the latest **BepInEx 6 IL2CPP win x64** bleeding edge build from https://builds.bepinex.dev/projects/bepinex_be (the file is called `BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.XXX.zip`). Road 96 is an IL2CPP game, so the normal BepInEx 5 or the Mono build will not work.
2. Unzip it into the Road 96 folder, next to `Road 96.exe`. On Steam right click Road 96, Manage, Browse local files.
3. Start the game once and wait until you reach the main menu. The first start takes a minute while BepInEx sets itself up. Close the game.
4. Download `Road96Toolkit.zip` from the [Releases](../../releases) page and unzip it into the same Road 96 folder. You should end up with `BepInEx/plugins/Road96Toolkit/Road96Toolkit.dll`.
5. Start the game and press F1.

If you also have MelonLoader installed, remove or rename its `version.dll` first. Two loaders at once will fight.

## Notes

* The teen's age only appears on the teen cards in the main menu and on loading screens, so that is where the age chooser shows up.
* Teen and gender changes show on the trip map, the posters and the loading screens. You play in first person so you will not see yourself in game.
* Time of day and area changes are picked up by the next scenario, not the one you are in.
* Starting a mission from the Levels tab works best during a trip. "Load the level only" skips the story setup, so some levels will be empty or stuck, test levels most of all.
* Copying characters from other levels is experimental. A level can run some of its own logic in the second it is loaded, so save first.
* Auto pick and the minigame help read the game's own data. If a scene does something unusual they may be wrong, so save first if it matters.
* Third person is experimental. The stand in body walks with you but some characters do not have a walk animation the toolkit can find, so they may slide.
* Spawned characters are copies. They look and animate like the original but are not part of the story, so they will not talk to you.
* Ending a trip early saves it like a normal ending. Make a snapshot in the Saves tab first if you care about it. The game's own save is in `%USERPROFILE%\AppData\LocalLow\DigixArt\Road 96`

## Open source

All the code is here under the MIT license, so you can read it, change it, fork it and reuse it in your own mods. Bug reports, ideas and pull requests are welcome in the Issues and Pull requests tabs.

How it is put together:
* `src/Plugin.cs` loads the plugin and its settings
* `src/ToolkitBehaviour.cs` is the window and the tabs
* One file per tab or feature (`StoryTab.cs`, `LevelsTab.cs`, `DevView.cs`, `ChoiceHints.cs` and so on)
* `src/Gui.cs` is a small layout helper, since the game strips Unity's automatic GUI layout

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

MIT, see LICENSE. Road 96 belongs to DigixArt. This project ships no game files. You need a legal copy of the game to run this mod.

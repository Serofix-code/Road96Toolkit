# TypeDump

Lists what is inside the game's classes, straight from the BepInEx interop DLLs. Use it to find the exact name of a field or method before you write code, or to check if a game update changed something.

```
dotnet run -- find Collectible
dotnet run -- NarrativeBrain NarrativeContext Run
dotnet run -- CollectiblesAssembly Collectible CollectibleManager
```

`find` takes a word or a regex and shows which assembly each matching class is in. The second form prints every field, property and method of the classes you name. Enums print their values.

If Road 96 is not in the default Steam folder, set `ROAD96` to your game folder first:

```
set ROAD96=D:\Games\Road 96
```

BepInEx has to have run once so `BepInEx/interop` exists.

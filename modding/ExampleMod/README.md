# Example mod

A small Road 96 mod to copy when you start your own.

* F8 gives you money (amount in `BepInEx/config/yourname.road96examplemod.cfg`)
* F9 writes your trip, scenario, money, karma and every fact you have to `BepInEx/LogOutput.log`
* Every new fact is written to the log as it happens

## Build

1. Set `GameDir` in `ExampleMod.csproj` to your Road 96 folder. BepInEx must have run once.
2. `dotnet build -c Release`
3. Start the game.

Change the GUID in `Plugin.cs` (`yourname.road96examplemod`) to your own before you share your mod.

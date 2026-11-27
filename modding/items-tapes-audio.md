# Items, tapes, sound and text

## Items

`BlueEyes.Inventory.InventoryItem` (InventoryAssembly.dll) is a ScriptableObject. Every item in your bag is one: tickets, vouchers, tapes, abilities.

```csharp
item.ID              // get only. Used in the save file
item.name            // asset name
item._icon           // Sprite
item.IsDisposedAtTheEndOfTheRun, item.IsDisposedAtTheEndOfTheBubble
```

Giving and taking, on the NarrativeContext:

```csharp
ctx.AddItem(item);
ctx.RemoveItem(item);
ctx.HasItem(item);
ctx.IsItemNew(item);  ctx.RemoveItemNewState(item);
ctx._items            // List<InventoryItem>
ctx._itemsID          // List<string>, what goes in the save
ctx._itemsNewState    // List<bool>, same order as _itemsID
ctx._onItemGained, ctx._onItemLost
```

Finding an item by ID. The main list is on the narrative settings:

```csharp
ItemList list = NB.NarrativeController.Instance._settings._items;
var item = list.GetItemByID("SOME_ID");
list.RegisterItemInList(myItem);   // add your own
list._items                        // List<InventoryItem>
```

There can be more than one `ItemList` loaded. To be safe, find all of them with `FindObjectsOfTypeAll(Il2CppType.Of<ItemList>())`.

Abilities are items too: `BlueEyes.Abilities.Ability : InventoryItem` in AbilitiesAssembly.dll. The ones you can learn are in `settings._abilities`.

## Cassette tapes

`Collectible : InventoryItem` (CollectiblesAssembly.dll, no namespace):

```csharp
tape._tape              // AudioDescriptor, the walkman sound
tape._spacializedTape   // AudioDescriptor, the 3D sound in cars and rooms
tape._trackName         // text key, see Localization below
tape._trackAuthor       // text key
tape._trackLore         // text key
tape._boxSideTexture    // Sprite on the side of the box
tape._tapeMaterial      // Material of the tape model
tape._type              // TapeType
```

The manager decides which tapes can still be found:

```csharp
var mgr = CollectibleManager.Instance;
mgr._allCollectibles          // List<Collectible>
mgr._availableCollectibles    // the ones not found yet
mgr.UpdateAvailableCollectibles();
mgr.TakeNextAvailableCollectible();
mgr.CanHaveNewCollectibles
```

### Adding your own tape

This is what Road96 Mixtape does (its source is at https://github.com/Serofix-code/Road96Mixtape):

1. Find a real `Collectible` with `FindObjectsOfTypeAll` and `Object.Instantiate` it. Give the copy a new `name`.
2. Copy its `AudioDescriptor`s with `Object.Instantiate` too, and set `_clips` to your own `AudioClip` (see below).
3. Set `_trackName`, `_trackAuthor`, `_trackLore` to your own keys and answer them in a `Localize` hook.
4. `RegisterItemInList` it, add it to `CollectibleManager._allCollectibles`, then `ctx.AddItem` it.
5. Take it out of `_itemsID` and `_items` while the game saves (hook `SaveManager.SaveContext`), or the save will point at an item that does not exist without your mod.

## Sound

`BlueEyes.Audio.AudioDescriptor` (AudioAssembly.dll) is a ScriptableObject that wraps normal Unity `AudioClip`s:

```csharp
desc._clips          // Il2CppReferenceArray<AudioClip>
desc._volume, desc._pitch, desc._looping, desc._isMusic
desc._randomPlayTime, desc._startTime
desc.Play();
desc.Play(position);            // returns an AudioHandle
desc.GetRandomClip();
```

```csharp
var am = BlueEyes.Audio.AudioManager.Instance;
am.PlayBGM(desc);  am.StopBGM();
am.GetBGMVolume(); am.SetBGMVolume(0.5f);
am.Play(desc, position, null);
```

Loading your own sound file at runtime works with `UnityWebRequestMultimedia.GetAudioClip(url, AudioType.OGGVORBIS)` and `DownloadHandlerAudioClip.GetContent(req)`. Poll `req.isDone` in `Update` instead of using a coroutine.

```csharp
var clips = new Il2CppReferenceArray<AudioClip>(new[] { myClip });
desc._clips = clips;
```

## Game text

`BlueEyes.Localization.LocalizationManager` (LocalizationAssembly.dll). Almost every text in the game is a key, and this turns it into the text in the player's language:

```csharp
string text = BlueEyes.Localization.LocalizationManager.Localize("SOME_KEY", false);
```

Patch `Localize` with a prefix to answer your own keys:

```csharp
static bool LocalizePrefix(string __0, ref string __result)
{
    if (__0 != null && __0.StartsWith("MYMOD_")) { __result = "My text"; return false; }
    return true;
}
```

Under it the game uses I2 Localization (`I2.Loc.LocalizationManager` in I2Assembly.dll), but the BlueEyes one is what the game code calls.

# The story system

All of this is in `NarrativeBrain.dll`, namespace `BlueEyes.NarrativeBrain` unless said otherwise.

## Getting the main objects

```csharp
using NB = BlueEyes.NarrativeBrain;

var ctrl  = NB.NarrativeController.Instance;   // null at the very start
var ctx   = ctrl.CurrentContext;               // NarrativeContext: everything about your playthrough
var brain = ctrl.CurrentBrain;                 // NarrativeBrain: picks the next scenario
var set   = ctrl._settings;                    // NarrativeSettings: game rules and item list
```

The game has more than one NarrativeContext at a time (the three teen cards each have one). `ctrl.CurrentContext` is the real one.

## Words the game uses

| Game name | What it is |
|---|---|
| Tag | a fact, like "met Zoe" or "helped Fanny". Just a string |
| NarrativeElement, bubble | one scenario (one scene with one character) |
| NarrativeImpact | one way a scenario can end, with what it gives and takes |
| Run | one trip, one teen |
| Arc | a character's story line |
| Walkthrough | the whole playthrough, all the trips together |
| Karma | the hidden "bad decisions" value that decides police and danger |
| WorldGauge | the election vote, one gauge per side |

## Facts (tags)

```csharp
ctx.HasTag("SOME_TAG");
ctx.AddTag("SOME_TAG");
ctx.RemoveTag("SOME_TAG");
ctx._currentTags            // List<string> of every fact you have now
ctx._onTagGained            // Action<string>, fires on every new fact
ctx._onTagLost              // Action<string>
```

When you finish a scenario, its ID is added as a fact too, so `ctx.HasTag("SCENARIO_ID")` tells you if it was played.

The readable text for many facts is on `NarrativeTagWrapper` assets (no namespace):

```csharp
foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<NarrativeTagWrapper>()))
{
    var w = o.TryCast<NarrativeTagWrapper>();
    string tag  = w._tag;
    string text = BlueEyes.Localization.LocalizationManager.Localize(w._bigChoiceText, false);
}
```

## Money, karma, days, energy

```csharp
ctx._currentMoney          ctx.AddMoney(50);  ctx.RemoveMoney(50);  ctx.HasEnoughMoney(50);
ctx._currentKarma          ctx.SetKarma(0.5f);
ctx._currentEnergy         ctx.ImpactEnergy(1f, set, true);   ctx.MaxEnergy(set);
ctx._daysRemaining         ctx.PassADay(true, 1);
ctx._distanceRemaining     ctx.MoveDistance(100);
ctx._abilities             ctx.AddAbility("ID");  ctx.HasAbility("ID");  ctx.RemoveAbility("ID");
ctx._isNewGamePlus
ctx._currentPlayerState    // PlayerState, see the enum below
```

Setting a field like `_currentMoney` works but the HUD may not update until the next change. The `Add...` calls fire the game's own events, so prefer them.

## Scenarios (NarrativeElement)

A ScriptableObject per scenario. Get all of them with `FindObjectsOfTypeAll`, or one by ID:

```csharp
var el = brain.GetBubbleById("SCENARIO_ID");
el.ID                 // string
el.NPC                // character ID
el.Arc
el.Synopsis           // a short writer's note, not translated
el.Title
el.RequiredTags       // Il2CppStringArray: facts you need for it to show up
el.UnwantedTags       // facts that stop it showing up
el.RequiredPlayerState
el.EntryKarmaLevelMin, el.EntryKarmaLevelMax
el.IsBorder
el.NarrativeImpacts   // List<NarrativeImpact>, every ending of this scenario
```

### Endings of a scenario (NarrativeImpact)

A struct, one per exit:

```csharp
imp._synopsis
imp._addedTags, imp._removedTags      // List<string>
imp._wantedTags, imp._unwantedTags    // facts needed for this exit to be possible
imp._karmaImpact, imp._moneyImpact, imp._energyImpact
imp._worldGaugeImpacts                // List<WorldGaugeImpact>, votes for the election
imp._exitPlayerState
imp._nextArc
imp._isGameOver
imp.IsAvailable(ctx, set, brain)
```

`WorldGaugeImpact` (no namespace) has `_gaugeType` and `_impact`.

### Choosing the next scenario

```csharp
ctx._overrideNextBubble = "SCENARIO_ID";   // the game picks this one next
ctrl._nextBubble = el;                     // set this too if the game already picked one
ctrl.GoToNextBubble(false, true);          // leave now and go to the next one
ctrl.PickNextBubble();                     // what the game would pick
ctrl.NextBubble                            // what it picked
ctrl.GoToMainMenu();
```

`brain.Elements` is the scored list the brain chooses from, each with a score.

Special scenarios like the border and the final road are on `brain._narrativeInput`: `_borderBubble`, `_borderZoeBubble`, `_borderFinal`, `_finalRoadBubble`, `_finalRoadZoeBubble`.

## Trips (Run) and played scenarios (Bubble)

```csharp
ctx.CurrentRun          // Run
ctx.CurrentRuns         // trip number
ctx._allTimeRuns        // List<Run>, every trip ever
ctx._runs
ctx._runStarted

run._bubbles            // List<Bubble>, scenarios played this trip in order
run._npcsThisRun, run._arcsThisRun, run._abilitiesThisRun
run._playerFaceId       // which teen
run._ending             // RunEnding
run._runFinished

bubble._bubbleId
bubble._tagsGained, bubble._itemsGained
bubble._diceRolls       // List<DiceRoll>: _narrativeProbability, _actualProbability, _succeeded
bubble._moneyIn, bubble._moneyOut, bubble._karmaIn, bubble._karmaOut
bubble._validated
```

Facts are stored on the context, not on the Run, so to see "facts gained this trip" you have to watch `AddTag` yourself (see [hooks.md](hooks.md)).

Other useful checks on the context: `HasSeenBubble(id)`, `HasSeenBubbleInAllWalkthroughs(id)`, `HasMeetNPC(npc)`, `HasBeenWithNPCThisRun(npc)`, `HadArcThisRun(arc)`, `IsNewGamePlus()`, `IsAtRoad96(set)`.

## The election

```csharp
ctx.GetWorldGauge(NB.GaugeType.Revolutionary);
ctx.ImpactWorldGaugeInternal(NB.GaugeType.Moderate, 0.1f);
ctx._worldGauges
```

## Dice rolls (NarrativeProbability)

Every chance based answer ("70%") is a `NarrativeProbability` asset (no namespace):

```csharp
float chance = prob.GetProbability(ctx, set);   // 0 to 1
bool ok      = prob.RollDice(ctx, set);
prob._baseValue, prob._clamp
```

Patching `RollDice` with a postfix lets you force results. See [hooks.md](hooks.md).

## Enums

```
PlayerState: Any 0, Driving 1, Passenger 2, Walking 3, Location 4, Border 5,
             CrossedBorder 6, FailedBorder 7, Taxi 8, Bus 9
RunEnding:   Passed_Border 0, Arrested_Border 1, Died 2, Arrested 3, Died_Border 4
GaugeType:   Revolutionary 0, Individualist 1, Moderate 2
```

## Other NarrativeContext calls

These exist but I have not used all of them, so test first:

`OnRunStarted(set)`, `OnRunEnded(set)`, `OnBubbleEnter(el, set)`, `OnBubbleExit(el, validated, set, brain)`, `SetPlayerState(state)`, `SetEnvironment(env)`, `SetTimeOfDay(time, feedback)`, `SetArc(arc)`, `Clone()`, `GetCurrentPlayerFace(set)`, `GetWalkthroughCompletion(set)`, `GetBubbleTitle(el)`.

# Conversations, payphones and what they check

How the game decides which answers you get, which lines a character says and what happens when you call a number. Road96 Toolkit uses all of this for Story > Unlocks.

## Conversations are NodeCanvas dialogue trees

Every conversation is a `NodeCanvas.DialogueTrees.DialogueTree` (a `Graph`). The one playing right now is the static `DialogueTree.currentDialogue`, null when nobody is talking.

`graph.allNodes` is a list of every node. The ones that matter:

| Node | What it does |
|---|---|
| `BlueEyes.Dialogues.DialogLineNode` | one spoken line. `statement._i2` is the text key (pass it to the I2 localization), `statement._text` the fallback |
| `BlueEyes.Dialogues.DialogueChoiceNode` | the answers you pick from. `availableChoices` is a list of `DialogueChoiceNode.Choice` |
| `NodeCanvas.DialogueTrees.ConditionNode` | a branch. `condition` decides if it goes down the first or the second connection |
| `NodeCanvas.DialogueTrees.MultipleConditionNode` | `conditions[i]` picks connection `i`, the first true one wins |

To follow the tree, `node.outConnections[i].targetNode` gives the next node.

## Answers

`DialogueChoiceNode.Choice` has everything about one answer:

* `statement`: the text (`i2` key, `text` fallback)
* `condition`: a `ConditionTask`. When it is false the game leaves the answer out completely
* `impact` (karma), `worldImpact` (election gauge), `moneyRequired`, `item` with `takeItem`, `probability` (dice roll), `isExit` and `exitType`, `onlyOnce`

Answers the condition hides are still in `availableChoices`, they just never get a button. `DialogueChoiceNode.OnOptionSelected(index)` picks an answer by its index in `availableChoices`, hidden or not. The buttons on screen are `DialogChoiceInteraction` objects in `DialogInteraction._currentChoices`, and `DialogInteraction.ResetAllDialogChoices()` clears them.

## Conditions

Every check is a `NodeCanvas.Framework.ConditionTask`. `_invert` flips it. The kinds the game uses:

| Class | Checks | Field |
|---|---|---|
| `ConditionList` | several conditions, all of them (`allTrueRequired`) or any one | `conditions` |
| `BlueEyes.NarrativeBrain.NodeCanvasCheckTag` | the teen has a fact | `_tag` (string) |
| `BlueEyes.NarrativeBrain.NodeCanvasCheckTagWrapper` | same, through an asset | `_tag._tag` |
| `NodeCanvasCheckItem` | the teen carries an item | `_value` (InventoryItem) |
| `NodeCanvasCheckAbility` | the teen has an ability | `_value` (Ability, use `.ID`) |
| `BlueEyes.NarrativeBrain.NodeCanvasCheckWorldState` | a world state asset | `_state` (WorldStateCondition) |
| `BlueEyes.NarrativeBrain.NodeCanvasCheckGender` | the teen is a boy or a girl | `_gender` |
| `BlueEyes.NarrativeBrain.NodeCanvasCheckCostWrapper` | the teen can pay | `_impact` |
| `NodeCanvasCheckState` | a game state is active | `_value` |

World states (`WorldStateCondition`, all have `IsConditionValid(NarrativeContext)`):

* `TagWSC`: a fact is there (`_isPresent` true) or not. The fact is `tagWrapper._tag`, or `_tag` when there is no wrapper
* `WorldState`: `_anyRequirements`, true when any one requirement has all of its `_requiredConditions` and none of its `_unwantedConditions`
* `CharacterSeenWSC`: you met `_npc`
* plus `StoryActWSC`, `RunIDWSC`, `WorldGaugeWSC`, `PreviousRunResultWSC` and a few more for the trip and election

To make a check pass, give the teen what it asks for with the `NarrativeContext` calls: `AddTag` / `RemoveTag`, `AddItem` / `RemoveItem`, `AddAbility` / `RemoveAbility`. Answers are filtered when the choice node starts, so change things before the answers show up, or pick the hidden answer yourself with `OnOptionSelected`.

## Payphones

`BlueEyes.Phone.PhoneController` is the payphone. Its `_directory` is a `PhoneDirectory` with every number in `_numbers` and the `_wrongNumber` you get for anything else. `GetNumber(int)` looks one up.

`BlueEyes.Phone.PhoneNumber` is one number:

* `_numbers`: the digits, as an int
* `_dialogue`: the conversation it plays, a normal `DialogueTree` with all the checks above
* `_actorNames`: who talks (`_dialogActorName`, `_displayedActorName`)
* `_sub`, `_audio`: the subtitle and sound

`Resources.FindObjectsOfTypeAll(Il2CppType.Of<PhoneNumber>())` lists every number once a level with a payphone is loaded. You can read its `_dialogue` checks before you even pick up the phone. Facts like `CALLHOME1` and `HC_VOICEMAIL` are what the home call and the voicemail leave behind.

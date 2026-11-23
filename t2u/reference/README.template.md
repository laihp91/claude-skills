# __GAME_NAME__ (Unity)

One paragraph: what the game is, Unity version, render pipeline, colour space, input, UI system, target platforms.

## Architecture
Folder table (Core / View / UI / Infra / Dev / Editor / Data / TestData). Rule: Core never references View/UI.
Diagram (text): Input → Core.Input(ev) → Core.Update(1/60) → events (sfx, fx, save, ads…) → View/UI/Infra.

## How the game flows
Plain-C# explanation of the main loop (bootstrap → FixedStepper → Core), how timed sequences work (Scheduler callbacks /
JsPromise chains if kept), and which Core fields are presentation state. One worked example (e.g. "what happens when a
bubble pops", file:line references).

## Changing gameplay
- Tuning numbers: `Data/*.json` (list the files and what they control).
- Add a level: steps. Add a booster/object type: steps (Core, View, tests).
- Add/modify a screen: edit the prefab in `Prefabs/UI`; bind elements with `UiRefs` names; texts via `Data/strings.json`.

## Changing art
Where each picture lives (`Art/...` folders, the look table ScriptableObject, UI skins in `Art/UI`), how to replace one
(same file name → done; new name → drag it into the field named …), sizes/9-slice borders to keep. Nothing is drawn in
code: every look is a file.

## Tests
- Run: Test Runner ▸ EditMode ▸ Run All (or batch command).
- Golden replay tests: what they check; **after an intended gameplay change** → Accept Current Behaviour As Golden →
  review diff → commit; **record a new scenario** → steps.

## Build
Android dev/release (BuildTools), signing (keystore outside git), AAB, iOS status.

## Debug & known notes
Debug keys (editor/dev only), AutomationDriver flags, logs, known engine quirks (SRP Batcher off, Gamma, …),
dev-only tooling (MCP / Roslyn: keep or remove).

## Where SDKs go
Ads (IAdService), IAP, analytics (event hooks), audio/haptics.

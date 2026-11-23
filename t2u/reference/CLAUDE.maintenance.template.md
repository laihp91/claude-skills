# __GAME_NAME__ (Unity) — MAINTENANCE mode

Converted from a Three.js game; the conversion is finished. The Unity project is now the source of truth.
Code, comments and commits in English. (If the person you talk to is the producer: answer in short plain Vietnamese.)

## Architecture (Assets/_Game)
- `Scripts/Core` — game rules, pure C# (asmdef Game.Core, no UnityEngine). Deterministic: fixed step 1/60, seeded
  `SeededRandom`, `Scheduler` callbacks, `JsMath` for sin/cos/exp/log/hypot (`Js.Pow` for a fractional power). Talks to the outside only via interfaces/events.
- `Scripts/View` (presents Core state), `Scripts/UI` (uGUI screens via `UiRefs`), `Scripts/Infra` (audio, save, ads,
  haptics), `Scripts/Dev` (AutomationDriver, debug tools — editor/dev builds only).
- `Data/` levels and tuning JSON, `Data/strings.json` player texts. `TestData/Golden/` regression recordings.

## Rules
- Keep Core deterministic: no UnityEngine, no `System.Random`, no `DateTime`/`Time.*`, no `Math.Sin` etc. (use JsMath),
  no async/Task. All randomness from the game's SeededRandom; all timing from Update(dt).
- Player texts: keys in `Data/strings.json`, never literals in C#.
- Art is files: every picture is a PNG in `Art/`, wired by Inspector fields / ScriptableObjects. Never create a
  texture, sprite or shape in code (no `new Texture2D`, `SetPixel`, generated meshes for looks); shaders only
  blend/tint/fade. Before adding a visual, name the PNG it uses.
- Save format changes: bump `SaveData.Version` and add a migration in `Load`.
- Scenes/prefabs are edited in the Editor (generators are frozen: `generatedFrozen` in ProjectSettings/T2UKit.json).
- One MonoBehaviour per file; files under 600 lines; no Find/FindObjectOfType in gameplay; no allocations in Update paths.
- Debug code only under `#if UNITY_EDITOR || DEVELOPMENT_BUILD`.

## Changing gameplay
1. Change Core; add/adjust unit tests (Tests/EditMode).
2. Run all EditMode tests. Golden replay tests will show the first differing frame if behaviour changed.
3. Intended change → `Tools/T2U Kit/Golden Tests/Accept Current Behaviour As Golden`, review `git diff TestData/Golden`,
   commit together with the change. Unintended → fix the code.
4. New scenario worth protecting → record it in the Editor (AutomationDriver recording), then Accept.

## Done = tests green, 0 compile warnings, `python3 ~/.claude/skills/t2u/kit/tools/handover_check.py .` still passes.

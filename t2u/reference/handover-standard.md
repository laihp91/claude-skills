# Handover standard — a converted Unity project the dev team can maintain

Goal: a Unity dev who has NEVER seen the web game can clone only the Unity repo, understand it in an hour, change
gameplay safely, add levels/screens, and find bugs. Checked by `python3 <kit>/tools/handover_check.py <UnityProject>`
(must be 0 FAIL and score ≥ 85 before `STATUS: DONE`). Build these in from Phase 2 — retrofitting costs more.

| # | Rule | Why | How (kit support) |
|---|---|---|---|
| H1 | Nothing in Tests/Scripts reads files outside the Unity repo (`../web`) | Team clones only the Unity repo | Copy fixtures (goldens, levels, math cases) into the repo |
| H2 | Golden recordings + expected logs live in the repo: `TestData/Golden/` (T2UKit.json `goldenDir`) | They become the team's regression suite | Copy `recordings/*.input.json/.state.jsonl/.events.jsonl` there in Phase 3 |
| H3 | Replay tests cannot pass with zero cases (a `HasRecordings` test) | Empty folder = false green | See kit `PopRushParityTests.HasRecordings` |
| H4 | CLAUDE.md in MAINTENANCE mode after handover | Conversion rules ("never improve the JS logic") block normal work | `reference/CLAUDE.maintenance.template.md` |
| H5 | README with: architecture, how to change gameplay / add a level / add a screen, golden tests (accept + record new), build, debug & known notes | Onboarding in an hour | `reference/README.template.md` |
| H6 | Generated scenes/prefabs frozen: `"generatedFrozen": true`; game generators only create what is missing | Regenerating must never erase team edits; UI is edited in prefabs, not in C# | Kit UiLayoutImporter default = new screens only; make game SceneBuilder check `KitSettings.Load().generatedFrozen` |
| H7 | `.gitattributes` (UnityYAMLMerge, binaries) + `.editorconfig` | Fewer merge conflicts, one code style | `kit/project-files/gitattributes`, `editorconfig` |
| H8 | Debug shortcuts/cheats only in `#if UNITY_EDITOR \|\| DEVELOPMENT_BUILD` | No cheats in store builds | — |
| H9 | Automation (command-line flags, capture, replay-in-player) in its own `AutomationDriver` component, editor/dev only | Bootstrap stays readable | — |
| H10 | Player-facing texts are keys in `Data/strings.json`, shown via `TextTable` | Designers edit texts; translation later | Kit `TextTable`; logic emits keys (`toast.noSlots`), UI resolves |
| H11 | Save data has a `Version` and a migration step | Updates must not wipe player progress | `Version = 1`; `Load` migrates older versions |
| H12 | No script > 600 lines; one feature per file (partial classes by feature are fine: `GameModel.Boosters.cs`) | Findability | — |
| H13 | Dev-only tooling (MCP, Roslyn) explained in README (keep/remove) | Surprises in the repo | — |
| H14 | One MonoBehaviour per file, file name = class | Prefab references break otherwise | — |
| H15 | Art is files: every picture is a PNG in `Assets/_Game/Art`, wired by Inspector fields / ScriptableObjects; no texture, sprite or shape is created in game code; README says how to replace a picture | The producer and artists change art by swapping files, never by asking a programmer | Checker: `new Texture2D`, `SetPixel(s)`, `Sprite.Create` in Scripts outside Editor/Dev = FAIL |

Also required (not auto-checked): 0 compile warnings; all EditMode tests green; the "How the game flows" section of
README explains any JS-derived patterns kept in Core (scheduler callbacks, Legacy JsPromise chains, presentation state
inside Core) in plain C# terms with one example; Core public API has XML summaries.

## After an intended gameplay change (the team's workflow — goes into README + CLAUDE.md)
1. Change the C# rules. Run tests → golden tests fail and show the first differing frame/field.
2. If every difference is intended: `Tools/T2U Kit/Golden Tests/Accept Current Behaviour As Golden`
   (rewrites expected logs) → review `git diff TestData/Golden` → commit with the gameplay change.
3. New regression scenario: play it in the Editor with recording on (AutomationDriver + `RecordingWriter`, saves
   `TestData/Golden/<name>.input.json`), then Accept (creates its expected logs).
The web game is no longer the reference after handover; the goldens are.

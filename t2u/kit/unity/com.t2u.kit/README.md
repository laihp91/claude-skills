# com.t2u.kit

Reusable parts for Three.js → Unity ports (see ../../../SKILL.md and reference/fast-path.md).

| Assembly | Contents |
|---|---|
| `T2U.Kit` (pure C#, no UnityEngine) | `TextTable` (texts by key), `Replay.RecordingWriter` (record sessions in Unity), `JsMath` (Chrome-exact sin/cos/exp/log/hypot), `Js.Pow` (the template's `pow(b, e)`), `SeededRandom` (mulberry32 + Range/Int/Pick/Shuffle), `Scheduler` (after/every/tween, JS clock.js twin), `Easing`, `Js.Round/R3`, `FixedStepper`, `Replay.ReplayDriver` / `Replay.Recordings` / `Replay.JsonDiff`, `Legacy.JsPromise` + `Legacy.Microtasks` |
| `T2U.Kit.Unity` | `UiRefs` (named elements of imported UI screens: `Text`, `OnClick`, `Show`, `State`, `Fill`, `List`, `Row`, `Scroll`, `Animate`), `UiStates` (the look of every state the web switched with a CSS class), `UiFill` (bars the game fills), `UiFlex` + `UiFlexItem` (CSS flexbox), `UiList` + `UiRow` (lists, row prefabs), `UiAnim` (CSS @keyframes), `SafeAreaPadding`; `Automation.IAutomationHost` + `Automation.AutomationDriver` (dev builds: reference shots through the real scene, watch a recording, record a session) |
| `T2U.Kit.Editor` | menu **Tools/T2U Kit**: Edit Settings (ProjectSettings/T2UKit.json), Setup Project, Sync From Web Project, Import TMP Essentials, Build TMP Fonts, Import UI Layouts (new screens only / overwrite), Check UI Prefabs Against ui.json, Capture UI Screens, Capture Reference Shots (Play mode), Build Android, Golden Tests ▸ Accept Current Behaviour As Golden; `PlayModeCapture`; `KitImportRules` |
| `T2U.Kit.Tests` | twin goldens vs Chrome, Pop Rush port (`Tests/Editor/PopRush`) + replay parity of its web recordings |

Install: copy this folder to `<UnityProject>/Packages/com.t2u.kit` (embedded). Requires Unity 6, URP, ugui, Newtonsoft JSON.

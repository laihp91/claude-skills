---
name: t2u-convert
description: Phases 2–5 of the Three.js → Unity conversion — create the Unity project skeleton, import assets, port core logic with tests, build scenes/prefabs from exported JSON, then rebuild view/UI/VFX/audio. Called by /t2u; works one module at a time following unity-convert/PLAN.md.
---

# Phases 2–5 — Build the Unity project

Read first: `t2u/reference/mapping.md`, `t2u/reference/lessons.md`, `unity-convert/PLAN.md`, `unity-convert/GAME_SPEC.md`.
`MODE: READY` → also `t2u/reference/fast-path.md` (overrides this file where they differ).

**Always use the T2U Kit** (`~/.claude/skills/t2u/kit/unity/com.t2u.kit`): copy it into `<Unity>/Packages/com.t2u.kit`
right after the project exists (embedded package). It gives, ready and tested: JsMath (bit-exact JS math), SeededRandom,
Scheduler + Easing, Js.Round/R3, ReplayDriver/Recordings (parity tests), Legacy JsPromise/Microtasks (promise-based
JS), ProjectSetup (mobile defaults incl. SRP Batcher off, Gamma), WebSync, FontBuilder, TmpEssentials, UiLayoutImporter
(+ UiRefs), BuildTools (Android), PlayModeCapture, KitImportRules, SafeAreaPadding, FixedStepper. Configure it with
`ProjectSettings/T2UKit.json`. Do not re-write these per game; if one needs a fix, fix it in the KIT REPO (path in
`~/.claude/skills/t2u/KIT_SOURCE`, procedure in t2u SKILL.md "Changing the kit"), re-copy the package into the game,
and add the lesson to the kit repo's lessons.md. Never edit `~/.claude/skills/...` (overwritten at the next install).

## How to drive Unity
1. Preferred: Unity MCP tools if connected (create scripts, manage scenes/prefabs/materials, read console, run tests).
2. Fallback: Unity batch mode from terminal. Unity may NOT be in /Applications: use the Editors folder recorded in PLAN.md or given in the autopilot prompt (it can be on an external drive, or Unity Hub's secondary install path in `~/Library/Application Support/UnityHub/secondaryInstallPath.json`). If that drive is not mounted, log it in REVIEW.md and set `STATUS: STOPPED`. Example on macOS:
   `"/Applications/Unity/Hub/Editor/<ver>/Unity.app/Contents/MacOS/Unity" -batchmode -nographics -projectPath <path> -executeMethod <Class.Method> -logFile - -quit`
   Tests: `... -runTests -testPlatform EditMode -testResults results.xml`
3. After every batch of script changes: make sure there are **zero compile errors** (read console / log) before continuing.

## Maintainability from day one (handover-standard.md H1–H14 — do NOT leave them for Phase 7)
- Tests read only files inside the Unity repo: golden recordings + expected logs in `TestData/Golden/` (goldenDir),
  other fixtures in `TestData/`. Copy them from `../web/unity-convert/` with a small editor/sync step; never `../web` paths
  in C#. Every TestCaseSource-based suite gets a "has cases" test.
- Bootstrap stays small: automation (shots, replay-in-player, recording a session) is the kit's
  `T2U.Kit.Automation.AutomationDriver` (dev builds only); the bootstrap only implements `IAutomationHost` and calls
  `AutomationDriver.Recording?.Input(ev)` / `?.Tick()`. Game-specific debug keys go to `Scripts/Dev/`
  (`#if UNITY_EDITOR || DEVELOPMENT_BUILD`).
- Texts the logic shows (toasts, labels decided by rules) → keys in `Data/strings.json` + kit `TextTable`. Keep the JS
  text in strings.json "en" so logs/behaviour stay identical (the event log can log the key).
- `SaveData.Version = 1` from the start, `Load` ready to migrate.
- Big JS files → C# partial classes by feature (`GameModel.Input.cs`, `GameModel.Boosters.cs`…), each < 600 lines.
  Keep the order of operations identical; only the file split differs.
- Generators (SceneBuilder, UI import) must be re-runnable during the conversion AND respect `generatedFrozen`
  (create-if-missing only when frozen). Copy `kit/project-files/gitattributes` → `.gitattributes`, `editorconfig` →
  `.editorconfig` when creating the repo.
- Keep JS-derived patterns readable: if you must keep promise chains (Legacy.JsPromise), give each chain a named
  method and a one-line comment saying what the sequence does in game terms.

## Phase 2 — Skeleton + assets
- Create the Unity project (Unity 6 LTS, URP template) at the location in PLAN.md. If Unity cannot be created headless, ask the user to create it from Unity Hub with exact clicks, then continue.
- **Autopilot project creation:** do it headless: `<Unity binary> -batchmode -nographics -createProject <path> -logFile - -quit` with the newest installed Unity 6 LTS, then add URP and settings via packages + an editor script run in batch mode. If it fails because of licensing/login, log in REVIEW.md ("Cần đăng nhập Unity Hub") and set `STATUS: STOPPED`.
- Add `.gitignore` for Unity, `git init`, commit.
- **Set up Unity MCP right after the project exists** (it is a package inside the Unity project, so it cannot exist earlier):
  1. Find which Unity MCP package the user already uses: look at `Packages/manifest.json` of other Unity projects on the machine and copy that same dependency line. If none is found, add MCP for Unity: `"com.coplaydev.unity-mcp": "https://github.com/CoplayDev/unity-mcp.git?path=/MCPForUnity"`. Record the choice in PLAN.md.
  2. Open the Editor yourself (`open -n -a <Editors>/<ver>/Unity.app --args -projectPath <path>`; Unity Hub may not
     know an editor on an external drive) and wait until it has imported. MCP for Unity registers itself with Claude
     Code (stdio) for the Unity folder; check `claude mcp get UnityMCP` in the folder the sessions run from and add
     the same stdio command there if missing (lessons.md "Automation"). Do not pre-register an HTTP server.
  3. Tell the user (Vietnamese): Unity đang mở, để nguyên; mở phiên Claude mới (hoặc khởi động lại) rồi gõ /t2u —
     Claude Code only loads a newly added MCP server in a new session.
     Autopilot: do not ask. Configure Claude Code for the MCP server, then launch the Unity Editor yourself in the background (non-batch, `-projectPath <path>`), wait until it has finished importing, then end the run so the next run (a fresh session) loads the MCP server. Never run batch mode on a project while its Editor is open (the project is locked): if MCP is used, always use MCP; if MCP fails, quit the Editor and use batch mode only.
  4. Next run: verify the MCP tools work (e.g. read console). If they still do not work after one fix attempt, continue with batch mode and note it in PROGRESS.md — do not block the conversion on MCP.
- Packages: Input System, TextMeshPro (ugui), Newtonsoft JSON, Test Framework, URP, glTFast (only if GLB assets). No tween library needed for ported logic (kit Scheduler/Easing); DOTween/LitMotion only for pure-view animation (record choice). Remove packages you do not use (MCP tools sometimes add Cinemachine/ProBuilder/VFX Graph).
- Project settings: run kit `ProjectSetup` (Gamma for 2D/web-like blending, Linear only for 3D PBR games — record why; URP mobile; SRP Batcher off; Input System only; IL2CPP ARM64). Target frame rate 60 in bootstrap.
- Create the folder structure + asmdefs from mapping.md.
- Write `CLAUDE.md` in the Unity project root: conventions from mapping.md, layer rules (Core has no MonoBehaviour), "talk to the user in Vietnamese", link to the Three.js repo path and `unity-convert/`.
- Assets: copy models/textures/audio into `Assets/_Game/Art|Audio` (art is files: t2u SKILL.md "Hard rules"). Import GLB with glTFast (or convert to FBX via Blender CLI if animations need Mecanim and glTFast is insufficient — record decision). Create URP materials from exported material params (editor script, not by hand).
- Coordinate test scene `Scenes/_AxisTest.unity` (see mapping.md) and screenshot it; compare with Three.js reference.

## Phase 3 — Core logic (most important for "no bugs")
For each module in PLAN.md order (A first, then logic parts of B):
1. Port to plain C# classes in `Scripts/Core` (no MonoBehaviour, no UnityEngine if feasible; use System.Numerics or own small Vec types only if needed — otherwise UnityEngine.Vector3 is acceptable, decide once and record).
2. Keep names, numbers and order of operations identical to the JS. Do not "improve" logic during port. Note any JS bug you find in PLAN.md → "Bug gốc" and keep behavior identical unless the user approves a fix.
3. Implement the RNG from `unity-convert/rng.md` identically (same algorithm, same call order).
4. Config: kit `WebSync` copies the JSON into `Assets/_Game/Data` as TextAssets; Core receives typed plain classes
   (`JObject.ToObject<T>()`). ScriptableObjects only if designers need to edit in the Inspector.
5. Tests in `Tests/EditMode`: port existing JS tests if any; otherwise write tests for each public behavior from GAME_SPEC.md (damage, rewards, spawn rules, state transitions, edge cases).
6. Run tests. Module is done only when all tests pass and no compile errors. Tick it in PROGRESS.md, commit.
7. Replay parity: implement `IReplayGame` on the Core (or a thin adapter) and write the parity test like the kit's
   `PopRushParityTests` (`Recordings.Check(dir, name, factory, writeUnityLogs: true)`). If the web harness steps frames
   differently (e.g. drains promises), write a game-specific runner modelled on `ReplayDriver` that mirrors the harness
   order exactly. Run it as soon as the first module exists: the first differing frame tells you which module is wrong.

## Phase 4 — Scenes and prefabs (generated, not hand-placed)
- `Editor/SceneBuilder.cs`: menu `Tools/T2U/Build Scenes` reads `export/scene_*.json`, applies the coordinate conversion from mapping.md (only for data that was not imported via glTFast), creates prefabs for repeated objects, lights, camera, and saves scenes in `Scenes/`.
- Re-running the builder must be safe (overwrites generated content only). Hand-tuned changes go into prefabs/settings, not into generated scenes — record this rule in the Unity CLAUDE.md.
- Hierarchy must be readable: root groups `--- Environment ---`, `--- Gameplay ---`, `--- UI ---`, `--- Systems ---`; meaningful names, no "Object (12)".

## Phase 5 — View, UI, VFX, audio
- View MonoBehaviours read Core state and present it (Core never references View). A `GameBootstrap` MonoBehaviour creates Core, ticks it in Update/FixedUpdate, wires views.
- Animation: Animator Controllers matching JS clip names and transitions.
- UI: uGUI + TextMeshPro, one prefab per screen, Canvas Scaler (reference resolution from the original game). If the DOM is
  tagged (`data-ui-screen` / `data-ui` / `data-ui-button`, or you can add the tags in Phase 1 harness-only via a
  measuring script), use kit `UiLayoutImporter` + `UiRefs` (it also builds flexbox `UiFlex`, scrolling, lists with row
  prefabs and CSS animations `UiAnim` — fast-path.md Phase 4–5); otherwise build screens with an editor script from
  measured CSS geometry. Never hand-place UI in the Editor. Match layout from reference screenshots.
- Input: Input System; map the same actions as the JS version, touch first for mobile.
- VFX: rebuild with Particle System / sprite animations using the JS effect as the spec for MOTION (timing, speed,
  spread, fade) and the game's PNGs for the LOOK (particle material = the effect's sprite, never the default particle).
  Shaders only blend/tint/fade pictures (sprite default, additive, a tint/flash parameter); a shader that draws a
  shape or a gradient breaks the art-is-files rule. Ask the user to compare visually only for effects listed as risks.
- Every view component exposes its pictures as serialized `Sprite`/`Texture2D` fields (or reads them from a
  ScriptableObject "look" table, the C# twin of the web's `data/look.json` + sprite names) — no `Resources.Load` by
  string scattered in code, no picture created at runtime.
- Audio: AudioSource + AudioMixer (Music/SFX groups).
- After each screen/feature: Play-mode check (no errors in console), screenshot, tick PROGRESS.md, commit.

## User checkpoints (keep them rare and concrete)
Autopilot: do not stop. Do the visual check yourself (screenshots vs reference), fix obvious problems, and write the checkpoint as a REVIEW.md entry instead.
- After Phase 2: "Mở Unity, mở scene _AxisTest, xem ảnh so sánh ở unity-convert/compare/ — hướng có giống không?"
- After Phase 5: "Mở scene Main, bấm Play, chơi thử 2–3 phút, nói cho tôi chỗ nào khác bản web."

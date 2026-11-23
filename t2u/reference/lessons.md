# Lessons from earlier conversions (read before every phase)

Format: symptom → cause → fix. Add new ones at the end of the right section (2–4 lines each).
Source game in brackets: [FC] = Floaty Cats (Nov 2026, 40 levels, 57/57 recordings bit-identical).

## Logic parity
- Same seed, positions drift after a few hundred frames → C# `Math.Sin/Cos/Exp` differ from Chrome in the last bit
  (~4% of inputs) and chaotic physics amplifies it → use `T2U.Kit.JsMath` (double-double, correctly rounded = Chrome). [FC]
- Goldens made in Node disagree with Chrome → Node's V8 uses fdlibm for sin/cos; generate math goldens in Chrome
  (Playwright with the installed Chrome), the same engine that made the recordings. [FC]
- `Math.pow(x, 3)` differs in ~1/600 results even with a correctly-rounded C# pow → Chrome's pow is not correctly
  rounded. New games: no pow in logic (x*x*x, exp(x*LN2)). Legacy: accept or port fdlibm pow. [kit tests]
- `Math.round` → C# `Math.Round` rounds half to even → `Js.Round` / `Js.R3`. [FC]
- Promise chains (`tween().then(...)`) run continuations in microtask order → C# needs `Legacy.JsPromise` + `Microtasks`
  with spec-exact job counts, drained after every Update and after every input. Very costly — /game-new games use
  clock callbacks instead. [FC]
- Logic RNG and visual RNG shared one `Math.random` → split them: logic = seeded stream, visuals = separate stream,
  log `rngCalls` in snapshots to catch call-order drift at the first frame. [FC]
- Web harness used setTimeout for the fake ad → wall clock in a frame-based replay → put platform answers on the frame
  clock and record them as inputs (`{t:'adDone'}`). [FC]
- `Math.log` (cost curves, `pow(b, e) = exp(e·ln b)`) differs from System.Math.Log AND from fdlibm's log in ~4% of
  inputs → Chrome's log is correctly rounded (measured: 4M samples) → `JsMath.Log` (one Newton step in double-double,
  rounded once); `Js.Pow` for the template's `pow`. [Slime Tide]
- Check Unity Mono vs .NET 8 once (`dotnet test` on the pure Core) — they matched; fast way to run Core tests without
  opening Unity. [FC]

- Porting closures: JS `for (let k ...)` gives every callback its own `k`; a C# `for` loop variable is ONE variable
  shared by every lambda (they all see the last value) → copy it (`int kk = k;`) before any `clock.After(() => ...)`.
  Also every JS int/int division is a float division: `/ 255.0`, `(n - 1) / 2.0`, `(double)(dx * q) / n`. [Slime Tide]
- Iterate on the Core port without Unity (and without dotnet): the Mac's Mono has `csc` + `mono`. Compile Core + kit
  Runtime (Js, JsMath, SeededRandom, Scheduler, Easing, Replay) + Newtonsoft.Json.dll from Library/PackageCache (+ the
  Mono `Facades/netstandard.dll` reference), run `Recordings.Check` per recording in parallel (`xargs -P`): Slime Tide's
  18 recordings (200k frames) in 95 s; Unity Editor gave the same 18/18. NUnit tests run too via a reflection runner
  over `nunit.framework.dll` from PackageCache. [Slime Tide]
- IL2CPP builds go through clang, which may fuse `a*b+c` into one FMA on ARM64 (different last bit than Chrome/Mono) →
  replay the golden recordings ON THE DEVICE once before trusting parity there. [Slime Tide, not yet measured]

## Unity setup / rendering
- Sprites sharing a material show the wrong texture (Unity 6.3, URP) → SRP Batcher OFF in the URP asset
  (ProjectSetup does it). Re-check after Unity upgrades. [FC]
- Translucent sprites/particles brighter than the web → web blends in gamma space → `colorSpace: Gamma`. [FC]
- Spine colours lighter/darker than the web → web double-premultiplied straight-alpha atlas pages; Unity was right.
  Ask the producer which to keep (FC kept Unity). Export Spine with straight alpha and no PMA on the web side. [FC]
- `Mesh.vertices is too small` errors flood the console → dynamic particle meshes must always set full-length arrays. [FC]
- No sound in Editor Play → the generated scene had no AudioListener → SceneBuilder adds one + runtime fallback. [FC]
- Several MonoBehaviours in one .cs file → prefab references break → one MonoBehaviour per file, file name = class. [FC]
- SkeletonGraphic (Spine UI) gigantic → set scale explicitly from skeleton bounds / reference box. [FC]
- TMP `enableWordWrapping` obsolete → `textWrappingMode`. Android min API 24 obsolete → 25. [FC]
- Visual effects drift vs reference shots → effects timed with wall clock / Time.deltaTime; drive them from GAME time
  (sum of fixed steps) and tick fx before draining logic callbacks. [FC]

- Sprites drawn by a custom batch from a Unity SpriteAtlas come out stretched (a post with gaps becomes a wall) →
  the atlas trims transparent margins of Tight-mesh sprites (`sprite.textureRect` smaller than `sprite.rect`) →
  import sprites as FullRect (kit `KitImportRules` does it) and pack with tight packing off. [Slime Tide]
- Screenshots through MCP `manage_camera screenshot` render the camera only (no Screen Space Overlay UI) → for the
  whole game view use `ScreenCapture.CaptureScreenshot(path)` from `execute_code` in Play mode; set a phone-shaped
  Game view first with `PlayModeWindow.SetCustomRenderingResolution(540, 1170, "Phone")`. [Slime Tide]

## Automation (batch mode, MCP, captures)
- Batch mode fails / hangs on a project that is open in the Editor (project lock) → while the Editor is open use MCP;
  for batch jobs (captures, builds) use a clean clone: `git clone <unity repo> /tmp/<name>` (reset with
  `git checkout -- . && git clean -fdq && git pull`). [FC]
- `WaitForEndOfFrame` never fires in batch Play mode → `yield return null` before capturing. [FC]
- Edit-mode `camera.Render()` binds sprite textures wrongly → capture in Play mode (PlayModeCapture). UI tests: Canvas
  in ScreenSpaceCamera + `Canvas.ForceUpdateCanvases()` renders fine in edit mode. [FC, kit]
- Editor unfocused → Play mode throttled, MCP checks stall → `Application.runInBackground = true` in the Editor. [FC]
- MCP `execute_code`: write `UnityEngine.Object` explicitly (ambiguous with System.Object). [FC]
- `AssetDatabase.ImportPackage` (TMP Essentials) is async → in batch, wait for `importPackageCompleted` before exit
  (`TmpEssentials.ImportBatch`). [kit]
- Measuring GC: `GC.GetAllocatedBytesForCurrentThread` returns 0 in Unity Mono; ProfilerRecorder "GC Allocated In Frame"
  includes editor noise → measure `Profiler.GetMonoUsedSizeLong()` deltas around the game's own update. [FC]

- Unity Hub does not list the editor that batch mode uses (an editor on an external drive, not "located" in Hub), so
  "open the project in Unity Hub" would ask the producer to pick a version → open the Editor yourself:
  `open -n -a <Editors>/<ver>/Unity.app --args -projectPath <Unity project>`. [Slime Tide]
- MCP for Unity (#main, server 10.3.0) runs over STDIO: on Editor start it re-registers itself with Claude Code for the
  Unity project folder and REMOVES other `UnityMCP` entries (an HTTP one added beforehand, even the parent folder's) →
  do not pre-register HTTP; after the Editor has started, check `claude mcp get UnityMCP` in the folder the sessions
  run from and, if missing, add the same stdio command there (`claude mcp add UnityMCP -- <uvx> --offline --from
  mcpforunityserver==<ver> mcp-for-unity`). Tools load in the NEXT session. [Slime Tide]

## UI
- Rebuilding DOM UI by hand took days (715-line UiBuilder) → tag the DOM (`data-ui*`), export with the harness
  (`exportUi`), import with `UiLayoutImporter`; hand-tune only what the importer cannot express. [FC → kit]
- `RectTransform` with only anchors set has zero size → always set `sizeDelta`. [FC]
- Glyphs missing in the game font (★, emoji) show as boxes in TMP → use shapes/images or a font that has them. [kit]

## Maintainability (handover review of FC: 11/100 before fixes)
- Tests read `../web/...` → team clone of the Unity repo alone fails, and TestCaseSource suites silently run 0 cases
  → goldens/fixtures inside the repo (`TestData/`), plus a "has cases" test. [FC review]
- Golden parity tests frozen to the web: the first intended balance change turns 57 tests red with no way forward
  → kit `GoldenTools` Accept + `RecordingWriter`; documented workflow in README/CLAUDE.md. [FC review]
- UI generated by a 715-line C# builder from CSS numbers: moving a button meant editing code, and "Build Scenes"
  erased hand edits → DOM export + UiLayoutImporter during conversion, then `generatedFrozen`. [FC review]
- Conversion-mode CLAUDE.md ("never improve the logic", stale settings) left in the delivered repo → swap to the
  maintenance template at handover. [FC review]
- Toast texts as C# literals, save data without version, debug keys in release, automation flags in the bootstrap,
  870-line GameModel.Play.cs → handover-standard H8–H12 from day one. [FC review]

## Android build
- Mono cannot target ARM64 → IL2CPP is required. [FC]
- First IL2CPP Release build ~1,500 C++ files took hours on the laptop → development builds use Debug C++ config +
  OptimizeSize + incremental (BuildTools does it): first build still slow (~3 h on that Mac), later ones minutes.
  Release/AAB builds: run on a faster machine or CI. [FC]
- Emulator: `~/Library/Android/sdk/emulator/emulator -avd <name> -no-window -no-audio -gpu swiftshader_indirect`,
  wait `sys.boot_completed`, `adb install -r`, `adb shell monkey -p <bundle> -c android.intent.category.LAUNCHER 1`,
  screenshot with `adb exec-out screencap -p`. A system dialog ("Bluetooth keeps stopping") may cover the game —
  dismiss it. Emulator FPS is meaningless (software GPU): ask for a 10-minute real-phone test. [FC]

- MCP for Unity's "roslyn" compiler drops Microsoft.CodeAnalysis DLLs into `Assets/Plugins/Roslyn` with default
  import settings = EVERY platform → they would ship in the APK (and go through IL2CPP). Make them editor-only
  (PluginImporter) and git-ignore the folder; the tool re-installs them. The same tooling session also added
  Cinemachine, glTFast, ProBuilder and VFX Graph to the manifest: check `git diff Packages/manifest.json` before a build. [Slime Tide]
- A sprite atlas set to uncompressed (for crisp Editor captures) costs ~45 MB on a phone at 4096 × 2048 → give the
  atlas an Android override ASTC 4×4 in the atlas generator (~11 MB). [Slime Tide]
- Headless emulator: the app starts and is paused/destroyed within 0.1 s (no crash) → the screen is locked:
  `adb shell input keyevent KEYCODE_WAKEUP; adb shell wm dismiss-keyguard`, then `am start -W -n <pkg>/<activity>`
  (Unity 6: `com.unity3d.player.UnityPlayerGameActivity`). A development IL2CPP APK from a clean clone built in ~2 min
  on an M-series Mac (not hours). [Slime Tide]

## UI import (ui.json version 2, baked skins)
- Unity's titles a little WIDER than the web's (about 1.5%) → the browser kerns letter pairs, TextMesh Pro did not →
  the importer sets `fontFeatures` (kern, liga, mark, mkmk) on every text. [Slime Tide]
- A gradient button never 9-slices: no two columns identical → Chrome DITHERS gradients (±1 per channel) → runs of
  columns are compared with a tolerance of 1, against the run's first column. A sprite also differs by that much
  between two exports → `samePicture()` keeps the old name and file, so git only shows real changes. [Slime Tide]
- Seams bleed where a 9-slice border meets its stretched middle → the border takes one pixel of the uniform run with
  it (GUARD), so the seam lies between two identical pixels. [Slime Tide]
- A row of a scrolling list measured at 0 x 0, or its states all wrong → the export measures a hidden element by
  showing it ALONE; popups are still growing (CSS animation) when just shown → `settle()` finishes animations before
  any measurement. [Slime Tide]
- Every `<button>` of a game had lost its border and padding: `#ui button { border: 0; padding: 0 }` (an id) beats the
  class rules. The export shows what the browser really draws, so Unity matches the web — tell the producer, do not
  "fix" it in Unity. [Slime Tide]
- Text edges differ between web and Unity by a hairline everywhere → compare pictures by AREAS (`ui_compare.mjs`: a
  5x5 block that differs), never by counting differing pixels. First import of 15 screens: 13 without any differing
  area, 2 with a title one to two design pixels off. [Slime Tide]
- Neighbours of an icon sit ~9 design px off once flexbox runs in Unity → the icon is drawn with `transform: scale()`:
  the browser measures the DRAWN box, flexbox places the untransformed one → the export gives `flow.lbox` (the room,
  measured with the transform off) and UiFlex places the room, the drawn box keeps its size around its centre. [Slime Tide]
- A state that hides a container made the check fail on its children → the check laid out hidden containers too;
  at runtime a hidden container does nothing (no LateUpdate) → `UiFlex.LayoutAll` skips hidden ones. [Slime Tide]
- A tip card showed its text ONE LETTER PER LINE down the screen (playtest) → a wrapping text (`white-space: normal`)
  in a column flex box was exported around its resting text "-" (12 px wide) and kept that width. UiFlexItem
  `wrapText` (set by the importer for a wrapping text, own or its one text child, in a column): as wide as the text
  up to the column, as tall as its lines; a box exactly as wide as one line gets 1 unit of slack or TMP breaks it.
  Check every card that is hidden at rest with a LONG sample text, in each language. [Slime Tide]
- A card pinned by its BOTTOM in a state (`.bycharge { top: auto; bottom: … }`) grew DOWN over the button it
  explains → anchors come from the nearest side, so the card grew from its top. The export now probes each placed
  flex box (grow it, see which edge stays) and writes `pin`; the importer makes that edge the pivot (anchors stay)
  and a state that pins another edge switches the pivot (`UiStates.Change.setPivot`). [Slime Tide]
- A label hidden under its toggle in Unity was the SAME in the web's reference shot ("DMG NUMBE…"): look at the
  web's picture before blaming the port; fix the text/CSS in the web, re-export, re-sync. [Slime Tide]
- Visual comparison of a crowd game: the Unity battle shots had far more ground stains than the web's → the bootstrap
  drew every frame while the driver waited to capture (each draw stamps up to 128 decals); the web harness draws only
  on render(). Under automation the bootstrap must not draw: the driver calls Render. Also: each web shot is a FRESH
  PAGE — a view that keeps per-body memory (seen slots, fade-ins, numbers, camera pan) must be reset on Begin, or
  new soldiers skip their fade-in. [Slime Tide]
- A rate showed "4.4/s" in Unity and "4.3/s" on the web → .NET/Mono `ToString("F1")` rounds a 15-digit copy (4.35),
  JS toFixed the exact double (4.3499999…) → kit `Js.ToFixed` / `Js.ToStr` for every number the player reads. [Slime Tide]
- Do not copy a style the CSS overrides: ui.js set `top: row*290` on pick cards but `.pickcard { top: auto !important }`
  ignores it — copied into Unity it pushed the cards apart and off the list. Check the computed style. [Slime Tide]
- `python3` on an Apple-silicon Mac may have x86-only numpy (make_visual.py fails with "incompatible architecture") →
  `arch -x86_64 python3 tools/t2u/make_visual.py`. [Slime Tide]
- Parallel crowd maths WITHOUT breaking parity: a one-thread "scatter" pass (each pair once, writes both bodies) can run
  on many cores when its work units never write the same bodies and each body is written by ONE unit in the old order
  (Slime Tide: grid rows of one parity — a row writes itself and the row above). Prove it first in the fast Mono loop:
  run the units in REVERSE order; goldens unchanged = independent. Then a Burst IJobParallelFor over the units, arrays
  of the pure core pinned (GCHandle) and read through pointers, FloatMode.Strict: goldens stayed bit-identical. [Slime Tide]
- Burst can be OFF in one Editor (per-machine pref: `Jobs ▸ Burst ▸ Enable Compilation`) and the Editor compiles scripts
  in Debug: both make Editor timings meaningless (Slime Tide: 41 ms vs 6.8 ms per step). Check
  `BurstCompiler.IsEnabled` and `CompilationPipeline.codeOptimization` before measuring; trust the phone. [Slime Tide]
- A crowd drawn sprite by sprite from managed code (SpriteBatch.Add into a NativeArray) cost MORE than the whole
  simulation (Slime Tide, ~7,500 bodies: 6.7 ms vs 3.9 ms) → Burst jobs: per-body smoothing in parallel, an integer
  sort pass that also gives each body its own sprite slots, then the quads written straight into the mesh arrays in
  parallel; the rare marks in one ordered pass. 0.8 ms. Keep the managed path and compare vertex buffers side by
  side; a 1/255 colour difference in the Editor is Mono computing floats wider, not a bug. [Slime Tide]
- A slot-ordered crowd loop (each body moves, then hits others, inside one loop) cannot run on many cores as it is.
  What worked (Slime Tide, producer accepted): per-body PREPARE in parallel (timers, flags, start positions), the few
  ordered things in one thread (shared counters, sums), per-body STEER in parallel reading other bodies only from the
  start-of-move snapshot, then RESOLVE in slot order (exits, deaths, attacks; skip the dead; no attack when stunned
  this step). Keep the old loop behind a switch, prove the managed and Burst versions bit-identical (recordings, a big
  crowd, one worker), and compare old vs new with a play bot over MANY seeds: 5 seeds hid the effect, 30 showed it
  (+20 % wall damage; cause found by switching each change off in turn). Then the producer plays, then re-record the
  goldens as the game's own baseline and tag the last parity commit. [Slime Tide]

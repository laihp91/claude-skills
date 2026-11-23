# Fast path for Unity-ready games (PLAN.md: `MODE: READY`)

A game is READY when `check_ready.mjs` scores ≥ 90 with 0 ERR and it has `src/core/` with `createGame`,
`src/t2u/harness.js`, `src/t2u/scenarios.js`, `data/`. (Made by /game-new.) Otherwise `MODE: LEGACY` (normal phases).

What changes per phase:

## Phase 0 — Audit
- Run the checker; record score + MODE in PLAN.md. Module table: one row per file in src/core (A = pure logic),
  view/ui/services rows (C = rebuild). Usually 1 run.

## Phase 1 — Prepare (no game code changes at all)
- Copy/refresh `<kit>/web-tools/*` into `tools/t2u/` if missing or older. `npm install`.
- Add bot scenarios only if a rule is not covered (that is a test-only change in src/t2u/scenarios.js, allowed).
- `node tools/t2u/bot.mjs record && node tools/t2u/bot.mjs verify` (ALL deterministic), then `export` (data, ui.json,
  goldens.json) and `shots`. Done.

## Phase 2 — Skeleton
- Create the Unity project as usual, then copy `<kit>/unity/com.t2u.kit` into `<Unity>/Packages/com.t2u.kit`
  (embedded package: the team gets it with the repo; update by re-copying). Add `"testables": ["com.t2u.kit"]` only
  while checking the kit itself.
- Write `ProjectSettings/T2UKit.json` (product, bundleId, webProject relative path, scenes, portrait, colorSpace Gamma
  unless the game is 3D/PBR). Run `ProjectSetup`, `TmpEssentials.ImportBatch`, `WebSync`, `FontBuilder`.
- Run the kit tests once (13 tests) — proves JsMath/RNG/Scheduler twins work in this Unity version.
- Copy the game's `unity-convert/export/goldens.json` over the kit's `Tests/Data/goldens.json` and re-run
  `TwinGoldenTests` — proves the game's core/lib copy is unchanged (if it fails, the game edited lib: port the edit).

## Phase 3 — Core (can run on Sonnet)
- Port `src/core/*.js` line by line to `Scripts/Core` (asmdef referencing T2U.Kit, noEngineReferences). Mapping:
  `createRng` → `SeededRandom` (Range/Int/Pick/Shuffle), `createClock` → `Scheduler` (After/Every/Tween/Cancel/Clear/Count),
  `ease.x` → `Easing.X`, `M.sin/cos/exp/log/hypot` → `JsMath.*`, `r3/round` → `Js.R3/Js.Round`, `pow(b, e)` → `Js.Pow` (never `JsMath.Pow`: that is the legacy `Math.pow`), `out(name, data)` →
  `Action<string, JObject>`, `input(ev)` → `IReplayGame.Input(JObject)`, config/levels → typed classes via
  `JObject.ToObject<T>()` (Newtonsoft matches camelCase JSON to PascalCase fields).
- Follow `Tests/Editor/PopRush/PopRushGame.cs` (same structure as the template's game.js).
- Copy `../web/unity-convert/recordings/*.{input.json,state.jsonl,events.jsonl}` into `<Unity>/TestData/Golden/`
  (the Unity repo must be self-contained, handover-standard H1–H2).
- Parity test = copy `PopRushParityTests.cs` (incl. `HasRecordings`), point Dir at `KitSettings goldenDir` (TestData/Golden), use the game's factory,
  `Recordings.Check(dir, name, factory, writeUnityLogs: true)`. All recordings must pass; then
  `python3 tools/t2u/diff_states.py` for the report. This IS Phase 6a, done early.

## Phase 4–5 — Scene, view, UI
- Data and pictures: `WebSync` copies `data/*.json` (incl. `look.json`: effect colours, sizes, timings — the view reads
  them like the web's view/look.js), `public/assets/{sprites,audio,fonts}`, and the game's EXTRAS — pictures the web
  paints in code and bakes for Unity (`window.__t2u.extras`, e.g. `export/terrain/*.png`). Use those files; never
  re-create the picture in C# (art is files, t2u SKILL.md "Hard rules").
- UI: `WebSync` copies `export/ui.json` + the baked skins `export/ui/*.png`; `UiLayoutImporter` builds every screen
  prefab + UICanvas (skins as 9-sliced sprites, texts with shadow/spacing/kerning, hidden elements inactive, states).
  Game code uses `UiRefs` exactly as src/ui/ui.js uses its helpers: `text(name, v)` → `Text(name).text = v`,
  `show(name, on)` → `Show(name, on)`, `cls(name, state, on)` → `State(name, state, on)`, `style(name, 'width', n%)` on a
  `data-ui-fill` bar → `Fill(name).Set(n / 100f)`, clicks → `OnClick(id => game.Input(click))`.
- Check it before writing UI code: `Tools ▸ T2U Kit ▸ Check UI Prefabs Against ui.json` (every box, every state on and
  off, by numbers) and `Capture UI Screens` + `node tools/t2u/ui_compare.mjs <Unity>/Captures/ui` (pictures against the
  web's from `npm run t2u:uicheck`). One batch call does sync + import + check + capture:
  `-executeMethod T2U.Kit.Editor.UiPreviewCapture.SyncImportCaptureBatch` (no `-nographics`).
- The importer also builds what moves (kit 1.3) — do not hand-build these, and do not add Unity layout groups:
  `UiFlex` + `UiFlexItem` (flexbox: neighbours move when a sibling is hidden or a text grows; the Check proves it
  reproduces every resting place and every state), `ScrollRect` + "Content" for `scroll`, `UiList` + `UiRow` + a row
  prefab per row shape in `Prefabs/UI/Rows` (`refs.List(name).Add(template, key)`, `refs.Row(name).Text(".lv")`), and
  `UiAnim` playing the CSS @keyframes (shown → plays, states switch animations, `refs.Animate(name, spec)` for the
  ones the web started from code). Read the import log: it names what is NOT played (CSS `filter` such as brightness,
  rotate) and flex containers that wrap (kept at their resting places) — handle those few by hand and list them in
  PROGRESS.md.
- Bootstrap (= main.js): implement `T2U.Kit.Automation.IAutomationHost` on it (Begin / Input / Step / Render, the
  harness's begin / input / tick / render; `AutomationActive` = stop own stepping AND own drawing: the driver calls Render, like the web harness) and add `AutomationDriver` to its
  GameObject (dev builds only). That is what Phase 6 replays the web's reference shots through.
- Bootstrap loop: `FixedStepper.Advance(Time.unscaledDeltaTime, game.Update)`, route `out` events to
  AudioService/Haptics/save/ads/view fx exactly like main.js `route()`. Ads: real SDK later → call `game.Input({t:'adDone'})`.
- View: port src/view with sprites/meshes; effects on game time.

## Phase 6 — Verify (can run on Sonnet)
- Logic: already green from Phase 3; re-run after any change.
- Visual: `Tools ▸ T2U Kit ▸ Capture Reference Shots` (batch: `-executeMethod T2U.Kit.Editor.ShotCapture.RunBatch`,
  no `-quit`, no `-nographics`) replays every shot's input prefix (`unity-convert/reference/<id>.input.json`) through
  the real scene via `IAutomationHost`, draws once, and saves `unity-convert/compare/unity/<id>.png` at the web's
  viewport size; then `python3 tools/t2u/make_visual.py` → `compare/visual.html`. Fix what differs, re-capture.

## When the web game changes during the conversion
The web game stays the reference until handover. When the producer changes it (numbers, a fix, art, UI):
1. In the web project: `npm run check` (still 100), `npm run t2u:record && npm run t2u:verify` (ALL deterministic),
   `npm run t2u:export`, `npm run t2u:uicheck`, `npm run t2u:shots`. Commit (`unity-convert/` included).
2. In Unity: WebSync (data, sprites, ui.json + skins, extras); re-import UI ("new screens only"; "OVERWRITE all"
   only while no screen has been hand-tuned — it rebuilds every prefab), then Check UI Prefabs; copy recordings into
   `TestData/Golden/` again.
3. Rules changed → the C# port must change the same way: port the JS diff (`git diff <last synced web commit>` in
   `src/core`), then all parity tests green. Only numbers changed → parity tests should pass with no C# change.
4. PROGRESS.md: "Đồng bộ web <commit>" + what changed.

## Phase 7 — same as LEGACY (BuildTools, emulator, perf report, README).

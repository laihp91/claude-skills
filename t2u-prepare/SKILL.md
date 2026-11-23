---
name: t2u-prepare
description: Phase 1 of the Three.js → Unity conversion. Add non-invasive debug tooling to the Three.js game (seeded RNG hook, input record/replay, state logging, scene/config export) and capture golden recordings used later to verify the Unity port. Called by /t2u.
---

# Phase 1 — Prepare the Three.js side (tooling only, no gameplay change)

**MODE: READY** (game made with /game-new): skip everything below except "Done when" — follow
`t2u/reference/fast-path.md` Phase 1 (copy tools, record, verify, export, shots). No game code changes.

**MODE: LEGACY:** reuse instead of rewriting: the comparison tools come from `~/.claude/skills/t2u/kit/web-tools/`
(diff_states.py, make_visual.py, tolerance.json; bot.mjs as a starting point — its page API must be adapted to this
game's harness). Design the harness so the platform answers (ads, timers) are recorded as input events (lessons.md).

All new code goes into `src/__t2u__/` (or the project's source root) and is only active when the URL contains `?t2u=1`. Normal play must behave exactly as before. Commit before and after.

## 1. Deterministic hooks
- Seeded RNG: if the game uses `Math.random`, in t2u mode replace it at startup with a seeded PRNG (mulberry32, seed from `?seed=`). Write the exact algorithm in `unity-convert/rng.md` so C# can implement the identical one.
- Time: if the game uses variable delta time, in t2u mode force a fixed delta (1/60) per frame so runs are reproducible.

## 2. Input recorder / replayer
- Record all input events with frame number: key down/up, pointer down/move/up (normalized 0–1 screen coords), plus any UI button clicks (by button id/name).
- Save as `unity-convert/recordings/<name>.input.json` (download button in t2u mode, or write via a tiny local dev endpoint if the dev server allows; simplest: copy JSON from a debug panel / console).
- Replayer: `?t2u=1&replay=<name>` feeds the recorded events at the same frames.

## 3. State logger
- Every N frames (default 10) log a compact snapshot of game state: score, currency, player position/rotation, HP, enemy count + positions, current game state/screen, timers, RNG call count.
- Use the same field names you will use in C#. Round floats to 3 decimals.
- Save as `unity-convert/recordings/<name>.state.jsonl` (one JSON object per line).

## 4. Exports
- `unity-convert/export/scene_<name>.json`: for each level/scene, walk the scene graph and export nodes: name, parent, position/rotation(quaternion)/scale in THREE.JS space (conversion happens in Unity importer), asset reference (model path or primitive type + params), material params (color, map, metalness, roughness, emissive, opacity, transparent), light params, camera params.
- `unity-convert/export/config/*.json`: every gameplay config/constant table, extracted as data (copy JSON files that already exist; for constants hard-coded in JS, extract them into JSON without changing the JS behavior).
- `unity-convert/export/assets.json`: list of all asset files with type and where used.
- Reference screenshots: in t2u mode, a key (e.g. F9) or URL param sets fixed camera positions (list them in `unity-convert/export/shots.json`: camera pos/rot/fov) and saves PNGs to `unity-convert/reference/`.

## 5. Golden recordings
Use the browser (built-in browser or Chrome tools if available) to run the game in t2u mode and create recordings. Minimum set:
- `boot`: start game, idle 10 s.
- `basic`: 30–60 s of normal play covering main mechanics.
- one per important feature/level (from PLAN.md).
If you cannot drive the game yourself, ask the user in Vietnamese with exact steps: "Mở link ..., chơi 1 phút như bình thường, bấm nút 'Lưu bản ghi' khi xong." Keep it to one simple request.

**Autopilot (and preferred in general):** create recordings yourself with a headless browser bot, no human needed:
- Install Playwright as a dev tool inside `unity-convert/tools/` (its own package.json, so the game's dependencies are untouched) and Chromium.
- Write `unity-convert/tools/bot.mjs`: starts the dev server (or serves `dist/`), opens the game with `?t2u=1&seed=<n>`, plays with a simple scripted/heuristic bot (tap/drag/keys that exercise the main mechanics from GAME_SPEC.md: start, move, attack/collect, lose, retry, open each UI screen), records input + state log, saves files to `recordings/`.
- Make at least: `boot`, `basic` (60 s), one per main feature, and 3 random-seed runs of 60 s. The bot does not need to play well; it needs to cover the code paths.
- Also capture the reference screenshots with the same bot.

## Done when
- Normal game unchanged (play without `?t2u=1` works as before).
- Replay of each recording produces the same state log twice in a row (proves determinism). If not deterministic, find and fix the source in the hooks (not in gameplay), or document the non-deterministic parts in PLAN.md (e.g. physics) and exclude them from strict comparison.
- Exports and reference screenshots exist.
- PROGRESS.md updated, commit done. After this phase the Three.js code is read-only.

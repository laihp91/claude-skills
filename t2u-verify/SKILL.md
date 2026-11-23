---
name: t2u-verify
description: Phases 6–7 of the Three.js → Unity conversion — verify logic parity by replaying golden recordings and diffing state logs, compare reference screenshots, then profile and optimize for mobile. Also use whenever the user reports "bản Unity khác bản web". Called by /t2u.
---

# Phases 6–7 — Verify and optimize

## Phase 6a — Logic parity (replay diff)
1. For every recording in `unity-convert/recordings/`, run the Unity replay runner → `<name>.unity.state.jsonl`.
2. Use the kit's `diff_states.py` (copy from `~/.claude/skills/t2u/kit/web-tools/` with ucdir.py + tolerance.json): compares JS vs Unity state logs line by line.
   - Exact match for ints/strings/states; tolerance for floats (default 0.01, configurable per field in `unity-convert/tools/tolerance.json`).
   - Output: first diverging frame, field, JS value vs Unity value, plus a summary table → `unity-convert/compare/logic_report.md`.
3. On divergence: find the FIRST diverging frame, inspect the responsible module, compare JS vs C# line by line (common causes: Z flip, degrees/radians, int division, RNG call order, sort stability, update order, float vs double). Fix C#, add a unit test reproducing the case, rerun.
4. Physics-driven fields (if marked non-deterministic in PLAN.md): compare with loose tolerance or statistical checks (e.g. similar end positions/outcomes), not frame-exact.
5. Done when all recordings pass. Commit.

## Phase 6b — Visual parity
1. Unity-ready games (fast path): kit `Tools/T2U Kit/Capture Reference Shots` replays each shot through the scene
   (`IAutomationHost` + `AutomationDriver`) → `unity-convert/compare/unity/<id>.png`. Legacy games: an editor script
   that, for each entry in `export/shots.json`, places the camera (converted coords) and renders to the same folder.
2. Run the kit's `make_visual.py` → `unity-convert/compare/visual.html` (web | Unity | difference per shot, captions from shots.json `desc` in Vietnamese).
3. Look at the images yourself first and fix obvious issues (orientation, scale, missing objects, much darker/brighter lighting, wrong colors from sRGB/linear).
4. Then ask the user once: "Mở file visual.html, xem từng cặp ảnh, ghi lại chỗ nào khác." Fix what they report. Autopilot: skip asking; add a REVIEW.md entry pointing to visual.html and listing any differences you could not fix.

## Phase 7 — Mobile performance
1. Settings: `Application.targetFrameRate` (60 or 30 per game type — ask the user only if unsure about game type), VSync off on mobile, URP mobile asset: shadows minimal, HDR off unless needed, MSAA 2x or off, render scale 0.8–1.0.
2. Batching: static batching for environment, GPU instancing / SRP Batcher compatible materials, texture compression ASTC, mipmaps, max size sane.
3. Pooling for spawned objects; zero GC allocations per frame in gameplay (check with Profiler / Unity MCP profiler tools).
4. Build Android (development build first) with kit `BuildTools.BuildAndroidDev` — in a clean clone if the Editor is open (lessons.md "Automation", "Android build"). Copy the APK to `<parent>/dist-android/`. If a device/emulator is available, run and capture FPS + Profiler data; otherwise give the user exact steps to install and play 10 minutes, then report "nóng máy / FPS".
   Autopilot: start an existing Android emulator (`emulator -avd <name> -no-window` if supported), install the APK with `adb`, run an automated play session (simple input via `adb shell input` or an in-game autoplay flag in development builds), collect frame stats (`adb shell dumpsys gfxinfo <package>`). Emulator FPS is only indicative: add a REVIEW.md entry asking the user to test 10 minutes on a real phone for heat. If no emulator exists, skip device testing and note it in REVIEW.md.
5. Write `unity-convert/compare/perf_report.md`: before/after numbers, settings changed.

## User reports a difference ("bản Unity khác bản web", `/t2u sửa …`)
1. Restate it in one line; find a recording/shot that shows it (or make one with the bot / a short replay prefix).
2. Logic difference → first differing frame in diff_states / the parity test → fix C# → regression test. Visual → capture
   both, compare, fix view/UI, re-capture.
3. Re-run all parity tests, commit, report in ≤ 5 Vietnamese lines what was wrong and what changed.

## Handover gate (before STATUS: DONE)
1. Copy goldens into the repo if not yet (H1–H3); the replay tests must read `TestData/Golden`.
2. Replace the Unity `CLAUDE.md` with `t2u/reference/CLAUDE.maintenance.template.md` (filled in) — the conversion rules
   move to `../web/unity-convert/` history. Write README from `README.template.md` (all sections, real file:line examples).
3. Set `"generatedFrozen": true` in ProjectSettings/T2UKit.json; check game generators respect it.
4. `.gitattributes`, `.editorconfig`; 0 compile warnings; all tests green.
5. `python3 ~/.claude/skills/t2u/kit/tools/handover_check.py <Unity project>` → 0 FAIL, score ≥ 85. Paste the score in
   REVIEW.md and perf_report/README.
6. Try the team workflow once yourself: make a tiny intended rule change on a scratch branch, see golden tests fail,
   Accept, see them pass, then discard the branch. Note the result in REVIEW.md.

## Final handover
- Unity `README.md` for the team (English): how the project is structured, how to run tests, how to rebuild scenes/configs from JSON, where SDKs go.
- Final short Vietnamese summary to the user: what is done, what differs from the web version (if anything) and why, next steps (SDKs: ads, IAP, analytics). Write it at the top of REVIEW.md too, then set `STATUS: DONE` in PROGRESS.md and commit.

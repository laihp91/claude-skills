---
name: t2u-audit
description: Phase 0 of the Three.js → Unity conversion. Analyze the Three.js project and produce unity-convert/PLAN.md and PROGRESS.md. Called by /t2u; do not change any game code here.
---

# Phase 0 — Audit the Three.js project

Goal: fully understand the game WITHOUT changing it, and produce a conversion plan.

## Steps
1. Make sure the project is a git repo and the working tree is committed (`git init` + initial commit if needed).
2. Readiness: run `node ~/.claude/skills/t2u/kit/web-tools/check_ready.mjs .` (score, ERR/WARN list). If the game is
   Unity-ready (see `t2u/reference/fast-path.md`: score ≥ 90, 0 ERR, src/core + src/t2u + data/), write `MODE: READY`
   at the top of PLAN.md and follow the fast path; otherwise `MODE: LEGACY`. Put the score in the report.
3. Inventory (read code, do not guess):
   - Build tool and entry point (Vite/webpack/plain), how to run it locally (`npm run dev` etc.). Run it once to confirm it starts.
   - Every source file: path, line count, role (logic / rendering / UI / input / audio / physics / util / config).
   - Game loop: where update happens, fixed or variable timestep, how time is measured.
   - State: where game state lives (globals, singletons, classes), how it changes.
   - Randomness: every use of Math.random / seeded RNG.
   - External libs from package.json (three addons, physics, tween, UI libs) and what each one is used for.
   - Assets: models (GLB/GLTF/FBX/OBJ), textures, audio, fonts, procedural geometry created in code.
   - Every look the web draws IN CODE (canvas, CSS shapes, generated textures, coloured geometry): list it with the PNG
     it will use in Unity — an existing sprite, a UI skin from the export, a game extra the export bakes
     (`window.__t2u.extras`), or "cần ảnh" (goes into REVIEW.md). Unity never draws art in code (t2u SKILL.md "Hard
     rules"), so nothing may stay without a file.
   - Custom shaders / ShaderMaterial / onBeforeCompile / post-processing passes.
   - UI: HTML overlay or in-canvas; list of screens.
   - Input: keyboard, mouse, touch, gestures.
   - Persistence: localStorage keys.
4. Classify each module by how hard it is to port:
   - **A — pure logic** (no `three` import, no DOM): port nearly 1:1.
   - **B — mixed** (logic tangled with meshes/DOM): logic must be separated during port (do NOT refactor the JS; separate in C#).
   - **C — engine-specific** (rendering, shaders, UI, physics): rebuild the Unity way, using the JS as visual/behavior spec.
5. Risk list (check `t2u/reference/lessons.md` — most risks have a known fix): things that will not match exactly (physics engine, custom shaders, lighting intensity, post effects, fonts) and how you will handle each.
6. Write `unity-convert/GAME_SPEC.md`: plain description of game rules, all tunable numbers and where they live, state machine (screens/game states), controls. This is the "source of truth" for behavior.
7. Write `unity-convert/PLAN.md`:
   - Summary (what the game is, size of codebase)
   - Module table: JS file → planned C# class(es) → layer (Core/View/UI/Infra) → class A/B/C → order
   - Asset list and import plan
   - Risks + approach
   - "Quyết định" section: every default you chose (Unity version, URP, UI system, tween lib, etc.) with 1-line reason
   - Unity project location: default is a sibling folder `../<ProjectName>-Unity`
8. Write `unity-convert/PROGRESS.md` starting with the two machine lines `CURRENT_PHASE: 1` and `STATUS: RUNNING`, followed by a checklist of all phases and all modules (unchecked). Create an empty `unity-convert/REVIEW.md` titled "Những điều cần anh xem lại".
9. Check the environment and record in PLAN.md: Is Unity installed? Check `/Applications/Unity/Hub/Editor`, external drives under `/Volumes/*/` (e.g. `Unity/Editors`), and Unity Hub's secondary install path (`~/Library/Application Support/UnityHub/secondaryInstallPath.json`). Record the exact Editors folder in PLAN.md. Which versions? Is Unity MCP connected? Is Node available? If Unity is missing, tell the user exactly what to install (Unity Hub + Unity 6 LTS with Android/iOS modules). Also check: Playwright can be installed (`npx playwright --version`), Android SDK/emulator present (`~/Library/Android/sdk`, `adb`, `emulator -list-avds`). Record results in PLAN.md.
   Autopilot: if Unity is missing, continue with Phases 0–1 (they do not need Unity), log it at the top of REVIEW.md, and set `STATUS: STOPPED` only when Phase 2 cannot start.

## Report to the user (Vietnamese, short)
- Game có bao nhiêu phần, phần nào dễ/khó convert (dùng ngôn ngữ đời thường).
- Rủi ro chính (tối đa 3).
- Ước lượng số lần chạy /t2u.
- Bạn cần làm: usually "Xem qua PLAN.md nếu muốn, rồi gõ /t2u". Only ask if a real product decision is needed (e.g. "giữ hiệu ứng X hay bỏ?").

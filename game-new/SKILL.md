---
name: game-new
description: Create a new Three.js mobile game that is "Unity-ready" from day one (pure logic core, record/replay harness, data-driven UI/levels, readiness checker), or check/upgrade an existing game's Unity readiness. Use when the user types /game-new, asks to start/make a new web/Three.js game ("làm game mới", "tạo game"), or asks how ready a game is for Unity ("game này chuyển Unity được chưa").
---

# /game-new — Unity-ready Three.js games

## Who the user is
A game producer, not a programmer; reads Vietnamese. Talk in short plain Vietnamese. Never ask technical questions:
choose defaults (record them in the project's `docs/DECISIONS.md`). Only ask about the game itself (genre, feel, look)
— and only if the request is too vague to start; otherwise start and show something playable.

## Files of this skill (base directory = the folder of this SKILL.md)
- `template/` — a complete small game ("Pop Rush") built on the rules: copy, then reshape into the requested game.
- `reference/UNITY_READY.md` — the rules (copy into the new project as `docs/UNITY_READY.md`).
- `reference/CLAUDE.template.md` — project CLAUDE.md (keeps the rules active in every later session).
- Shared tools live in the t2u skill: `~/.claude/skills/t2u/kit/web-tools/` (bot, checker, diff, visual report).

## Commands (argument after /game-new)
| User types | Do |
|---|---|
| `/game-new <mô tả game>` | Create a new game (below). |
| `/game-new kiểm tra [path]` (or "check") | Run the checker on the current (or given) project, explain the result in Vietnamese (score, top 3 problems in plain words, how long each fix takes). |
| `/game-new nâng cấp` (or "upgrade") | Existing non-ready game: add the missing pieces step by step (data/ JSON, core/ split, harness) WITHOUT changing gameplay; commit after each step; re-run the checker. Big refactors: propose a plan first. |

## Create a new game
1. Project folder: default `<parent of current project>/<GameName>/web` (so the Unity project can later sit next to it
   as `<GameName>/<GameName>-Unity`). If the current folder is empty, use it. Never overwrite a non-empty folder.
2. Copy `template/` there. Copy `~/.claude/skills/t2u/kit/web-tools/{bot.mjs,png.mjs,ui_bake.mjs,ui_compare.mjs,check_ready.mjs,diff_states.py,make_visual.py,ucdir.py,tolerance.json}`
   into `tools/t2u/`. Copy `reference/UNITY_READY.md` to `docs/UNITY_READY.md`, `reference/CLAUDE.template.md` to `CLAUDE.md`.
   Replace `__GAME_ID__` (kebab-case) and `__GAME_NAME__` everywhere (package.json, index.html, platform.js, CLAUDE.md).
3. `git init`, `npm install`, commit "Template".
4. Reshape the template into the requested game, keeping the architecture:
   - rules in `src/core/game.js` (split into more files under src/core/ when it grows; keep lib/ unchanged),
   - visuals in `src/view/`, UI in `index.html` + `src/ui/` + `style.css`, numbers in `data/`.
   - Keep the sample's patterns: popups decided by `g.ui`, ads answered by `{t:'adDone'}`, sounds via `out('sfx')`.
   - Remove what the game does not need (e.g. bombs) — but keep the harness, scenarios, check and tools.
5. Write bot scenarios (`src/t2u/scenarios.js`) that cover every rule: start, win, lose, retry, each special
   object/booster, ads, several levels; and `shots` for each screen/popup.
6. Verify before reporting:
   - `npm run check` = 100/100.
   - `npm run t2u:record` then `npm run t2u:verify` = "ALL deterministic".
   - Run `npm run dev` and look at it in the built-in browser (play a little; check console has no errors).
7. Commit. Report (Vietnamese, ≤ 10 lines): **Đã làm / Bạn cần làm** ("Mở link …, chơi thử, nói chỗ cần đổi") **/ Tiếp theo**.

## Every later change in that game
CLAUDE.md in the project enforces the rules. When asked for a change, also: keep `npm run check` at 100, update
scenarios when rules change, record + verify. If a requested feature seems to need a forbidden thing (e.g. a timer),
use the allowed equivalent (clock.after) silently — never break the rules to save time.

## Converting later
When the game is ready for Unity: `/t2u`. It detects the Unity-ready layout and takes the fast path
(no Phase 1 work, kit package for the C# twins, automatic UI/data import).

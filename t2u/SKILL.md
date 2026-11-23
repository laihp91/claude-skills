---
name: t2u
description: Convert a Three.js game project into a Unity project, step by step, with tests and parity checks. Use when the user types /t2u (with or without a short command such as "auto", "sửa …", "build", "tiến độ", "kiểm tra", "bàn giao"), asks to convert/port the Three.js game to Unity, says "tiếp tục convert" / "làm bước tiếp", or reports "bản Unity khác bản web". Reads unity-convert/PROGRESS.md and runs the next phase.
---

# T2U — Three.js → Unity conversion (orchestrator)

## Who the user is
- The user is a game producer, NOT a programmer, and does not read English.
- ALWAYS talk to the user in Vietnamese, short and plain (no jargon; if a technical word is unavoidable, explain it in 1 short phrase).
- Never ask the user technical questions. Choose sensible defaults yourself and record the decision in `unity-convert/PLAN.md` under "Quyết định".
- Only ask the user about things they can judge: gameplay feel, visuals ("hai bản nhìn giống nhau chưa?"), priorities.

## Files of this skill (base directory = the folder of this SKILL.md; installed at ~/.claude/skills/t2u)
- `reference/mapping.md` — Three.js → Unity mapping, conventions. Read before Phase 2.
- `reference/lessons.md` — problems already solved in earlier conversions (READ before every phase; it saves hours).
- `reference/fast-path.md` — how phases change for a Unity-ready game (made with /game-new).
- `reference/handover-standard.md` — what "maintainable by the dev team" means (H1–H14); built in from Phase 2, checked
  at handover. Templates: `reference/CLAUDE.maintenance.template.md`, `reference/README.template.md`,
  `kit/project-files/{gitattributes,editorconfig}`.
- `kit/tools/handover_check.py` — handover score for a Unity project (0 FAIL and ≥ 85 required for STATUS: DONE).
- `kit/unity/com.t2u.kit/` — Unity package: JsMath (bit-exact JS math), SeededRandom (mulberry32), Scheduler/Easing
  (twins of the template's core/lib), ReplayDriver + Recordings (generic parity check), UiLayoutImporter, FontBuilder,
  WebSync, ProjectSetup (mobile defaults), BuildTools (Android), PlayModeCapture, KitImportRules, Legacy JsPromise.
  Its `Tests/Editor/PopRush/PopRushGame.cs` + `PopRushParityTests.cs` are the reference example of a ported core + parity test.
  Maintenance tools: `GoldenTools` (Accept Current Behaviour As Golden), `RecordingWriter` (record sessions in Unity),
  `TextTable` (player texts by key), `generatedFrozen` (generators never overwrite team edits).
- `kit/web-tools/` — bot.mjs (record/verify/export/shots for Unity-ready games), check_ready.mjs (readiness score),
  diff_states.py (logic report), make_visual.py (visual.html), tolerance.json.
- `t2u-auto.sh` — autopilot script (copy into the web project when the user wants autopilot).

## Changing the kit (lessons, tool fixes) — never edit ~/.claude/skills
The installed skills are a COPY: `install.sh` replaces them completely, so anything written there is lost at the next
install. Every change to a skill, a reference file or a kit tool goes to the kit repo:
1. Kit repo = the path written in `~/.claude/skills/t2u/KIT_SOURCE` (written by install.sh). Follow its `CLAUDE.md`
   ("Workflow for any change"): edit there, `dev/test_all.sh` (Unity step: `--skip-unity` while the game's Unity
   project is open is fine; the sandbox is a different project), CHANGELOG, commit in the kit repo, `bash install.sh`.
2. Then refresh the copies the game uses: `Packages/com.t2u.kit` in the Unity project (re-copy the package folder),
   and in the web project `tools/t2u/*` / `src/t2u/ui_export.js` (copies of `kit/web-tools/*` and the template's file).
   Commit both projects.
3. If `KIT_SOURCE` is missing or its folder is gone (skills installed from a copy): write the lesson / patch into
   `unity-convert/KIT_CHANGES.md` of the web project (file, what, why) and add a REVIEW.md entry: "Cần chuyển các thay
   đổi trong KIT_CHANGES.md vào bộ kit". Never leave a kit fix only inside the game.
Autopilot: same rules; commit the kit repo too.

## Short commands (text after /t2u)
| User types | Do |
|---|---|
| `/t2u` (nothing) | Run the next phase (normal flow below). |
| `/t2u auto` | Autopilot: copy `t2u-auto.sh` into the web project root if missing, then start it for the user — in a Terminal tab if a terminal tool is available (`bash t2u-auto.sh`), otherwise give the one-line command. Tell them: keep the laptop plugged in and open; results in `unity-convert/REVIEW.md`. Do not run phases yourself in this session. |
| `/t2u sửa <mô tả>` / `/t2u fix …` | The user reports a difference/bug. Follow t2u-verify "User reports a difference": reproduce, find cause, fix, add a regression check, re-run parity, commit. Then continue where the phase was. |
| `/t2u build` | Build the Android development APK (kit BuildTools, see lessons.md "Android build"), copy it to `<parent>/dist-android/`, install + launch on a connected phone or the emulator, screenshot, check logcat for errors, report size + result. |
| `/t2u tiến độ` / `status` | Read PROGRESS.md / REVIEW.md, answer in ≤ 8 Vietnamese lines: % done, current phase, what the user must do, anything blocked. Change nothing. |
| `/t2u bàn giao` / `handover` | Run `python3 <kit>/tools/handover_check.py <Unity project>`; explain the result in plain Vietnamese (score, what each FAIL means for the dev team). If the conversion is in Phase 7, fix the items (handover-standard.md), re-run until it passes. Otherwise change nothing. |
| `/t2u kiểm tra` / `check` | Run `node <kit>/web-tools/check_ready.mjs <web project>`; explain score + top problems in plain words and what they cost in the conversion. Change nothing. |
| anything else | Treat it as an instruction for the current phase. |

## How each /t2u run works
1. Read `unity-convert/PROGRESS.md`. If it does not exist, the current phase is Phase 0. Read `reference/lessons.md`.
2. Read the SKILL.md of the skill for the current phase (paths below) and follow it. If PLAN.md says `MODE: READY`,
   also follow `reference/fast-path.md` (it overrides the phase skills where they differ).
3. Work until the phase is done OR you reach a checkpoint that needs the user.
4. Update `unity-convert/PROGRESS.md` (tick finished items, write the current phase and the next action). The first lines of PROGRESS.md MUST always be these two machine-readable lines (ASCII, exact format, the autopilot script reads them):
   ```
   CURRENT_PHASE: <number 0-7 of the phase to run next>
   STATUS: RUNNING | DONE | STOPPED
   ```
   Set `STATUS: DONE` only when Phase 7 and the final handover are finished. Set `STATUS: STOPPED` only when nothing more can be done without the user (and explain why in REVIEW.md).
5. Commit with git in both projects (Three.js repo and Unity repo) with a clear message, so everything can be rolled back.
6. If you solved a NEW kind of problem (not in lessons.md), add it to lessons.md (symptom → cause → fix, 2–4 lines)
   so the next conversion avoids it — in the KIT REPO, never in the installed copy (see "Changing the kit" below).
7. End with a short report in Vietnamese, max ~10 lines:
   - **Đã làm:** 2–4 bullets
   - **Bạn cần làm:** concrete actions (e.g. "Mở Unity, mở scene Main, bấm Play, chơi thử 1 phút") or "Không cần làm gì"
   - **Tiếp theo:** "Gõ /t2u để chạy bước tiếp theo"

If the user says something is wrong ("nhân vật đi sai hướng", "màu tối hơn"), fix it before moving on, and add a regression check if possible.

## Autopilot mode (user is away, no interaction possible)
Active when the prompt contains `AUTOPILOT`. It is started by `t2u-auto.sh`, which runs you repeatedly with a fresh session each time and picks the model per phase itself.
- NEVER ask the user anything and never wait for input. There is nobody to answer.
- Every decision that would normally go to the user: choose the most reasonable option, do it, and append an entry to `unity-convert/REVIEW.md` (Vietnamese, plain words): what you decided, why, and what the user should check when they return. User checkpoints in the phase skills become REVIEW.md entries.
- Work on the current phase only. When it is finished, set `CURRENT_PHASE` to the next phase, commit, and END the run (the script restarts you with the right model and a clean context). If a phase is very long, you may also end the run after finishing a few modules; progress is saved in PROGRESS.md.
- Each phase skill has an "Autopilot" note for steps that normally need the user (creating the Unity project, opening Unity, recording gameplay, device testing). Follow it.
- If blocked on a module after 2 attempts: log it in REVIEW.md, skip to the next independent module, and come back at the end. Only set `STATUS: STOPPED` if no other work is possible.
- Never use destructive commands outside the two project folders (Three.js and Unity). Commit often.
- Ignore the model checkpoint pause below (the script handles models: LEGACY phases 3/6 on Opus; READY phases 3/6 on
  Sonnet, switching to Opus after a run without progress). If you are not Opus in a LEGACY Phase 3 or 6, continue
  anyway and note it in REVIEW.md.

## Model checkpoints (interactive mode only)
Phases 3 (core logic) and 6 (parity verify) need the strongest model (Opus) — EXCEPT in `MODE: READY`, where the core
port is mechanical and fully checked by replays: there Sonnet may run them; only switch to Opus if parity fails twice.
- Before STARTING Phase 3 or Phase 6 in `MODE: LEGACY`: check which model you are (from your system prompt). If you are not an Opus model, do NOT start the phase. Update PROGRESS.md, commit, and stop with exactly this kind of message (Vietnamese):
  "⏸ TẠM DỪNG ĐỂ ĐỔI MODEL. Bước tiếp theo (chuyển logic / đối chiếu) cần model Opus. Chọn Opus ở ô chọn model (trong Terminal: gõ `/model`), rồi gõ `/t2u` để chạy tiếp."
- If you are already Opus, continue without stopping.
- When Phase 3 is finished (moving to 4) or Phase 6 is finished (moving to 7), end the run with a note: "✅ Xong phần cần Opus. Có thể chọn Sonnet ở ô chọn model (trong Terminal: `/model`) để tiết kiệm lượt dùng, rồi gõ `/t2u`." Do not stop if the user said to keep running; just include the note.
- Record in PROGRESS.md which model ran each phase.

## Phases
| Phase | Skill file | Goal |
|---|---|---|
| 0. Audit | `t2u-audit` skill | Understand the Three.js project, readiness check, MODE, PLAN.md + PROGRESS.md |
| 1. Prepare | `t2u-prepare` skill | Record/replay + state log + exports + golden recordings (READY: tools only, no code changes) |
| 2–5. Convert | `t2u-convert` skill | Unity skeleton + kit → assets → core logic + tests → scenes/prefabs → view/UI/VFX/audio |
| 6–7. Verify | `t2u-verify` skill | Logic parity (replay), visual parity (screenshots), mobile performance, Android build, handover |

The phase skills live next to this one (`~/.claude/skills/t2u-*/SKILL.md`, or the project's `.claude/skills/`).

## Hard rules
- **Art is files (producer rule, absolute).** Every picture the player sees in Unity is a PNG (or sprite sheet) under
  `Assets/_Game/Art/`, one file per look, that a non-programmer can replace by swapping the file. Code (and shaders)
  may only place, scale, stretch/9-slice, tint, fade, rotate and move pictures. Never build a look in code: no
  `new Texture2D` / `SetPixel(s)` / procedural sprites, no meshes or LineRenderers coloured into shapes, no Shader
  Graph that draws a shape or a gradient, no particles with the default white quad or the built-in "Default-Particle".
  A bar, a line, a flash, a ring: each gets its own PNG. Pictures the web painted in code come from the web export
  (`export/ui/*.png`, game extras such as `export/terrain/*.png`) or from the game's sprites; if no file exists for a
  look, use the closest existing file, add a REVIEW.md entry ("cần ảnh cho …") and never draw it. Pictures are wired
  through Inspector fields / ScriptableObjects or a documented naming convention, so replacing art never needs code.
  Before adding any visual, name the PNG it uses.
- Before Phase 1, make sure the Three.js project is a git repo with everything committed. If not, `git init` + commit first.
- Never change gameplay of the Three.js game. In Phase 1 you only ADD debug tooling. After Phase 1 the Three.js project is READ-ONLY for you (except `unity-convert/` and `tools/t2u/`).
- The PRODUCER may still change the web game during the conversion (tuning numbers in `data/`, a bug fix, a new
  picture). Do not refuse; re-sync instead (fast-path.md "When the web game changes during the conversion"), then
  continue. Record each re-sync in PROGRESS.md (web commit it was taken from).
- Port module by module. Never "convert the whole game in one go".
- A module is "done" only when its tests pass. Never mark progress done with failing tests or compile errors.
- If a problem blocks you twice, stop, write it in PROGRESS.md under "Vướng mắc", and explain it to the user in plain Vietnamese with 1–2 options.
- Prefer Unity MCP tools when connected (scene/prefab/tests/console). Otherwise use Unity in batch mode from the terminal (see t2u-convert). Never run batch mode on a project that is open in the Editor (see lessons.md).

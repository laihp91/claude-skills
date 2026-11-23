# Unity-ready rules for Three.js games

Why: a game written this way converts to Unity mostly mechanically — the logic is ported 1:1, checked automatically
against recordings, and the UI/data/assets are imported by tools. Every rule below removed days of work in the
Floaty Cats conversion. `npm run check` (tools/t2u/check_ready.mjs) enforces the checkable ones.

## 1. Folders (one job each)
```
src/core/        game RULES only (ported 1:1 to C#)          → Unity Scripts/Core (asmdef, no UnityEngine)
src/core/lib/    rng, clock, ease, math (C# twins in T2U Kit)  → T2U.Kit (do not edit casually; re-run goldens if you do)
src/view/        three.js drawing, reads core state            → Unity View MonoBehaviours
src/ui/          DOM UI, reads core state, sends clicks         → uGUI prefabs generated from export/ui.json
src/services/    audio, save, haptics, ads (web versions)       → Unity Infra services
src/t2u/         harness (record/replay), scenarios, ui export  → not ported (tooling)
src/main.js      composition root (wires everything)            → GameBootstrap
data/            config.json, levels.json … (all tuning numbers) → Assets/_Game/Data (same files)
public/assets/   images (PNG), audio (ogg/mp3), fonts (TTF/OTF) → Assets/_Game/Art|Audio (same files)
```

## 2. Core logic (src/core) — hard rules (ERR in the checker)
- No `three`, no DOM/`window`/`document`/`localStorage`/`navigator`, no `requestAnimationFrame`.
- No `Math.random()` — use `createRng(seed)` (lib/rng.js). Logic RNG is created from the seed given to createGame.
- No wall clock (`Date.now`, `performance.now`) — time only comes from `update(dt)`.
- No `setTimeout`/`setInterval`, no `Promise`/`async`/`await`/`.then` — use `clock.after/every/tween` (lib/clock.js).
  (Promise continuation order cost a whole custom scheduler in Floaty Cats.)
- Shape: `createGame({ config, levels, seed, save, out })` returns `{ state, input(ev), update(dt), snapshot() }`.
  - `input(ev)`: EVERY player action and platform answer is a plain JSON event: `{t:'pd', x, y}` (world coords),
    `{t:'click', id}`, `{t:'start', level}`, `{t:'adDone', placement, ok}` … This is what gets recorded/replayed.
  - `out(name, data)`: EVERYTHING the outside needs (sounds `sfx`, `vibrate`, `save`, `ad`, game events for effects).
    Data is plain JSON; floats rounded with `r3`.
  - `update(dt)` is always called with exactly 1/60 (main.js fixed-step loop; harness; Unity FixedStepper).
  - `snapshot()` returns plain JSON of the important state, floats with `r3`, plus `rng.calls` and `clock.count`.
  - `state` is read-only for view/UI.
- UI state that decides what buttons do (which popup is open) lives in core (`g.ui`), not in the DOM.
- Texts decided by the rules (toasts, dynamic labels) are KEYS: `out('toast', {key: 'toast.noSlots'})`; the UI looks
  them up in `data/strings.json` (`{ "en": {...}, "vi": {...} }`). Static texts in index.html are fine (they are imported
  into the Unity prefabs and edited there).
- Save data has a `version` field from the first release; loading migrates older versions.
- Keep each file under ~500 lines: split rules by feature (core/boosters.js, core/levels.js…) — the C# port keeps the
  same split (partial classes), which keeps the Unity project readable.

## 3. Core logic — exactness rules (WARN in the checker)
- Math: only `+ - * /`, `Math.min/max/floor/ceil/abs/sqrt/trunc/sign`, and `M.sin/cos/exp/log/hypot/PI/LN2` from
  lib/math.js (bit-exact twins in C#). No `Math.pow` (Chrome rounds some results differently from any C# pow): write
  `x * x * x`; `2^x = M.exp(x * M.LN2)`; a fractional power (cost curves, reward formulas): `pow(b, e)` from
  lib/math.js (= `M.exp(e * M.log(b))`, C# `Js.Pow`). No `atan2/tan/asin/acos` in rules (fine in view).
- No `Math.round` — use `round`/`r3` (JS rounds .5 up; C# rounds half to even).
- `sort()` always with a comparator that has a tie-breaker (sort stability/order differs between engines).
- Iterate arrays, not object keys (`for…in`, `Object.keys` order is easy to get wrong in C#). Use arrays of objects
  with ids instead of dictionaries when order matters.
- Integers stay integers (counts, ids); never rely on float equality.
- Keep the order of RNG calls simple and in one place (e.g. compute all random values of a spawn in a fixed order).

## 4. View (src/view)
- Reads `game.state` every frame; never changes it. Visual-only randomness may use Math.random.
- Effects react to `out()` events and age with GAME time (the `now()` game clock passed in), never wall time,
  so replays/screenshots/Unity look the same.
- World units = logic units (e.g. x in [-W/2, W/2], y up). One orthographic camera fitted to the world. No ad-hoc
  pixel math in the logic.

## 5. UI (src/ui + index.html + style.css)
- Screens/popups: `data-ui-screen="<name>"`; every element Unity needs: `data-ui="<screen>.<part>"`;
  buttons: `data-ui-button="<id>"` → `send({t:'click', id})`.
- Sizes in `calc(N * var(--u))` (1080-wide design units). Text that changes at runtime: give it a box (or put it as a
  lone `<span>` inside a box: the exporter uses the box, and where the text sits in it).
- EVERYTHING that shows gets a name: a text, a picture, and a shape drawn with CSS (a gem made of a rotated square is
  its own `data-ui` element, so Unity gets one sprite an artist can replace). The export warns about what it finds
  unnamed (an unnamed shape is baked into its parent's picture; an unnamed text would become a picture).
- An ICON is a picture FILE, never a shape made with CSS and never a file recoloured with `filter`: `<img>`, or a box
  painted by one stretched picture (`background: url('/assets/sprites/icon_gem.png') center / 100% 100% no-repeat`,
  which keeps the box a `<div>` for layout). Shown at any size (or scaled by `transform: scale()`), Unity gets the one
  file. A white version of an icon is its own file (`icon_sound_white.png`). Frames (buttons, panels, bars) may be
  drawn with CSS: those are photographed.
- Looks of FRAMES are free: gradients, borders, rounded corners, shadows, `::before/::after`. `npm run t2u:export`
  PHOTOGRAPHS each element's look into a sprite (9-sliced where it can stretch) → `unity-convert/export/ui/*.png`, so
  Unity shows the same pixels and a reskin is a matter of replacing PNGs. `<img>` and `background: url()` stay the
  image file itself.
- A class the code switches at runtime (the UI's `cls(name, state, on)`) is a STATE. List every one in
  `data-ui-states="no on ready ..."` on the UI root; never use a state class as a permanent look (give that its own
  class). The export describes each state, and each combination on one element, as what changes (skin, colour,
  place, animation).
- A bar the code fills: `data-ui-fill="x|y"` (the code sets its width/height in %). A container whose children are
  built from config: `data-ui-list` (the export says which rows share a shape: one row prefab per shape).
- CSS animations are exported by name with their keyframes. Animations driven by JavaScript on the browser's clock
  (`element.animate`, timers) are not: write the Unity equivalent in a comment next to them.
- Fonts: TTF/OTF in public/assets/fonts with `@font-face`; every glyph shown must be in the font (no emoji/★ fallback —
  use shapes or images). Same file → same TMP font in Unity.
- After any UI change: `npm run t2u:export && npm run t2u:uicheck`. The check draws `export/ui.json` back by the
  importer's rules (boxes by numbers, skins as 9-sliced sprites, texts by their font facts — none of the game's CSS)
  and compares every screen and every state with the real UI. It must end with "UI CHECK: all ... match".
- Layout that moves is rebuilt by the kit from the export's facts: flexbox (`layout`/`flow`: one line — a container
  that WRAPS keeps the places of the resting page in Unity, so avoid `flex-wrap` where rows change), scrolling
  (`scroll`), lists (`data-ui-list`) and CSS @keyframes animating transform (scale/translate), opacity or left/top.
  Not rebuilt: `filter` in keyframes (brightness pulses), rotate/skew, CSS transitions — prefer scale/opacity
  pulses, or note the Unity equivalent in a comment next to them.

## 6. Data and assets
- All levels and tuning numbers in `data/*.json` (no magic numbers in code except true constants).
- What the game LOOKS like is data too: the colours of effects and of the ground, which sprite an effect uses, the
  size tables of things that fly or fall → a view-only file such as `data/look.json` (read by `src/view`, never by
  `src/core`). An artist changes it without touching code, and Unity reads the same file. How an effect MOVES stays code.
- Pictures only the web can paint (a ground painted with the browser's canvas, a procedural texture) are baked for
  Unity by the game itself: `window.__t2u.extras['terrain/map1.png'] = async () => canvas.toDataURL('image/png')`
  (src/t2u/extras.js); `npm run t2u:export` writes each one to `unity-convert/export/`. Keep big baked pictures out of
  git (they are rebuilt by the export).
- Everything the game shows is a picture file an artist can replace: no shape is built in code. A bar, a line, a
  flash that the view stretches or tints is still its own sprite (`bar_fill`, `front_dash`, `flash`), not a shared
  white square. (How things MOVE is code; what they LOOK like is a file.)
- Images: PNG masters (WebP only as an extra web build step). Audio: ogg or mp3. Names: lowercase_with_underscores.
- Spine: export straight alpha, no premultiplied alpha on the web side (Floaty Cats had wrong colours from double PMA).

## 7. Determinism toolkit (keep it working)
- `?t2u=1` harness, `src/t2u/scenarios.js` bot scenarios covering every rule (win, lose, each booster/feature,
  ads, every level type).
- After any rule change: `npm run t2u:record && npm run t2u:verify` (must print ALL deterministic) and `npm run check`.

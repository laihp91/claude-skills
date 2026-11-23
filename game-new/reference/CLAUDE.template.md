# __GAME_NAME__ (Three.js, Unity-ready)

Talk to the user in Vietnamese, short and plain (they are a game producer, not a programmer). Never ask technical
questions: choose sensible defaults and mention them in one line.

This game is built to be converted to Unity later with `/t2u`. Follow `docs/UNITY_READY.md` for EVERY change:
- Game rules only in `src/core/` (pure JS: no three, no DOM, no Math.random, no timers/Promise/async, no Math.pow/round).
- Every player action → `input({t: ...})`; everything that leaves the rules → `out(name, data)`.
- Numbers in `data/*.json`, assets in `public/assets/` (PNG, ogg/mp3, TTF).
- UI elements tagged `data-ui`, `data-ui-screen`, `data-ui-button`; sizes in `var(--u)`.

After each change:
1. `npm run check` → must stay 100/100 (fix any ERR/WARN you introduced).
2. If rules changed: add/adjust a bot scenario in `src/t2u/scenarios.js`, then `npm run t2u:record` and
   `npm run t2u:verify` (must print "ALL deterministic").
3. Run the game (`npm run dev`) and look at it in the browser before saying it is done.
4. Commit with a clear message.

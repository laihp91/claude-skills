// T2U harness — active only with ?t2u=1. Generic: works with any game built on the Unity-ready rules
// (createGame(...) -> { input(ev), update(dt), snapshot(), state }), so it needs no changes per game.
//   ?t2u=1           tools drive the game: window.__t2u (begin / input / step / replay / exportUi ...)
//   ?t2u=1&live=1    a person plays at 60 fixed steps per second; button "Lưu bản ghi" downloads the recording
// Recording = { seed, dt, logEvery, totalFrames, events:[{f, ...inputEvent}] }. Replaying it must give the same
// state log (snapshot every logEvery frames) and event log (every out()) — in the browser AND in Unity.
import { exportUi, createUiExport, buildPreview } from './ui_export.js';

export const DT = 1 / 60;

/**
 * @param {object} o
 * @param {(seed:number, out:(name:string, data:object)=>void) => any} o.newGame  creates a fresh game with that seed
 * @param {() => void} o.render  draws the current game state
 * @param {HTMLElement} o.frame  the 1080-wide design frame that contains the UI
 * @param {object} o.config  data/config.json (adSeconds)
 * @param {object} [o.scenarios]  bot scenarios (src/t2u/scenarios.js)
 */
export function installHarness({ newGame, render, frame, config, scenarios = {} }) {
  const q = new URLSearchParams(location.search);
  const T = { DT, frame: 0, seed: 1, logEvery: parseInt(q.get('logEvery') || '10', 10), events: [], states: [], log: [], timers: [], queue: null, replaying: false, game: null };
  window.__t2u = T;

  const clone = (d) => (d == null ? {} : JSON.parse(JSON.stringify(d)));
  function out(name, data) {
    T.log.push({ f: T.frame, ev: name, ...clone(data) });
    // platform answers that come later (rewarded ad) are scheduled on the FRAME clock and recorded as inputs
    if (name === 'ad' && !T.replaying) T.timers.push({ at: T.frame + Math.round((config.adSeconds ?? 1.5) * 60), ev: { t: 'adDone', placement: data?.placement, ok: true } });
  }

  T.begin = ({ seed = 1, level = null } = {}) => {
    T.seed = seed >>> 0;
    Object.assign(T, { frame: 0, events: [], states: [], log: [], timers: [], queue: null });
    T.onTick?.();
    T.game = newGame(T.seed, out);
    if (level != null) T.input({ t: 'start', level });
    return T.game;
  };
  T.input = (ev) => {
    if (!T.replaying) T.events.push({ f: T.frame, ...ev });
    T.game.input(ev);
  };
  const applyDue = () => {
    while (T.queue && T.queue.length && T.queue[0].f <= T.frame) {
      const { f, ...ev } = T.queue.shift();
      T.game.input(ev);
    }
  };
  T.snapshot = () => ({ f: T.frame, ...T.game.snapshot() });
  /** one fixed step: queued inputs -> due platform answers -> update(1/60) -> frame++ -> snapshot every logEvery */
  T.tick = () => {
    applyDue();
    const due = T.timers.filter((t) => t.at <= T.frame);
    if (due.length) {
      T.timers = T.timers.filter((t) => t.at > T.frame);
      for (const t of due) T.input(t.ev);
    }
    T.game.update(DT);
    T.frame++;
    T.onTick?.();
    if (T.frame % T.logEvery === 0) T.states.push(T.snapshot());
  };
  T.step = (n = 1) => {
    for (let i = 0; i < n; i++) T.tick();
  };
  T.result = () => ({ seed: T.seed, dt: DT, logEvery: T.logEvery, totalFrames: T.frame, events: T.events, states: T.states, log: T.log });
  T.finish = () => (T.states.push(T.snapshot()), T.result());
  T.replay = (rec) => {
    T.begin({ seed: rec.seed });
    T.logEvery = rec.logEvery ?? T.logEvery;
    T.replaying = true;
    T.queue = rec.events.map((e) => ({ ...e }));
    while (T.frame < rec.totalFrames) T.tick();
    applyDue();
    T.replaying = false;
    T.queue = null;
    return T.finish();
  };
  T.render = render;
  T.exportUi = () => exportUi(frame); // the description alone
  // the full UI export, step by step (bot.mjs takes the pictures in between), and the check that draws it back
  T.uiExport = () => (T.ui = createUiExport(frame));
  T.uiPreview = (doc, screen, spriteUrl, state, solo, at) => buildPreview(frame, doc, screen, spriteUrl, state, solo, at);
  // Files the GAME bakes for Unity on top of the kit's export (pictures only the web can paint, tables...):
  //   T.extras['terrain/map1.png'] = async () => canvas.toDataURL('image/png')   (or any JSON value → a .json file)
  // `npm run t2u:export` writes each one to unity-convert/export/<name>. The game fills this in (src/t2u/extras.js).
  T.extras = {};
  T.scenarioNames = () => Object.keys(scenarios);
  T.runScenario = (name) => {
    scenarios[name](T);
    return T.finish();
  };

  if (q.has('live')) {
    T.begin({ seed: parseInt(q.get('seed') || '1', 10), level: parseInt(q.get('level') || '1', 10) });
    const btn = document.createElement('button');
    btn.textContent = 'Lưu bản ghi';
    btn.style.cssText = 'position:fixed;right:8px;top:8px;z-index:999;font:16px sans-serif;padding:8px 12px';
    btn.onclick = (e) => {
      e.stopPropagation();
      const a = document.createElement('a');
      a.href = URL.createObjectURL(new Blob([JSON.stringify({ name: 'human', ...T.finish() })], { type: 'application/json' }));
      a.download = `human-${Date.now()}.input.json`;
      a.click();
    };
    document.body.appendChild(btn);
  }
  return T;
}

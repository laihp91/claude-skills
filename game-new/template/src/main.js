// Composition root: wires logic, view, UI and services (Unity: GameBootstrap). The only place that knows about all of them.
import config from '../data/config.json';
import levels from '../data/levels.json';
import { createGame } from './core/game.js';
import { createView } from './view/view.js';
import { createUi } from './ui/ui.js';
import { createAudio } from './services/audio.js';
import { save, haptics, ads } from './services/platform.js';

const DT = 1 / 60;
const q = new URLSearchParams(location.search);
const t2u = q.has('t2u');
const frameEl = document.getElementById('frame');
const canvas = document.getElementById('game');
let gameTime = 0; // seconds of game time (fixed steps), drives visual effects
const view = createView(canvas, config, () => gameTime);
const audio = createAudio();
let game = null;
let send = null;

/** everything the logic says goes through here: sounds, vibration, saves, ads, visual effects */
function route(name, d) {
  if (name === 'sfx') audio.play(d.n, { rate: d.rate ?? 1 });
  else if (name === 'vibrate') haptics.vibrate(d.ms);
  else if (name === 'save' && !t2u) save.write(d);
  else if (name === 'ad' && !t2u) ads.show(config.adSeconds).then((ok) => send({ t: 'adDone', placement: d.placement, ok }));
  view.onEvent(name, d);
}
const newGame = (seed, extraOut) =>
  (game = createGame({ config, levels, seed, save: t2u ? {} : save.load(), out: (n, d = {}) => (extraOut?.(n, d), route(n, d)) }));

const ui = createUi(document.getElementById('ui'), (ev) => send(ev));
canvas.addEventListener('pointerdown', (e) => {
  audio.unlock();
  const p = view.toWorld(e.clientX, e.clientY);
  send({ t: 'pd', x: Math.round(p.x * 1000) / 1000, y: Math.round(p.y * 1000) / 1000 });
});

// 9:16 design frame, letterboxed; --u = frame width / 1080 (all UI sizes use it)
function layout() {
  const ar = config.world.w / config.world.h;
  let w = innerWidth;
  let h = innerHeight;
  if (w / h > ar) w = h * ar;
  else h = w / ar;
  Object.assign(frameEl.style, { width: `${w}px`, height: `${h}px` });
  frameEl.style.setProperty('--u', `${w / 1080}px`);
  view.resize(w, h);
}
addEventListener('resize', layout);
layout();

const draw = () => {
  ui.update(game.state);
  view.render(game.state);
};

await document.fonts.ready; // UI measurements and screenshots need the real font

if (t2u) {
  const { installHarness } = await import('./t2u/harness.js');
  const { scenarios } = await import('./t2u/scenarios.js');
  const T = installHarness({ newGame, render: draw, frame: frameEl, config, scenarios });
  T.onTick = () => (gameTime = T.frame * DT);
  send = (ev) => T.input(ev);
  window.game = () => game;
  if (q.has('live')) {
    const loop = () => (T.tick(), draw(), requestAnimationFrame(loop));
    requestAnimationFrame(loop);
  } else T.begin({ seed: 1, level: 1 });
} else {
  newGame(Date.now() >>> 0);
  send = (ev) => game.input(ev);
  send({ t: 'start', level: save.load().level ?? 1 });
  // fixed-step loop: the logic always advances in steps of exactly 1/60 s (same as the harness and Unity)
  let acc = 0;
  let last = performance.now();
  const loop = (now) => {
    acc += Math.min(0.25, (now - last) / 1000);
    last = now;
    while (acc >= DT) {
      game.update(DT);
      gameTime += DT;
      acc -= DT;
    }
    draw();
    requestAnimationFrame(loop);
  };
  requestAnimationFrame(loop);
}

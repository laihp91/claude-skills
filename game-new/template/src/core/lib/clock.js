// Timers and tweens for GAME LOGIC, driven only by clock.update(dt) (game time, never wall time).
// C# twin: T2U.Kit.Scheduler (same rules => same callback order on the same frame).
// Rules:
//  - items run in creation order;
//  - an item created during update() is first advanced on the NEXT update();
//  - after(sec): fires once when its elapsed time >= sec; every(sec): fires at most once per update, keeps the remainder;
//  - tween(sec, fn(k, raw), ease, done): fn every update with raw = min(1, t / sec), k = ease(raw); done() after raw reaches 1;
//  - cancel(id) is safe at any time (also from inside callbacks); clear() drops everything (e.g. on level restart).
// Use this instead of setTimeout / setInterval / Promise / async in src/core.
import { ease as E } from './ease.js';

export function createClock() {
  let items = [];
  let nextId = 1;
  const add = (it) => (items.push({ id: nextId++, t: 0, dead: false, ...it }), nextId - 1);
  const c = {
    time: 0,
    after: (sec, fn) => add({ kind: 'after', d: sec, fn }),
    every: (sec, fn) => add({ kind: 'every', d: sec, fn }),
    tween: (sec, fn, ease = E.linear, done = null) => add({ kind: 'tween', d: sec, fn, ease, done }),
    cancel(id) {
      for (const it of items) if (it.id === id) it.dead = true;
      for (const it of running) if (it.id === id) it.dead = true;
    },
    clear() {
      for (const it of items) it.dead = true;
      for (const it of running) it.dead = true;
      items = [];
    },
    /** live timers/tweens (call outside update) */
    get count() {
      return items.filter((it) => !it.dead).length;
    },
    update(dt) {
      c.time += dt;
      running = items;
      items = [];
      const keep = [];
      for (const it of running) {
        if (it.dead) continue;
        it.t += dt;
        if (it.kind === 'tween') {
          const raw = Math.min(1, it.t / it.d);
          it.fn(it.ease(raw), raw);
          if (raw >= 1 && !it.dead) {
            it.dead = true;
            if (it.done) it.done();
          }
        } else if (it.t >= it.d) {
          if (it.kind === 'every') it.t -= it.d;
          else it.dead = true;
          it.fn();
        }
        if (!it.dead) keep.push(it);
      }
      running = [];
      items = keep.concat(items.filter((it) => !it.dead));
    },
  };
  let running = [];
  return c;
}

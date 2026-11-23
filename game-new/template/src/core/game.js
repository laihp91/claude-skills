// Pop Rush — sample game RULES. This file is the part that gets ported 1:1 to C# (Unity).
// PURE logic: no three.js, no DOM/window, no Math.random, no Date/performance, no setTimeout, no Promise/async.
//  - everything the player (or the platform) does arrives through input(ev)  (plain JSON: recorded + replayed)
//  - everything the outside world needs leaves through out(name, data)      (sound, fx, save, ads... + event log)
//  - update(dt) is always called with the fixed step 1/60
//  - snapshot() returns the plain-JSON state used to compare web and Unity frame by frame
import { createRng } from './lib/rng.js';
import { createClock } from './lib/clock.js';
import { ease } from './lib/ease.js';
import { M, r3 } from './lib/math.js';

export function createGame({ config, levels, seed, save = {}, out = () => {} }) {
  const rng = createRng(seed);
  const clock = createClock();
  const W = config.world.w;
  const H = config.world.h;

  /** Read-only for the view and UI. */
  const g = {
    state: 'ready', // ready | playing | won | lost
    ui: '', // '' | win | lose | ad   (which popup is open: logic decides, UI only draws it)
    levelNo: save.level ?? 1,
    time: 0,
    score: 0,
    goal: 0,
    misses: 0,
    combo: 0,
    stars: 0,
    revived: false,
    bubbles: [],
    nextId: 1,
    spawnT: 0,
    comboId: 0,
    level: null,
  };

  function loadLevel(n) {
    clock.clear();
    g.levelNo = n;
    g.level = levels[(n - 1) % levels.length];
    g.state = 'playing';
    g.ui = '';
    g.time = g.level.time;
    g.score = 0;
    g.goal = g.level.goal;
    g.misses = 0;
    g.combo = 0;
    g.stars = 0;
    g.revived = false;
    g.bubbles = [];
    g.spawnT = 0;
    g.comboId = 0;
    out('level', { n });
  }

  function spawn() {
    const L = g.level;
    const kind = rng.next() < L.bombChance ? 'bomb' : 'normal';
    const r = rng.range(L.radius[0], L.radius[1]);
    const b = {
      id: g.nextId++,
      kind,
      r,
      x0: rng.range(-W / 2 + r, W / 2 - r),
      x: 0,
      y: -r,
      vy: rng.range(L.speed[0], L.speed[1]),
      phase: rng.range(0, 2 * M.PI),
      amp: rng.range(0.2, 0.6),
      age: 0,
      s: 1,
      popping: false,
    };
    b.x = b.x0;
    g.bubbles.push(b);
    out('spawn', { id: b.id, kind });
  }

  function restartCombo() {
    if (g.comboId) clock.cancel(g.comboId);
    g.comboId = clock.after(config.comboWindow, () => {
      g.combo = 0;
      g.comboId = 0;
    });
  }

  function pop(b) {
    b.popping = true;
    if (b.kind === 'bomb') {
      g.time = Math.max(0, g.time - config.bombPenalty);
      g.combo = 0;
      out('sfx', { n: 'bomb' });
      out('vibrate', { ms: 80 });
    } else {
      g.score++;
      g.combo++;
      restartCombo();
      out('sfx', { n: 'pop', rate: r3(1 + Math.min(g.combo, 8) * 0.05) });
    }
    out('pop', { id: b.id, kind: b.kind, x: r3(b.x), y: r3(b.y), combo: g.combo });
    clock.tween(
      config.popTime,
      (k) => {
        b.s = 1 + 0.4 * k;
      },
      ease.outQuad,
      () => {
        const i = g.bubbles.indexOf(b);
        if (i >= 0) g.bubbles.splice(i, 1);
      }
    );
  }

  function tap(x, y) {
    for (let i = g.bubbles.length - 1; i >= 0; i--) {
      const b = g.bubbles[i];
      if (b.popping) continue;
      if (M.hypot(x - b.x, y - b.y) <= b.r) {
        pop(b);
        return;
      }
    }
    out('sfx', { n: 'tap_miss' });
  }

  function win() {
    g.state = 'won';
    g.stars = g.misses === 0 ? 3 : g.misses <= 3 ? 2 : 1;
    out('sfx', { n: 'win' });
    out('win', { level: g.levelNo, stars: g.stars });
    out('save', { level: g.levelNo + 1 });
    clock.after(0.8, () => {
      g.ui = 'win';
    });
  }

  function lose() {
    g.state = 'lost';
    out('sfx', { n: 'lose' });
    out('lose', { level: g.levelNo });
    clock.after(0.5, () => {
      g.ui = 'lose';
    });
  }

  function click(id) {
    if (g.ui === 'win' && id === 'next') {
      out('sfx', { n: 'click' });
      loadLevel(g.levelNo + 1);
    } else if (g.ui === 'lose' && id === 'retry') {
      out('sfx', { n: 'click' });
      loadLevel(g.levelNo);
    } else if (g.ui === 'lose' && id === 'moreTime' && !g.revived) {
      out('sfx', { n: 'click' });
      g.ui = 'ad';
      out('ad', { placement: 'moreTime' });
    }
  }

  function input(ev) {
    switch (ev.t) {
      case 'start':
        loadLevel(ev.level ?? g.levelNo);
        break;
      case 'pd':
        if (g.state === 'playing') tap(ev.x, ev.y);
        break;
      case 'click':
        click(ev.id);
        break;
      case 'adDone':
        if (g.ui !== 'ad') break;
        if (ev.ok) {
          g.revived = true;
          g.time += config.adBonusTime;
          g.state = 'playing';
          g.ui = '';
        } else g.ui = 'lose';
        break;
    }
  }

  function update(dt) {
    clock.update(dt);
    if (g.state !== 'playing') return;
    g.time = Math.max(0, g.time - dt);
    g.spawnT -= dt;
    if (g.spawnT <= 0) {
      spawn();
      g.spawnT += g.level.spawnEvery;
    }
    for (const b of g.bubbles) {
      if (b.popping) continue;
      b.age += dt;
      b.y += b.vy * dt;
      b.x = b.x0 + M.sin(b.age * 2 + b.phase) * b.amp;
    }
    for (let i = g.bubbles.length - 1; i >= 0; i--) {
      const b = g.bubbles[i];
      if (!b.popping && b.y - b.r > H) {
        g.bubbles.splice(i, 1);
        if (b.kind === 'normal') {
          g.misses++;
          out('miss', { id: b.id });
        }
      }
    }
    if (g.score >= g.goal) win();
    else if (g.time <= 0) lose();
  }

  function snapshot() {
    return {
      state: g.state,
      ui: g.ui,
      level: g.levelNo,
      time: r3(g.time),
      score: g.score,
      goal: g.goal,
      misses: g.misses,
      combo: g.combo,
      rng: rng.calls,
      timers: clock.count,
      bubbles: g.bubbles.map((b) => ({ id: b.id, k: b.kind, x: r3(b.x), y: r3(b.y), r: r3(b.r), s: r3(b.s), pop: b.popping ? 1 : 0 })),
    };
  }

  return { state: g, input, update, snapshot };
}

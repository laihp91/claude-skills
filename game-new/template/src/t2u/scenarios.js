// Bot scenarios used to create recordings (npm run t2u:record). They drive the harness T synchronously.
// Deterministic: the bot has its own seeded RNG (never Math.random). Cover every rule: win, lose, retry, ad, each level.
import { createRng } from '../core/lib/rng.js';

/** tap the first non-popping bubble with probability `chance` each frame; press popup buttons like a player */
function play(T, frames, { chance = 0.06, botSeed = 7, moreTime = true } = {}) {
  const bot = createRng(botSeed);
  for (let i = 0; i < frames; i++) {
    const g = T.game.state;
    if (g.ui === 'win' && bot.next() < 0.05) T.input({ t: 'click', id: 'next' });
    else if (g.ui === 'lose' && bot.next() < 0.05) T.input({ t: 'click', id: moreTime && !g.revived ? 'moreTime' : 'retry' });
    else if (g.state === 'playing' && bot.next() < chance) {
      const live = g.bubbles.filter((b) => !b.popping);
      if (live.length && bot.next() < 0.85) {
        const b = bot.pick(live);
        T.input({ t: 'pd', x: Math.round(b.x * 1000) / 1000, y: Math.round(b.y * 1000) / 1000 });
      } else T.input({ t: 'pd', x: Math.round((bot.next() * 9 - 4.5) * 1000) / 1000, y: Math.round(bot.next() * 16 * 1000) / 1000 });
    }
    T.step(1);
  }
}

export const scenarios = {
  boot: (T) => (T.begin({ seed: 1, level: 1 }), T.step(180)),
  win_l1: (T) => (T.begin({ seed: 2, level: 1 }), play(T, 60 * 30, { chance: 0.12 })),
  lose_ad_retry: (T) => (T.begin({ seed: 3, level: 3 }), play(T, 60 * 60, { chance: 0.015, botSeed: 9 })),
  bombs_l4: (T) => (T.begin({ seed: 4, level: 4 }), play(T, 60 * 40, { chance: 0.1, botSeed: 11 })),
  sweep_1_5: (T) => (T.begin({ seed: 5, level: 1 }), play(T, 60 * 150, { chance: 0.2, botSeed: 13, moreTime: false })),
};

/** Reference screenshots (npm run t2u:shots): replay recording `rec` up to `frame` — or to the first logged frame where
 * the snapshot field `ui` equals `ui`, plus `plus` frames — then capture. `desc` is the caption shown to the producer. */
export const shots = [
  { id: 'l1_start', rec: 'boot', frame: 60, desc: 'Màn 1, giây đầu' },
  { id: 'l1_play', rec: 'boot', frame: 180, desc: 'Màn 1 sau 3 giây' },
  { id: 'popup_win', rec: 'win_l1', ui: 'win', plus: 0, desc: 'Bảng thắng màn' },
  { id: 'popup_lose', rec: 'lose_ad_retry', ui: 'lose', plus: 0, desc: 'Bảng thua màn' },
];

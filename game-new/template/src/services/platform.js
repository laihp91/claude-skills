// Platform services: save, vibration, rewarded ads. Unity: ISaveStore / Haptics / IAdService (real SDK later).
const KEY = '__GAME_ID__.save';
export const save = {
  load() {
    try {
      return JSON.parse(localStorage.getItem(KEY)) || {};
    } catch {
      return {};
    }
  },
  write(data) {
    localStorage.setItem(KEY, JSON.stringify({ ...save.load(), ...data }));
  },
};
export const haptics = { vibrate: (ms) => navigator.vibrate?.(ms) };
/** Fake rewarded ad: resolves true after `seconds`. The answer goes back to the logic as input {t:'adDone'}. */
export const ads = {
  show: (seconds) => new Promise((res) => setTimeout(() => res(true), seconds * 1000)),
};

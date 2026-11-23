// Deterministic random numbers for GAME LOGIC (mulberry32).
// C# twin: T2U.Kit.SeededRandom + RandomExt (bit-identical, same call order => same results).
// Never use Math.random() in src/core. The view may use Math.random() for purely visual effects.
export function createRng(seed) {
  let a = seed >>> 0;
  const rng = {
    /** number of values drawn so far (logged in snapshots to catch call-order drift early) */
    calls: 0,
    /** uniform double in [0, 1) */
    next() {
      rng.calls++;
      a = (a + 0x6d2b79f5) >>> 0;
      let t = a;
      t = Math.imul(t ^ (t >>> 15), t | 1);
      t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
      return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
    },
    /** a + next() * (b - a) */
    range: (lo, hi) => lo + rng.next() * (hi - lo),
    /** integer in [lo, hi] (inclusive) */
    int: (lo, hi) => Math.floor(rng.range(lo, hi + 1)),
    pick: (arr) => arr[Math.floor(rng.next() * arr.length)],
    /** in-place Fisher-Yates */
    shuffle(arr) {
      for (let i = arr.length - 1; i > 0; i--) {
        const j = Math.floor(rng.next() * (i + 1));
        [arr[i], arr[j]] = [arr[j], arr[i]];
      }
      return arr;
    },
  };
  return rng;
}

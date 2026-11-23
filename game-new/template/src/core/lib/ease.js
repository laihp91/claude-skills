// Easing curves for game logic. C# twin: T2U.Kit.Easing (same names, same formulas).
// Written with * and M.exp/M.sin only: Math.pow is NOT reproducible bit-for-bit in C# (Chrome rounds some results differently).
import { M } from './math.js';
const c1 = 1.70158;
const c3 = c1 + 1;
export const ease = {
  linear: (t) => t,
  inQuad: (t) => t * t,
  outQuad: (t) => 1 - (1 - t) * (1 - t),
  inOutQuad: (t) => (t < 0.5 ? 2 * t * t : 1 - ((-2 * t + 2) * (-2 * t + 2)) / 2),
  inCubic: (t) => t * t * t,
  outCubic: (t) => 1 - (1 - t) * (1 - t) * (1 - t),
  inOutCubic: (t) => (t < 0.5 ? 4 * t * t * t : 1 - ((-2 * t + 2) * (-2 * t + 2) * (-2 * t + 2)) / 2),
  inBack: (t) => c3 * t * t * t - c1 * t * t,
  outBack: (t) => 1 + c3 * (t - 1) * (t - 1) * (t - 1) + c1 * (t - 1) * (t - 1),
  // 2^x = exp(x * ln2)
  outElastic: (t) => (t === 0 || t === 1 ? t : M.exp(-10 * t * M.LN2) * M.sin((t * 10 - 0.75) * ((2 * M.PI) / 3)) + 1),
};

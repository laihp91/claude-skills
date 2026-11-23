// The ONLY math functions game logic may use besides + - * / and the exact Math.min/max/floor/ceil/abs/sqrt/trunc/sign.
// C# twin: T2U.Kit.JsMath — bit-identical to Chrome for sin, cos, exp, log and hypot (Chrome rounds sin/cos/exp/log
// correctly; the twin computes them in double-double and rounds once. Checked against Chrome by `npm run t2u:export`
// → goldens.json → the kit's TwinGoldenTests).
// Not allowed in src/core (not bit-exact in C#): Math.random, Math.round (use round below), Math.pow (use pow below,
// or write x * x * x; for 2^x use M.exp(x * M.LN2)), tan, atan, atan2, asin, acos, cbrt...
export const M = {
  PI: Math.PI,
  LN2: Math.LN2,
  sin: Math.sin,
  cos: Math.cos,
  exp: Math.exp,
  hypot: Math.hypot,
  log: Math.log,
};
/** b^e via exp/log (fractional exponents: cost curves, reward formulas). b<=0 -> 0. C#: T2U.Kit.Js.Pow */
export const pow = (b, e) => (b <= 0 ? 0 : M.exp(e * M.log(b)));
/** JS Math.round (C#: T2U.Kit.Js.Round — C# Math.Round rounds half to even) */
export const round = (v) => Math.round(v);
/** round to 3 decimals: use for every float you put in snapshot() or out() events */
export const r3 = (v) => Math.round(v * 1000) / 1000;

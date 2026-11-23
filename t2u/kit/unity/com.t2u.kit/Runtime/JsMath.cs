using System;

namespace T2U.Kit
{
    /// <summary>
    /// JavaScript (Chrome/V8) Math functions with bit-identical results.
    /// Measured (export/jsmath_cases.json, 16k samples): Chrome's Math.sin/cos/exp are correctly rounded, while
    /// System.Math (platform libm) differs in the last bit for ~4% of sin/cos inputs — enough to make the chaotic
    /// bubble pile drift. We therefore evaluate in double-double (~106-bit) precision and round once, which yields the
    /// correctly rounded result (Math.log is correctly rounded in Chrome too: see Log). Math.hypot follows V8's
    /// Kahan-summed algorithm exactly.
    /// </summary>
    public static class JsMath
    {
        // ------------------------------------------------------------------ double-double arithmetic
        readonly struct DD
        {
            public readonly double Hi, Lo;
            public DD(double hi, double lo) { Hi = hi; Lo = lo; }
        }

        static DD TwoSum(double a, double b)
        {
            double s = a + b, bb = s - a;
            return new DD(s, (a - (s - bb)) + (b - bb));
        }

        static DD QuickTwoSum(double a, double b)
        {
            double s = a + b;
            return new DD(s, b - (s - a));
        }

        static void Split(double a, out double hi, out double lo)
        {
            double t = 134217729.0 * a; // 2^27 + 1 (Veltkamp)
            hi = t - (t - a);
            lo = a - hi;
        }

        static DD TwoProd(double a, double b)
        {
            double p = a * b;
            Split(a, out var ah, out var al);
            Split(b, out var bh, out var bl);
            return new DD(p, ((ah * bh - p) + ah * bl + al * bh) + al * bl);
        }

        static DD Add(DD a, DD b)
        {
            var s = TwoSum(a.Hi, b.Hi);
            var t = TwoSum(a.Lo, b.Lo);
            var u = QuickTwoSum(s.Hi, s.Lo + t.Hi);
            return QuickTwoSum(u.Hi, u.Lo + t.Lo);
        }

        static DD Neg(DD a) => new DD(-a.Hi, -a.Lo);

        static DD Mul(DD a, DD b)
        {
            var p = TwoProd(a.Hi, b.Hi);
            return QuickTwoSum(p.Hi, p.Lo + (a.Hi * b.Lo + a.Lo * b.Hi));
        }

        static DD DivD(DD a, double b)
        {
            double q1 = a.Hi / b;
            var p = TwoProd(q1, b);
            var s = TwoSum(a.Hi, -p.Hi);
            double e = s.Lo - p.Lo + a.Lo;
            double q2 = (s.Hi + e) / b;
            return QuickTwoSum(q1, q2);
        }

        static DD Div(DD a, DD b)
        {
            double q1 = a.Hi / b.Hi;
            var r = Add(a, Neg(Mul(new DD(q1, 0), b)));
            double q2 = r.Hi / b.Hi;
            r = Add(r, Neg(Mul(new DD(q2, 0), b)));
            double q3 = r.Hi / b.Hi;
            var q = QuickTwoSum(q1, q2);
            return Add(q, new DD(q3, 0));
        }

        static double Round(DD a) => a.Hi + a.Lo; // (Hi, Lo) normalised: Hi + Lo rounds to nearest

        // pi/2 and ln2 as triple-doubles (residual ~1e-49), computed with mpmath
        static readonly double PIO2_1 = BitConverter.Int64BitsToDouble(0x3FF921FB54442D18);
        static readonly double PIO2_2 = BitConverter.Int64BitsToDouble(0x3C91A62633145C07);
        static readonly double PIO2_3 = BitConverter.Int64BitsToDouble(unchecked((long)0xB91F1976B7ED8FBC));
        static readonly double LN2_1 = BitConverter.Int64BitsToDouble(0x3FE62E42FEFA39EF);
        static readonly double LN2_2 = BitConverter.Int64BitsToDouble(0x3C7ABC9E3B39803F);
        static readonly double LN2_3 = BitConverter.Int64BitsToDouble(0x3907B57A079A1934);

        static readonly DD[] InvFact = BuildInvFact(40);

        static DD[] BuildInvFact(int n)
        {
            var f = new DD[n + 1];
            f[0] = new DD(1, 0);
            for (int i = 1; i <= n; i++) f[i] = DivD(f[i - 1], i);
            return f;
        }

        /// <summary>r = x - k*pi/2 in double-double.</summary>
        static DD ReducePio2(DD x, double k)
        {
            var r = Add(x, Neg(TwoProd(k, PIO2_1)));
            r = Add(r, Neg(TwoProd(k, PIO2_2)));
            return Add(r, new DD(-(k * PIO2_3), 0));
        }

        static DD SinTaylor(DD r)
        {
            var r2 = Mul(r, r);
            var term = r;
            var sum = r;
            for (int i = 3; i <= 39; i += 2)
            {
                term = Mul(term, r2);
                var t = Mul(term, InvFact[i]);
                sum = ((i >> 1) & 1) == 1 ? Add(sum, Neg(t)) : Add(sum, t);
                if (Math.Abs(t.Hi) < 1e-36 * Math.Abs(sum.Hi)) break;
            }
            return sum;
        }

        static DD CosTaylor(DD r)
        {
            var r2 = Mul(r, r);
            var term = new DD(1, 0);
            var sum = new DD(1, 0);
            for (int i = 2; i <= 40; i += 2)
            {
                term = Mul(term, r2);
                var t = Mul(term, InvFact[i]);
                sum = ((i >> 1) & 1) == 1 ? Add(sum, Neg(t)) : Add(sum, t);
                if (Math.Abs(t.Hi) < 1e-36) break;
            }
            return sum;
        }

        static void SinCosReduced(double x, out DD r, out int q)
        {
            double k = Math.Round(x * 0.63661977236758134308);
            r = ReducePio2(new DD(x, 0), k);
            q = (int)(((long)k % 4 + 4) % 4);
        }

        public static double Sin(double x)
        {
            if (double.IsNaN(x) || double.IsInfinity(x)) return double.NaN;
            if (x == 0) return x; // keeps -0
            if (Math.Abs(x) > 1e9) return Math.Sin(x); // never used by the game
            SinCosReduced(x, out var r, out var q);
            switch (q)
            {
                case 0: return Round(SinTaylor(r));
                case 1: return Round(CosTaylor(r));
                case 2: return -Round(SinTaylor(r));
                default: return -Round(CosTaylor(r));
            }
        }

        public static double Cos(double x)
        {
            if (double.IsNaN(x) || double.IsInfinity(x)) return double.NaN;
            if (Math.Abs(x) > 1e9) return Math.Cos(x);
            SinCosReduced(x, out var r, out var q);
            switch (q)
            {
                case 0: return Round(CosTaylor(r));
                case 1: return -Round(SinTaylor(r));
                case 2: return -Round(CosTaylor(r));
                default: return Round(SinTaylor(r));
            }
        }

        /// <summary>e^a for a double-double argument; returns a double-double before final rounding.</summary>
        static DD ExpDD(DD a, out int k)
        {
            k = (int)Math.Round(a.Hi * 1.4426950408889634074);
            var r = Add(a, Neg(TwoProd(k, LN2_1)));
            r = Add(r, Neg(TwoProd(k, LN2_2)));
            r = Add(r, new DD(-(k * LN2_3), 0));
            var sum = new DD(1, 0);
            var term = new DD(1, 0);
            for (int i = 1; i <= 40; i++)
            {
                term = Mul(term, r);
                var t = Mul(term, InvFact[i]);
                sum = Add(sum, t);
                if (Math.Abs(t.Hi) < 1e-36) break;
            }
            return sum;
        }

        static double ScaleByPow2(DD v, int k)
        {
            // multiply by 2^k exactly (game values never get near the subnormal range)
            if (k > 1023 || k < -1022) return Round(v) * Math.Pow(2, k);
            double f = BitConverter.Int64BitsToDouble((long)(k + 1023) << 52);
            return Round(QuickTwoSum(v.Hi * f, v.Lo * f));
        }

        public static double Exp(double x)
        {
            if (double.IsNaN(x)) return x;
            if (x > 709.782712893384) return double.PositiveInfinity;
            if (x < -745.1332191019412) return 0;
            if (x == 0) return 1;
            var v = ExpDD(new DD(x, 0), out var k);
            return ScaleByPow2(v, k);
        }

        /// <summary>
        /// Math.log. Chrome's is correctly rounded (measured in Chrome 157: 4,000,000 samples — near 1, whole numbers,
        /// 1e-17..1e17, subnormals — against this algorithm, no mismatch), while the classic fdlibm log and the platform
        /// libm differ from it in the last bit for ~4% of inputs. So: take any estimate, refine it with one Newton
        /// step in double-double (y = y0 + x·e^-y0 − 1) and round once.
        /// </summary>
        public static double Log(double x)
        {
            if (double.IsNaN(x) || x < 0) return double.NaN;
            if (x == 0) return double.NegativeInfinity;
            if (double.IsPositiveInfinity(x)) return x;
            if (x == 1) return 0;
            double y0 = Math.Log(x); // good to a few ulp on every platform; the step below makes it exact
            var v = ExpDD(new DD(-y0, 0), out var k); // e^-y0 = v * 2^k
            int k1 = k >> 1;
            double xs = x * Pow2(k1) * Pow2(k - k1); // x * 2^k, exact (in two halves: k reaches ±1075)
            var c = Add(Mul(v, new DD(xs, 0)), new DD(-1, 0)); // x * e^-y0 - 1
            return Round(Add(new DD(y0, 0), c));
        }

        static double Pow2(int k) => BitConverter.Int64BitsToDouble((long)(k + 1023) << 52);

        /// <summary>
        /// Math.pow, correctly rounded for small integer exponents and for base 2; other forms fall back to System.Math.Pow.
        /// WARNING: Chrome's own pow is NOT always correctly rounded (about 1 in 600 results of x^3 differ in the last bit),
        /// so new game logic must not use Math.pow at all (write x * x * x; 2^x = Exp(x * Ln2); a fractional power:
        /// the template's pow(b, e) = Js.Pow). Kept for legacy ports.
        /// </summary>
        public static double Pow(double x, double y)
        {
            if (y == 0) return 1;
            if (double.IsNaN(x) || double.IsNaN(y) || double.IsInfinity(x) || double.IsInfinity(y)) return Math.Pow(x, y);
            if (y == Math.Floor(y) && Math.Abs(y) <= 64)
            {
                int n = (int)Math.Abs(y);
                var b = new DD(x, 0);
                var acc = new DD(1, 0);
                while (n > 0)
                {
                    if ((n & 1) == 1) acc = Mul(acc, b);
                    n >>= 1;
                    if (n > 0) b = Mul(b, b);
                }
                if (y < 0) acc = Div(new DD(1, 0), acc);
                return Round(acc);
            }
            if (x == 2)
            {
                // y*ln2 in double-double
                var a = Add(TwoProd(y, LN2_1), Add(TwoProd(y, LN2_2), new DD(y * LN2_3, 0)));
                var v = ExpDD(a, out var k);
                return ScaleByPow2(v, k);
            }
            return Math.Pow(x, y);
        }

        // ------------------------------------------------------------------ hypot (V8 builtins math.tq, Kahan sum)
        public static double Hypot(double a, double b)
        {
            double x = Math.Abs(a), y = Math.Abs(b);
            if (double.IsInfinity(x) || double.IsInfinity(y)) return double.PositiveInfinity;
            if (double.IsNaN(x) || double.IsNaN(y)) return double.NaN;
            double max = x > y ? x : y;
            if (max == 0) return 0;
            double sum = 0, comp = 0;
            double n = x / max, summand = n * n - comp, pre = sum + summand;
            comp = (pre - sum) - summand;
            sum = pre;
            n = y / max;
            summand = n * n - comp;
            sum += summand;
            return Math.Sqrt(sum) * max;
        }

        /// <summary>Math.atan2 (only used for the twin link sprite angle, visual).</summary>
        public static double Atan2(double y, double x) => Math.Atan2(y, x);
    }
}

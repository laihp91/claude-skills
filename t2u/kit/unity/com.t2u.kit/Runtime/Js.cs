using System;
using System.Globalization;
using System.Numerics;

namespace T2U.Kit
{
    /// <summary>JS number helpers (template src/core/lib/math.js: round, r3, pow) and JS number → text (toFixed, String(x)).</summary>
    public static class Js
    {
        /// <summary>JS Math.round: ties toward +infinity (C# Math.Round rounds half to even).</summary>
        public static double Round(double x)
        {
            if (double.IsNaN(x) || double.IsInfinity(x)) return x;
            double f = Math.Floor(x);
            return x - f >= 0.5 ? f + 1 : f;
        }

        /// <summary>r3(v) = Math.round(v * 1000) / 1000 — every float in snapshots and events.</summary>
        public static double R3(double v) => Round(v * 1000) / 1000;

        /// <summary>
        /// pow(b, e) of the template's math.js: b^e as e^(e·ln b), 0 for b &lt;= 0. Bit-identical to Chrome (unlike
        /// Math.pow, whose last bit differs between engines). NOT JsMath.Pow, which is the legacy Math.pow twin.
        /// </summary>
        public static double Pow(double b, double e) => b <= 0 ? 0 : JsMath.Exp(e * JsMath.Log(b));
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>
        /// JS <c>x.toFixed(digits)</c> (digits 0..20), exactly like the browser: it rounds the double's EXACT value, a
        /// tie going up. .NET/Mono "F" rounds a 15-digit copy first: 4.35 (really 4.3499999999999996…) gives "4.4"
        /// there and "4.3" in JS — players saw different numbers on the two versions.
        /// </summary>
        public static string ToFixed(double x, int digits)
        {
            if (double.IsNaN(x)) return "NaN";
            if (double.IsInfinity(x) || Math.Abs(x) >= 1e21) return ToStr(x);
            string sign = x < 0 ? "-" : ""; // JS: (-0).toFixed(1) "0.0", (-0.01).toFixed(1) "-0.0"
            long bits = BitConverter.DoubleToInt64Bits(Math.Abs(x));
            int exp = (int)((bits >> 52) & 0x7FF);
            long man = bits & 0xFFFFFFFFFFFFFL;
            if (exp == 0) exp = 1; else man |= 1L << 52;
            exp -= 1075; // |x| = man · 2^exp
            var num = new BigInteger(man) * BigInteger.Pow(10, digits);
            BigInteger n;
            if (exp >= 0) n = num << exp;
            else
            {
                var den = BigInteger.One << -exp;
                n = BigInteger.DivRem(num, den, out var rem);
                if (rem * 2 >= den) n += 1;
            }
            string s = n.ToString(Inv);
            if (digits > 0)
            {
                if (s.Length <= digits) s = new string('0', digits - s.Length + 1) + s;
                s = s.Substring(0, s.Length - digits) + "." + s.Substring(s.Length - digits);
            }
            return sign + s;
        }

        /// <summary>
        /// JS <c>String(x)</c>: the shortest digits that read back as the same double, written the JS way (no
        /// exponent from 1e-6 to 1e21; "1e+21", "1.5e-7" outside). .NET "R"/"G" may give 15 or 17 digits and "E+021".
        /// </summary>
        public static string ToStr(double x)
        {
            if (double.IsNaN(x)) return "NaN";
            if (x == 0) return "0";
            if (double.IsInfinity(x)) return x > 0 ? "Infinity" : "-Infinity";
            if (x < 0) return "-" + ToStr(-x);
            string e = null;
            for (int p = 1; p <= 17; p++)
            {
                e = x.ToString("E" + (p - 1), Inv);
                // (an overflowing read-back, "2E+308" for the largest double, is simply not that double)
                if (double.TryParse(e, NumberStyles.Float, Inv, out double back) && back == x) break;
            }
            int at = e.IndexOf('E');
            string digits = e.Substring(0, at).Replace(".", "").TrimEnd('0');
            if (digits.Length == 0) digits = "0";
            int n = int.Parse(e.Substring(at + 1), NumberStyles.AllowLeadingSign, Inv) + 1; // where the point goes
            int k = digits.Length;
            if (k <= n && n <= 21) return digits + new string('0', n - k);
            if (0 < n && n <= 21) return digits.Substring(0, n) + "." + digits.Substring(n);
            if (-6 < n && n <= 0) return "0." + new string('0', -n) + digits;
            int ex = n - 1;
            return (k == 1 ? digits : digits.Substring(0, 1) + "." + digits.Substring(1)) + "e" + (ex >= 0 ? "+" : "-") + Math.Abs(ex).ToString(Inv);
        }
    }
}

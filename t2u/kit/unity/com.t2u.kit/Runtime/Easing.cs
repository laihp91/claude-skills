using System;

namespace T2U.Kit
{
    /// <summary>Easing curves, twin of the template's src/core/lib/ease.js (same names and formulas: only * and JS-exact exp/sin, no pow).</summary>
    public static class Easing
    {
        const double C1 = 1.70158, C3 = C1 + 1;
        public static readonly Func<double, double> Linear = t => t;
        public static readonly Func<double, double> InQuad = t => t * t;
        public static readonly Func<double, double> OutQuad = t => 1 - (1 - t) * (1 - t);
        public static readonly Func<double, double> InOutQuad = t => t < 0.5 ? 2 * t * t : 1 - ((-2 * t + 2) * (-2 * t + 2)) / 2;
        public static readonly Func<double, double> InCubic = t => t * t * t;
        public static readonly Func<double, double> OutCubic = t => 1 - (1 - t) * (1 - t) * (1 - t);
        public static readonly Func<double, double> InOutCubic = t => t < 0.5 ? 4 * t * t * t : 1 - ((-2 * t + 2) * (-2 * t + 2) * (-2 * t + 2)) / 2;
        public static readonly Func<double, double> InBack = t => C3 * t * t * t - C1 * t * t;
        public static readonly Func<double, double> OutBack = t => 1 + C3 * (t - 1) * (t - 1) * (t - 1) + C1 * (t - 1) * (t - 1);
        public static readonly Func<double, double> OutElastic = t =>
            t == 0 || t == 1 ? t : JsMath.Exp(-10 * t * Ln2) * JsMath.Sin((t * 10 - 0.75) * ((2 * Math.PI) / 3)) + 1;

        /// <summary>JS Math.LN2</summary>
        public const double Ln2 = 0.6931471805599453;

        /// <summary>Look up by the JS name ("outBack" ...), e.g. for easings named in data files.</summary>
        public static Func<double, double> ByName(string name) => name switch
        {
            "linear" => Linear, "inQuad" => InQuad, "outQuad" => OutQuad, "inOutQuad" => InOutQuad,
            "inCubic" => InCubic, "outCubic" => OutCubic, "inOutCubic" => InOutCubic,
            "inBack" => InBack, "outBack" => OutBack, "outElastic" => OutElastic,
            _ => throw new ArgumentException("unknown easing " + name),
        };
    }
}

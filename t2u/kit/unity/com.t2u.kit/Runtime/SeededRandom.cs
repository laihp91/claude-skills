using System;
using System.Collections.Generic;

namespace T2U.Kit
{
    /// <summary>Source of uniform doubles in [0,1), same contract as JS Math.random().</summary>
    public interface IRandom
    {
        double Next();
    }

    /// <summary>
    /// mulberry32, bit-identical to <c>createRng</c> in the template's src/core/lib/rng.js (and the t2u harness).
    /// Same seed + same call order = same numbers in the browser and in Unity.
    /// </summary>
    public sealed class SeededRandom : IRandom
    {
        uint _a;

        /// <summary>Number of values drawn so far (JS <c>rng.calls</c>, logged in snapshots).</summary>
        public int Calls { get; private set; }

        public SeededRandom(uint seed) { _a = seed; }

        public double Next()
        {
            Calls++;
            unchecked
            {
                _a += 0x6D2B79F5u;
                uint t = _a;
                t = (t ^ (t >> 15)) * (t | 1u);
                t ^= t + (t ^ (t >> 7)) * (t | 61u);
                return (t ^ (t >> 14)) / 4294967296.0;
            }
        }

        /// <summary>rng.range(lo, hi) = lo + next() * (hi - lo)</summary>
        public double Range(double lo, double hi) => lo + Next() * (hi - lo);

        /// <summary>rng.int(lo, hi): integer in [lo, hi] (inclusive)</summary>
        public int Int(int lo, int hi) => (int)Math.Floor(Range(lo, hi + 1));

        /// <summary>rng.pick(arr)</summary>
        public T Pick<T>(IReadOnlyList<T> arr) => arr[(int)Math.Floor(Next() * arr.Count)];

        /// <summary>rng.shuffle(arr): in-place Fisher-Yates, same iteration order as the JS.</summary>
        public List<T> Shuffle<T>(List<T> arr)
        {
            for (int i = arr.Count - 1; i > 0; i--)
            {
                int j = (int)Math.Floor(Next() * (i + 1));
                (arr[i], arr[j]) = (arr[j], arr[i]);
            }
            return arr;
        }
    }
}

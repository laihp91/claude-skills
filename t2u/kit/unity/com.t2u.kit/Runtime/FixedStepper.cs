using System;

namespace T2U.Kit
{
    /// <summary>
    /// Fixed-step game loop (template main.js): accumulate real frame time (capped) and advance the logic in steps of
    /// exactly 1/60 s, so the game behaves the same at any frame rate and the same as the recordings.
    /// </summary>
    public sealed class FixedStepper
    {
        public const double Dt = 1.0 / 60;
        public double MaxFrame = 0.25;
        double _acc;

        /// <summary>Returns the number of steps taken.</summary>
        public int Advance(double frameSeconds, Action<double> step)
        {
            _acc += Math.Min(MaxFrame, Math.Max(0, frameSeconds));
            int n = 0;
            while (_acc >= Dt)
            {
                step(Dt);
                _acc -= Dt;
                n++;
            }
            return n;
        }
    }
}

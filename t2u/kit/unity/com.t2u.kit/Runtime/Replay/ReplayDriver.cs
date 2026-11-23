using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace T2U.Kit.Replay
{
    /// <summary>What a ported game core must offer to be replayed (same contract as the JS core: input / update / snapshot).</summary>
    public interface IReplayGame
    {
        /// <summary>One input event exactly as recorded on the web (without the "f" frame field).</summary>
        void Input(JObject ev);

        void Update(double dt);

        /// <summary>Plain JSON state with the same field names and r3 rounding as the JS snapshot().</summary>
        JObject Snapshot();
    }

    /// <summary>
    /// C# twin of the template harness (src/t2u/harness.js) in replay mode. Per frame: apply recorded inputs whose
    /// frame &lt;= current frame → Update(1/60) → frame++ → snapshot every logEvery frames; after the last frame: apply
    /// the remaining inputs and take a final snapshot. Every out(name, data) of the game is logged as {f, ev, ...data}.
    /// </summary>
    public sealed class ReplayDriver
    {
        public const double Dt = 1.0 / 60;

        public int Frame { get; private set; }
        public int LogEvery { get; private set; } = 10;
        public IReplayGame Game { get; private set; }
        public readonly List<JObject> States = new List<JObject>();
        public readonly List<JObject> Log = new List<JObject>();

        /// <summary>The out(name, data) sink to give the game: logs the event with the current frame.</summary>
        public void Emit(string name, JObject data)
        {
            var e = new JObject { ["f"] = Frame, ["ev"] = name };
            if (data != null) foreach (var p in data.Properties()) e[p.Name] = p.Value.DeepClone();
            Log.Add(e);
        }

        /// <param name="recording">contents of &lt;name&gt;.input.json</param>
        /// <param name="newGame">creates the game: (seed, out) → game</param>
        public static ReplayDriver Run(JObject recording, Func<uint, Action<string, JObject>, IReplayGame> newGame)
        {
            var d = new ReplayDriver { LogEvery = (int?)recording["logEvery"] ?? 10 };
            d.Game = newGame((uint)(long)recording["seed"], d.Emit);
            var queue = new Queue<JObject>(((JArray)recording["events"]).Cast<JObject>());
            int total = (int)recording["totalFrames"];
            while (d.Frame < total)
            {
                d.ApplyDue(queue);
                d.Game.Update(Dt);
                d.Frame++;
                if (d.Frame % d.LogEvery == 0) d.States.Add(d.Snap());
            }
            d.ApplyDue(queue);
            d.States.Add(d.Snap());
            return d;
        }

        void ApplyDue(Queue<JObject> q)
        {
            while (q.Count > 0 && (int)q.Peek()["f"] <= Frame)
            {
                var ev = (JObject)q.Dequeue().DeepClone();
                ev.Remove("f");
                Game.Input(ev);
            }
        }

        JObject Snap()
        {
            var o = new JObject { ["f"] = Frame };
            foreach (var p in Game.Snapshot().Properties()) o[p.Name] = p.Value;
            return o;
        }
    }
}

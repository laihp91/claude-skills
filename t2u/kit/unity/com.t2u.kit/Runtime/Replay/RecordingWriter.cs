using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace T2U.Kit.Replay
{
    /// <summary>
    /// Records a play session INSIDE Unity in the same format as the web harness (&lt;name&gt;.input.json), so the team can add
    /// new regression recordings after the port (the web game is no longer the reference). Usage in the bootstrap
    /// (editor / development builds only): <c>rec = new RecordingWriter(seed)</c>; call <see cref="Input"/> with every input
    /// event BEFORE giving it to the game, <see cref="Tick"/> after every fixed step, <see cref="Save"/> at the end.
    /// Then run the parity tests once in "accept" mode to create the expected logs for it.
    /// </summary>
    public sealed class RecordingWriter
    {
        readonly uint _seed;
        readonly int _logEvery;
        readonly List<JObject> _events = new List<JObject>();
        public int Frame { get; private set; }

        public RecordingWriter(uint seed, int logEvery = 10)
        {
            _seed = seed;
            _logEvery = logEvery;
        }

        public void Input(JObject ev)
        {
            var e = new JObject { ["f"] = Frame };
            foreach (var p in ev.Properties()) e[p.Name] = p.Value.DeepClone();
            _events.Add(e);
        }

        public void Tick() => Frame++;

        public JObject ToJson(string name) => new JObject
        {
            ["name"] = name, ["seed"] = _seed, ["dt"] = ReplayDriver.Dt, ["logEvery"] = _logEvery, ["totalFrames"] = Frame, ["events"] = new JArray(_events),
        };

        /// <summary>Writes &lt;dir&gt;/&lt;name&gt;.input.json and returns its path.</summary>
        public string Save(string dir, string name)
        {
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, name + ".input.json");
            File.WriteAllText(path, ToJson(name).ToString(Formatting.None));
            return path;
        }
    }
}

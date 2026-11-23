using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace T2U.Kit.Replay
{
    /// <summary>Reads web recordings (unity-convert/recordings) and checks a ported game against them.</summary>
    public static class Recordings
    {
        public static IEnumerable<string> Names(string dir) =>
            Directory.GetFiles(dir, "*.input.json").Select(f => Path.GetFileName(f)).Select(f => f.Substring(0, f.Length - ".input.json".Length)).OrderBy(n => n, StringComparer.Ordinal);

        public static JObject Input(string dir, string name) => JObject.Parse(File.ReadAllText(Path.Combine(dir, name + ".input.json")));

        public static List<JObject> Jsonl(string path) =>
            File.ReadAllLines(path).Where(l => l.Trim().Length > 0).Select(JObject.Parse).ToList();

        public static void WriteJsonl(string path, IEnumerable<JObject> lines) =>
            File.WriteAllLines(path, lines.Select(l => l.ToString(Formatting.None)));

        /// <summary>
        /// Replays recording <paramref name="name"/> through the ported game and compares state + event logs with the web
        /// logs. Returns null when identical, otherwise the first difference ("state line 12 (frame 120): .bubbles[3].x: 1.25 vs 1.251").
        /// When <paramref name="writeUnityLogs"/> is set, writes &lt;name&gt;.unity.state.jsonl / .unity.events.jsonl for diff_states.py.
        /// </summary>
        /// <summary>
        /// True when the team has accepted the current behaviour as the new reference (after an intended gameplay change):
        /// env T2U_REBASELINE=1, or a file named ".rebaseline" in the recordings folder (Tools/T2U Kit/Accept Current
        /// Behaviour As Golden creates and removes it). In that mode <see cref="Check"/> rewrites the expected logs.
        /// </summary>
        public static bool RebaselineRequested(string dir) =>
            Environment.GetEnvironmentVariable("T2U_REBASELINE") == "1" || File.Exists(Path.Combine(dir, ".rebaseline"));

        public static string Check(string dir, string name, Func<uint, Action<string, JObject>, IReplayGame> newGame, bool writeUnityLogs = false)
        {
            var d = ReplayDriver.Run(Input(dir, name), newGame);
            if (RebaselineRequested(dir))
            {
                WriteJsonl(Path.Combine(dir, name + ".state.jsonl"), d.States);
                WriteJsonl(Path.Combine(dir, name + ".events.jsonl"), d.Log);
                return null;
            }
            if (writeUnityLogs)
            {
                WriteJsonl(Path.Combine(dir, name + ".unity.state.jsonl"), d.States);
                WriteJsonl(Path.Combine(dir, name + ".unity.events.jsonl"), d.Log);
            }
            foreach (var (kind, mine) in new[] { ("state", d.States), ("events", d.Log) })
            {
                var web = Jsonl(Path.Combine(dir, $"{name}.{kind}.jsonl"));
                var diff = JsonDiff.FirstLineDifference(mine, web);
                if (diff != null)
                {
                    var (i, what) = diff.Value;
                    var f = i < web.Count ? (string)web[i]["f"] : "?";
                    return $"{kind} line {i} (frame {f}): {what}   [unity vs web]";
                }
            }
            return null;
        }
    }
}

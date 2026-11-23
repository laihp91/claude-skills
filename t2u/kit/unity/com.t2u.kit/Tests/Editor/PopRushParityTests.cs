using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using T2U.Kit.Replay;
using T2U.Kit.Samples.PopRush;

namespace T2U.Kit.Tests
{
    /// <summary>
    /// End-to-end proof of the pipeline on the template game: every web recording replayed through the C# port must give
    /// the same state log and event log. Copy this test for a real game (point Dir at ../web/unity-convert/recordings).
    /// </summary>
    public class PopRushParityTests
    {
        static string Dir => Path.Combine(KitTestData.Dir, "poprush");
        static PopRushConfig Config => JObject.Parse(File.ReadAllText(Path.Combine(Dir, "config.json"))).ToObject<PopRushConfig>();
        static List<PopRushLevel> Levels => JArray.Parse(File.ReadAllText(Path.Combine(Dir, "levels.json"))).ToObject<List<PopRushLevel>>();

        static IEnumerable<string> Names => Recordings.Names(Dir);

        [TestCaseSource(nameof(Names))]
        public void Recording_MatchesWeb(string name)
        {
            var cfg = Config;
            var lv = Levels;
            var diff = Recordings.Check(Dir, name, (seed, output) => new PopRushGame(cfg, lv, seed, output));
            Assert.IsNull(diff, diff);
        }

        [Test]
        public void HasRecordings() => Assert.GreaterOrEqual(Names.Count(), 3);
    }
}

namespace T2U.Kit.Tests
{
    public class MaintenanceToolsTests
    {
        [Test]
        public void RecordingWriter_RoundTrips_ThroughReplay()
        {
            var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "t2u_rec_" + System.Guid.NewGuid().ToString("N"));
            var src = System.IO.Path.Combine(KitTestData.Dir, "poprush");
            var cfg = JObject.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(src, "config.json"))).ToObject<PopRushConfig>();
            var lv = JArray.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(src, "levels.json"))).ToObject<System.Collections.Generic.List<PopRushLevel>>();
            // play a session "live" in C#, recording inputs
            var rec = new Replay.RecordingWriter(77);
            var game = new PopRushGame(cfg, lv, 77, null);
            void Send(JObject ev) { rec.Input(ev); game.Input(ev); }
            Send(new JObject { ["t"] = "start", ["level"] = 1 });
            for (int f = 0; f < 600; f++)
            {
                if (f % 20 == 0 && game.Bubbles.Count > 0) Send(new JObject { ["t"] = "pd", ["x"] = game.Bubbles[0].X, ["y"] = game.Bubbles[0].Y });
                game.Update(Replay.ReplayDriver.Dt);
                rec.Tick();
            }
            rec.Save(dir, "live");
            // accept as golden, then the normal check must pass and see the same final score
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, ".rebaseline"), "");
            Assert.IsNull(Recordings.Check(dir, "live", (s, o) => new PopRushGame(cfg, lv, s, o)));
            System.IO.File.Delete(System.IO.Path.Combine(dir, ".rebaseline"));
            Assert.IsNull(Recordings.Check(dir, "live", (s, o) => new PopRushGame(cfg, lv, s, o)));
            var last = Recordings.Jsonl(System.IO.Path.Combine(dir, "live.state.jsonl")).Last();
            Assert.AreEqual(game.Score, (int)last["score"]);
            Assert.Greater(game.Score, 0);
            System.IO.Directory.Delete(dir, true);
        }

        [Test]
        public void TextTable_FallsBack_ToEnglish_ThenKey()
        {
            var t = TextTable.Parse("{\"en\":{\"a\":\"Hello {n}\",\"b\":\"B\"},\"vi\":{\"a\":\"Xin chào {n}\"}}");
            t.Language = "vi";
            Assert.AreEqual("Xin chào Mai", t.Format("a", new System.Collections.Generic.Dictionary<string, object> { ["n"] = "Mai" }));
            Assert.AreEqual("B", t.Get("b"));
            Assert.AreEqual("missing.key", t.Get("missing.key"));
        }
    }
}

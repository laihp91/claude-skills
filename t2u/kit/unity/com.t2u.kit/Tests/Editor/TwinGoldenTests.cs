using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace T2U.Kit.Tests
{
    /// <summary>
    /// The C# twins must give exactly what Chrome gives for the template's core/lib (goldens.json is written by
    /// `node tools/t2u/bot.mjs export`). A game that changes its core/lib must re-export and re-run these.
    /// </summary>
    public class TwinGoldenTests
    {
        static JObject _g;
        static JObject G => _g ??= KitTestData.Json("goldens.json");
        static string H(double d) => KitTestData.Hex(d);

        [Test]
        public void Rng_SameSequence_Int_Shuffle()
        {
            foreach (var p in ((JObject)G["rng"]).Properties())
            {
                var r = new SeededRandom(uint.Parse(p.Name));
                var next = ((JArray)p.Value["next"]).Select(t => (string)t).ToList();
                for (int i = 0; i < next.Count; i++) Assert.AreEqual(next[i], H(r.Next()), $"seed {p.Name} next #{i}");
                var ints = ((JArray)p.Value["int"]).Select(t => (int)t).ToList();
                for (int i = 0; i < ints.Count; i++) Assert.AreEqual(ints[i], r.Int(-5, 17), $"seed {p.Name} int #{i}");
                var sh = r.Shuffle(Enumerable.Range(0, 10).ToList());
                CollectionAssert.AreEqual(((JArray)p.Value["shuffle"]).Select(t => (int)t).ToList(), sh, $"seed {p.Name} shuffle");
            }
        }

        [Test]
        public void Easing_BitExact()
        {
            var bad = new List<string>();
            foreach (var p in ((JObject)G["ease"]).Properties())
            {
                var f = Easing.ByName(p.Name);
                int miss = 0;
                foreach (JArray row in p.Value)
                    if (H(f(KitTestData.Hex((string)row[0]))) != (string)row[1]) miss++;
                if (miss > 0) bad.Add($"{p.Name}: {miss} mismatches");
            }
            Assert.IsEmpty(bad, string.Join("; ", bad));
        }

        [TestCase("sin", 1)]
        [TestCase("cos", 1)]
        [TestCase("exp", 1)]
        [TestCase("log", 1)]
        [TestCase("pow", 2)]
        [TestCase("hypot", 2)]
        public void Math_BitExact_WithChrome(string fn, int argc)
        {
            Func<double[], double> f = fn switch
            {
                "sin" => a => JsMath.Sin(a[0]),
                "cos" => a => JsMath.Cos(a[0]),
                "exp" => a => JsMath.Exp(a[0]),
                "log" => a => JsMath.Log(a[0]),
                "pow" => a => Js.Pow(a[0], a[1]), // the template's pow(b, e), not Math.pow
                _ => a => JsMath.Hypot(a[0], a[1]),
            };
            Assert.IsNotNull(G["math"][fn], $"goldens.json has no math.{fn}: re-export with the current bot.mjs");
            Assert.Greater(((JArray)G["math"][fn]).Count, 0, $"goldens.json math.{fn} is empty");
            int bad = 0;
            string first = null;
            foreach (JArray row in G["math"][fn])
            {
                var a = Enumerable.Range(0, argc).Select(i => KitTestData.Hex((string)row[i])).ToArray();
                var got = H(f(a));
                if (got != (string)row[argc]) { bad++; first ??= $"{fn}({string.Join(",", a)}) = {got}, Chrome {(string)row[argc]}"; }
            }
            Assert.AreEqual(0, bad, first);
        }

        /// <summary>Same script as bot.mjs export (clock trace): every callback, in order, on the same frame.</summary>
        [Test]
        public void Scheduler_SameCallbackOrder()
        {
            var c = new Scheduler();
            var log = new JArray();
            double T6() => double.Parse(c.Time.ToString("F6", System.Globalization.CultureInfo.InvariantCulture), System.Globalization.CultureInfo.InvariantCulture);
            c.After(0.05, () => log.Add(new JArray("a1", T6())));
            int ev = c.Every(0.04, () => log.Add(new JArray("e1", T6())));
            c.Tween(0.1, (k, raw) => log.Add(new JArray("tw", H(k), H(raw))), Easing.OutBack, () =>
            {
                log.Add(new JArray("tw.done", T6()));
                c.After(0, () => log.Add(new JArray("a0-from-done", T6())));
            });
            int id2 = c.After(0.2, () => log.Add(new JArray("never")));
            c.After(0.07, () =>
            {
                log.Add(new JArray("cancel", c.Count));
                c.Cancel(id2);
                c.Cancel(ev);
                c.After(0.01, () => log.Add(new JArray("nested", T6())));
            });
            for (int i = 0; i < 20; i++)
            {
                c.Update(1.0 / 60);
                log.Add(new JArray("frame", i, c.Count));
            }
            c.Clear();
            c.Update(1.0 / 60);
            log.Add(new JArray("cleared", c.Count));

            var want = (JArray)G["clock"];
            for (int i = 0; i < Math.Min(want.Count, log.Count); i++)
                Assert.IsTrue(JToken.DeepEquals(Norm(want[i]), Norm(log[i])), $"step {i}: web {want[i].ToString(Newtonsoft.Json.Formatting.None)} vs unity {log[i].ToString(Newtonsoft.Json.Formatting.None)}");
            Assert.AreEqual(want.Count, log.Count, "number of callbacks");
        }

        // numbers as doubles so 1 == 1.0
        static JToken Norm(JToken t) => new JArray(((JArray)t).Select(x => x.Type == JTokenType.Integer || x.Type == JTokenType.Float ? new JValue((double)x) : x));
    }
}

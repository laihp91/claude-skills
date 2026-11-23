using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace T2U.Kit.Replay
{
    /// <summary>Semantic JSON comparison (numbers compared as doubles, so 1 == 1.0). Returns the first difference path.</summary>
    public static class JsonDiff
    {
        public static string FirstDifference(JToken a, JToken b, string path = "")
        {
            if (a == null || b == null) return a == b ? null : $"{path}: {a?.ToString() ?? "missing"} vs {b?.ToString() ?? "missing"}";
            bool na = a.Type == JTokenType.Integer || a.Type == JTokenType.Float;
            bool nb = b.Type == JTokenType.Integer || b.Type == JTokenType.Float;
            if (na && nb) return (double)a == (double)b ? null : $"{path}: {a} vs {b}";
            if (a.Type != b.Type) return $"{path}: {a} ({a.Type}) vs {b} ({b.Type})";
            switch (a)
            {
                case JObject oa:
                    var ob = (JObject)b;
                    foreach (var p in oa.Properties())
                    {
                        var d = FirstDifference(p.Value, ob[p.Name], $"{path}.{p.Name}");
                        if (d != null) return d;
                    }
                    foreach (var p in ob.Properties()) if (oa[p.Name] == null) return $"{path}.{p.Name}: missing vs {p.Value}";
                    return null;
                case JArray aa:
                    var ab = (JArray)b;
                    for (int i = 0; i < System.Math.Min(aa.Count, ab.Count); i++)
                    {
                        var d = FirstDifference(aa[i], ab[i], $"{path}[{i}]");
                        if (d != null) return d;
                    }
                    return aa.Count == ab.Count ? null : $"{path}: length {aa.Count} vs {ab.Count}";
                default:
                    return JToken.DeepEquals(a, b) ? null : $"{path}: {a} vs {b}";
            }
        }

        /// <summary>Compare two logs line by line; returns (index, description) of the first mismatch or null.</summary>
        public static (int index, string diff)? FirstLineDifference(IList<JObject> unity, IList<JObject> web)
        {
            int n = System.Math.Min(unity.Count, web.Count);
            for (int i = 0; i < n; i++)
            {
                var d = FirstDifference(unity[i], web[i]);
                if (d != null) return (i, d);
            }
            if (unity.Count != web.Count) return (n, $"line count {unity.Count} (unity) vs {web.Count} (web)");
            return null;
        }
    }
}

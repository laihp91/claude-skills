using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace T2U.Kit
{
    /// <summary>
    /// Player-facing texts by key (data/strings.json: { "en": { "toast.noSlots": "Waiting slots are empty" }, "vi": {...} }).
    /// Logic emits KEYS (out('toast', {key})), UI shows <c>Get(key)</c>. Missing key → the key itself (visible, never crashes).
    /// Ready for Unity Localization later: the keys become the string-table entries.
    /// </summary>
    public sealed class TextTable
    {
        readonly Dictionary<string, Dictionary<string, string>> _langs = new Dictionary<string, Dictionary<string, string>>();
        public string Language = "en";
        public string Fallback = "en";

        public static TextTable Parse(string json)
        {
            var t = new TextTable();
            foreach (var lang in JObject.Parse(json).Properties())
            {
                var d = new Dictionary<string, string>();
                foreach (var kv in ((JObject)lang.Value).Properties()) d[kv.Name] = (string)kv.Value;
                t._langs[lang.Name] = d;
            }
            return t;
        }

        public string Get(string key)
        {
            if (_langs.TryGetValue(Language, out var d) && d.TryGetValue(key, out var v)) return v;
            if (_langs.TryGetValue(Fallback, out d) && d.TryGetValue(key, out v)) return v;
            return key;
        }

        /// <summary>Get(key) with {name} placeholders replaced.</summary>
        public string Format(string key, IDictionary<string, object> args)
        {
            var s = Get(key);
            foreach (var kv in args) s = s.Replace("{" + kv.Key + "}", kv.Value?.ToString());
            return s;
        }
    }
}

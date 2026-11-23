using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace T2U.Kit.Editor
{
    /// <summary>
    /// The moving parts of an imported UI screen (kit 1.3), added by <see cref="UiLayoutImporter"/> after the elements are
    /// built: flexbox containers and items (<see cref="UiFlex"/>, <see cref="UiFlexItem"/>), CSS animations
    /// (<see cref="UiAnim"/>), lists with their rows and row prefabs (<see cref="UiList"/>, <see cref="UiRow"/>).
    /// </summary>
    static class UiLayoutParts
    {
        public sealed class Part
        {
            public RectTransform Rt, Holder;
            public Rect Box, HolderBox;
            public TMP_Text Tmp;
            public float Ax, Ay;
            public JObject Layout, Flow;
            public JArray AnimList, List;
            public bool Hidden, Wrap;
            public float[] TBox;
            public int Dom;
            public string Parent, Scroll;
        }

        static float F(JToken t, float def = 0) => t == null || t.Type == JTokenType.Null ? def : (float)t;

        // ------------------------------------------------------------------ flexbox + animations
        public static void Flex(string screen, Dictionary<string, Part> parts, Rect rootBox, JObject json, out Dictionary<string, Part> all)
        {
            all = parts;
            int containers = 0, items = 0, fits = 0, hugs = 0, wraps = 0;
            var flexOf = new Dictionary<string, UiFlex>();
            foreach (var kv in parts)
            {
                var p = kv.Value;
                if (p.Layout == null) continue;
                if ((bool?)p.Layout["wrap"] == true) { wraps++; Debug.Log($"[T2U] UI {screen}: {kv.Key} wraps its children onto several lines: kept at the places of the resting page (UiFlex does one line)"); continue; }
                var f = p.Holder.gameObject.AddComponent<UiFlex>();
                f.Set((string)p.Layout["dir"] ?? "row", (string)p.Layout["justify"] ?? "normal", (string)p.Layout["align"] ?? "normal");
                f.gap = F(p.Layout["gap"]);
                if (p.Layout["pad"] is JArray pad) f.pad = new Vector4(F(pad[0]), F(pad[1]), F(pad[2]), F(pad[3]));
                flexOf[kv.Key] = f;
                containers++;
            }
            foreach (var kv in parts)
            {
                var p = kv.Value;
                if (p.Flow == null || p.Parent == null || !flexOf.TryGetValue(p.Parent, out var f)) continue;
                var it = p.Rt.gameObject.AddComponent<UiFlexItem>();
                it.grow = F(p.Flow["grow"]);
                it.order = (int)F(p.Flow["order"]);
                it.alignSelf = (string)p.Flow["self"] ?? "";
                if (p.Flow["margin"] is JArray m) it.margin = new Vector4(F(m[0]), F(m[1]), F(m[2]), F(m[3]));
                it.restSize = new Vector2(p.Box.width, p.Box.height);
                if (p.Flow["lbox"] is JArray lb)
                {
                    // drawn with a transform: the room it takes is the box without it
                    var room = new Rect(F(lb[0]), F(lb[1]), F(lb[2]), F(lb[3]));
                    it.transformed = true;
                    it.restSize = room.size;
                    it.drawnOffset = p.Box.center - room.center;
                }
                it.dom = p.Dom;
                // a text that decides its element's width (web: width auto): the element follows the text
                if (f.row && it.grow == 0 && !it.transformed && p.Tmp != null && !p.Wrap && !string.IsNullOrEmpty(p.Tmp.text))
                {
                    float pref = p.Tmp.GetPreferredValues(p.Tmp.text, float.PositiveInfinity, float.PositiveInfinity).x;
                    float textW = p.TBox != null ? p.TBox[2] : p.Box.width;
                    if (Mathf.Abs(pref - textW) <= Mathf.Max(2f, textW * 0.04f)) { it.hugText = p.Tmp; it.restTextWidth = pref; hugs++; }
                }
                // a wrapping text in a column (web: white-space normal, width auto): the item follows its text's
                // width up to the column and its lines' height. Its text is its own or that of its one text child.
                if (!f.row && it.grow == 0 && !it.transformed && it.hugText == null)
                {
                    var tp = p.Tmp != null ? p : null;
                    if (tp == null)
                    {
                        var kids = parts.Values.Where(c => c.Parent == kv.Key).ToList();
                        if (kids.Count == 1 && kids[0].Tmp != null && kids[0].Flow == null) tp = kids[0];
                    }
                    if (tp != null && tp.Wrap)
                    {
                        var pp = parts[p.Parent];
                        float room = pp.Box.width - f.pad.y - f.pad.w - it.margin.y - it.margin.w;
                        var pref = tp.Tmp.GetPreferredValues(tp.Tmp.text, float.PositiveInfinity, float.PositiveInfinity);
                        float textBoxW = tp != p ? tp.Box.width : tp.TBox != null ? tp.TBox[2] : p.Box.width;
                        it.hugText = tp.Tmp; it.wrapText = true;
                        // stretched across the column (web align stretch) while its resting text is narrower
                        it.wrapFill = Mathf.Abs(p.Box.width - room) <= 1f && pref.x + 1f < p.Box.width;
                        // the text's own room at rest: padding around it = the box minus that room
                        it.restTextWidth = it.wrapFill ? textBoxW : pref.x;
                        it.restTextHeight = it.wrapFill ? tp.Tmp.GetPreferredValues(tp.Tmp.text, textBoxW, float.PositiveInfinity).y : pref.y;
                        hugs++;
                    }
                }
                items++;
            }
            // containers whose size follows their content: a scrolling list's content, or a box exactly as big as what
            // flows in it (web: width/height auto) that nothing stretches
            foreach (var kv in flexOf)
            {
                var p = parts[kv.Key]; var f = kv.Value;
                int main = f.row ? 0 : 1;
                if (p.Scroll != null) { f.fitMain = (p.Scroll == "x") == f.row; if (f.fitMain) fits++; continue; }
                var kids = parts.Values.Where(c => c.Parent == kv.Key && c.Flow != null && !c.Hidden).ToList();
                if (kids.Count == 0 || kids.Any(c => F(c.Flow["grow"]) > 0)) continue;
                if ((main == 0 ? p.Ax : p.Ay) < 0) continue; // stretched by its parent
                if (p.Flow != null && F(p.Flow["grow"]) > 0) continue; // sized by its own container
                float used = kids.Sum(c => Room(c)[main] + (c.Flow["margin"] is JArray m ? (main == 0 ? F(m[1]) + F(m[3]) : F(m[0]) + F(m[2])) : 0));
                used += f.gap * (kids.Count - 1) + (main == 0 ? f.pad.y + f.pad.w : f.pad.x + f.pad.z);
                float size = main == 0 ? p.Box.width : p.Box.height;
                if (Mathf.Abs(used - size) <= 1f) { f.fitMain = true; fits++; }
            }
            int anims = Anims(screen, parts, json);
            Debug.Log($"[T2U] UI {screen}: {containers} flex containers ({fits} follow their content, {wraps} wrapping kept as is), {items} flex items ({hugs} follow their text), {anims} animated elements");
        }

        /// <summary>The room a flex item takes: its box, or the box without its transform.</summary>
        static Vector2 Room(Part c) => c.Flow?["lbox"] is JArray lb ? new Vector2(F(lb[2]), F(lb[3])) : c.Box.size;

        // ------------------------------------------------------------------ CSS animations
        static int Anims(string screen, Dictionary<string, Part> parts, JObject json)
        {
            var keyframes = json["keyframes"] as JObject;
            // the animations each element can play: at rest and in its states
            var uses = new Dictionary<string, HashSet<string>>();
            void Use(string node, JToken list)
            {
                if (!(list is JArray a)) return;
                if (!uses.TryGetValue(node, out var set)) uses[node] = set = new HashSet<string>();
                foreach (var x in a) set.Add((string)x["name"]);
            }
            foreach (var kv in parts) Use(kv.Key, kv.Value.AnimList);
            if (json["states"] is JArray states)
                foreach (JObject st in states)
                    foreach (var p in ((JObject)st["set"]).Properties())
                        if (parts.ContainsKey(p.Name) && p.Value is JObject d && d.Property("anim") != null) Use(p.Name, d["anim"]);
            int n = 0;
            var warned = new HashSet<string>();
            foreach (var kv in uses)
            {
                var p = parts[kv.Key];
                var a = p.Rt.gameObject.AddComponent<UiAnim>();
                a.rest = AnimText(p.AnimList);
                var pb = p.Parent != null && parts.TryGetValue(p.Parent, out var pp) ? pp.HolderBox : new Rect(0, 0, 0, 0);
                a.restLeftTop = new Vector2(p.Box.x - pb.x, p.Box.y - pb.y);
                foreach (var name in kv.Value)
                {
                    if (!(keyframes?[name] is JArray frames)) { if (warned.Add(name)) Debug.LogWarning($"[T2U] UI {screen}: @keyframes {name} not in ui.json"); continue; }
                    a.clips.Add(Clip(name, frames, screen, warned));
                }
                if (a.clips.Any(c => c.keys.Any(k => k.hasBright))) a.brightnessShader = BrightnessShader;
                n++;
            }
            return n;
        }

        const string BrightnessShaderPath = "Packages/com.t2u.kit/Runtime.Unity/Shaders/UiBrightness.shader";
        static Shader BrightnessShader => AssetDatabase.LoadAssetAtPath<Shader>(BrightnessShaderPath);

        public static string AnimText(JToken t) => t == null || t.Type != JTokenType.Array ? "" : string.Join(", ", t.Select(a => $"{(string)a["name"]} {((float)a["duration"]).ToString(CultureInfo.InvariantCulture)}s {(string)a["timing"]} {(string)a["iterations"]} {(string)a["direction"]}"));

        static UiAnim.Clip Clip(string name, JArray frames, string screen, HashSet<string> warned)
        {
            var clip = new UiAnim.Clip { name = name };
            foreach (JObject fr in frames)
            {
                var style = (JObject)fr["style"];
                foreach (var at in ((string)fr["at"]).Split(','))
                {
                    var s = at.Trim();
                    float t = s == "from" ? 0 : s == "to" ? 1 : float.Parse(s.TrimEnd('%'), CultureInfo.InvariantCulture) / 100;
                    var k = new UiAnim.Key { at = t };
                    foreach (var prop in style.Properties())
                    {
                        var v = (string)prop.Value;
                        switch (prop.Name)
                        {
                            case "transform": k.hasTransform = true; k.transform = Transform(v, name, screen, warned); break;
                            case "opacity": k.hasOpacity = true; k.opacity = Num(v); break;
                            case "left": k.hasLeft = true; k.left = Num(v); break;
                            case "top": k.hasTop = true; k.top = Num(v); break;
                            case "filter":
                                // brightness() is played (T2U/UI Brightness); other filter functions are not
                                var bm = Regex.Match(v, @"^\s*brightness\(\s*([-\d.]+)(%?)\s*\)\s*$");
                                if (v.Trim() == "none") { k.hasBright = true; k.bright = 1; }
                                else if (bm.Success) { k.hasBright = true; k.bright = float.Parse(bm.Groups[1].Value, CultureInfo.InvariantCulture) / (bm.Groups[2].Value == "%" ? 100 : 1); }
                                else if (warned.Add(name + "/" + v)) Debug.LogWarning($"[T2U] UI {screen}: @keyframes {name} uses filter `{v}`: only brightness() is played");
                                break;
                            case "animation-timing-function": k.timing = v; break;
                            default: if (warned.Add(name + "/" + prop.Name)) Debug.LogWarning($"[T2U] UI {screen}: @keyframes {name} animates `{prop.Name}`, which UiAnim does not play (it keeps its resting look)"); break;
                        }
                    }
                    clip.keys.Add(k);
                }
            }
            clip.keys.Sort((x, y) => x.at.CompareTo(y.at));
            return clip;
        }

        static float Num(string v) => float.Parse(Regex.Match(v, @"-?[\d.]+(e-?\d+)?").Value, CultureInfo.InvariantCulture);

        static UiAnim.Fn[] Transform(string v, string anim, string screen, HashSet<string> warned)
        {
            var r = new List<UiAnim.Fn>();
            if (v == "none") return r.ToArray();
            foreach (Match m in Regex.Matches(v, @"([a-zA-Z0-9]+)\(([^)]*)\)"))
            {
                var fn = m.Groups[1].Value;
                var args = m.Groups[2].Value.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();
                bool pct = args.Any(x => x.EndsWith("%"));
                float A(int i, float def) => i < args.Length ? Num(args[i]) : def;
                switch (fn)
                {
                    case "scale": r.Add(new UiAnim.Fn { kind = 0, x = A(0, 1), y = A(1, A(0, 1)) }); break;
                    case "scaleX": r.Add(new UiAnim.Fn { kind = 0, x = A(0, 1), y = 1 }); break;
                    case "scaleY": r.Add(new UiAnim.Fn { kind = 0, x = 1, y = A(0, 1) }); break;
                    case "translate": r.Add(new UiAnim.Fn { kind = pct ? 2 : 1, x = A(0, 0), y = A(1, 0) }); break;
                    case "translateX": r.Add(new UiAnim.Fn { kind = pct ? 2 : 1, x = A(0, 0), y = 0 }); break;
                    case "translateY": r.Add(new UiAnim.Fn { kind = pct ? 2 : 1, x = 0, y = A(0, 0) }); break;
                    default: if (warned.Add(anim + "/" + fn)) Debug.LogWarning($"[T2U] UI {screen}: @keyframes {anim} uses {fn}(), which UiAnim does not play"); break;
                }
            }
            return r.ToArray();
        }

        // ------------------------------------------------------------------ lists, rows, row prefabs
        public static void Lists(string screen, List<string> domOrder, Dictionary<string, Part> parts, GameObject root, string outDir)
        {
            var kids = new Dictionary<string, List<string>>();
            foreach (var name in domOrder)
            {
                var p = parts[name];
                if (p.Parent == null) continue;
                if (!kids.TryGetValue(p.Parent, out var l)) kids[p.Parent] = l = new List<string>();
                l.Add(name);
            }
            List<string> Below(string name)
            {
                var r = new List<string> { name };
                for (int i = 0; i < r.Count; i++) if (kids.TryGetValue(r[i], out var l)) r.AddRange(l);
                return r;
            }
            string Suffix(string row, string name) => name == row ? "" : name.StartsWith(row) ? name.Substring(row.Length) : "/" + name;
            string Sig(string row) => string.Join("|", Below(row).Select(x => Suffix(row, x)).OrderBy(x => x, System.StringComparer.Ordinal));

            var screenStates = root.GetComponent<UiStates>();
            var refs = root.GetComponent<UiRefs>();
            foreach (var listName in domOrder.Where(n => parts[n].List != null))
            {
                var lp = parts[listName];
                var ul = lp.Rt.gameObject.AddComponent<UiList>();
                ul.container = lp.Holder;
                var tpls = lp.List.Cast<JObject>().Select(t => new { name = (string)t["tpl"], prefix = (string)t["prefix"], sample = (string)t["sample"] }).ToList();
                var sigs = tpls.ToDictionary(t => t.name, t => parts.ContainsKey(t.sample) ? Sig(t.sample) : "");
                if (!kids.TryGetValue(listName, out var rows)) rows = new List<string>();
                foreach (var row in rows)
                {
                    var sig = Sig(row);
                    var tpl = tpls.FirstOrDefault(t => sigs[t.name] == sig) ?? tpls.Where(t => row.StartsWith(t.prefix)).OrderByDescending(t => t.prefix.Length).FirstOrDefault() ?? tpls.FirstOrDefault();
                    var ur = parts[row].Rt.gameObject.AddComponent<UiRow>();
                    ur.key = row;
                    ur.template = tpl?.name ?? "";
                    foreach (var name in Below(row))
                        ur.entries.Add(new UiRow.Entry { suffix = Suffix(row, name), rect = parts[name].Rt, button = ButtonOf(refs, name) });
                }
                // one prefab per row shape, made from its first row
                var dir = $"{outDir}/Rows";
                Directory.CreateDirectory(dir);
                foreach (var t in tpls)
                {
                    if (!parts.ContainsKey(t.sample)) continue;
                    var sampleGo = parts[t.sample].Rt.gameObject;
                    var clone = Object.Instantiate(sampleGo);
                    clone.name = t.name;
                    clone.SetActive(true);
                    var row = clone.GetComponent<UiRow>();
                    if (row != null) { row.key = ""; row.template = t.name; }
                    var rowNames = new HashSet<string>(Below(t.sample));
                    if (screenStates != null)
                    {
                        var hosts = new List<UiStates.Host>();
                        foreach (var h in screenStates.hosts.Where(h => rowNames.Contains(h.name)))
                            hosts.Add(new UiStates.Host
                            {
                                name = Suffix(t.sample, h.name), states = h.states.ToArray(), mask = h.mask,
                                rest = h.rest.Select(c => Map(c, sampleGo.transform, clone.transform)).ToList(),
                                variants = h.variants.Select(v => new UiStates.Variant { on = v.on, changes = v.changes.Select(c => Map(c, sampleGo.transform, clone.transform)).ToList() }).ToList(),
                            });
                        if (hosts.Count > 0) clone.AddComponent<UiStates>().hosts = hosts;
                    }
                    var path = $"{dir}/{t.name.Replace('#', '_')}.prefab";
                    var saved = PrefabUtility.SaveAsPrefabAsset(clone, path);
                    Object.DestroyImmediate(clone);
                    ul.templates.Add(new UiList.Template { name = t.name, sample = t.sample, prefab = saved.GetComponent<UiRow>() });
                }
                Debug.Log($"[T2U] UI {screen}: list {listName}: {rows.Count} rows, {tpls.Count} row prefab(s) in {dir}");
            }
        }

        static string ButtonOf(UiRefs refs, string name)
        {
            foreach (var e in refs.entries) if (e.name == name) return e.button ?? "";
            return "";
        }

        static UiStates.Change Map(UiStates.Change c, Transform from, Transform to) => new UiStates.Change
        {
            target = Map(c.target, from, to),
            setRect = c.setRect, anchoredPosition = c.anchoredPosition, sizeDelta = c.sizeDelta,
            setPivot = c.setPivot, pivot = c.pivot,
            setSkin = c.setSkin, skin = Map(c.skin, from, to), sprite = c.sprite, skinMin = c.skinMin, skinMax = c.skinMax,
            setColor = c.setColor, graphic = Map(c.graphic, from, to), color = c.color,
            setFont = c.setFont, text = Map(c.text, from, to), fontColor = c.fontColor, fontSize = c.fontSize,
            setAlpha = c.setAlpha, group = Map(c.group, from, to), alpha = c.alpha,
            setActive = c.setActive, active = c.active,
            setAnim = c.setAnim, anim = c.anim,
        };

        /// <summary>The same component in the copy of a hierarchy (by path from the root).</summary>
        static T Map<T>(T c, Transform from, Transform to) where T : Component
        {
            if (c == null) return null;
            var path = new List<string>();
            for (var t = c.transform; t != from; t = t.parent)
            {
                if (t == null) return null; // outside the row
                path.Insert(0, t.name);
            }
            var hit = path.Count == 0 ? to : to.Find(string.Join("/", path));
            return hit == null ? null : hit.GetComponent<T>();
        }
    }
}

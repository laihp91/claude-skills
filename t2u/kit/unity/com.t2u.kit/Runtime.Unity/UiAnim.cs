using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace T2U.Kit
{
    /// <summary>
    /// Plays the web's CSS animations (@keyframes) on a uGUI element: scale / translate (transform), opacity, left/top.
    /// Written by the UI layout importer from ui.json `keyframes` + the element's `anim` (at rest and in each state).
    /// Like CSS: an animation (re)starts when the element is shown or when a state gives it another animation, a finite
    /// one ends at its resting look, transforms turn around the element's centre and do not move its neighbours.
    /// Not played (the importer logs them): filter (brightness …: needs a shader), rotate/skew, colours, steps().
    /// Time = real time (Time.unscaledDeltaTime), as in the browser.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UiAnim : MonoBehaviour
    {
        [Serializable]
        public struct Fn
        {
            /// <summary>0 = scale(x, y), 1 = translate(x, y) in design units, 2 = translate in % of the element's size.</summary>
            public int kind;
            public float x, y;
        }

        [Serializable]
        public sealed class Key
        {
            /// <summary>0..1</summary>
            public float at;
            public bool hasTransform; public Fn[] transform = new Fn[0];
            public bool hasOpacity; public float opacity;
            public bool hasLeft; public float left;
            public bool hasTop; public float top;
            /// <summary>CSS filter: brightness(b) — needs <see cref="brightnessShader"/>.</summary>
            public bool hasBright; public float bright = 1;
            /// <summary>animation-timing-function written in this keyframe ("" = the animation's).</summary>
            public string timing = "";
        }

        [Serializable]
        public sealed class Clip
        {
            public string name;
            public List<Key> keys = new List<Key>();
        }

        /// <summary>The keyframes this element can play.</summary>
        public List<Clip> clips = new List<Clip>();
        /// <summary>The animation at rest ("bob 0.8s ease-in-out infinite alternate", several joined by ", "), "" = none.</summary>
        public string rest = "";
        /// <summary>left/top of the element inside its parent at rest (design units, web axes): where left/top keyframes start from.</summary>
        public Vector2 restLeftTop;
        /// <summary>
        /// The kit's "T2U/UI Brightness" shader, set by the importer when a clip animates `filter: brightness()`: while
        /// one plays, the element's images draw with it (each channel × b, as in the browser); otherwise their own material.
        /// Text keeps its look (TMP cannot be brighter than its colour; a white label stays white anyway).
        /// </summary>
        public Shader brightnessShader;

        sealed class Playing
        {
            public Clip clip; public float duration; public Bezier timing; public float iterations; public string direction; public float t;
        }

        readonly List<Playing> _playing = new List<Playing>();
        string _current;
        bool _started;
        // what this component last wrote, to tell its own offset from a new place set by layout / states
        Vector2 _offset, _written; bool _wroteOffset;
        float _baseAlpha = -1; CanvasGroup _group;
        int _startFrame = -1;
        Material _brightMat; readonly List<Graphic> _brightOn = new List<Graphic>();
        static readonly int BrightnessId = Shader.PropertyToID("_Brightness");

        /// <summary>The animation list playing (or last played): what <see cref="Play"/> was given.</summary>
        public string Current => _current ?? rest;

        /// <summary>Plays this animation list now (from the start), or stops all with "" / null.</summary>
        public void Play(string spec)
        {
            _current = spec ?? "";
            _startFrame = Time.frameCount;
            _playing.Clear();
            foreach (var part in SplitTop(_current))
            {
                var p = ParseOne(part);
                if (p != null) _playing.Add(p);
            }
            if (_playing.Count == 0) Restore();
            else Apply();
        }

        void OnEnable()
        {
            // CSS restarts an element's animations when it goes from hidden to shown
            Play(_started ? _current : rest);
            _started = true;
        }

        void OnDisable() => Restore();

        void LateUpdate()
        {
            if (_playing.Count == 0) return;
            // started during this frame: the frame's time passed before it started (a popup opened at the end of a
            // long frame — a replay, a map load — would otherwise jump straight to its end)
            float dt = Time.frameCount == _startFrame ? 0 : Time.unscaledDeltaTime;
            for (int i = _playing.Count - 1; i >= 0; i--)
            {
                var p = _playing[i];
                p.t += dt;
                if (!float.IsInfinity(p.iterations) && p.t >= p.duration * p.iterations) _playing.RemoveAt(i);
            }
            if (_playing.Count == 0) Restore();
            else Apply();
        }

        void Apply()
        {
            var rt = (RectTransform)transform;
            Vector2 s = Vector2.one, t = Vector2.zero, lt = Vector2.zero; float alpha = -1, bright = -1; bool hasLt = false;
            foreach (var p in _playing)
            {
                float prog = Progress(p);
                Sample(p, prog, rt.rect.size, ref s, ref t, ref alpha, ref lt, ref hasLt, ref bright);
            }
            SetBrightness(bright);
            // CSS turns around the centre, Unity scales around the pivot: move by what the pivot change costs
            var centreFromPivot = (new Vector2(0.5f, 0.5f) - rt.pivot) * rt.rect.size;
            var offset = new Vector2(t.x, -t.y) + Vector2.Scale(Vector2.one - s, centreFromPivot);
            if (hasLt) offset += new Vector2(lt.x - restLeftTop.x, -(lt.y - restLeftTop.y));
            SetOffset(rt, offset);
            rt.localScale = new Vector3(s.x, s.y, 1);
            if (alpha >= 0)
            {
                if (_group == null) _group = GetComponent<CanvasGroup>();
                if (_group == null) _group = gameObject.AddComponent<CanvasGroup>();
                if (_baseAlpha < 0) _baseAlpha = _group.alpha;
                _group.alpha = alpha;
            }
            else if (_baseAlpha >= 0) { _group.alpha = _baseAlpha; _baseAlpha = -1; }
        }

        /// <summary>b &lt; 0: no brightness animation (the images get their own material back).</summary>
        void SetBrightness(float b)
        {
            if (b < 0 || brightnessShader == null)
            {
                if (_brightOn.Count == 0) return;
                foreach (var g in _brightOn) if (g != null && g.material == _brightMat) g.material = null;
                _brightOn.Clear();
                return;
            }
            if (_brightMat == null) _brightMat = new Material(brightnessShader) { name = "UiBrightness (" + name + ")", hideFlags = HideFlags.DontSave };
            _brightMat.SetFloat(BrightnessId, b);
            if (_brightOn.Count > 0) return;
            // CSS filter: the element and everything inside it (its images: skin, icons)
            foreach (var g in GetComponentsInChildren<Graphic>(true))
            {
                if (g is TMPro.TMP_Text || g.material != g.defaultMaterial) continue; // text, or an image with its own material
                g.material = _brightMat;
                _brightOn.Add(g);
            }
        }

        void OnDestroy()
        {
            if (_brightMat != null) Destroy(_brightMat);
        }

        void Restore()
        {
            SetBrightness(-1);
            var rt = (RectTransform)transform;
            SetOffset(rt, Vector2.zero);
            rt.localScale = Vector3.one;
            if (_baseAlpha >= 0 && _group != null) _group.alpha = _baseAlpha;
            _baseAlpha = -1;
        }

        void SetOffset(RectTransform rt, Vector2 offset)
        {
            // if layout / a state moved the element since we wrote, its new place is the base (it has no offset in it)
            var basePos = _wroteOffset && rt.anchoredPosition == _written ? _written - _offset : rt.anchoredPosition;
            rt.anchoredPosition = basePos + offset;
            _offset = offset; _written = rt.anchoredPosition; _wroteOffset = true;
        }

        static float Progress(Playing p)
        {
            if (p.duration <= 0) return 1;
            float cycles = p.t / p.duration;
            if (!float.IsInfinity(p.iterations) && cycles >= p.iterations) cycles = p.iterations;
            int i = Mathf.FloorToInt(cycles);
            float f = cycles - i;
            if (f == 0 && cycles > 0 && !float.IsInfinity(p.iterations) && cycles >= p.iterations) { f = 1; i--; }
            bool rev = p.direction == "reverse" || (p.direction == "alternate" && (i & 1) == 1) || (p.direction == "alternate-reverse" && (i & 1) == 0);
            return rev ? 1 - f : f;
        }

        static void Sample(Playing p, float prog, Vector2 size, ref Vector2 s, ref Vector2 t, ref float alpha, ref Vector2 lt, ref bool hasLt, ref float bright)
        {
            var keys = p.clip.keys;
            // each property is interpolated between the keyframes that set it
            if (Pick(keys, prog, k => k.hasTransform, out var a, out var b, out var u, p.timing))
            {
                Compose(a == b ? a.transform : Mix(a.transform, b.transform, u), size, out var ks, out var kt);
                if (a != b && !SameShape(a.transform, b.transform))
                {
                    Compose(a.transform, size, out var s0, out var t0); Compose(b.transform, size, out var s1, out var t1);
                    ks = Vector2.LerpUnclamped(s0, s1, u); kt = Vector2.LerpUnclamped(t0, t1, u);
                }
                s = ks; t = kt; // CSS: of two animations of the same property, the later one wins
            }
            if (Pick(keys, prog, k => k.hasBright, out a, out b, out u, p.timing)) bright = Mathf.Max(0, Mathf.LerpUnclamped(a.bright, b.bright, u));
            if (Pick(keys, prog, k => k.hasOpacity, out a, out b, out u, p.timing)) alpha = Mathf.Clamp01(Mathf.LerpUnclamped(a.opacity, b.opacity, u));
            if (Pick(keys, prog, k => k.hasLeft, out a, out b, out u, p.timing)) { lt.x = Mathf.LerpUnclamped(a.left, b.left, u); hasLt = true; }
            if (Pick(keys, prog, k => k.hasTop, out a, out b, out u, p.timing)) { lt.y = Mathf.LerpUnclamped(a.top, b.top, u); hasLt = true; }
        }

        /// <summary>The two keyframes around <paramref name="prog"/> that set a property, and the eased fraction between them.</summary>
        static bool Pick(List<Key> keys, float prog, Func<Key, bool> has, out Key a, out Key b, out float u, Bezier anim)
        {
            a = b = null; u = 0;
            foreach (var k in keys)
            {
                if (!has(k)) continue;
                if (k.at <= prog) a = k;
                if (k.at >= prog && b == null) b = k;
            }
            if (a == null && b == null) return false;
            if (a == null) a = b;
            if (b == null) b = a;
            if (a == b) { u = 0; return true; }
            float lin = (prog - a.at) / (b.at - a.at);
            u = (string.IsNullOrEmpty(a.timing) ? anim : Bezier.Parse(a.timing)).Ease(lin);
            return true;
        }

        static bool SameShape(Fn[] a, Fn[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i].kind != b[i].kind) return false;
            return true;
        }

        static Fn[] Mix(Fn[] a, Fn[] b, float u)
        {
            if (!SameShape(a, b)) return a;
            var r = new Fn[a.Length];
            for (int i = 0; i < a.Length; i++) r[i] = new Fn { kind = a[i].kind, x = Mathf.LerpUnclamped(a[i].x, b[i].x, u), y = Mathf.LerpUnclamped(a[i].y, b[i].y, u) };
            return r;
        }

        /// <summary>A transform list as scale s and translation t (applied left to right like CSS): p' = s·p + t.</summary>
        static void Compose(Fn[] fns, Vector2 size, out Vector2 s, out Vector2 t)
        {
            s = Vector2.one; t = Vector2.zero;
            foreach (var f in fns)
            {
                if (f.kind == 0) s = Vector2.Scale(s, new Vector2(f.x, f.y));
                else
                {
                    var d = f.kind == 2 ? new Vector2(f.x * size.x / 100, f.y * size.y / 100) : new Vector2(f.x, f.y);
                    t += Vector2.Scale(s, d);
                }
            }
        }

        Playing ParseOne(string part)
        {
            // "<name> <duration>s <timing> <iterations> <direction>" (the importer's order; CSS shorthand order also works)
            var words = SplitWords(part);
            if (words.Count == 0) return null;
            Clip clip = null; float dur = 0; string timing = "ease"; float iter = 1; string dir = "normal";
            foreach (var w in words)
            {
                if (clip == null) { var c = clips.Find(x => x.name == w); if (c != null) { clip = c; continue; } }
                if (w.EndsWith("ms") && float.TryParse(w.Substring(0, w.Length - 2), NumberStyles.Float, CultureInfo.InvariantCulture, out var ms)) dur = ms / 1000;
                else if (w.EndsWith("s") && float.TryParse(w.Substring(0, w.Length - 1), NumberStyles.Float, CultureInfo.InvariantCulture, out var sec)) dur = sec;
                else if (w == "infinite") iter = float.PositiveInfinity;
                else if (float.TryParse(w, NumberStyles.Float, CultureInfo.InvariantCulture, out var it)) iter = it;
                else if (w == "normal" || w == "reverse" || w == "alternate" || w == "alternate-reverse") dir = w;
                else if (w.StartsWith("cubic-bezier") || w.StartsWith("ease") || w == "linear") timing = w;
            }
            if (clip == null) return null;
            return new Playing { clip = clip, duration = dur, timing = Bezier.Parse(timing), iterations = iter, direction = dir };
        }

        /// <summary>Splits on commas that are not inside parentheses.</summary>
        public static List<string> SplitTop(string s)
        {
            var r = new List<string>();
            if (string.IsNullOrWhiteSpace(s)) return r;
            int depth = 0, from = 0;
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '(') depth++;
                else if (s[i] == ')') depth--;
                else if (s[i] == ',' && depth == 0) { r.Add(s.Substring(from, i - from).Trim()); from = i + 1; }
            }
            r.Add(s.Substring(from).Trim());
            r.RemoveAll(string.IsNullOrEmpty);
            return r;
        }

        static List<string> SplitWords(string s)
        {
            var r = new List<string>();
            int depth = 0, from = 0;
            for (int i = 0; i <= s.Length; i++)
            {
                if (i < s.Length && s[i] == '(') depth++;
                else if (i < s.Length && s[i] == ')') depth--;
                else if (i == s.Length || (char.IsWhiteSpace(s[i]) && depth == 0))
                {
                    if (i > from) r.Add(s.Substring(from, i - from));
                    from = i + 1;
                }
            }
            return r;
        }

        /// <summary>CSS timing functions (cubic-bezier and its named forms).</summary>
        public struct Bezier
        {
            float _x1, _y1, _x2, _y2; bool _linear;

            public static Bezier Parse(string s)
            {
                s = (s ?? "ease").Trim();
                switch (s)
                {
                    case "linear": return new Bezier { _linear = true };
                    case "ease": return Of(0.25f, 0.1f, 0.25f, 1);
                    case "ease-in": return Of(0.42f, 0, 1, 1);
                    case "ease-out": return Of(0, 0, 0.58f, 1);
                    case "ease-in-out": return Of(0.42f, 0, 0.58f, 1);
                }
                if (s.StartsWith("cubic-bezier("))
                {
                    var v = s.Substring(13).TrimEnd(')').Split(',');
                    if (v.Length == 4)
                    {
                        float F(int i) => float.Parse(v[i].Trim(), CultureInfo.InvariantCulture);
                        return Of(F(0), F(1), F(2), F(3));
                    }
                }
                return Of(0.25f, 0.1f, 0.25f, 1);
            }

            static Bezier Of(float x1, float y1, float x2, float y2) => new Bezier { _x1 = x1, _y1 = y1, _x2 = x2, _y2 = y2 };

            public float Ease(float x)
            {
                if (_linear || x <= 0 || x >= 1) return x;
                // solve bezierX(t) = x (Newton, then bisection), return bezierY(t)
                float t = x;
                for (int i = 0; i < 8; i++)
                {
                    float e = Bx(t) - x;
                    if (Mathf.Abs(e) < 1e-6f) return By(t);
                    float d = Dx(t);
                    if (Mathf.Abs(d) < 1e-6f) break;
                    t -= e / d;
                }
                float lo = 0, hi = 1; t = x;
                for (int i = 0; i < 30; i++)
                {
                    float v = Bx(t);
                    if (Mathf.Abs(v - x) < 1e-6f) break;
                    if (v < x) lo = t; else hi = t;
                    t = (lo + hi) / 2;
                }
                return By(t);
            }

            float Bx(float t) => ((1 - 3 * _x2 + 3 * _x1) * t + (3 * _x2 - 6 * _x1)) * t * t + 3 * _x1 * t;
            float By(float t) => ((1 - 3 * _y2 + 3 * _y1) * t + (3 * _y2 - 6 * _y1)) * t * t + 3 * _y1 * t;
            float Dx(float t) => 3 * (1 - 3 * _x2 + 3 * _x1) * t * t + 2 * (3 * _x2 - 6 * _x1) * t + 3 * _x1;
        }
    }
}

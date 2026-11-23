using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace T2U.Kit.Editor
{
    /// <summary>
    /// Rebuilds the web UI in uGUI from Assets/_Game/Data/UI/ui.json (written by `npm run t2u:export`, measured from the
    /// DOM in 1080-wide design units). Output (safe to re-run, overwrites generated prefabs only):
    ///   Assets/_Game/Prefabs/UI/&lt;screen&gt;.prefab  one per data-ui-screen, with a UiRefs component (names = data-ui)
    ///                                             and a UiStates component (the looks of every state)
    ///   Assets/_Game/Prefabs/UI/UICanvas.prefab   Canvas (Scale With Screen Size, 1080 wide, match width) + all screens
    /// Anchoring: each element is anchored to the closest side or the centre of its parent on each axis (full-size
    /// elements stretch), so taller phones keep popups centred and bars at their edges.
    /// Looks (ui.json version 2): `skin` = a sprite photographed from the web (Assets/_Game/Art/UI/&lt;skin&gt;.png, 9-sliced
    /// by the border in ui.json `sprites`), drawn by a child "Skin" that reaches `skinBox` outside the element (shadows).
    /// An element without a skin: a flat colour (`bg`), or — version 1 exports — a colour rounded by a generated sprite.
    /// Images: Assets/_Game/Art/Sprites/&lt;file name&gt;.png. Texts: TextMesh Pro with the web's size, colour, alignment,
    /// letter spacing, wrapping and shadow (a material preset per shadow next to the font).
    /// Fonts: "&lt;CSS font-family&gt; SDF" built by FontBuilder from Assets/_Game/Art/Fonts, else T2UKit.json uiFont, else TMP default.
    /// Layout (kit 1.3): `layout` → <see cref="UiFlex"/> (children that move when a sibling is hidden or a text grows),
    /// `flow` → <see cref="UiFlexItem"/>, `scroll` → ScrollRect + a "Content" child, `list` → <see cref="UiList"/> + a
    /// <see cref="UiRow"/> per row + one row prefab per row shape (Prefabs/UI/Rows), `anim` + `keyframes` →
    /// <see cref="UiAnim"/>. Not rebuilt: wrapping flex containers (positions of the resting page), CSS filters.
    /// Batch: -executeMethod T2U.Kit.Editor.UiLayoutImporter.RunBatch [-t2uUiJson path] [-t2uOverwrite]
    /// </summary>
    public static class UiLayoutImporter
    {
        const string DefaultJson = "Assets/_Game/Data/UI/ui.json";
        const string OutDir = "Assets/_Game/Prefabs/UI";
        const string SkinDir = "Assets/_Game/Art/UI";
        const string SpriteDir = "Assets/_Game/Art/Sprites";
        const string RoundPath = "Assets/_Game/Art/UI/t2u_round.png";
        const int RoundBorder = 64;

        sealed class Node
        {
            public string Name, Parent, Text, Image, Button, Skin, Anim;
            public float X, Y, W, H, Radius, Opacity = 1;
            public float[] SkinBox, TBox, IBox; // skinBox: left, top, right, bottom outside the element; boxes: x, y, w, h
            public Color? Bg;
            public bool Hidden, Scroll;
            public string ScrollAxis;
            public JObject Layout, Flow;
            /// <summary>ui.json `pin`: the edge a placed flex box stays on when its content makes it bigger ([x, y]; null entries: none).</summary>
            public JToken Pin;
            public JArray List, AnimList;
            public int Dom;
            public int? Z;
            public float FontSize, Spacing, LineHeight;
            public Color FontColor = Color.white;
            public bool Bold, Wrap, Upper;
            public string Align, VAlign, Family;
            public bool HasShadow; public float ShadowX, ShadowY, ShadowBlur; public Color ShadowColor;
            public string FillAxis, FillFrom, FillMode;
            public Rect Box => new Rect(X, Y, W, H);
        }

        sealed class Made
        {
            public Node N; public RectTransform Rt; public float Ax, Ay;
            /// <summary>Pivot in web axes (0 left/top, 1 right/bottom): the anchor, or the edge the web grows the box from (`pin`).</summary>
            public float Gx, Gy;
            public Image Skin, Graphic; public TMP_Text Tmp; public CanvasGroup Group;
            /// <summary>Where the children go: the element itself, or the Content of a scrolling element.</summary>
            public RectTransform Content; public Rect? ContentBox;
            public RectTransform Holder => Content != null ? Content : Rt;
            public Rect HolderBox => ContentBox ?? N.Box;
        }

        sealed class Ctx
        {
            public float Scale = 1;
            public JObject Sprites;
            public Sprite Round;
            public TMP_FontAsset Font;
        }

        /// <summary>Creates prefabs for screens that do not exist yet; never touches existing ones (safe after handover).</summary>
        [MenuItem("Tools/T2U Kit/Import UI Layouts (new screens only)", priority = 30)]
        public static void Run() => Import(DefaultJson, overwrite: false);

        [MenuItem("Tools/T2U Kit/Import UI Layouts (OVERWRITE all)", priority = 31)]
        static void RunOverwrite()
        {
            if (!EditorUtility.DisplayDialog("Overwrite UI prefabs?", "Every UI prefab and UICanvas will be rebuilt from ui.json. Hand edits in those prefabs are lost.", "Overwrite", "Cancel")) return;
            Import(DefaultJson, overwrite: true);
        }

        /// <summary>Batch: -t2uOverwrite rebuilds everything (refused when generatedFrozen is set in T2UKit.json).</summary>
        public static void RunBatch()
        {
            bool ow = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-t2uOverwrite") >= 0;
            if (ow && KitSettings.Load().generatedFrozen) { Debug.LogError("[T2U] generatedFrozen is set: refusing to overwrite UI prefabs."); KitSettings.ExitIfBatch(false); return; }
            Import(KitSettings.Arg("-t2uUiJson", DefaultJson), ow);
            KitSettings.ExitIfBatch(true);
        }

        public static void Import(string jsonPath, bool overwrite = true)
        {
            if (!File.Exists(jsonPath)) { Debug.LogError($"[T2U] {jsonPath} not found: run `npm run t2u:export` in the web project, then Tools/T2U Kit/Sync From Web Project."); return; }
            var json = JObject.Parse(File.ReadAllText(jsonPath));
            float refW = (float?)json["refWidth"] ?? 1080, refH = (float?)json["refHeight"] ?? 1920;
            Directory.CreateDirectory(OutDir);
            FontBuilder.Run();
            var ctx = new Ctx { Scale = (float?)json["scale"] ?? 1, Sprites = json["sprites"] as JObject, Font = Font() };
            if (ctx.Sprites != null) ImportSkins(ctx);
            else ctx.Round = RoundSprite(); // version 1: rounded corners from one generated sprite
            var screens = new List<(GameObject prefab, bool hidden)>();
            foreach (JObject scr in json["screens"])
            {
                var name = (string)scr["screen"];
                var existing = AssetDatabase.LoadAssetAtPath<GameObject>($"{OutDir}/{name}.prefab");
                if (existing != null && !overwrite)
                {
                    screens.Add((existing, (bool?)scr["startsHidden"] ?? false));
                    Debug.Log($"[T2U] UI {name}: prefab exists, kept as is");
                    continue;
                }
                var nodes = ((JArray)scr["nodes"]).Select(n => Parse((JObject)n)).ToList();
                for (int i = 0; i < nodes.Count; i++) nodes[i].Dom = i;
                var go = BuildScreen(name, nodes, refW, refH, ctx, json);
                var path = $"{OutDir}/{name}.prefab";
                var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
                Object.DestroyImmediate(go);
                screens.Add((prefab, (bool?)scr["startsHidden"] ?? false));
            }
            // canvas with every screen (hidden ones inactive), in DOM order — only (re)built when overwriting or missing
            if (!overwrite && AssetDatabase.LoadAssetAtPath<GameObject>($"{OutDir}/UICanvas.prefab") != null)
            {
                AssetDatabase.SaveAssets();
                Debug.Log("[T2U] UICanvas exists, kept as is (add new screens to it by hand)");
                return;
            }
            var canvasGo = new GameObject("UICanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(refW, refH);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0;
            scaler.referencePixelsPerUnit = 100;
            foreach (var (prefab, hidden) in screens)
            {
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, canvasGo.transform);
                inst.SetActive(!hidden);
            }
            PrefabUtility.SaveAsPrefabAsset(canvasGo, $"{OutDir}/UICanvas.prefab");
            Object.DestroyImmediate(canvasGo);
            AssetDatabase.SaveAssets();
            Debug.Log($"[T2U] UI imported: {string.Join(", ", screens.Select(s => s.prefab.name))} -> {OutDir}");
        }

        static Color? Col(JToken t) => t == null || t.Type == JTokenType.Null ? (Color?)null : new Color((float)t["r"], (float)t["g"], (float)t["b"], (float)t["a"]);
        static bool Has(JToken t) => t != null && t.Type != JTokenType.Null;
        static float[] Arr(JToken t) => t == null || t.Type != JTokenType.Array ? null : t.Select(v => (float)v).ToArray();
        static string AnimText(JToken t) => UiLayoutParts.AnimText(t);

        static void ParseFont(JObject f, Node n)
        {
            n.FontSize = (float)f["size"];
            n.FontColor = Col(f["color"]) ?? Color.white;
            n.Bold = (bool?)f["bold"] ?? false;
            n.Align = (string)f["align"];
            n.VAlign = (string)f["valign"] ?? "middle";
            n.Family = (string)f["family"];
            n.Spacing = (float?)f["spacing"] ?? 0;
            n.LineHeight = (float?)f["lineHeight"] ?? 0;
            n.Wrap = (bool?)f["wrap"] ?? false;
            n.Upper = (bool?)f["upper"] ?? false;
            if (f["shadow"] is JObject sh)
            {
                n.HasShadow = true;
                n.ShadowX = (float)sh["x"]; n.ShadowY = (float)sh["y"]; n.ShadowBlur = (float?)sh["blur"] ?? 0;
                n.ShadowColor = Col(sh["color"]) ?? Color.black;
            }
        }

        static Node Parse(JObject o)
        {
            var n = new Node
            {
                Name = (string)o["name"], Parent = (string)o["parent"], Text = (string)o["text"], Image = (string)o["image"], Button = (string)o["button"],
                X = (float)o["x"], Y = (float)o["y"], W = (float)o["w"], H = (float)o["h"], Radius = (float?)o["radius"] ?? 0, Opacity = (float?)o["opacity"] ?? 1,
                Bg = Col(o["bg"]),
                Skin = (string)o["skin"], SkinBox = Arr(o["skinBox"]), TBox = Arr(o["tbox"]), IBox = Arr(o["ibox"]),
                Hidden = (bool?)o["hidden"] ?? false, Scroll = o["scroll"] != null, Z = (int?)o["z"], Anim = AnimText(o["anim"]),
                ScrollAxis = (string)o["scroll"], Layout = o["layout"] as JObject, Flow = o["flow"] as JObject, Pin = o["pin"], List = o["list"] as JArray, AnimList = o["anim"] as JArray,
            };
            if (o["font"] is JObject f) ParseFont(f, n);
            if (o["fill"] is JObject fl) { n.FillAxis = (string)fl["axis"]; n.FillFrom = (string)fl["from"]; n.FillMode = (string)fl["mode"]; }
            return n;
        }

        // ------------------------------------------------------------------ skins (ui.json `sprites`)
        static void ImportSkins(Ctx ctx)
        {
            AssetDatabase.Refresh();
            foreach (var p in ctx.Sprites.Properties())
            {
                var path = $"{SkinDir}/{p.Name}.png";
                var ti = AssetImporter.GetAtPath(path) as TextureImporter;
                if (ti == null) { Debug.LogWarning($"[T2U] UI skin not found: {path} (run Tools/T2U Kit/Sync From Web Project: it copies export/ui/*.png)"); continue; }
                var b = Arr(p.Value["border"]) ?? new float[4]; // left, bottom, right, top (pixels of the sprite)
                var border = new Vector4(b[0], b[1], b[2], b[3]);
                float ppu = 100 * ctx.Scale;
                var st = new TextureImporterSettings();
                ti.ReadTextureSettings(st);
                bool same = ti.textureType == TextureImporterType.Sprite && ti.spriteImportMode == SpriteImportMode.Single && ti.spriteBorder == border && Mathf.Approximately(ti.spritePixelsPerUnit, ppu)
                    && st.spriteMeshType == SpriteMeshType.FullRect && !ti.mipmapEnabled && ti.textureCompression == TextureImporterCompression.Uncompressed;
                if (same) continue;
                ti.textureType = TextureImporterType.Sprite;
                ti.spriteImportMode = SpriteImportMode.Single;
                ti.spritePixelsPerUnit = ppu; // `scale` sprite pixels = one canvas unit
                ti.spriteBorder = border;
                ti.mipmapEnabled = false;
                ti.alphaIsTransparency = true;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.filterMode = FilterMode.Bilinear;
                ti.textureCompression = TextureImporterCompression.Uncompressed; // small pictures with soft edges: keep them exact
                ti.ReadTextureSettings(st);
                st.spriteMeshType = SpriteMeshType.FullRect; // sliced and filled images need the whole rectangle
                ti.SetTextureSettings(st);
                ti.SaveAndReimport();
            }
        }

        static Sprite SkinSprite(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            var s = AssetDatabase.LoadAssetAtPath<Sprite>($"{SkinDir}/{name}.png");
            if (s == null) Debug.LogWarning($"[T2U] UI skin sprite missing: {SkinDir}/{name}.png");
            return s;
        }

        static Sprite PictureSprite(string file, string forNode)
        {
            var s = AssetDatabase.LoadAssetAtPath<Sprite>($"{SpriteDir}/{Path.GetFileNameWithoutExtension(file)}.png");
            if (s == null) Debug.LogWarning($"[T2U] UI image not found for {forNode}: {SpriteDir}/{file} (sync assets first)");
            return s;
        }

        // ------------------------------------------------------------------ one screen
        static GameObject BuildScreen(string screen, List<Node> nodes, float refW, float refH, Ctx ctx, JObject json)
        {
            var root = new GameObject(screen, typeof(RectTransform));
            var rt = (RectTransform)root.transform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
            var refs = root.AddComponent<UiRefs>();
            refs.screen = screen;
            var made = new Dictionary<string, Made>();
            var rootNode = new Node { X = 0, Y = 0, W = refW, H = refH };
            var contentBoxes = ContentBoxes(nodes);
            foreach (var n in nodes)
            {
                var hasParent = n.Parent != null && made.ContainsKey(n.Parent);
                var parentRt = hasParent ? made[n.Parent].Holder : rt;
                var parentNode = hasParent ? made[n.Parent].N : rootNode;
                var parentBox = hasParent ? made[n.Parent].HolderBox : rootNode.Box;
                var go = new GameObject(n.Name, typeof(RectTransform));
                var r = (RectTransform)go.transform;
                r.SetParent(parentRt, false);
                var m = new Made { N = n, Rt = r };
                Anchors(n.Box, parentBox, out m.Ax, out m.Ay);
                Pivot(n.Pin, m.Ax, m.Ay, out m.Gx, out m.Gy);
                Place(r, n.Box, parentBox, m.Ax, m.Ay, m.Gx, m.Gy);
                Dress(go, n, parentNode, m, ctx);
                if (n.Scroll && contentBoxes.TryGetValue(n.Name, out var cb)) { m.Content = ScrollContent(go, n, cb); m.ContentBox = cb; }
                made[n.Name] = m;
                refs.entries.Add(new UiRefs.Entry { name = n.Name, rect = r, button = n.Button, anim = n.Anim });
            }
            // drawn over its later siblings (CSS z-index): moved to the end of its parent, lowest first
            foreach (var m in made.Values.Where(x => x.N.Z.HasValue && x.N.Z.Value > 0).OrderBy(x => x.N.Z.Value)) m.Rt.SetAsLastSibling();
            BuildStates(root, screen, made, rootNode, ctx, json);
            UiLayoutParts.Flex(screen, made.ToDictionary(kv => kv.Key, kv => new UiLayoutParts.Part { Rt = kv.Value.Rt, Holder = kv.Value.Holder, Box = kv.Value.N.Box, HolderBox = kv.Value.HolderBox, Tmp = kv.Value.Tmp, Ax = kv.Value.Ax, Ay = kv.Value.Ay, Layout = kv.Value.N.Layout, Flow = kv.Value.N.Flow, Hidden = kv.Value.N.Hidden, Wrap = kv.Value.N.Wrap, TBox = kv.Value.N.TBox, Dom = kv.Value.N.Dom, Parent = kv.Value.N.Parent, Scroll = kv.Value.N.ScrollAxis, AnimList = kv.Value.N.AnimList, List = kv.Value.N.List }), rootNode.Box, json, out var parts);
            // hidden at rest (web: the `hidden` attribute); the screen itself is shown and hidden by its Canvas entry
            foreach (var m in made.Values) if (m.N.Hidden && m.N.Parent != null) m.Rt.gameObject.SetActive(false);
            UiLayoutParts.Lists(screen, nodes.Select(x => x.Name).ToList(), parts, root, OutDir);
            return root;
        }

        /// <summary>
        /// The scrolled content of each scrolling element: as wide as the element and as tall as its children reach
        /// (+ the bottom padding) for a vertical list; the same across for a horizontal one.
        /// </summary>
        static Dictionary<string, Rect> ContentBoxes(List<Node> nodes)
        {
            var r = new Dictionary<string, Rect>();
            foreach (var s in nodes.Where(n => n.Scroll))
            {
                var kids = nodes.Where(n => n.Parent == s.Name).ToList();
                var pad = s.Layout?["pad"] as JArray;
                float padB = pad != null ? (float)pad[2] : 0, padR = pad != null ? (float)pad[1] : 0;
                if (s.ScrollAxis == "x")
                {
                    float right = kids.Count == 0 ? s.X + s.W : Mathf.Max(s.X + s.W, kids.Max(k => k.X + k.W + Margin(k, 1)) + padR);
                    r[s.Name] = new Rect(s.X, s.Y, right - s.X, s.H);
                }
                else
                {
                    float bottom = kids.Count == 0 ? s.Y + s.H : Mathf.Max(s.Y + s.H, kids.Max(k => k.Y + k.H + Margin(k, 2)) + padB);
                    r[s.Name] = new Rect(s.X, s.Y, s.W, bottom - s.Y);
                }
            }
            return r;
        }

        static float Margin(Node n, int side) => n.Flow?["margin"] is JArray m ? (float)m[side] : 0;

        /// <summary>A scrolling element: a ScrollRect on it and a "Content" child that holds its children.</summary>
        static RectTransform ScrollContent(GameObject go, Node n, Rect cb)
        {
            var c = new GameObject("Content", typeof(RectTransform));
            var crt = (RectTransform)c.transform;
            crt.SetParent(go.transform, false);
            bool x = n.ScrollAxis == "x";
            if (x) { crt.anchorMin = new Vector2(0, 0); crt.anchorMax = new Vector2(0, 1); crt.pivot = new Vector2(0, 0.5f); crt.sizeDelta = new Vector2(cb.width, 0); }
            else { crt.anchorMin = new Vector2(0, 1); crt.anchorMax = new Vector2(1, 1); crt.pivot = new Vector2(0.5f, 1); crt.sizeDelta = new Vector2(0, cb.height); }
            crt.anchoredPosition = Vector2.zero;
            // the empty parts of the list take the finger too (rows' buttons pass drags on to the ScrollRect)
            var hit = go.GetComponent<Image>();
            if (hit == null) { hit = go.AddComponent<Image>(); hit.color = Color.clear; }
            hit.raycastTarget = true;
            var sr = go.AddComponent<ScrollRect>();
            sr.content = crt;
            sr.viewport = (RectTransform)go.transform;
            sr.horizontal = x; sr.vertical = !x;
            sr.movementType = ScrollRect.MovementType.Elastic;
            sr.inertia = true;
            sr.scrollSensitivity = 30;
            return crt;
        }

        /// <summary>Anchor to the nearest side / centre of the parent per axis; full-size → stretch (-1).</summary>
        static void Anchors(Rect n, Rect p, out float ax, out float ay)
        {
            const float tol = 2f;
            float l = n.x - p.x, rgt = p.x + p.width - (n.x + n.width), cx = (n.x + n.width / 2) - (p.x + p.width / 2);
            ax = Mathf.Abs(l) <= tol && Mathf.Abs(rgt) <= tol ? -1 : Mathf.Abs(cx) <= tol ? 0.5f : l <= rgt ? 0 : 1;
            float t = n.y - p.y, b = p.y + p.height - (n.y + n.height), cy = (n.y + n.height / 2) - (p.y + p.height / 2);
            ay = Mathf.Abs(t) <= tol && Mathf.Abs(b) <= tol ? -1 : Mathf.Abs(cy) <= tol ? 0.5f : t <= b ? 0 : 1;
        }

        /// <summary>
        /// The pivot (web axes): the anchor, except where the web pins a placed flex box by another edge (`pin`). The
        /// anchors stay (where the element goes on a taller screen); the pivot is the edge that stays when its content
        /// makes it bigger (UiFlex fitMain), as on the web: a card pinned by its bottom grows up.
        /// </summary>
        static void Pivot(JToken pin, float ax, float ay, out float gx, out float gy)
        {
            gx = ax < 0 ? 0.5f : ax; gy = ay < 0 ? 0.5f : ay;
            if (!(pin is JArray a) || a.Count < 2) return;
            if (ax >= 0 && a[0].Type != JTokenType.Null) gx = (float)a[0];
            if (ay >= 0 && a[1].Type != JTokenType.Null) gy = (float)a[1];
        }

        /// <summary>Position and size for those anchors (x: 0 left, 0.5 centre, 1 right; y, web, down: 0 top, 0.5 middle, 1 bottom; -1 stretch) and that pivot (web axes; NaN: the anchor).</summary>
        static void Calc(Rect n, Rect p, float ax, float ay, out Vector2 pos, out Vector2 size, float gx = float.NaN, float gy = float.NaN)
        {
            float l = n.x - p.x, rgt = p.x + p.width - (n.x + n.width);
            float t = n.y - p.y, b = p.y + p.height - (n.y + n.height);
            if (float.IsNaN(gx)) gx = ax; if (float.IsNaN(gy)) gy = ay;
            pos = default; size = default;
            // anchoredPosition = the pivot's point minus the anchor's point (Unity: y up)
            if (ax < 0) { size.x = -(l + rgt); pos.x = (l - rgt) / 2; }
            else { size.x = n.width; pos.x = (n.x + gx * n.width) - (p.x + ax * p.width); }
            if (ay < 0) { size.y = -(t + b); pos.y = (b - t) / 2; }
            else { size.y = n.height; pos.y = -((n.y + gy * n.height) - (p.y + ay * p.height)); }
        }

        static void Place(RectTransform r, Rect n, Rect p, float ax, float ay, float gx, float gy)
        {
            Vector2 aMin, aMax, pivot;
            if (ax < 0) { aMin.x = 0; aMax.x = 1; pivot.x = 0.5f; } else { aMin.x = aMax.x = ax; pivot.x = gx; }
            if (ay < 0) { aMin.y = 0; aMax.y = 1; pivot.y = 0.5f; } else { float uy = 1 - ay; aMin.y = aMax.y = uy; pivot.y = 1 - gy; }
            Calc(n, p, ax, ay, out var pos, out var size, gx, gy);
            r.anchorMin = aMin; r.anchorMax = aMax; r.pivot = pivot; r.sizeDelta = size; r.anchoredPosition = pos;
        }

        /// <summary>A child that fills its parent, reaching (left, top, right, bottom) canvas units outside it.</summary>
        static RectTransform Child(GameObject parent, string name, float l = 0, float t = 0, float r = 0, float b = 0)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent.transform, false);
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(-l, -b); rt.offsetMax = new Vector2(r, t);
            return rt;
        }

        static void SkinOffsets(float[] box, out Vector2 min, out Vector2 max)
        {
            float l = box?[0] ?? 0, t = box?[1] ?? 0, r = box?[2] ?? 0, b = box?[3] ?? 0;
            min = new Vector2(-l, -b); max = new Vector2(r, t);
        }

        static void Dress(GameObject go, Node n, Node parent, Made m, Ctx ctx)
        {
            bool isButton = n.Button != null;
            Sprite skin = ctx.Sprites != null ? SkinSprite(n.Skin) : null;
            if (skin != null)
            {
                // the look photographed from the web, on a child that reaches outside the element where its shadow does
                var srt = Child(go, "Skin");
                SkinOffsets(n.SkinBox, out var min, out var max);
                srt.offsetMin = min; srt.offsetMax = max;
                var img = srt.gameObject.AddComponent<Image>();
                img.sprite = skin;
                img.type = skin.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
                img.raycastTarget = isButton;
                m.Skin = img;
            }
            else if (n.Bg != null && n.Image == null)
            {
                var img = go.AddComponent<Image>();
                img.color = n.Bg.Value;
                img.raycastTarget = isButton || (n.Parent == null && n.Bg != null); // buttons and full-screen shades block clicks
                if (ctx.Round != null && n.Radius > 0.5f)
                {
                    img.sprite = ctx.Round;
                    img.type = Image.Type.Sliced;
                    img.pixelsPerUnitMultiplier = RoundBorder / Mathf.Min(n.Radius, Mathf.Min(n.W, n.H) / 2);
                }
                m.Graphic = img;
            }
            if (n.Image != null)
            {
                var sprite = PictureSprite(n.Image, n.Name);
                GameObject host = go;
                if (skin != null || n.IBox != null)
                {
                    // a framed picture: the picture sits inside the frame's border
                    var ib = n.IBox ?? new[] { n.X, n.Y, n.W, n.H };
                    host = Child(go, "Image", -(ib[0] - n.X), -(ib[1] - n.Y), -(n.X + n.W - ib[0] - ib[2]), -(n.Y + n.H - ib[1] - ib[3])).gameObject;
                }
                var img = host.AddComponent<Image>();
                img.sprite = sprite;
                img.color = Color.white;
                img.raycastTarget = isButton && skin == null;
                if (sprite != null && sprite.border != Vector4.zero) img.type = Image.Type.Sliced;
                if (m.Graphic == null) m.Graphic = img;
            }
            if (n.Opacity < 0.999f) { m.Group = go.AddComponent<CanvasGroup>(); m.Group.alpha = n.Opacity; }
            if (n.Text != null) m.Tmp = AddText(go, n, skin != null || go.GetComponent<Image>() != null, ctx);
            if (isButton)
            {
                Graphic target = (Graphic)m.Skin ?? m.Graphic;
                if (target == null)
                {
                    // a button that draws nothing itself (its children do): something invisible to take the tap
                    var hit = go.AddComponent<Image>();
                    hit.color = Color.clear;
                    hit.raycastTarget = true;
                    target = hit;
                }
                go.AddComponent<Button>().targetGraphic = target;
            }
            if (n.Scroll) go.AddComponent<RectMask2D>(); // (scrolling itself: a ScrollRect added by hand, see ui.json `scroll`)
            if (n.FillAxis != null)
            {
                var fill = go.AddComponent<UiFill>();
                fill.image = m.Skin ?? m.Graphic as Image;
                fill.reveal = n.FillMode != "resize";
                bool x = n.FillAxis != "y";
                fill.edge = x ? (n.FillFrom == "right" ? RectTransform.Edge.Right : RectTransform.Edge.Left) : (n.FillFrom == "bottom" ? RectTransform.Edge.Bottom : RectTransform.Edge.Top);
                fill.full = x ? n.W : n.H;
                fill.inset = fill.edge == RectTransform.Edge.Left ? n.X - parent.X : fill.edge == RectTransform.Edge.Right ? parent.X + parent.W - n.X - n.W
                    : fill.edge == RectTransform.Edge.Top ? n.Y - parent.Y : parent.Y + parent.H - n.Y - n.H;
                if (fill.reveal && fill.image != null)
                {
                    fill.image.type = Image.Type.Filled;
                    fill.image.fillMethod = x ? Image.FillMethod.Horizontal : Image.FillMethod.Vertical;
                    fill.image.fillOrigin = x ? (n.FillFrom == "right" ? (int)Image.OriginHorizontal.Right : (int)Image.OriginHorizontal.Left)
                        : (n.FillFrom == "bottom" ? (int)Image.OriginVertical.Bottom : (int)Image.OriginVertical.Top);
                    fill.image.fillAmount = 1;
                }
            }
        }

        static TMP_Text AddText(GameObject go, Node n, bool onChild, Ctx ctx)
        {
            var host = go;
            if (onChild || n.TBox != null)
            {
                // the text's own rectangle inside the element (tbox), or the whole element
                var tb = n.TBox ?? new[] { n.X, n.Y, n.W, n.H };
                host = Child(go, "Text", -(tb[0] - n.X), -(tb[1] - n.Y), -(n.X + n.W - tb[0] - tb[2]), -(n.Y + n.H - tb[1] - tb[3])).gameObject;
            }
            var tmp = host.AddComponent<TextMeshProUGUI>();
            var font = n.Family != null ? AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontBuilder.AssetPathFor(n.Family)) : null;
            if (font == null) font = ctx.Font;
            if (font != null) tmp.font = font;
            tmp.text = n.Text;
            tmp.fontSize = n.FontSize;
            tmp.color = n.FontColor;
            var style = n.Bold ? FontStyles.Bold : FontStyles.Normal;
            if (n.Upper) style |= FontStyles.UpperCase;
            tmp.fontStyle = style;
            int h = n.Align == "left" || n.Align == "start" ? 0 : n.Align == "right" || n.Align == "end" ? 2 : 1;
            int v = n.VAlign == "top" ? 0 : n.VAlign == "bottom" ? 2 : 1;
            tmp.alignment = new[,]
            {
                { TextAlignmentOptions.TopLeft, TextAlignmentOptions.Top, TextAlignmentOptions.TopRight },
                { TextAlignmentOptions.Left, TextAlignmentOptions.Center, TextAlignmentOptions.Right },
                { TextAlignmentOptions.BottomLeft, TextAlignmentOptions.Bottom, TextAlignmentOptions.BottomRight },
            }[v, h];
            // the browser kerns letter pairs (and places accents) by the font's own tables: so must TextMesh Pro, or every
            // line comes out a little wider than on the web
            tmp.fontFeatures = new List<UnityEngine.TextCore.OTL_FeatureTag> { UnityEngine.TextCore.OTL_FeatureTag.kern, UnityEngine.TextCore.OTL_FeatureTag.liga, UnityEngine.TextCore.OTL_FeatureTag.mark, UnityEngine.TextCore.OTL_FeatureTag.mkmk };
            tmp.textWrappingMode = n.Wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
            tmp.overflowMode = TextOverflowModes.Overflow;
            if (n.Spacing != 0 && n.FontSize > 0) tmp.characterSpacing = n.Spacing / n.FontSize * 100f; // TMP counts in 1/100 em
            if (n.LineHeight > 0 && font != null && font.faceInfo.pointSize > 0)
            {
                float natural = n.FontSize * font.faceInfo.lineHeight / font.faceInfo.pointSize;
                tmp.lineSpacing = (n.LineHeight - natural) / n.FontSize * 100f;
            }
            if (n.HasShadow && font != null) tmp.fontSharedMaterial = ShadowMaterial(font, n);
            tmp.raycastTarget = false;
            return tmp;
        }

        /// <summary>
        /// The web's text-shadow as a TextMesh Pro underlay: one material preset per (font, shadow relative to the text
        /// size, colour), saved next to the font so texts that share a shadow share a material.
        /// </summary>
        static Material ShadowMaterial(TMP_FontAsset font, Node n)
        {
            // an underlay offset of 1 moves the shadow by the atlas padding (in atlas pixels); the atlas is drawn at
            // fontSize / pointSize
            float pad = Mathf.Max(1, font.atlasPadding);
            float k = font.faceInfo.pointSize / (n.FontSize * pad);
            float ox = Mathf.Round(n.ShadowX * k * 100) / 100, oy = Mathf.Round(-n.ShadowY * k * 100) / 100;
            float soft = Mathf.Clamp01(Mathf.Round(n.ShadowBlur * k * 100) / 100);
            if (Mathf.Abs(ox) > 1 || Mathf.Abs(oy) > 1) Debug.LogWarning($"[T2U] {n.Name}: its text shadow is further away than the font atlas padding allows ({ox}, {oy}): it is drawn closer. Rebuild the font with more padding to fix.");
            var c = n.ShadowColor;
            string key = $"{font.name} Shadow {ox:0.00} {oy:0.00} {soft:0.00} {ColorUtility.ToHtmlStringRGBA(c)}".Replace('-', 'm');
            string dir = $"{FontBuilder.Dir}/Materials";
            string path = $"{dir}/{key}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;
            Directory.CreateDirectory(dir);
            mat = new Material(font.material) { name = key };
            mat.EnableKeyword("UNDERLAY_ON");
            mat.SetColor("_UnderlayColor", c);
            mat.SetFloat("_UnderlayOffsetX", Mathf.Clamp(ox, -1, 1));
            mat.SetFloat("_UnderlayOffsetY", Mathf.Clamp(oy, -1, 1));
            mat.SetFloat("_UnderlayDilate", 0);
            mat.SetFloat("_UnderlaySoftness", soft);
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        // ------------------------------------------------------------------ states (ui.json `states`, `initial`)
        static void BuildStates(GameObject root, string screen, Dictionary<string, Made> made, Node rootNode, Ctx ctx, JObject json)
        {
            if (!(json["states"] is JArray all)) return;
            var hosts = new Dictionary<string, UiStates.Host>();
            var order = new List<UiStates.Host>();
            var touched = new Dictionary<string, Dictionary<string, HashSet<string>>>(); // host -> node -> fields
            foreach (JObject st in all)
            {
                var hostName = (string)st["host"];
                if (!made.ContainsKey(hostName)) continue; // another screen's
                var on = st["on"].Select(s => (string)s).ToArray();
                if (!hosts.TryGetValue(hostName, out var host))
                {
                    host = new UiStates.Host { name = hostName, states = new string[0] };
                    hosts[hostName] = host; order.Add(host); touched[hostName] = new Dictionary<string, HashSet<string>>();
                }
                foreach (var s in on) if (System.Array.IndexOf(host.states, s) < 0) host.states = host.states.Append(s).ToArray();
                var set = (JObject)st["set"];
                // where everything stands in this state: a listed box is absolute; the rest keeps its place in its parent
                var eff = new Dictionary<string, Rect>();
                Rect Eff(string name)
                {
                    if (name == null || !made.ContainsKey(name)) return rootNode.Box;
                    if (eff.TryGetValue(name, out var r)) return r;
                    var m = made[name];
                    if (set[name] is JObject d && Has(d["x"])) r = new Rect((float)d["x"], (float)d["y"], (float)d["w"], (float)d["h"]);
                    else
                    {
                        var bp = m.N.Parent != null && made.ContainsKey(m.N.Parent) ? made[m.N.Parent].N.Box : rootNode.Box;
                        var np = Eff(m.N.Parent);
                        r = new Rect(m.N.X + np.x - bp.x, m.N.Y + np.y - bp.y, m.N.W, m.N.H);
                    }
                    eff[name] = r;
                    return r;
                }
                // the box a child is placed in: its parent's, or the content of a scrolling parent (moved with it)
                Rect ParentNow(Made m)
                {
                    if (m.N.Parent == null || !made.TryGetValue(m.N.Parent, out var pm)) return rootNode.Box;
                    var e = Eff(m.N.Parent);
                    if (pm.ContentBox is Rect cb) return new Rect(cb.x + e.x - pm.N.X, cb.y + e.y - pm.N.Y, cb.width + e.width - pm.N.W, cb.height);
                    return e;
                }
                var variant = new UiStates.Variant { on = string.Join(" ", on) };
                foreach (var p in set.Properties())
                {
                    if (!made.TryGetValue(p.Name, out var m)) continue;
                    var d = (JObject)p.Value;
                    var ch = Change(m, d, Eff(p.Name), ParentNow(m), ctx, out var fields);
                    variant.changes.Add(ch);
                    if (!touched[hostName].TryGetValue(p.Name, out var f)) touched[hostName][p.Name] = f = new HashSet<string>();
                    f.UnionWith(fields);
                }
                host.variants.Add(variant);
            }
            if (order.Count == 0) return;
            // how each touched element rests: what switching every state off puts back
            foreach (var host in order)
                foreach (var kv in touched[host.name])
                    host.rest.Add(Rest(made[kv.Key], kv.Value, made, rootNode));
            var states = root.AddComponent<UiStates>();
            states.hosts = order;
            // states that are on in the web page as exported (a class used as a permanent look)
            if (json["initial"] is JArray initial)
                foreach (JObject i in initial)
                    foreach (var s in i["on"]) states.Set((string)i["host"], (string)s, true);
        }

        static UiStates.Change Change(Made m, JObject d, Rect now, Rect parentNow, Ctx ctx, out HashSet<string> fields)
        {
            fields = new HashSet<string>();
            var ch = new UiStates.Change { target = m.Rt };
            if (Has(d["x"]))
            {
                ch.setRect = true;
                // the state may pin it by another edge (web: top → bottom): another pivot
                Pivot(d.Property("pin") != null ? d["pin"] : m.N.Pin, m.Ax, m.Ay, out float gx, out float gy);
                Calc(now, parentNow, m.Ax, m.Ay, out ch.anchoredPosition, out ch.sizeDelta, gx, gy);
                if (gx != m.Gx || gy != m.Gy)
                {
                    ch.setPivot = true;
                    ch.pivot = new Vector2(m.Ax < 0 ? 0.5f : gx, m.Ay < 0 ? 0.5f : 1 - gy);
                    fields.Add("pivot");
                }
                fields.Add("rect");
            }
            if (d.Property("skin") != null && m.Skin != null)
            {
                ch.setSkin = true; ch.skin = m.Skin;
                ch.sprite = SkinSprite((string)d["skin"]);
                SkinOffsets(Arr(d["skinBox"]), out ch.skinMin, out ch.skinMax);
                fields.Add("skin");
            }
            if (d.Property("bg") != null && m.Graphic != null && Col(d["bg"]) is Color bg)
            {
                ch.setColor = true; ch.graphic = m.Graphic; ch.color = bg;
                fields.Add("color");
            }
            if (d["font"] is JObject f && m.Tmp != null)
            {
                ch.setFont = true; ch.text = m.Tmp;
                ch.fontColor = Col(f["color"]) ?? m.N.FontColor; ch.fontSize = (float?)f["size"] ?? m.N.FontSize;
                fields.Add("font");
            }
            if (Has(d["opacity"]))
            {
                if (m.Group == null) { m.Group = m.Rt.gameObject.AddComponent<CanvasGroup>(); m.Group.alpha = m.N.Opacity; }
                ch.setAlpha = true; ch.group = m.Group; ch.alpha = (float)d["opacity"];
                fields.Add("alpha");
            }
            if ((bool?)d["gone"] == true) { ch.setActive = true; ch.active = false; fields.Add("active"); }
            if (d.Property("anim") != null) { ch.setAnim = true; ch.anim = AnimText(d["anim"]); fields.Add("anim"); }
            return ch;
        }

        static UiStates.Change Rest(Made m, HashSet<string> fields, Dictionary<string, Made> made, Node rootNode)
        {
            var ch = new UiStates.Change { target = m.Rt };
            if (fields.Contains("rect")) { ch.setRect = true; ch.anchoredPosition = m.Rt.anchoredPosition; ch.sizeDelta = m.Rt.sizeDelta; }
            if (fields.Contains("pivot")) { ch.setPivot = true; ch.pivot = m.Rt.pivot; }
            if (fields.Contains("skin") && m.Skin != null) { ch.setSkin = true; ch.skin = m.Skin; ch.sprite = m.Skin.sprite; ch.skinMin = m.Skin.rectTransform.offsetMin; ch.skinMax = m.Skin.rectTransform.offsetMax; }
            if (fields.Contains("color") && m.Graphic != null) { ch.setColor = true; ch.graphic = m.Graphic; ch.color = m.Graphic.color; }
            if (fields.Contains("font") && m.Tmp != null) { ch.setFont = true; ch.text = m.Tmp; ch.fontColor = m.Tmp.color; ch.fontSize = m.Tmp.fontSize; }
            if (fields.Contains("alpha") && m.Group != null) { ch.setAlpha = true; ch.group = m.Group; ch.alpha = m.N.Opacity; }
            if (fields.Contains("active")) { ch.setActive = true; ch.active = true; }
            if (fields.Contains("anim")) { ch.setAnim = true; ch.anim = m.N.Anim; }
            return ch;
        }

        // ------------------------------------------------------------------ fonts, the version-1 rounded sprite
        static TMP_FontAsset Font()
        {
            var path = KitSettings.Load().uiFont;
            var f = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (f == null) f = TMP_Settings.defaultFontAsset;
            if (f == null) Debug.LogWarning("[T2U] No TMP font: import TMP Essentials (Window > TextMeshPro) or set uiFont in T2UKit.json.");
            return f;
        }

        /// <summary>White rounded square, 9-sliced with 64 px corners (generated once).</summary>
        static Sprite RoundSprite()
        {
            var s = AssetDatabase.LoadAssetAtPath<Sprite>(RoundPath);
            if (s != null) return s;
            const int size = RoundBorder * 2 + 2;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float rad = RoundBorder;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float px = x + 0.5f, py = y + 0.5f;
                    float dx = Mathf.Max(0, Mathf.Max(rad - px, px - (size - rad)));
                    float dy = Mathf.Max(0, Mathf.Max(rad - py, py - (size - rad)));
                    float a = Mathf.Clamp01(rad - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f);
                    tex.SetPixel(x, y, new Color(1, 1, 1, a));
                }
            Directory.CreateDirectory(Path.GetDirectoryName(RoundPath));
            File.WriteAllBytes(RoundPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(RoundPath);
            var ti = (TextureImporter)AssetImporter.GetAtPath(RoundPath);
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.mipmapEnabled = false;
            ti.spritePixelsPerUnit = 100;
            ti.spriteBorder = new Vector4(RoundBorder, RoundBorder, RoundBorder, RoundBorder);
            ti.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(RoundPath);
        }
    }
}

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
    /// Renders every imported UI screen prefab, alone on black, to a PNG — the Unity side of the UI comparison
    /// (the web side: `node tools/t2u/bot.mjs uicheck` writes unity-convert/logs/uicheck/web/&lt;screen&gt;.png the same way).
    /// The pictures are `scale` pixels per design unit (ui.json), the size of the web's.
    /// Batch: -executeMethod T2U.Kit.Editor.UiPreviewCapture.RunBatch [-t2uOutDir Captures/ui] [-t2uUiJson path]
    ///        -executeMethod T2U.Kit.Editor.UiPreviewCapture.SyncImportCaptureBatch   (sync from web + import + check + capture)
    /// Check(): the prefabs against ui.json by numbers (every box, every state on and off).
    /// Do not pass -nographics: the pictures are drawn by the graphics card.
    /// </summary>
    public static class UiPreviewCapture
    {
        const string DefaultJson = "Assets/_Game/Data/UI/ui.json";

        [MenuItem("Tools/T2U Kit/Capture UI Screens (Captures/ui)", priority = 32)]
        public static void RunMenu() => Run("Captures/ui", DefaultJson);

        public static void RunBatch()
        {
            Run(KitSettings.Arg("-t2uOutDir", "Captures/ui"), KitSettings.Arg("-t2uUiJson", DefaultJson));
            KitSettings.ExitIfBatch(true);
        }

        /// <summary>The whole UI path in one go: copy the web export, rebuild every screen prefab, capture.</summary>
        public static void SyncImportCaptureBatch()
        {
            WebSync.Run();
            AssetDatabase.Refresh();
            UiLayoutImporter.Import(KitSettings.Arg("-t2uUiJson", DefaultJson), overwrite: true);
            int bad = Check(KitSettings.Arg("-t2uUiJson", DefaultJson));
            Run(KitSettings.Arg("-t2uOutDir", "Captures/ui"), KitSettings.Arg("-t2uUiJson", DefaultJson));
            KitSettings.ExitIfBatch(bad == 0);
        }

        /// <summary>
        /// Checks the imported prefabs against ui.json by numbers: every element stands where the web measured it, and
        /// every state entry, switched on through UiStates, gives the skin, the text colour and the place the web has
        /// in that state — and switching it off puts everything back. Returns the number of mismatches (logged).
        /// </summary>
        [MenuItem("Tools/T2U Kit/Check UI Prefabs Against ui.json", priority = 33)]
        public static int Check() => Check(DefaultJson);

        public static int Check(string jsonPath)
        {
            var json = JObject.Parse(File.ReadAllText(jsonPath));
            float refW = (float?)json["refWidth"] ?? 1080, refH = (float?)json["refHeight"] ?? 1920;
            var rootGo = new GameObject("T2U Check Root", typeof(RectTransform));
            var root = (RectTransform)rootGo.transform;
            root.sizeDelta = new Vector2(refW, refH);
            int bad = 0, boxes = 0, states = 0, flex = 0;
            void Fail(string msg) { if (bad++ < 25) Debug.LogWarning("[T2U] UI check: " + msg); }
            Rect Of(RectTransform r)
            {
                var c = new Vector3[4];
                r.GetWorldCorners(c);
                Vector3 a = root.InverseTransformPoint(c[0]), b = root.InverseTransformPoint(c[2]);
                return new Rect(a.x + refW / 2, refH / 2 - b.y, b.x - a.x, b.y - a.y); // web coordinates: x right, y down from the top
            }
            bool Near(Rect r, float x, float y, float w, float h) => Mathf.Abs(r.x - x) < 0.6f && Mathf.Abs(r.y - y) < 0.6f && Mathf.Abs(r.width - w) < 0.6f && Mathf.Abs(r.height - h) < 0.6f;
            bool On(JToken t) => t != null && t.Type != JTokenType.Null;
            foreach (JObject scr in json["screens"])
            {
                var name = (string)scr["screen"];
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/_Game/Prefabs/UI/{name}.prefab");
                if (prefab == null) { Fail($"no prefab for screen {name}"); continue; }
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root);
                var refs = inst.GetComponent<UiRefs>();
                var baseNodes = ((JArray)scr["nodes"]).Cast<JObject>().ToDictionary(n => (string)n["name"]);
                Image SkinOf(string node) => refs.Get(node).Find("Skin")?.GetComponent<Image>();
                TMP_Text TextOf(string node) => refs.Get(node).GetComponent<TMP_Text>() ?? refs.Get(node).Find("Text")?.GetComponent<TMP_Text>();
                // 1) every element where the web measured it — after the flexbox containers have placed their children
                //    (UiFlex must reproduce the resting page exactly)
                flex += inst.GetComponentsInChildren<UiFlex>(true).Length;
                UiFlex.LayoutAll(inst.transform);
                foreach (var n in baseNodes.Values)
                {
                    if ((bool?)n["gone"] == true) continue;
                    boxes++;
                    var r = Of(refs.Get((string)n["name"]));
                    if (!Near(r, (float)n["x"], (float)n["y"], (float)n["w"], (float)n["h"])) Fail($"{n["name"]} stands at {r.x:0.#},{r.y:0.#} {r.width:0.#}x{r.height:0.#}, the web has {n["x"]},{n["y"]} {n["w"]}x{n["h"]}");
                }
                // 2) every state entry on, then off
                var ui = inst.GetComponent<UiStates>();
                if (json["states"] is JArray all && ui != null)
                    foreach (JObject st in all)
                    {
                        var host = (string)st["host"];
                        if (!baseNodes.ContainsKey(host)) continue;
                        states++;
                        var on = st["on"].Select(x => (string)x).ToArray();
                        var was = ui.hosts.Find(h => h.name == host)?.states.Where(s => ui.IsOn(host, s)).ToArray() ?? new string[0];
                        foreach (var s in was) ui.Set(host, s, false);
                        foreach (var s in on) refs.State(host, s, true);
                        UiFlex.LayoutAll(inst.transform); // what the containers do once a state hid, showed or resized something
                        foreach (var p in ((JObject)st["set"]).Properties())
                        {
                            var d = (JObject)p.Value;
                            if (d.Property("skin") != null)
                            {
                                var img = SkinOf(p.Name);
                                var want = (string)d["skin"];
                                var got = img != null && img.enabled && img.sprite != null ? img.sprite.name : null;
                                if (img != null && got != want) Fail($"{host} [{string.Join(" ", on)}]: {p.Name} shows the skin {got ?? "(none)"}, the web has {want ?? "(none)"}");
                            }
                            if (d["font"] is JObject f && On(f["color"]) && TextOf(p.Name) is TMP_Text t)
                            {
                                var c = new Color((float)f["color"]["r"], (float)f["color"]["g"], (float)f["color"]["b"], (float)f["color"]["a"]);
                                if (Mathf.Abs(t.color.r - c.r) + Mathf.Abs(t.color.g - c.g) + Mathf.Abs(t.color.b - c.b) > 0.02f) Fail($"{host} [{string.Join(" ", on)}]: the text of {p.Name} has the wrong colour");
                            }
                            if (On(d["x"]) && !Near(Of(refs.Get(p.Name)), (float)d["x"], (float)d["y"], (float)d["w"], (float)d["h"])) Fail($"{host} [{string.Join(" ", on)}]: {p.Name} stands at the wrong place");
                        }
                        foreach (var s in on) refs.State(host, s, false);
                        UiFlex.LayoutAll(inst.transform);
                        foreach (var p in ((JObject)st["set"]).Properties())
                        {
                            var n = baseNodes[p.Name];
                            var img = SkinOf(p.Name);
                            var want = (string)n["skin"];
                            var got = img != null && img.enabled && img.sprite != null ? img.sprite.name : null;
                            if (img != null && got != want) Fail($"{host}: after [{string.Join(" ", on)}] is switched off, {p.Name} keeps the skin {got ?? "(none)"} (rest: {want ?? "(none)"})");
                            if ((bool?)n["gone"] != true && !Near(Of(refs.Get(p.Name)), (float)n["x"], (float)n["y"], (float)n["w"], (float)n["h"])) Fail($"{host}: after [{string.Join(" ", on)}] is switched off, {p.Name} does not go back to its place");
                        }
                        foreach (var s in was) ui.Set(host, s, true);
                    }
                Object.DestroyImmediate(inst);
            }
            Object.DestroyImmediate(rootGo);
            Debug.Log($"[T2U] UI check: {boxes} elements placed, {states} state entries switched on and off, {flex} flex containers laid out, {bad} mismatch(es)");
            return bad;
        }

        public static void Run(string outDir, string jsonPath)
        {
            var json = JObject.Parse(File.ReadAllText(jsonPath));
            int refW = (int?)json["refWidth"] ?? 1080, refH = (int?)json["refHeight"] ?? 1920;
            int scale = Mathf.Max(1, Mathf.RoundToInt((float?)json["scale"] ?? 1));
            Directory.CreateDirectory(outDir);

            var camGo = new GameObject("T2U Capture Camera", typeof(Camera));
            var cam = camGo.GetComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.orthographic = true;
            var canvasGo = new GameObject("T2U Capture Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = 5;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(refW, refH);
            scaler.matchWidthOrHeight = 0;
            var rt = new RenderTexture(refW * scale, refH * scale, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            cam.targetTexture = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            int n = 0;
            foreach (var name in json["screens"].Select(s => (string)s["screen"]))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/_Game/Prefabs/UI/{name}.prefab");
                if (prefab == null) { Debug.LogWarning($"[T2U] no prefab for screen {name}"); continue; }
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, canvasGo.transform);
                inst.SetActive(true);
                Canvas.ForceUpdateCanvases();
                foreach (var t in inst.GetComponentsInChildren<TMP_Text>(false)) t.ForceMeshUpdate();
                Canvas.ForceUpdateCanvases();
                cam.Render();
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                RenderTexture.active = null;
                File.WriteAllBytes(Path.Combine(outDir, name + ".png"), tex.EncodeToPNG());
                Object.DestroyImmediate(inst);
                n++;
            }
            cam.targetTexture = null;
            Object.DestroyImmediate(canvasGo);
            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(tex);
            Debug.Log($"[T2U] UI screens captured: {n} -> {Path.GetFullPath(outDir)}");
        }
    }
}

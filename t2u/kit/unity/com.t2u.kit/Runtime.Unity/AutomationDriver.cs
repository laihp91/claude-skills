using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using T2U.Kit.Replay;
using UnityEngine;
using UnityEngine.UI;

namespace T2U.Kit.Automation
{
    /// <summary>
    /// Dev-only automation for a converted game (handover-standard H9), working through the game's
    /// <see cref="IAutomationHost"/>. Put it on the bootstrap's GameObject. Does nothing in release builds.
    /// <list type="bullet">
    /// <item>Reference shots: replays each web shot (unity-convert/export/shots.json → reference/&lt;id&gt;.input.json) through
    /// the real scene and saves &lt;out&gt;/&lt;id&gt;.png at the web's viewport size, for make_visual.py.
    /// <c>-t2uShots &lt;shots.json&gt; [-t2uShotsOut &lt;dir&gt;] [-t2uOnly id,id]</c>, or Tools ▸ T2U Kit ▸ Capture Reference Shots.</item>
    /// <item>Watch a recording: <c>-t2uReplay &lt;name.input.json&gt;</c> plays it at normal speed.</item>
    /// <item>Record a session: <c>-t2uRecord &lt;name&gt;</c>; the bootstrap calls <see cref="Recording"/>?.Input(ev) before
    /// giving an event to the core and ?.Tick() after each step; saved on quit to TestData/Golden (editor) or
    /// persistentDataPath (player), then "Accept Current Behaviour As Golden" creates its expected logs.</item>
    /// </list>
    /// </summary>
    public sealed class AutomationDriver : MonoBehaviour
    {
        /// <summary>The session being recorded (null when not recording).</summary>
        public static RecordingWriter Recording { get; private set; }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>Written by the editor menu, read once at start (path relative to the project).</summary>
        public const string RequestFile = "Temp/t2u_automation.json";

        IAutomationHost _host;
        string _recordName;

        static string Arg(string name)
        {
            var a = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
            return null;
        }

        void Start()
        {
            _host = GetComponents<MonoBehaviour>().OfType<IAutomationHost>().FirstOrDefault()
                    ?? FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).OfType<IAutomationHost>().FirstOrDefault();
            JObject req = null;
            if (File.Exists(RequestFile)) { req = JObject.Parse(File.ReadAllText(RequestFile)); File.Delete(RequestFile); }
            string shots = (string)req?["shots"] ?? Arg("-t2uShots");
            string replay = (string)req?["replay"] ?? Arg("-t2uReplay");
            _recordName = (string)req?["record"] ?? Arg("-t2uRecord");
            if ((shots != null || replay != null) && _host == null) { Debug.LogError("[T2U] AutomationDriver: no IAutomationHost in the scene (implement it on the bootstrap)"); Finish(false); return; }
            if (shots != null)
            {
                var only = ((string)req?["only"] ?? Arg("-t2uOnly"))?.Split(',').Select(s => s.Trim()).ToList();
                var outDir = (string)req?["out"] ?? Arg("-t2uShotsOut") ?? Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(shots))), "compare", "unity");
                StartCoroutine(Shots(shots, outDir, only));
            }
            else if (replay != null) StartCoroutine(Watch(replay));
            else if (_recordName != null) { Recording = new RecordingWriter(1); Debug.Log($"[T2U] recording session '{_recordName}'"); }
        }

        void OnApplicationQuit()
        {
            if (Recording == null || _recordName == null) return;
            var dir = Application.isEditor ? "TestData/Golden" : Application.persistentDataPath;
            Debug.Log("[T2U] recording saved: " + Recording.Save(dir, _recordName));
            Recording = null;
        }

        /// <summary>Plays a recording through the host: begin, inputs on their frames, one fixed step per frame.</summary>
        sealed class Player
        {
            readonly IAutomationHost _h; readonly Queue<JObject> _q; public readonly int Total; public int Frame;
            public Player(IAutomationHost h, JObject rec)
            {
                _h = h;
                _q = new Queue<JObject>(((JArray)rec["events"]).Cast<JObject>());
                Total = (int)rec["totalFrames"];
                h.Begin((uint)(long)rec["seed"]);
            }
            public void Due()
            {
                while (_q.Count > 0 && (int)_q.Peek()["f"] <= Frame)
                {
                    var ev = (JObject)_q.Dequeue().DeepClone();
                    ev.Remove("f");
                    _h.Input(ev);
                }
            }
            public void Tick() { Due(); _h.Step(); Frame++; }
        }

        IEnumerator Shots(string shotsPath, string outDir, List<string> only)
        {
            _host.AutomationActive = true;
            // let every Start() of the scene run first: a bootstrap that loads the player's save in its Start would
            // otherwise replace the first shot's game (the first shot showed the player's own level and popups)
            yield return null;
            var uc = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(shotsPath))); // …/unity-convert
            Directory.CreateDirectory(outDir);
            int n = 0;
            foreach (JObject s in JObject.Parse(File.ReadAllText(shotsPath))["shots"])
            {
                var id = (string)s["id"];
                if (only != null && !only.Contains(id)) continue;
                int w = (int?)s["viewport"]?["width"] ?? 540, h = (int?)s["viewport"]?["height"] ?? 960;
                var rec = JObject.Parse(File.ReadAllText(Path.Combine(uc, (string)s["replay"])));
                // the same as the web: replay the prefix without drawing, then draw once
                _host.Viewport(w, h);
                var p = new Player(_host, rec);
                while (p.Frame < p.Total) p.Tick();
                p.Due();
                _host.Render();
                yield return Capture(w, h, Path.Combine(outDir, id + ".png"), Time.realtimeSinceStartup);
                n++;
            }
            Debug.Log($"[T2U] reference shots captured: {n} -> {Path.GetFullPath(outDir)}");
            _host.AutomationActive = false;
            Finish(true);
        }

        IEnumerator Watch(string path)
        {
            _host.AutomationActive = true;
            yield return null; // every Start() first (see Shots)
            var p = new Player(_host, JObject.Parse(File.ReadAllText(path)));
            float acc = 0;
            while (p.Frame < p.Total)
            {
                acc += Time.unscaledDeltaTime;
                while (acc >= (float)ReplayDriver.Dt && p.Frame < p.Total) { p.Tick(); acc -= (float)ReplayDriver.Dt; }
                _host.Render();
                yield return null;
            }
            p.Due();
            Debug.Log($"[T2U] replay finished: {Path.GetFileName(path)} ({p.Total} frames)");
            _host.AutomationActive = false;
        }

        /// <summary>
        /// Draws world + UI into a w×h picture: overlay canvases are drawn by the camera for the moment (Screen Space -
        /// Camera, in front of everything), given a frame to lay out for that size, then put back.
        /// </summary>
        /// <summary>
        /// The web's bot waits this long after drawing before its screenshot (bot.mjs shots: waitForTimeout(100)): UI
        /// animations that start when a popup opens are that far along in the web's picture, so here too.
        /// </summary>
        public const float WebShotDelay = 0.1f;

        IEnumerator Capture(int w, int h, string path, float drawnAt = -1)
        {
            var cam = _host.CaptureCamera;
            if (cam == null) { Debug.LogError("[T2U] no camera to capture with"); yield break; }
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var prevTarget = cam.targetTexture;
            cam.targetTexture = rt;
            // frames that pillarbox a wide screen: laid out for the picture's size, not the window's
            var frames = FindObjectsByType<AspectFrame>(FindObjectsSortMode.None);
            foreach (var f in frames) f.Apply(w, h);
            var moved = new List<(Canvas c, RenderMode mode, Camera cam, float dist, int order)>();
            foreach (var c in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            {
                if (!c.isRootCanvas || c.renderMode != RenderMode.ScreenSpaceOverlay) continue;
                moved.Add((c, c.renderMode, c.worldCamera, c.planeDistance, c.sortingOrder));
                c.renderMode = RenderMode.ScreenSpaceCamera;
                c.worldCamera = cam;
                c.planeDistance = cam.nearClipPlane + 0.01f;
                c.sortingOrder = 32000 + c.sortingOrder / 8;
            }
            // canvas scalers, flex layouts and text meshes follow the new size on the next frame
            yield return null;
            yield return null;
            // as long after the drawing as the web's screenshot (the game does not step meanwhile)
            while (drawnAt >= 0 && Time.realtimeSinceStartup - drawnAt < WebShotDelay) yield return null;
            Canvas.ForceUpdateCanvases();
            cam.Render();
            var prevActive = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = prevActive;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Destroy(tex);
            foreach (var m in moved) { m.c.renderMode = m.mode; m.c.worldCamera = m.cam; m.c.planeDistance = m.dist; m.c.sortingOrder = m.order; }
            cam.targetTexture = prevTarget;
            foreach (var f in frames) f.Apply();
            rt.Release(); Destroy(rt);
            Debug.Log("[T2U] shot saved: " + path);
        }

        static void Finish(bool ok)
        {
#if UNITY_EDITOR
            if (Application.isBatchMode) UnityEditor.EditorApplication.Exit(ok ? 0 : 1);
            else UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit(ok ? 0 : 1);
#endif
        }
#else
        void Awake() => enabled = false;
#endif
    }
}

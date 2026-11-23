using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace T2U.Kit.Editor
{
    /// <summary>
    /// Batch helper: open a scene, enter Play mode, wait N frames, render the main camera at a given size to a PNG, quit.
    /// Edit-mode camera.Render() does not bind per-sprite textures correctly, Play mode does (what players see).
    /// Usage (no -quit):  -executeMethod T2U.Kit.Editor.PlayModeCapture.Run -t2uScene Assets/_Game/Scenes/Main.unity -t2uOut /path.png [-t2uW 540 -t2uH 960 -t2uFrames 10]
    /// </summary>
    [InitializeOnLoad]
    public static class PlayModeCapture
    {
        const string Key = "t2u.capture";
        static int _frames;

        static PlayModeCapture()
        {
            if (SessionState.GetBool(Key, false)) EditorApplication.playModeStateChanged += OnState;
            if (SessionState.GetBool("t2u.timed", false))
                EditorApplication.update += () =>
                {
                    if (EditorApplication.timeSinceStartup > SessionState.GetFloat("t2u.until", 0) && EditorApplication.isPlaying)
                    {
                        SessionState.SetBool("t2u.timed", false);
                        Debug.Log("[T2U] timed play finished");
                        EditorApplication.Exit(0);
                    }
                };
        }

        static string Arg(string name, string def)
        {
            var a = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
            return def;
        }

        public static void Run()
        {
            SessionState.SetString("t2u.out", Arg("-t2uOut", "capture.png"));
            SessionState.SetInt("t2u.w", int.Parse(Arg("-t2uW", "540")));
            SessionState.SetInt("t2u.h", int.Parse(Arg("-t2uH", "960")));
            SessionState.SetInt("t2u.frames", int.Parse(Arg("-t2uFrames", "10")));
            // when the game itself captures (its bootstrap reads -t2uCaptureFrame / -t2uReplay), only enter Play mode
            SessionState.SetBool(Key, Arg("-t2uCaptureFrame", null) == null && Arg("-t2uReplay", null) == null);
            EditorSceneManager.OpenScene(Arg("-t2uScene", "Assets/_Game/Scenes/Main.unity"));
            EditorApplication.EnterPlaymode();
        }

        /// <summary>Batch: play the scene normally (real time, loading screen, no automation flags) for N seconds, then quit. Console output goes to the log.</summary>
        public static void RunTimed()
        {
            SessionState.SetFloat("t2u.until", (float)EditorApplication.timeSinceStartup + float.Parse(Arg("-t2uSeconds", "12"), System.Globalization.CultureInfo.InvariantCulture));
            SessionState.SetBool("t2u.timed", true);
            EditorSceneManager.OpenScene(Arg("-t2uScene", "Assets/_Game/Scenes/Main.unity"));
            EditorApplication.EnterPlaymode();
        }

        static void OnState(PlayModeStateChange s)
        {
            if (s == PlayModeStateChange.EnteredPlayMode) { _frames = 0; EditorApplication.update += Tick; }
        }

        static void Tick()
        {
            if (++_frames < SessionState.GetInt("t2u.frames", 10)) return;
            EditorApplication.update -= Tick;
            int w = SessionState.GetInt("t2u.w", 540), h = SessionState.GetInt("t2u.h", 960);
            var cam = Camera.main;
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            var path = SessionState.GetString("t2u.out", "capture.png");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            File.WriteAllBytes(path, tex.EncodeToPNG());
            cam.targetTexture = null; RenderTexture.active = null;
            Debug.Log("[T2U] Play-mode capture saved: " + path);
            SessionState.SetBool(Key, false);
            EditorApplication.Exit(0);
        }
    }
}

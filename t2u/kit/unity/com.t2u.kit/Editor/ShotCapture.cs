using System.IO;
using Newtonsoft.Json.Linq;
using T2U.Kit.Automation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace T2U.Kit.Editor
{
    /// <summary>
    /// The Unity side of the visual comparison: plays every web reference shot (unity-convert/export/shots.json) through
    /// the game's main scene and saves unity-convert/compare/unity/&lt;id&gt;.png; then, in the web project,
    /// `python3 tools/t2u/make_visual.py` builds compare/visual.html (web | Unity | difference).
    /// Needs the kit's <see cref="AutomationDriver"/> on the bootstrap and the bootstrap implementing
    /// <see cref="IAutomationHost"/>. Play mode draws what players see (edit-mode rendering binds sprites wrongly).
    /// Batch (no -quit, no -nographics): -executeMethod T2U.Kit.Editor.ShotCapture.RunBatch [-t2uOnly id,id] [-t2uScene path]
    /// </summary>
    public static class ShotCapture
    {
        [MenuItem("Tools/T2U Kit/Capture Reference Shots (Play mode)", priority = 34)]
        public static void RunMenu() => Start(null);

        public static void RunBatch() => Start(KitSettings.Arg("-t2uOnly"));

        static void Start(string only)
        {
            var kit = KitSettings.Load();
            var shots = kit.WebPath("unity-convert", "export", "shots.json");
            if (!File.Exists(shots)) { Debug.LogError($"[T2U] {shots} not found: run `npm run t2u:shots` in the web project"); KitSettings.ExitIfBatch(false); return; }
            var req = new JObject { ["shots"] = shots, ["out"] = kit.WebPath("unity-convert", "compare", "unity") };
            if (!string.IsNullOrEmpty(only)) req["only"] = only;
            Directory.CreateDirectory(Path.GetDirectoryName(AutomationDriver.RequestFile));
            File.WriteAllText(AutomationDriver.RequestFile, req.ToString());
            var scene = KitSettings.Arg("-t2uScene", kit.scenes != null && kit.scenes.Length > 0 ? kit.scenes[0] : "Assets/_Game/Scenes/Main.unity");
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(scene);
            Debug.Log($"[T2U] capturing reference shots in {scene} (Play mode) -> {req["out"]}");
            EditorApplication.EnterPlaymode();
        }
    }
}

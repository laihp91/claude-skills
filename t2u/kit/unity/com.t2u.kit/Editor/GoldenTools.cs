using System.IO;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace T2U.Kit.Editor
{
    /// <summary>
    /// Maintenance tools for the golden replay tests (folder: T2UKit.json goldenDir, inside the Unity repo).
    ///  - "Accept Current Behaviour As Golden": after an INTENDED gameplay change, rewrites every expected state/event log
    ///    from the current C# game (runs the EditMode tests once in accept mode). Review the diff in git, then commit.
    ///  - "Open Golden Folder".
    /// Batch: -executeMethod T2U.Kit.Editor.GoldenTools.AcceptBatch (no -quit; exits when the run is finished).
    /// </summary>
    public static class GoldenTools
    {
        static string Marker => Path.Combine(KitSettings.Load().goldenDir, ".rebaseline");

        [MenuItem("Tools/T2U Kit/Golden Tests/Accept Current Behaviour As Golden", priority = 60)]
        static void AcceptMenu()
        {
            if (!EditorUtility.DisplayDialog("Accept current behaviour?",
                    "Every golden recording will be replayed and its expected logs REWRITTEN from the current game.\n\nOnly do this after an intended gameplay change. Check the diff in git before committing.", "Accept", "Cancel")) return;
            Accept(false);
        }

        public static void AcceptBatch() => Accept(true);

        static void Accept(bool exit)
        {
            var dir = KitSettings.Load().goldenDir;
            if (!Directory.Exists(dir)) { Debug.LogError($"[T2U] Golden folder {dir} not found (goldenDir in T2UKit.json)."); if (exit) KitSettings.ExitIfBatch(false); return; }
            File.WriteAllText(Marker, "accept current behaviour");
            var api = ScriptableObject.CreateInstance<TestRunnerApi>();
            api.RegisterCallbacks(new Done(exit));
            api.Execute(new ExecutionSettings(new Filter { testMode = TestMode.EditMode }));
        }

        sealed class Done : ICallbacks
        {
            readonly bool _exit;
            public Done(bool exit) { _exit = exit; }
            public void RunStarted(ITestAdaptor t) { }
            public void TestStarted(ITestAdaptor t) { }
            public void TestFinished(ITestResultAdaptor r) { }
            public void RunFinished(ITestResultAdaptor r)
            {
                if (File.Exists(Marker)) File.Delete(Marker);
                Debug.Log($"[T2U] Golden logs re-recorded from the current game. Tests: {r.PassCount} passed, {r.FailCount} failed. Review `git diff {KitSettings.Load().goldenDir}` and commit.");
                if (_exit) KitSettings.ExitIfBatch(r.FailCount == 0);
            }
        }

        [MenuItem("Tools/T2U Kit/Golden Tests/Open Golden Folder", priority = 61)]
        static void Open() => EditorUtility.RevealInFinder(KitSettings.Load().goldenDir);
    }
}

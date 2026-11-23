using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace T2U.Kit.Editor
{
    /// <summary>
    /// Android builds. Development = debug keystore + fast IL2CPP (Debug C++ config, size codegen): a release IL2CPP build
    /// can take hours on a laptop the first time; later builds are incremental.
    /// Batch: -buildTarget Android -executeMethod T2U.Kit.Editor.BuildTools.BuildAndroidDev  (or BuildAndroidRelease, add -t2uAab for an AAB)
    /// Output: Builds/Android/&lt;Product&gt;-dev.apk ; log line "[T2U] Android build Succeeded: ..." .
    /// Release signing: set PlayerSettings.Android keystore fields (keystore path/passwords stay OUT of git) before building.
    /// </summary>
    public static class BuildTools
    {
        static void Configure(bool dev, bool aab)
        {
            ProjectSetup.Run();
            EditorUserBuildSettings.androidBuildSystem = AndroidBuildSystem.Gradle;
            EditorUserBuildSettings.buildAppBundle = aab;
            EditorUserBuildSettings.androidBuildSubtarget = MobileTextureSubtarget.ASTC;
            EditorUserBuildSettings.development = dev;
            if (dev) PlayerSettings.Android.useCustomKeystore = false;
            var a = UnityEditor.Build.NamedBuildTarget.Android;
            PlayerSettings.SetIl2CppCompilerConfiguration(a, dev ? Il2CppCompilerConfiguration.Debug : Il2CppCompilerConfiguration.Release);
            PlayerSettings.SetIl2CppCodeGeneration(a, dev ? UnityEditor.Build.Il2CppCodeGeneration.OptimizeSize : UnityEditor.Build.Il2CppCodeGeneration.OptimizeSpeed);
            PlayerSettings.SetManagedStrippingLevel(a, dev ? ManagedStrippingLevel.High : ManagedStrippingLevel.Medium);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { UnityEngine.Rendering.GraphicsDeviceType.Vulkan, UnityEngine.Rendering.GraphicsDeviceType.OpenGLES3 });
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
        }

        public static BuildResult BuildAndroid(bool dev, bool aab = false)
        {
            var s = KitSettings.Load();
            Configure(dev, aab);
            Directory.CreateDirectory("Builds/Android");
            var path = $"Builds/Android/{s.Slug}-{(dev ? "dev" : "release")}.{(aab ? "aab" : "apk")}";
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = s.scenes,
                locationPathName = path,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = dev ? BuildOptions.Development : BuildOptions.None,
            });
            var r = report.summary;
            Debug.Log($"[T2U] Android build {r.result}: {path} size={(r.totalSize / 1048576.0):F1} MB time={r.totalTime} errors={r.totalErrors} warnings={r.totalWarnings}");
            return r.result;
        }

        [MenuItem("Tools/T2U Kit/Build Android APK (development)", priority = 50)]
        public static void BuildAndroidDev() => KitSettings.ExitIfBatch(BuildAndroid(true) == BuildResult.Succeeded);

        [MenuItem("Tools/T2U Kit/Build Android (release)", priority = 51)]
        public static void BuildAndroidRelease() => KitSettings.ExitIfBatch(BuildAndroid(false, System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-t2uAab") >= 0) == BuildResult.Succeeded);
    }
}

using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace T2U.Kit.Editor
{
    /// <summary>
    /// One-click mobile project configuration from T2UKit.json (safe to re-run). Lessons from earlier ports are built in:
    /// Gamma by default (web blending), Input System only, URP mobile asset without HDR/MSAA/shadows,
    /// SRP Batcher OFF (Unity 6.3: sprites sharing a material sampled the first sprite's texture), VSync off, IL2CPP ARM64.
    /// Batch: -executeMethod T2U.Kit.Editor.ProjectSetup.RunBatch
    /// </summary>
    public static class ProjectSetup
    {
        const string SettingsDir = "Assets/_Game/Settings";

        [MenuItem("Tools/T2U Kit/Setup Project (mobile defaults)", priority = 10)]
        public static void Run()
        {
            var s = KitSettings.Load();
            PlayerSettings.companyName = s.company;
            PlayerSettings.productName = s.product;
            PlayerSettings.bundleVersion = s.version;
            PlayerSettings.Android.bundleVersionCode = s.versionCode;
            PlayerSettings.iOS.buildNumber = s.versionCode.ToString();
            PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android, s.bundleId);
            PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.iOS, s.bundleId);
            PlayerSettings.defaultInterfaceOrientation = s.portrait ? UIOrientation.Portrait : UIOrientation.LandscapeLeft;
            PlayerSettings.allowedAutorotateToPortrait = s.portrait;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = !s.portrait;
            PlayerSettings.allowedAutorotateToLandscapeRight = !s.portrait;
            PlayerSettings.colorSpace = s.colorSpace == "Linear" ? ColorSpace.Linear : ColorSpace.Gamma;
            var android = UnityEditor.Build.NamedBuildTarget.Android;
            PlayerSettings.SetScriptingBackend(android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel25;
            PlayerSettings.SetManagedStrippingLevel(android, ManagedStrippingLevel.Medium);
            PlayerSettings.Android.optimizedFramePacing = true;

            // Input System only (0 = old Input Manager, 1 = Input System, 2 = both)
            var ps = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset")[0]);
            var ih = ps.FindProperty("activeInputHandler");
            if (ih != null && ih.intValue != 1) { ih.intValue = 1; ps.ApplyModifiedPropertiesWithoutUndo(); }

            Directory.CreateDirectory(SettingsDir);
            var rdPath = SettingsDir + "/Mobile_Renderer.asset";
            var rpPath = SettingsDir + "/Mobile_RPAsset.asset";
            var rd = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rdPath);
            if (rd == null) { rd = ScriptableObject.CreateInstance<UniversalRendererData>(); AssetDatabase.CreateAsset(rd, rdPath); }
            var rp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(rpPath);
            if (rp == null) { rp = UniversalRenderPipelineAsset.Create(rd); AssetDatabase.CreateAsset(rp, rpPath); }
            rp.supportsHDR = false;
            rp.msaaSampleCount = 1;
            rp.renderScale = 1f;
            var so = new SerializedObject(rp);
            Set(so, "m_MainLightShadowsSupported", false);
            Set(so, "m_AdditionalLightShadowsSupported", false);
            Set(so, "m_SoftShadowsSupported", false);
            Set(so, "m_UseSRPBatcher", false);
            Set(so, "m_ShadowDistance", 0f);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(rp);
            GraphicsSettings.defaultRenderPipeline = rp;
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = rp;
                QualitySettings.vSyncCount = 0;
            }
            QualitySettings.SetQualityLevel(Mathf.Min(1, QualitySettings.names.Length - 1), false);
            EditorBuildSettings.scenes = System.Array.ConvertAll(s.scenes, p => new EditorBuildSettingsScene(p, true));
            AssetDatabase.SaveAssets();
            Debug.Log($"[T2U] Project settings applied: {s.product} ({s.bundleId}), {PlayerSettings.colorSpace}, URP mobile, IL2CPP ARM64, {(s.portrait ? "portrait" : "landscape")}.");
        }

        public static void RunBatch()
        {
            Run();
            KitSettings.ExitIfBatch(true);
        }

        static void Set(SerializedObject so, string prop, object v)
        {
            var p = so.FindProperty(prop);
            if (p == null) { Debug.LogWarning("[T2U] URP property not found: " + prop); return; }
            if (v is bool b) p.boolValue = b; else if (v is float f) p.floatValue = f;
        }
    }
}

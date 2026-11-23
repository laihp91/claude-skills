using System.IO;
using UnityEditor;
using UnityEngine;

namespace T2U.Kit.Editor
{
    /// <summary>Imports TextMesh Pro Essential Resources (default font + shaders) if missing. Finishes on the next editor tick.</summary>
    public static class TmpEssentials
    {
        [MenuItem("Tools/T2U Kit/Import TMP Essentials", priority = 25)]
        public static void Import()
        {
            if (Directory.Exists("Assets/TextMesh Pro")) { Debug.Log("[T2U] TMP Essentials already imported"); return; }
            var pi = UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/com.unity.ugui");
            var pkg = Path.Combine(pi.resolvedPath, "Package Resources", "TMP Essential Resources.unitypackage");
            AssetDatabase.ImportPackage(pkg, false);
            Debug.Log("[T2U] TMP Essentials import started");
        }

        /// <summary>Batch (no -quit): imports, waits for completion, exits. -executeMethod T2U.Kit.Editor.TmpEssentials.ImportBatch</summary>
        public static void ImportBatch()
        {
            if (Directory.Exists("Assets/TextMesh Pro")) { KitSettings.ExitIfBatch(true); return; }
            AssetDatabase.importPackageCompleted += _ => { Debug.Log("[T2U] TMP Essentials imported"); KitSettings.ExitIfBatch(true); };
            AssetDatabase.importPackageFailed += (_, err) => { Debug.LogError("[T2U] TMP Essentials failed: " + err); KitSettings.ExitIfBatch(false); };
            Import();
        }
    }
}

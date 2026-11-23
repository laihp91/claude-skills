using System.IO;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace T2U.Kit.Editor
{
    /// <summary>Per-project kit settings, stored as plain JSON in ProjectSettings/T2UKit.json (created with defaults).</summary>
    public sealed class KitSettings
    {
        const string PathOnDisk = "ProjectSettings/T2UKit.json";

        public string company = "MyCompany";
        public string product = "My Game";
        public string bundleId = "com.mycompany.mygame";
        public string version = "0.1.0";
        public int versionCode = 1;
        /// <summary>Scenes in the build (first = start scene).</summary>
        public string[] scenes = { "Assets/_Game/Scenes/Main.unity" };
        public bool portrait = true;
        /// <summary>"Gamma" matches web canvas blending (translucent sprites look the same); "Linear" for 3D/PBR games.</summary>
        public string colorSpace = "Gamma";
        /// <summary>The Three.js project, relative to the Unity project folder.</summary>
        public string webProject = "../web";
        /// <summary>TMP font asset for imported UI ("" = TMP default).</summary>
        public string uiFont = "";
        /// <summary>Apply the kit's import rules to Assets/_Game/Art/Sprites and Assets/_Game/Audio.</summary>
        public bool importRules = true;
        /// <summary>Golden recordings + expected logs used by the replay tests, INSIDE the Unity repo (relative to the project folder).</summary>
        public string goldenDir = "TestData/Golden";
        /// <summary>
        /// After handover: generated scenes/prefabs are owned by the team. Generators then only create what is missing and
        /// never overwrite (the "overwrite" menus ask for confirmation).
        /// </summary>
        public bool generatedFrozen = false;

        public static KitSettings Load()
        {
            if (!File.Exists(PathOnDisk)) new KitSettings().Save();
            var s = JsonConvert.DeserializeObject<KitSettings>(File.ReadAllText(PathOnDisk)) ?? new KitSettings();
            // -t2uWeb <path>: another web project for this run only (never saved)
            var web = Arg("-t2uWeb");
            if (!string.IsNullOrEmpty(web)) s.webProject = web;
            return s;
        }

        public void Save() => File.WriteAllText(PathOnDisk, JsonConvert.SerializeObject(this, Formatting.Indented));

        public string WebPath(params string[] parts) => Path.GetFullPath(Path.Combine(webProject, Path.Combine(parts)));

        public string Slug => System.Text.RegularExpressions.Regex.Replace(product, "[^A-Za-z0-9]+", "");

        [MenuItem("Tools/T2U Kit/Edit Settings (T2UKit.json)", priority = 0)]
        static void Edit()
        {
            Load();
            EditorUtility.OpenWithDefaultApp(Path.GetFullPath(PathOnDisk));
        }

        /// <summary>Command-line value: -name value</summary>
        public static string Arg(string name, string def = null)
        {
            var a = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
            return def;
        }

        public static void ExitIfBatch(bool ok)
        {
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }
    }
}

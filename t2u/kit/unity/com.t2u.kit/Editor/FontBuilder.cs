using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace T2U.Kit.Editor
{
    /// <summary>
    /// Makes a TextMesh Pro font for every TTF/OTF in Assets/_Game/Art/Fonts: "&lt;family name&gt; SDF.asset" (dynamic atlas,
    /// ASCII + Vietnamese pre-warmed). The UI importer picks it by the CSS font-family of each text.
    /// Batch: -executeMethod T2U.Kit.Editor.FontBuilder.RunBatch
    /// </summary>
    public static class FontBuilder
    {
        public const string Dir = "Assets/_Game/Art/Fonts";
        const string Prewarm = " !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~…" +
                               "ÀÁÂÃÈÉÊÌÍÒÓÔÕÙÚÝàáâãèéêìíòóôõùúýĂăĐđĨĩŨũƠơƯưẠạẢảẤấẦầẨẩẪẫẬậẮắẰằẲẳẴẵẶặẸẹẺẻẼẽẾếỀềỂểỄễỆệỈỉỊịỌọỎỏỐốỒồỔổỖỗỘộỚớỜờỞởỠỡỢợỤụỦủỨứỪừỬửỮữỰựỲỳỴỵỶỷỸỹ";

        public static string AssetPathFor(string family) => $"{Dir}/{family} SDF.asset";

        [MenuItem("Tools/T2U Kit/Build TMP Fonts", priority = 26)]
        public static void Run()
        {
            if (!Directory.Exists(Dir)) return;
            foreach (var path in Directory.GetFiles(Dir))
            {
                if (!(path.EndsWith(".ttf") || path.EndsWith(".otf"))) continue;
                var p = path.Replace('\\', '/');
                var importer = AssetImporter.GetAtPath(p) as TrueTypeFontImporter;
                var family = importer != null && !string.IsNullOrEmpty(importer.fontTTFName) ? importer.fontTTFName : Path.GetFileNameWithoutExtension(p);
                var outPath = AssetPathFor(family);
                if (AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(outPath) != null) continue;
                var font = AssetDatabase.LoadAssetAtPath<Font>(p);
                var fa = TMP_FontAsset.CreateFontAsset(font, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, true);
                fa.name = family + " SDF";
                AssetDatabase.CreateAsset(fa, outPath);
                fa.material.name = family + " SDF Material";
                AssetDatabase.AddObjectToAsset(fa.material, fa);
                foreach (var t in fa.atlasTextures) { t.name = family + " SDF Atlas"; AssetDatabase.AddObjectToAsset(t, fa); }
                fa.TryAddCharacters(Prewarm, out var missing);
                EditorUtility.SetDirty(fa);
                Debug.Log($"[T2U] Font {outPath}" + (string.IsNullOrEmpty(missing) ? "" : $" (not in the font: {missing.Length} chars)"));
            }
            AssetDatabase.SaveAssets();
        }

        public static void RunBatch()
        {
            Run();
            KitSettings.ExitIfBatch(true);
        }
    }
}

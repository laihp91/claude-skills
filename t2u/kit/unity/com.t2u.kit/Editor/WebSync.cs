using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace T2U.Kit.Editor
{
    /// <summary>
    /// Copies what the web game exports into the Unity project (folder conventions of the /game-new template):
    ///   web/unity-convert/export/data/*.json  -> Assets/_Game/Data/*.json      (TextAssets: levels, tuning)
    ///   web/unity-convert/export/ui.json      -> Assets/_Game/Data/UI/ui.json  (then: Import UI Layouts)
    ///   web/unity-convert/export/ui/*.png     -> Assets/_Game/Art/UI/          (the baked looks of the UI: skins)
    ///   web/unity-convert/export/&lt;other&gt;/**   -> Assets/_Game/Art/&lt;Other&gt;/     (what the game itself baked: terrain ...)
    ///   web/public/assets/images|sprites/**.png -> Assets/_Game/Art/Sprites/
    ///   web/public/assets/audio/sfx|music/**  -> Assets/_Game/Audio/Sfx|Music/  (files right in audio/: music* -> Music, others -> Sfx)
    ///   web/public/assets/fonts/*.ttf|otf     -> Assets/_Game/Art/Fonts/
    /// Run `npm run t2u:export` in the web project first. Batch: -executeMethod T2U.Kit.Editor.WebSync.RunBatch
    /// </summary>
    public static class WebSync
    {
        [MenuItem("Tools/T2U Kit/Sync From Web Project", priority = 20)]
        public static void Run()
        {
            var s = KitSettings.Load();
            int n = 0;
            n += Copy(s.WebPath("unity-convert", "export", "data"), "Assets/_Game/Data", "*.json");
            n += CopyFile(s.WebPath("unity-convert", "export", "ui.json"), "Assets/_Game/Data/UI/ui.json");
            n += Copy(s.WebPath("unity-convert", "export", "ui"), "Assets/_Game/Art/UI", "*.png");
            n += Copy(s.WebPath("public", "assets", "images"), "Assets/_Game/Art/Sprites", "*.png");
            n += Copy(s.WebPath("public", "assets", "sprites"), "Assets/_Game/Art/Sprites", "*.png");
            n += Copy(s.WebPath("public", "assets", "audio", "sfx"), "Assets/_Game/Audio/Sfx", "*.*");
            n += Copy(s.WebPath("public", "assets", "audio", "music"), "Assets/_Game/Audio/Music", "*.*");
            // audio files that lie directly in assets/audio: "music*" is music, the rest are sound effects
            n += Copy(s.WebPath("public", "assets", "audio"), "Assets/_Game/Audio/Music", "music*.*", SearchOption.TopDirectoryOnly);
            n += Copy(s.WebPath("public", "assets", "audio"), "Assets/_Game/Audio/Sfx", "*.*", SearchOption.TopDirectoryOnly, skip: "music");
            // what the game itself baked for Unity (window.__t2u.extras): every other folder of export/
            var export = s.WebPath("unity-convert", "export");
            if (Directory.Exists(export))
                foreach (var dir in Directory.GetDirectories(export))
                {
                    var name = Path.GetFileName(dir);
                    if (name == "data" || name == "ui") continue;
                    n += Copy(dir, "Assets/_Game/Art/" + char.ToUpperInvariant(name[0]) + name.Substring(1), "*.*");
                }
            n += Copy(s.WebPath("public", "assets", "fonts"), "Assets/_Game/Art/Fonts", "*.?tf");
            AssetDatabase.Refresh();
            Debug.Log($"[T2U] Synced {n} file(s) from {s.WebPath()}");
        }

        public static void RunBatch()
        {
            Run();
            KitSettings.ExitIfBatch(true);
        }

        static int Copy(string from, string to, string pattern, SearchOption depth = SearchOption.AllDirectories, string skip = null)
        {
            if (!Directory.Exists(from)) return 0;
            int n = 0;
            foreach (var f in Directory.GetFiles(from, pattern, depth))
            {
                if (f.EndsWith(".meta") || Path.GetFileName(f).StartsWith(".")) continue;
                if (skip != null && Path.GetFileName(f).StartsWith(skip)) continue;
                n += CopyFile(f, Path.Combine(to, Path.GetRelativePath(from, f)));
            }
            return n;
        }

        static int CopyFile(string from, string to)
        {
            if (!File.Exists(from)) return 0;
            Directory.CreateDirectory(Path.GetDirectoryName(to));
            if (File.Exists(to) && new FileInfo(to).Length == new FileInfo(from).Length && File.ReadAllBytes(to).AsSpan().SequenceEqual(File.ReadAllBytes(from))) return 0;
            File.Copy(from, to, true);
            return 1;
        }
    }
}

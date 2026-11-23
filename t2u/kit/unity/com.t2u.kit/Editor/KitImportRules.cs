using UnityEditor;
using UnityEngine;

namespace T2U.Kit.Editor
{
    /// <summary>
    /// Import settings by folder (so every re-import gives the same result). Turn off with "importRules": false in T2UKit.json.
    ///   Assets/_Game/Art/Sprites/**  sprite, no mipmaps, clamp, max 2048, uncompressed (atlases compress) (9-slice: file name ending in _9s&lt;px&gt;, e.g. button_9s40.png)
    ///   Assets/_Game/Audio/Sfx/**    Vorbis q0.7, decompress on load      Assets/_Game/Audio/Music/** Vorbis q0.6, streaming
    /// </summary>
    public class KitImportRules : AssetPostprocessor
    {
        const string Sprites = "Assets/_Game/Art/Sprites/";
        const string Sfx = "Assets/_Game/Audio/Sfx/";
        const string Music = "Assets/_Game/Audio/Music/";
        static bool? _on;
        static bool On => _on ??= KitSettings.Load().importRules;

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(Sprites) || !On) return;
            var ti = (TextureImporter)assetImporter;
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.spritePixelsPerUnit = 100;
            ti.mipmapEnabled = false;
            ti.alphaIsTransparency = true;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.filterMode = FilterMode.Bilinear;
            ti.maxTextureSize = 2048;
            ti.isReadable = false;
            // uncompressed source: sprites are packed into sprite atlases, which choose their own compression; a
            // compressed source is decompressed for packing and loses its detail twice (Unity warns while packing)
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            // FullRect: a sprite keeps its whole frame, transparent margins included, also when Unity packs it into a
            // sprite atlas (a Tight mesh is trimmed there, and a batch that draws the frame stretches the trimmed picture)
            var st = new TextureImporterSettings();
            ti.ReadTextureSettings(st);
            st.spriteMeshType = SpriteMeshType.FullRect;
            ti.SetTextureSettings(st);
            var m = System.Text.RegularExpressions.Regex.Match(System.IO.Path.GetFileNameWithoutExtension(assetPath), @"_9s(\d+)$");
            if (m.Success) { int b = int.Parse(m.Groups[1].Value); ti.spriteBorder = new Vector4(b, b, b, b); }
        }

        void OnPreprocessAudio()
        {
            bool sfx = assetPath.StartsWith(Sfx), music = assetPath.StartsWith(Music);
            if (!(sfx || music) || !On) return;
            var ai = (AudioImporter)assetImporter;
            var s = ai.defaultSampleSettings;
            s.compressionFormat = AudioCompressionFormat.Vorbis;
            s.loadType = sfx ? AudioClipLoadType.DecompressOnLoad : AudioClipLoadType.Streaming;
            s.quality = sfx ? 0.7f : 0.6f;
            if (sfx) s.preloadAudioData = true;
            ai.defaultSampleSettings = s;
        }
    }
}

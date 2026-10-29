using System.IO;
using UnityEditor;
using UnityEngine;

namespace BirimizKovulacak.EditorTools
{
    /// <summary>
    /// UI asset rules (owner: ui). Every PNG under <see cref="UiResourcesDir"/> is a UI texture loaded at runtime with
    /// Resources.Load (see BirimizKovulacak.Game.UiArt): no mipmaps, no power-of-two rescale (9-slice borders are
    /// in source pixels), clamp, BC7 (all kit images have dimensions divisible by 4), max 2048.
    /// Fonts under Resources/ui/fonts are plain TTF imports; UI Toolkit builds dynamic SDF font assets from them at
    /// runtime (StyleFontDefinition(Font)), so nothing has to be generated here.
    /// </summary>
    public static partial class ProjectBuilder
    {
        public const string UiResourcesDir = Root + "/UI/Resources/ui";

        /// <summary>Art/UI folders the runtime UI loads (others are world decals, store art, references, and the v2 HUD /
        /// phone / how-to / results art: bubbles, howto, phone, titles, week). Everything synced here ships in the build.</summary>
        static readonly string[] UiArtFolders = { "backgrounds", "buttons", "cards", "icons", "logo", "meeting", "portraits", "stamps",
            "rounds", "tricks", "icons_v3", "computer", "deskphone", "meeting_v3", "onboarding", "stamps_v3", "logo_en", "week_v3" };

        /// <summary>Copies the UI art kit (repo Art/UI/&lt;folder&gt;/*.png) into Resources/ui/&lt;folder&gt;/ (changed files only).</summary>
        [MenuItem("Birimiz Kovulacak/UI/Sync UI art from Art/UI")]
        public static void SyncUiArt()
        {
            string src = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Art", "UI"));
            if (!Directory.Exists(src)) { Log("No Art/UI at " + src); return; }
            int copied = 0;
            foreach (string folder in UiArtFolders)
            {
                string from = Path.Combine(src, folder);
                if (!Directory.Exists(from)) continue;
                string to = Path.Combine(UiResourcesDir, folder);
                Directory.CreateDirectory(to);
                foreach (string file in Directory.GetFiles(from, "*.png"))
                {
                    string name = Path.GetFileName(file);
                    if (name.StartsWith("_")) continue;
                    string dst = Path.Combine(to, name);
                    if (File.Exists(dst) && new FileInfo(dst).Length == new FileInfo(file).Length && File.GetLastWriteTimeUtc(dst) >= File.GetLastWriteTimeUtc(file)) continue;
                    File.Copy(file, dst, true);
                    copied++;
                }
            }
            AssetDatabase.Refresh();
            Log("UI art synced: " + copied + " file(s) copied");
        }

        [MenuItem("Birimiz Kovulacak/UI/Reimport UI textures")]
        public static void ReimportUiTextures()
        {
            if (!AssetDatabase.IsValidFolder(UiResourcesDir)) { Log("No UI resources at " + UiResourcesDir); return; }
            string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { UiResourcesDir });
            foreach (string g in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(g);
                var imp = AssetImporter.GetAtPath(path) as TextureImporter;
                if (imp == null) continue;
                if (UiTextureImportRules.Apply(imp)) imp.SaveAndReimport();
            }
            Log("UI textures checked: " + guids.Length);
        }
    }

    /// <summary>Applies the UI texture rules automatically whenever a PNG inside the UI Resources folder is imported.</summary>
    public sealed class UiTextureImportRules : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            if (!assetPath.Replace('\\', '/').StartsWith(ProjectBuilder.UiResourcesDir + "/")) return;
            Apply((TextureImporter)assetImporter);
        }

        /// <summary>Returns true if a setting changed.</summary>
        public static bool Apply(TextureImporter imp)
        {
            bool changed = false;
            string file = Path.GetFileNameWithoutExtension(imp.assetPath);
            bool tile = file == "paper_tile";
            if (imp.textureType != TextureImporterType.Default) { imp.textureType = TextureImporterType.Default; changed = true; }
            if (imp.npotScale != TextureImporterNPOTScale.None) { imp.npotScale = TextureImporterNPOTScale.None; changed = true; }
            if (imp.mipmapEnabled) { imp.mipmapEnabled = false; changed = true; }
            if (!imp.alphaIsTransparency) { imp.alphaIsTransparency = true; changed = true; }
            if (!imp.sRGBTexture) { imp.sRGBTexture = true; changed = true; }
            var wrap = tile ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            if (imp.wrapMode != wrap) { imp.wrapMode = wrap; changed = true; }
            if (imp.filterMode != FilterMode.Bilinear) { imp.filterMode = FilterMode.Bilinear; changed = true; }
            if (imp.textureCompression != TextureImporterCompression.CompressedHQ) { imp.textureCompression = TextureImporterCompression.CompressedHQ; changed = true; }
            if (imp.maxTextureSize != 2048) { imp.maxTextureSize = 2048; changed = true; }
            if (imp.isReadable) { imp.isReadable = false; changed = true; }
            return changed;
        }
    }
}

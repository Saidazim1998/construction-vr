using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace NBM500U.EditorTools
{
    /// <summary>
    /// Turns a roughness map into the texture the URP Lit shader expects in its Metallic Map slot:
    /// RGB = metallic, A = smoothness (1 - roughness). Select one or more roughness textures in the
    /// Project window and use Assets > Texture Tools. The result is written next to the source as
    /// "&lt;name&gt;_MetallicSmoothness.png". If a metallic map with the same base name sits in the same
    /// folder (…_metallic / …_metalness / …_metal) it is used; otherwise a constant value is.
    /// </summary>
    public static class RoughnessToMetallicSmoothness
    {
        private const string MenuRoot = "Assets/Texture Tools/";
        private const string OutputSuffix = "_MetallicSmoothness";

        private static readonly Regex RoughnessSuffix =
            new Regex(@"[_\-\s.]?(roughness|rough)$", RegexOptions.IgnoreCase);

        private static readonly string[] OverridePlatforms =
            { "Standalone", "Android", "iPhone", "WebGL", "WindowsStoreApps" };

        // ------------------------------------------------------------------- menu

        [MenuItem(MenuRoot + "Roughness to Metallic-Smoothness (non-metal)")]
        private static void ConvertNonMetalMenu() => ConvertSelection(0f);

        [MenuItem(MenuRoot + "Roughness to Metallic-Smoothness (metal)")]
        private static void ConvertMetalMenu() => ConvertSelection(1f);

        [MenuItem(MenuRoot + "Roughness to Metallic-Smoothness (non-metal)", true)]
        [MenuItem(MenuRoot + "Roughness to Metallic-Smoothness (metal)", true)]
        private static bool ValidateMenu() => SelectedTexturePaths().Count > 0;

        private static List<string> SelectedTexturePaths()
        {
            var paths = new List<string>();
            foreach (var tex in Selection.GetFiltered<Texture2D>(SelectionMode.Assets))
            {
                string path = AssetDatabase.GetAssetPath(tex);
                if (!string.IsNullOrEmpty(path) && AssetImporter.GetAtPath(path) is TextureImporter)
                    paths.Add(path);
            }
            return paths;
        }

        private static void ConvertSelection(float fallbackMetallic)
        {
            var paths = SelectedTexturePaths();
            var created = new List<UnityEngine.Object>();
            try
            {
                for (int i = 0; i < paths.Count; i++)
                {
                    EditorUtility.DisplayProgressBar("Roughness to Metallic-Smoothness",
                        Path.GetFileName(paths[i]), (float)i / paths.Count);
                    try
                    {
                        string output = Convert(paths[i], fallbackMetallic);
                        created.Add(AssetDatabase.LoadAssetAtPath<Texture2D>(output));
                        Debug.Log($"Metallic-Smoothness map: {output}", created[created.Count - 1]);
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"Could not convert {paths[i]}: {e.Message}");
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
            if (created.Count > 0) Selection.objects = created.ToArray();
        }

        // ------------------------------------------------------------------- conversion

        /// <summary>
        /// Converts one roughness texture and returns the asset path of the packed map.
        /// <paramref name="fallbackMetallic"/> (0..1) is used when no metallic map is found next to it.
        /// </summary>
        public static string Convert(string roughnessPath, float fallbackMetallic = 0f)
        {
            string folder = Path.GetDirectoryName(roughnessPath).Replace('\\', '/');
            string baseName = RoughnessSuffix.Replace(Path.GetFileNameWithoutExtension(roughnessPath), "");
            string outputPath = $"{folder}/{baseName}{OutputSuffix}.png";

            Color32[] rough = ReadRaw(roughnessPath, out int width, out int height);

            Color32[] metal = null;
            int metalWidth = 0, metalHeight = 0;
            string metallicPath = FindMetallicSibling(folder, baseName);
            if (metallicPath != null) metal = ReadRaw(metallicPath, out metalWidth, out metalHeight);

            byte constantMetal = (byte)Mathf.RoundToInt(Mathf.Clamp01(fallbackMetallic) * 255f);
            var packed = new Color32[rough.Length];
            for (int y = 0; y < height; y++)
            {
                // Nearest sample of the metallic map, in case its size differs from the roughness map.
                int metalRow = metal == null ? 0 : Mathf.Min(y * metalHeight / height, metalHeight - 1) * metalWidth;
                for (int x = 0; x < width; x++)
                {
                    int i = y * width + x;
                    byte m = metal == null
                        ? constantMetal
                        : metal[metalRow + Mathf.Min(x * metalWidth / width, metalWidth - 1)].r;
                    packed[i] = new Color32(m, m, m, (byte)(255 - rough[i].r));
                }
            }

            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
            try
            {
                texture.SetPixels32(packed);
                File.WriteAllBytes(outputPath, texture.EncodeToPNG());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }

            AssetDatabase.ImportAsset(outputPath, ImportAssetOptions.ForceUpdate);
            ConfigureOutput(outputPath);
            return outputPath;
        }

        /// <summary>The packed map holds data, not colour: linear, alpha kept as it is.</summary>
        private static void ConfigureOutput(string path)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (!importer.sRGBTexture && importer.alphaSource == TextureImporterAlphaSource.FromInput &&
                !importer.alphaIsTransparency)
                return;
            importer.sRGBTexture = false;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = false;
            importer.SaveAndReimport();
        }

        private static string FindMetallicSibling(string folder, string baseName)
        {
            var pattern = new Regex("^" + Regex.Escape(baseName) + @"[_\-\s.]?(metallic|metalness|metal)$",
                RegexOptions.IgnoreCase);
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetDirectoryName(path).Replace('\\', '/') != folder) continue;
                if (pattern.IsMatch(Path.GetFileNameWithoutExtension(path))) return path;
            }
            return null;
        }

        /// <summary>
        /// Reads the stored pixel values of a texture asset, whatever its import settings are. Works on a
        /// temporary copy imported uncompressed, linear and at full size, so the original asset and its
        /// .meta are never touched.
        /// </summary>
        private static Color32[] ReadRaw(string assetPath, out int width, out int height)
        {
            string folder = Path.GetDirectoryName(assetPath).Replace('\\', '/');
            string tempPath = AssetDatabase.GenerateUniqueAssetPath(
                $"{folder}/__r2s_tmp{Path.GetExtension(assetPath)}");
            if (!AssetDatabase.CopyAsset(assetPath, tempPath))
                throw new IOException($"could not make a working copy of {assetPath}");
            try
            {
                var importer = (TextureImporter)AssetImporter.GetAtPath(tempPath);
                importer.textureType = TextureImporterType.Default;
                importer.textureShape = TextureImporterShape.Texture2D;
                importer.sRGBTexture = false;
                importer.isReadable = true;
                importer.mipmapEnabled = false;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.maxTextureSize = 16384;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                foreach (string platform in OverridePlatforms)
                {
                    var settings = importer.GetPlatformTextureSettings(platform);
                    if (!settings.overridden) continue;
                    settings.overridden = false;
                    importer.SetPlatformTextureSettings(settings);
                }
                importer.SaveAndReimport();

                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(tempPath);
                if (texture == null) throw new IOException($"could not import a working copy of {assetPath}");
                width = texture.width;
                height = texture.height;
                return texture.GetPixels32();
            }
            finally
            {
                AssetDatabase.DeleteAsset(tempPath);
            }
        }
    }
}

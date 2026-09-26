using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Bento
{
    enum TerrainTextureRole
    {
        Diffuse,
        Normal,
        Mask
    }

    sealed class TerrainLayerGenerationEntry
    {
        public string name;
        public Texture2D diffuse;
        public Texture2D normal;
        public Texture2D mask;
        public string outputPath;
        public string warning;
    }

    static class TerrainLayerGenerationService
    {
        static readonly string[] TextureExtensions =
        {
            ".png", ".jpg", ".jpeg", ".tga", ".tif", ".tiff", ".psd", ".exr"
        };

        static readonly (string suffix, TerrainTextureRole role)[] SuffixRules =
        {
            ("_BaseColor", TerrainTextureRole.Diffuse),
            ("_Albedo", TerrainTextureRole.Diffuse),
            ("_Diffuse", TerrainTextureRole.Diffuse),
            ("_BC", TerrainTextureRole.Diffuse),
            ("_Normal", TerrainTextureRole.Normal),
            ("_NormalMap", TerrainTextureRole.Normal),
            ("_N", TerrainTextureRole.Normal),
            ("_MRO", TerrainTextureRole.Mask),
            ("_Mask", TerrainTextureRole.Mask),
            ("_MaskMap", TerrainTextureRole.Mask),
            ("_MR", TerrainTextureRole.Mask)
        };

        public static List<TerrainLayerGenerationEntry> BuildFromSelection(string outputFolder)
        {
            return Build(CollectSelectedTextures(), outputFolder);
        }

        public static List<TerrainLayerGenerationEntry> Build(
            IEnumerable<Texture2D> textures,
            string outputFolder)
        {
            var groups = new Dictionary<string, TerrainLayerGenerationEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (Texture2D texture in textures.Where(texture => texture != null).Distinct())
            {
                ParseTextureName(texture.name, out string baseName, out TerrainTextureRole role);
                if (!groups.TryGetValue(baseName, out TerrainLayerGenerationEntry entry))
                {
                    entry = new TerrainLayerGenerationEntry { name = baseName };
                    groups.Add(baseName, entry);
                }

                Assign(entry, texture, role);
            }

            foreach (TerrainLayerGenerationEntry entry in groups.Values)
            {
                if (entry.diffuse == null)
                    entry.warning = "缺少基础色贴图，不会生成。";

                string safeName = BentoUtility.SanitizeFileName(entry.name, "TerrainLayer");
                entry.outputPath = $"{outputFolder.TrimEnd('/')}/{safeName}.terrainlayer";
                if (AssetDatabase.LoadAssetAtPath<TerrainLayer>(entry.outputPath) != null)
                    entry.warning = AppendWarning(entry.warning, "目标已存在，默认跳过，不会覆盖。");
            }

            return groups.Values
                .OrderBy(entry => entry.name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static List<TerrainLayer> Generate(
            IEnumerable<TerrainLayerGenerationEntry> entries,
            Vector2 tileSize)
        {
            var created = new List<TerrainLayer>();
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (TerrainLayerGenerationEntry entry in entries)
                {
                    if (entry.diffuse == null ||
                        AssetDatabase.LoadAssetAtPath<TerrainLayer>(entry.outputPath) != null)
                        continue;

                    var layer = new TerrainLayer
                    {
                        name = entry.name,
                        diffuseTexture = entry.diffuse,
                        normalMapTexture = entry.normal,
                        maskMapTexture = entry.mask,
                        tileSize = tileSize
                    };
                    AssetDatabase.CreateAsset(layer, entry.outputPath);
                    created.Add(layer);
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            return created;
        }

        static IEnumerable<Texture2D> CollectSelectedTextures()
        {
            var assetPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (UnityEngine.Object selected in Selection.objects)
            {
                string path = AssetDatabase.GetAssetPath(selected);
                if (string.IsNullOrEmpty(path)) continue;

                if (AssetDatabase.IsValidFolder(path))
                {
                    foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { path }))
                        assetPaths.Add(AssetDatabase.GUIDToAssetPath(guid));
                }
                else if (IsTexturePath(path))
                {
                    assetPaths.Add(path);
                }
            }

            return assetPaths
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Select(AssetDatabase.LoadAssetAtPath<Texture2D>)
                .Where(texture => texture != null);
        }

        static bool IsTexturePath(string path)
        {
            string extension = Path.GetExtension(path);
            return TextureExtensions.Any(candidate =>
                candidate.Equals(extension, StringComparison.OrdinalIgnoreCase));
        }

        static void ParseTextureName(
            string textureName,
            out string baseName,
            out TerrainTextureRole role)
        {
            foreach ((string suffix, TerrainTextureRole candidateRole) in
                     SuffixRules.OrderByDescending(rule => rule.suffix.Length))
            {
                if (!textureName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) ||
                    textureName.Length <= suffix.Length)
                    continue;

                baseName = textureName.Substring(0, textureName.Length - suffix.Length);
                role = candidateRole;
                return;
            }

            baseName = textureName;
            role = TerrainTextureRole.Diffuse;
        }

        static void Assign(
            TerrainLayerGenerationEntry entry,
            Texture2D texture,
            TerrainTextureRole role)
        {
            Texture2D existing = role == TerrainTextureRole.Normal
                ? entry.normal
                : role == TerrainTextureRole.Mask
                    ? entry.mask
                    : entry.diffuse;

            if (existing != null)
            {
                entry.warning = AppendWarning(
                    entry.warning,
                    $"{role} 有多个候选，保留 {existing.name}，忽略 {texture.name}。");
                return;
            }

            if (role == TerrainTextureRole.Normal) entry.normal = texture;
            else if (role == TerrainTextureRole.Mask) entry.mask = texture;
            else entry.diffuse = texture;
        }

        static string AppendWarning(string current, string addition)
        {
            return string.IsNullOrEmpty(current) ? addition : current + "\n" + addition;
        }
    }
}

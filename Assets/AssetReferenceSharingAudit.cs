using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Bento
{
    public static class AssetReferenceSharingAudit
    {
        const string OutputPath = "Temp/Bento_asset_reference_sharing_audit.json";

        [Serializable]
        class Report
        {
            public int scannedModels;
            public int modelsWithRenderers;
            public int uniqueMaterials;
            public int uniqueTextures;
            public int sharedMaterials;
            public int sharedTexturesAcrossMaterials;
            public int sharedTexturesAcrossModels;
            public List<SharedEntry> topSharedMaterials = new List<SharedEntry>();
            public List<SharedEntry> topSharedTextures = new List<SharedEntry>();
        }

        [Serializable]
        class SharedEntry
        {
            public string path;
            public int modelCount;
            public int ownerCount;
            public List<string> sampleModels = new List<string>();
        }

        [MenuItem("Tools/Bento/审计资产引用复用", priority = -9980)]
        public static void Run()
        {
            var materialModels = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            var textureModels = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            var textureMaterials = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            var modelGuids = AssetDatabase.FindAssets("t:GameObject", Roots);
            var report = new Report();

            try
            {
                for (int index = 0; index < modelGuids.Length; index++)
                {
                    string modelPath = AssetDatabase.GUIDToAssetPath(modelGuids[index]);
                    if (!modelPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase) &&
                        !modelPath.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase) &&
                        !modelPath.EndsWith(".obj", StringComparison.OrdinalIgnoreCase))
                        continue;

                    report.scannedModels++;
                    if (EditorUtility.DisplayCancelableProgressBar(
                        "Bento 资产引用复用审计",
                        $"{report.scannedModels}: {modelPath}",
                        (float)index / Math.Max(1, modelGuids.Length)))
                        break;

                    var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
                    if (model == null) continue;
                    var materials = model.GetComponentsInChildren<Renderer>(true)
                        .SelectMany(renderer => renderer.sharedMaterials)
                        .Where(material => material != null)
                        .Distinct()
                        .ToList();
                    if (materials.Count == 0) continue;
                    report.modelsWithRenderers++;

                    foreach (var material in materials)
                    {
                        string materialPath = AssetDatabase.GetAssetPath(material);
                        if (string.IsNullOrEmpty(materialPath)) continue;
                        AddOwner(materialModels, materialPath, modelPath);
                        foreach (var texture in GetTextures(material))
                        {
                            string texturePath = AssetDatabase.GetAssetPath(texture);
                            if (string.IsNullOrEmpty(texturePath)) continue;
                            AddOwner(textureModels, texturePath, modelPath);
                            AddOwner(textureMaterials, texturePath, materialPath);
                        }
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            report.uniqueMaterials = materialModels.Count;
            report.uniqueTextures = textureModels.Count;
            report.sharedMaterials = materialModels.Count(pair => pair.Value.Count > 1);
            report.sharedTexturesAcrossMaterials = textureMaterials.Count(pair => pair.Value.Count > 1);
            report.sharedTexturesAcrossModels = textureModels.Count(pair => pair.Value.Count > 1);
            report.topSharedMaterials = BuildTop(materialModels, materialModels, 40);
            report.topSharedTextures = BuildTop(textureModels, textureMaterials, 60);

            Directory.CreateDirectory(Path.GetDirectoryName(OutputPath));
            File.WriteAllText(OutputPath, JsonUtility.ToJson(report, true));
            Debug.Log($"[Bento] 资产引用复用审计完成：{Path.GetFullPath(OutputPath)}");
        }

        static IEnumerable<Texture> GetTextures(Material material)
        {
            if (material.shader == null) yield break;
            int propertyCount = ShaderUtil.GetPropertyCount(material.shader);
            for (int index = 0; index < propertyCount; index++)
            {
                if (ShaderUtil.GetPropertyType(material.shader, index) != ShaderUtil.ShaderPropertyType.TexEnv) continue;
                var texture = material.GetTexture(ShaderUtil.GetPropertyName(material.shader, index));
                if (texture != null) yield return texture;
            }
        }

        static void AddOwner(Dictionary<string, HashSet<string>> map, string key, string owner)
        {
            if (!map.TryGetValue(key, out var owners))
            {
                owners = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                map[key] = owners;
            }
            owners.Add(owner);
        }

        static List<SharedEntry> BuildTop(
            Dictionary<string, HashSet<string>> modelOwners,
            Dictionary<string, HashSet<string>> directOwners,
            int limit)
        {
            return modelOwners
                .Where(pair => pair.Value.Count > 1)
                .OrderByDescending(pair => pair.Value.Count)
                .Take(limit)
                .Select(pair => new SharedEntry
                {
                    path = pair.Key,
                    modelCount = pair.Value.Count,
                    ownerCount = directOwners.TryGetValue(pair.Key, out var owners) ? owners.Count : pair.Value.Count,
                    sampleModels = pair.Value.Take(8).ToList()
                })
                .ToList();
        }
    }
}

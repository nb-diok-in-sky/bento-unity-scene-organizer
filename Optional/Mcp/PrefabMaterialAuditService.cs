using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Bento
{
    static class PrefabMaterialAuditService
    {
        public static object Audit(string materialFolder, IReadOnlyList<string> prefabPaths)
        {
            var targetMaterials = AssetDatabase.FindAssets("t:Material", new[] { materialFolder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => !string.IsNullOrEmpty(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path)
                .ToArray();
            var prefabReports = prefabPaths.Select(AuditPrefab).ToArray();
            var prefabMaterialPaths = prefabReports
                .SelectMany(report => report.materialPaths)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var loadedSceneMaterialPaths = UnityEngine.Object.FindObjectsOfType<Renderer>(true)
                .Where(renderer => renderer.gameObject.scene.IsValid())
                .SelectMany(renderer => renderer.sharedMaterials)
                .Where(material => material != null)
                .Select(AssetDatabase.GetAssetPath)
                .Where(path => !string.IsNullOrEmpty(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            return new
            {
                material_folder = materialFolder,
                material_count = targetMaterials.Length,
                prefabs = prefabReports.Select(report => new
                {
                    prefab_path = report.prefabPath,
                    material_count = report.materialPaths.Length,
                    materials = report.materialPaths
                }).ToArray(),
                used_by_target_prefabs = targetMaterials.Where(prefabMaterialPaths.Contains).ToArray(),
                used_by_loaded_scenes = targetMaterials.Where(loadedSceneMaterialPaths.Contains).ToArray(),
                used_by_both = targetMaterials.Where(path =>
                    prefabMaterialPaths.Contains(path) && loadedSceneMaterialPaths.Contains(path)).ToArray(),
                unused_by_targets_and_loaded_scenes = targetMaterials.Where(path =>
                    !prefabMaterialPaths.Contains(path) && !loadedSceneMaterialPaths.Contains(path)).ToArray()
            };
        }

        static PrefabReport AuditPrefab(string prefabPath)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            string[] materials = prefab == null
                ? Array.Empty<string>()
                : prefab.GetComponentsInChildren<Renderer>(true)
                    .SelectMany(renderer => renderer.sharedMaterials)
                    .Where(material => material != null)
                    .Select(AssetDatabase.GetAssetPath)
                    .Where(path => !string.IsNullOrEmpty(path))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(path => path)
                    .ToArray();
            return new PrefabReport(prefabPath, materials);
        }

        readonly struct PrefabReport
        {
            public PrefabReport(string prefabPath, string[] materialPaths)
            {
                this.prefabPath = prefabPath;
                this.materialPaths = materialPaths;
            }

            public readonly string prefabPath;
            public readonly string[] materialPaths;
        }
    }
}

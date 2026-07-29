using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Bento
{
    public static class AIAssetOrganizerPlanner
    {
        public static AIAssetOrganizerPlan Build(AIAssetOrganizerConfig config)
        {
            ValidateConfig(config);
            var plan = new AIAssetOrganizerPlan { config = CloneConfig(config) };
            var sourceAssets = LoadSourceAssets(config.sourceRoot);
            plan.entries.AddRange(SafeAssetMoveService.BuildPlan(
                sourceAssets,
                config.prefabFolder,
                config.materialFolder,
                config.textureFolder,
                config.otherFolder,
                new AssetNamingOptions()));

            foreach (var entry in plan.entries)
            {
                if (!IsInside(entry.sourcePath, config.sourceRoot))
                {
                    entry.include = false;
                    entry.warning = "资产位于来源根目录之外，已锁定为不处理";
                }

                if (!string.IsNullOrEmpty(entry.warning))
                {
                    entry.include = false;
                }
            }

            plan.warnings.AddRange(plan.entries
                .Where(entry => !string.IsNullOrEmpty(entry.warning))
                .Select(entry => $"{entry.sourcePath}: {entry.warning}"));
            return plan;
        }

        static AIAssetOrganizerConfig CloneConfig(AIAssetOrganizerConfig config) => new AIAssetOrganizerConfig
        {
            sourceRoot = config.sourceRoot,
            prefabFolder = config.prefabFolder,
            materialFolder = config.materialFolder,
            textureFolder = config.textureFolder,
            otherFolder = config.otherFolder
        };

        static IEnumerable<UnityEngine.Object> LoadSourceAssets(string sourceRoot)
        {
            foreach (string guid in AssetDatabase.FindAssets(string.Empty, new[] { sourceRoot }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path) || AssetDatabase.IsValidFolder(path)) continue;
                var asset = AssetDatabase.LoadMainAssetAtPath(path);
                if (asset != null) yield return asset;
            }
        }

        public static void ValidateConfig(AIAssetOrganizerConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (!AssetDatabase.IsValidFolder(config.sourceRoot)) throw new InvalidOperationException("请选择有效的待整理资产根目录");
            ValidateTarget(config.prefabFolder, "Prefab/模型");
            ValidateTarget(config.materialFolder, "材质球");
            ValidateTarget(config.textureFolder, "贴图");
            ValidateTarget(config.otherFolder, "其他资产");
        }

        static void ValidateTarget(string path, string label)
        {
            if (!AssetDatabase.IsValidFolder(path)) throw new InvalidOperationException($"请选择有效的{label}输出目录");
        }

        public static bool IsInside(string path, string root)
        {
            return path.Equals(root, StringComparison.OrdinalIgnoreCase) ||
                   path.StartsWith(root.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase);
        }
    }
}

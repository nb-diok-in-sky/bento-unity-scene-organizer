using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Bento
{
    public static class McpAnalysisFacade
    {
        public static object AnalyzeResourceVariant(AssetNamingOptions naming)
        {
            var settings = new ResourceVariantPreset();
            if (naming != null)
            {
                settings.prefix = naming.prefix;
                settings.baseName = naming.baseName;
                settings.suffix = naming.suffix;
                settings.appendNumber = naming.appendNumber;
                settings.startNumber = naming.startNumber;
                settings.numberDigits = naming.numberDigits;
            }
            var plan = new ResourceVariantPlanner(settings)
                .Build(BentoUtility.GetTopLevelSelectedSceneObjects());
            return ResourceVariantMcpSummary.Build(plan);
        }

        public static object AnalyzeAssetMove(
            string prefabFolder,
            string materialFolder,
            string textureFolder,
            AssetNamingOptions naming)
        {
            var plan = SafeAssetMoveService.BuildPlan(
                Selection.objects.Where(IsSupportedMoveSource),
                prefabFolder,
                materialFolder,
                textureFolder,
                naming ?? new AssetNamingOptions());
            return new
            {
                count = plan.Count,
                valid_count = plan.Count(entry => string.IsNullOrEmpty(entry.warning)),
                entries = plan.Select(entry => new
                {
                    source_path = entry.sourcePath,
                    source_guid = entry.sourceGuid,
                    target_path = entry.targetPath,
                    kind = entry.kind.ToString(),
                    is_dependency = entry.isDependency,
                    owner_path = entry.ownerPath,
                    warning = entry.warning,
                    prefab_reference_count = entry.prefabReferenceCount,
                    material_reference_count = entry.materialReferenceCount,
                    scene_reference_count = entry.sceneReferenceCount,
                    keeps_original_name = entry.keepOriginalNameBecauseShared,
                    reference_samples = entry.referenceSamples.ToArray()
                }).ToArray()
            };
        }

        public static object AnalyzeSelectedAssetInfo()
        {
            var document = AssetInfoCollector.Collect(Selection.objects);
            return new
            {
                count = document.assets.Count,
                assets = document.assets.Select(asset => new
                {
                    asset.originalName,
                    asset.format,
                    asset.path,
                    asset.sourceAsset,
                    asset.nodeCount,
                    asset.rendererCount,
                    asset.materialCount,
                    asset.textureCount,
                    asset.colliderCount,
                    bounds = asset.boundsSize
                }).ToArray()
            };
        }

        public static object AnalyzeSelectedPrefabLayers()
        {
            var plan = PrefabLayerPlanner.Build(Selection.objects);
            return new
            {
                available_layers = ProjectLayerCatalog.GetDefinedNames().ToArray(),
                apply_mode = PrefabLayerApplyMode.DefaultNodesOnly.ToString(),
                entries = plan.Select(entry => new
                {
                    entry.assetPath,
                    entry.currentRootLayer,
                    entry.suggestedLayer,
                    entry.reason,
                    entry.confidence,
                    entry.nodeCount,
                    entry.defaultLayerNodeCount,
                    entry.specialLayerNodeCount,
                    existing_layers = entry.existingLayers.ToArray()
                }).ToArray()
            };
        }

        public static object AnalyzeAssetOrganizer(AIAssetOrganizerConfig config)
        {
            var plan = AIAssetOrganizerPlanner.Build(config);
            return new
            {
                source_root = config.sourceRoot,
                count = plan.entries.Count,
                warnings = plan.warnings.ToArray(),
                entries = plan.entries.Select(entry => new
                {
                    source_path = entry.sourcePath,
                    kind = entry.kind.ToString(),
                    target_path = entry.targetPath,
                    included = entry.include,
                    warning = entry.warning
                }).ToArray()
            };
        }

        static bool IsSupportedMoveSource(UnityEngine.Object asset)
        {
            if (asset == null) return false;
            string path = AssetDatabase.GetAssetPath(asset);
            return !string.IsNullOrEmpty(path) && !AssetDatabase.IsValidFolder(path) &&
                   (asset is GameObject || asset is Material || asset is Texture);
        }
    }
}

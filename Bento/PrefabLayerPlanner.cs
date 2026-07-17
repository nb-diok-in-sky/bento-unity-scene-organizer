using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Bento
{
    public static class PrefabLayerPlanner
    {
        public static List<PrefabLayerPlanEntry> Build(IEnumerable<UnityEngine.Object> selection)
        {
            var document = AssetInfoCollector.Collect(selection);
            return document.assets.Select(BuildEntry).ToList();
        }

        static PrefabLayerPlanEntry BuildEntry(AssetInfoRecord asset)
        {
            string assetPath = ToAssetPath(asset.path);
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            var layers = asset.nodes
                .Select(node => ResolveNodeLayer(root, node.hierarchyPath))
                .Where(layer => layer >= 0)
                .ToList();
            var suggestion = Suggest(asset);
            return new PrefabLayerPlanEntry
            {
                assetPath = assetPath,
                assetGuid = AssetDatabase.AssetPathToGUID(assetPath),
                currentRootLayer = root != null ? LayerMask.LayerToName(root.layer) : string.Empty,
                suggestedLayer = suggestion.layer,
                reason = suggestion.reason,
                confidence = suggestion.confidence,
                include = ProjectLayerCatalog.IsDefined(suggestion.layer),
                nodeCount = layers.Count,
                defaultLayerNodeCount = layers.Count(layer => layer == 0),
                specialLayerNodeCount = layers.Count(layer => layer != 0),
                existingLayers = layers.Select(LayerMask.LayerToName).Where(name => !string.IsNullOrEmpty(name)).Distinct().ToList()
            };
        }

        static (string layer, string reason, float confidence) Suggest(AssetInfoRecord asset)
        {
            string semantic = (asset.originalName + " " + asset.inferredObject).ToLowerInvariant();
            float maxHorizontal = Mathf.Max(asset.boundsSize.x, asset.boundsSize.z);
            return PrefabLayerSemantics.Suggest(semantic, maxHorizontal);
        }

        static int ResolveNodeLayer(GameObject root, string hierarchyPath)
        {
            if (root == null) return -1;
            if (hierarchyPath == root.name) return root.layer;
            string relative = hierarchyPath.StartsWith(root.name + "/", StringComparison.Ordinal)
                ? hierarchyPath.Substring(root.name.Length + 1)
                : hierarchyPath;
            var transform = root.transform.Find(relative);
            return transform != null ? transform.gameObject.layer : -1;
        }

        static string ToAssetPath(string absolutePath)
        {
            string normalized = absolutePath.Replace('\\', '/');
            int index = normalized.LastIndexOf("/Assets/", StringComparison.OrdinalIgnoreCase);
            return index >= 0 ? normalized.Substring(index + 1) : absolutePath;
        }

    }
}

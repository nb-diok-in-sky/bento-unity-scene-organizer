using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Bento
{
    static class PrefabLayerPlanner
    {
        public static List<PrefabLayerPlanEntry> Build(
            IEnumerable<UnityEngine.Object> selection,
            PrefabLayerMatchSettings settings = null)
        {
            var document = AssetInfoCollector.Collect(selection);
            var projectLayers = ProjectLayerCatalog.GetDefinedNames();
            return document.assets.Select(asset => BuildEntry(asset, projectLayers, settings)).ToList();
        }

        static PrefabLayerPlanEntry BuildEntry(
            AssetInfoRecord asset,
            IReadOnlyList<string> projectLayers,
            PrefabLayerMatchSettings settings)
        {
            string assetPath = ToAssetPath(asset.path);
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            var layers = asset.nodes
                .Select(node => ResolveNodeLayer(root, node.hierarchyPath))
                .Where(layer => layer >= 0)
                .ToList();
            string currentRootLayer = root != null ? LayerMask.LayerToName(root.layer) : string.Empty;
            float footprint = Mathf.Max(asset.boundsSize.x, asset.boundsSize.z);
            var suggestion = PrefabLayerMatcher.Suggest(asset.originalName, footprint, projectLayers, settings);
            bool matched = suggestion.Layer != null;
            return new PrefabLayerPlanEntry
            {
                assetPath = assetPath,
                assetGuid = AssetDatabase.AssetPathToGUID(assetPath),
                currentRootLayer = currentRootLayer,
                suggestedLayer = matched ? suggestion.Layer : currentRootLayer,
                reason = suggestion.Reason,
                confidence = suggestion.Confidence,
                include = matched && ProjectLayerCatalog.IsDefined(suggestion.Layer),
                nodeCount = layers.Count,
                defaultLayerNodeCount = layers.Count(layer => layer == 0),
                specialLayerNodeCount = layers.Count(layer => layer != 0),
                existingLayers = layers.Select(LayerMask.LayerToName).Where(name => !string.IsNullOrEmpty(name)).Distinct().ToList()
            };
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

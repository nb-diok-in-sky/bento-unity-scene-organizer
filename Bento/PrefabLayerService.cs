using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Bento
{
    public static class PrefabLayerService
    {
        public static PrefabLayerHistory Apply(IReadOnlyList<PrefabLayerPlanEntry> plan, PrefabLayerApplyMode mode)
        {
            var history = new PrefabLayerHistory();
            foreach (var entry in plan.Where(item => item.include))
            {
                int targetLayer = LayerMask.NameToLayer(entry.suggestedLayer);
                if (targetLayer < 0) throw new InvalidOperationException($"Layer 不存在：{entry.suggestedLayer}");
                string currentPath = AssetDatabase.GUIDToAssetPath(entry.assetGuid);
                if (!string.Equals(currentPath, entry.assetPath, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Prefab 路径或 GUID 已变化，请重新分析：{entry.assetPath}");
                ApplyOne(entry, targetLayer, mode, history);
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return history;
        }

        public static void Restore(PrefabLayerHistory history)
        {
            if (history == null) return;
            foreach (var entry in history.entries)
            {
                string path = AssetDatabase.GUIDToAssetPath(entry.assetGuid);
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    foreach (var change in entry.changes)
                    {
                        var target = Find(root.transform, change.hierarchyPath);
                        if (target != null) target.gameObject.layer = change.oldLayer;
                    }
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        static void ApplyOne(PrefabLayerPlanEntry entry, int targetLayer, PrefabLayerApplyMode mode, PrefabLayerHistory history)
        {
            var root = PrefabUtility.LoadPrefabContents(entry.assetPath);
            var historyEntry = new PrefabLayerHistoryEntry { assetGuid = entry.assetGuid, assetPath = entry.assetPath };
            try
            {
                foreach (var transform in SelectTargets(root, mode))
                {
                    if (transform.gameObject.layer == targetLayer) continue;
                    historyEntry.changes.Add(new PrefabLayerChangeRecord
                    {
                        hierarchyPath = BuildPath(root.transform, transform),
                        oldLayer = transform.gameObject.layer,
                        newLayer = targetLayer
                    });
                    transform.gameObject.layer = targetLayer;
                }
                if (historyEntry.changes.Count > 0)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, entry.assetPath);
                    history.entries.Add(historyEntry);
                }
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        static IEnumerable<Transform> SelectTargets(GameObject root, PrefabLayerApplyMode mode)
        {
            if (mode == PrefabLayerApplyMode.RootOnly) return new[] { root.transform };
            var all = root.GetComponentsInChildren<Transform>(true);
            return mode == PrefabLayerApplyMode.DefaultNodesOnly ? all.Where(item => item.gameObject.layer == 0) : all;
        }

        static Transform Find(Transform root, string path)
        {
            if (path == root.name) return root;
            string relative = path.StartsWith(root.name + "/", StringComparison.Ordinal) ? path.Substring(root.name.Length + 1) : path;
            return root.Find(relative);
        }

        static string BuildPath(Transform root, Transform current)
        {
            string path = current.name;
            while (current != root && current.parent != null) { current = current.parent; path = current.name + "/" + path; }
            return path;
        }
    }
}

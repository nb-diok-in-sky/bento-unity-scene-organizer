using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Bento
{
    static class PrefabLayerService
    {
        public static PrefabLayerHistory Apply(IReadOnlyList<PrefabLayerPlanEntry> plan, PrefabLayerApplyMode mode)
        {
            var history = new PrefabLayerHistory();
            var included = plan.Where(item => item.include).ToList();
            ValidatePlan(included);
            try
            {
                foreach (var entry in included)
                {
                    int targetLayer = LayerMask.NameToLayer(entry.suggestedLayer);
                    ApplyOne(entry, targetLayer, mode, history);
                }
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                return history;
            }
            catch (Exception applyException)
            {
                try { Restore(history); }
                catch (Exception restoreException)
                {
                    throw new AggregateException(
                        "Prefab Layer 修改失败，且已修改项的自动恢复未完成。",
                        applyException,
                        restoreException);
                }
                throw;
            }
        }

        public static void Restore(PrefabLayerHistory history)
        {
            if (history == null) return;
            ValidateHistory(history);
            ValidateHistoryTargets(history);
            foreach (var entry in history.entries)
            {
                string path = AssetDatabase.GUIDToAssetPath(entry.assetGuid);
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    foreach (var change in entry.changes)
                    {
                        var target = Find(root.transform, change);
                        if (target == null)
                            throw new InvalidOperationException($"Prefab 结构已变化，无法安全恢复 Layer：{path} / {change.hierarchyPath}");
                        target.gameObject.layer = change.oldLayer;
                    }
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        static void ValidateHistory(PrefabLayerHistory history)
        {
            foreach (var entry in history.entries)
            {
                string currentPath = AssetDatabase.GUIDToAssetPath(entry.assetGuid);
                if (string.IsNullOrEmpty(currentPath))
                    throw new InvalidOperationException("找不到待恢复 Prefab GUID：" + entry.assetGuid);
                if (!string.Equals(currentPath, entry.assetPath, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Prefab 恢复路径已变化：{entry.assetPath} → {currentPath}");
            }
        }

        static void ValidateHistoryTargets(PrefabLayerHistory history)
        {
            foreach (var entry in history.entries)
            {
                string path = AssetDatabase.GUIDToAssetPath(entry.assetGuid);
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    foreach (var change in entry.changes)
                        if (Find(root.transform, change) == null)
                            throw new InvalidOperationException($"Prefab 结构已变化，无法安全恢复 Layer：{path} / {change.hierarchyPath}");
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
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
                        siblingIndexPath = BuildSiblingIndexPath(root.transform, transform),
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

        static void ValidatePlan(IReadOnlyList<PrefabLayerPlanEntry> entries)
        {
            if (entries.Count == 0) throw new InvalidOperationException("没有勾选任何 Prefab Layer 计划");
            foreach (var entry in entries)
            {
                if (!ProjectLayerCatalog.IsDefined(entry.suggestedLayer)) throw new InvalidOperationException($"Layer 不存在：{entry.suggestedLayer}");
                string currentPath = AssetDatabase.GUIDToAssetPath(entry.assetGuid);
                if (!string.Equals(currentPath, entry.assetPath, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Prefab 路径或 GUID 已变化，请重新分析：{entry.assetPath}");
            }
        }

        static Transform Find(Transform root, PrefabLayerChangeRecord change)
        {
            if (!string.IsNullOrEmpty(change.siblingIndexPath))
            {
                Transform current = root;
                foreach (string segment in change.siblingIndexPath.Split('/'))
                {
                    if (!int.TryParse(segment, out int index) || index < 0 || index >= current.childCount) { current = null; break; }
                    current = current.GetChild(index);
                }
                if (current != null) return current;
            }
            if (change.hierarchyPath == root.name) return root;
            string relative = change.hierarchyPath.StartsWith(root.name + "/", StringComparison.Ordinal)
                ? change.hierarchyPath.Substring(root.name.Length + 1)
                : change.hierarchyPath;
            return root.Find(relative);
        }

        static string BuildPath(Transform root, Transform current)
        {
            string path = current.name;
            while (current != root && current.parent != null) { current = current.parent; path = current.name + "/" + path; }
            return path;
        }

        static string BuildSiblingIndexPath(Transform root, Transform current)
        {
            if (current == root) return string.Empty;
            var indices = new Stack<int>();
            while (current != root && current.parent != null)
            {
                indices.Push(current.GetSiblingIndex());
                current = current.parent;
            }
            return string.Join("/", indices);
        }
    }
}

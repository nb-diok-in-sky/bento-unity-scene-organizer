using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace Bento
{
    public static class AssetReferenceAuditService
    {
        static readonly Dictionary<string, string[]> DependencyCache = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        public static void Audit(IReadOnlyList<AssetMoveEntry> entries)
        {
            if (entries == null || entries.Count == 0) return;

            var targets = new HashSet<string>(
                entries.Where(entry => entry.kind == AssetMoveKind.Material || entry.kind == AssetMoveKind.Texture)
                    .Select(entry => entry.sourcePath),
                StringComparer.OrdinalIgnoreCase);
            if (targets.Count == 0) return;

            var targetEntries = entries
                .Where(entry => targets.Contains(entry.sourcePath))
                .ToDictionary(entry => entry.sourcePath, StringComparer.OrdinalIgnoreCase);

            var ownerPaths = EnumeratePotentialOwners().ToList();
            bool cancelled = false;
            try
            {
                for (int index = 0; index < ownerPaths.Count; index++)
                {
                    string ownerPath = ownerPaths[index];
                    if (EditorUtility.DisplayCancelableProgressBar(
                        "Bento 引用审计",
                        ownerPath,
                        (float)index / Math.Max(1, ownerPaths.Count)))
                    {
                        cancelled = true;
                        break;
                    }
                    foreach (string dependencyPath in GetDependencies(ownerPath))
                    {
                        if (!targetEntries.TryGetValue(dependencyPath, out var entry)) continue;
                        CountReference(entry, ownerPath);
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            foreach (var entry in targetEntries.Values)
            {
                entry.keepOriginalNameBecauseShared = cancelled || (entry.kind == AssetMoveKind.Material
                    ? entry.prefabReferenceCount > 1 || entry.sceneReferenceCount > 0
                    : entry.materialReferenceCount > 1 || entry.prefabReferenceCount > 1 || entry.sceneReferenceCount > 0);
            }
        }

        static IEnumerable<string> EnumeratePotentialOwners()
        {
            var guids = AssetDatabase.FindAssets("t:Prefab")
                .Concat(AssetDatabase.FindAssets("t:Material"))
                .Concat(AssetDatabase.FindAssets("t:Scene"))
                .Distinct();
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrEmpty(path)) yield return path;
            }
        }

        static string[] GetDependencies(string ownerPath)
        {
            if (!DependencyCache.TryGetValue(ownerPath, out var dependencies))
            {
                dependencies = AssetDatabase.GetDependencies(ownerPath, recursive: true);
                DependencyCache[ownerPath] = dependencies;
            }
            return dependencies;
        }

        static void CountReference(AssetMoveEntry entry, string ownerPath)
        {
            if (string.Equals(ownerPath, entry.sourcePath, StringComparison.OrdinalIgnoreCase)) return;
            if (ownerPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)) entry.prefabReferenceCount++;
            else if (ownerPath.EndsWith(".mat", StringComparison.OrdinalIgnoreCase)) entry.materialReferenceCount++;
            else if (ownerPath.EndsWith(".unity", StringComparison.OrdinalIgnoreCase)) entry.sceneReferenceCount++;

            if (entry.referenceSamples.Count < 8 && !entry.referenceSamples.Contains(ownerPath))
                entry.referenceSamples.Add(ownerPath);
        }
    }
}

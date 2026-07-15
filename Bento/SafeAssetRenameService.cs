using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;

namespace Bento
{
    [Serializable]
    public class AssetRenameEntry
    {
        public string sourcePath;
        public string targetPath;
        public string sourceGuid;
        public bool include = true;
        public string warning;
    }

    [Serializable]
    public class AssetRenameHistory
    {
        public List<AssetMoveHistoryEntry> renames = new List<AssetMoveHistoryEntry>();
    }

    public static class SafeAssetRenameService
    {
        public static AssetRenameEntry BuildEntry(string sourcePath, string newName)
        {
            string extension = Path.GetExtension(sourcePath);
            string folder = Path.GetDirectoryName(sourcePath)?.Replace('\\', '/');
            string currentName = Path.GetFileNameWithoutExtension(sourcePath);
            string safeName = BentoUtility.SanitizeFileName(newName, currentName);
            var entry = new AssetRenameEntry
            {
                sourcePath = sourcePath,
                targetPath = $"{folder}/{safeName}{extension}",
                sourceGuid = AssetDatabase.AssetPathToGUID(sourcePath)
            };
            entry.warning = ValidateEntry(entry);
            return entry;
        }

        public static void Execute(IReadOnlyList<AssetRenameEntry> entries, AssetRenameHistory history)
        {
            if (history == null) throw new ArgumentNullException(nameof(history));
            var included = entries.Where(entry => entry.include).ToList();
            ValidatePlanOrThrow(included);
            history.renames.Clear();

            try
            {
                foreach (var entry in included)
                {
                    string error = AssetDatabase.MoveAsset(entry.sourcePath, entry.targetPath);
                    if (!string.IsNullOrEmpty(error))
                        throw new InvalidOperationException($"重命名失败：{entry.sourcePath} → {entry.targetPath}\n{error}");

                    history.renames.Add(new AssetMoveHistoryEntry
                    {
                        guid = entry.sourceGuid,
                        oldPath = entry.sourcePath,
                        newPath = entry.targetPath
                    });
                    VerifyGuidAndLoad(entry.sourceGuid, entry.targetPath);
                }
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
            catch (Exception renameException)
            {
                try
                {
                    Restore(history);
                    history.renames.Clear();
                }
                catch (Exception rollbackException)
                {
                    throw new AggregateException(
                        "资产重命名失败，且自动回滚未完成。历史已保留，请立即人工恢复。",
                        renameException,
                        rollbackException);
                }
                throw;
            }
        }

        public static void Restore(AssetRenameHistory history)
        {
            if (history?.renames == null) return;
            foreach (var rename in history.renames.AsEnumerable().Reverse())
            {
                string currentPath = AssetDatabase.GUIDToAssetPath(rename.guid);
                if (string.IsNullOrEmpty(currentPath))
                    throw new InvalidOperationException($"无法找到待恢复资产 GUID：{rename.guid}");
                string error = AssetDatabase.MoveAsset(currentPath, rename.oldPath);
                if (!string.IsNullOrEmpty(error))
                    throw new InvalidOperationException($"恢复重命名失败：{currentPath} → {rename.oldPath}\n{error}");
                VerifyGuidAndLoad(rename.guid, rename.oldPath);
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        static string ValidateEntry(AssetRenameEntry entry)
        {
            if (string.IsNullOrEmpty(entry.sourceGuid)) return "资产没有有效 GUID";
            if (!entry.sourcePath.StartsWith("Assets/", StringComparison.Ordinal)) return "只允许重命名 Assets/ 下的资产";
            if (AssetDatabase.IsValidFolder(entry.sourcePath)) return "该服务不重命名文件夹";
            if (entry.sourcePath.EndsWith(".unity", StringComparison.OrdinalIgnoreCase) ||
                entry.sourcePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                entry.sourcePath.EndsWith(".asmdef", StringComparison.OrdinalIgnoreCase))
                return "禁止重命名场景、脚本或程序集定义";
            if (string.Equals(entry.sourcePath, entry.targetPath, StringComparison.OrdinalIgnoreCase)) return "名称没有变化";
            if (!string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(entry.targetPath))) return "目标文件名已存在";
            return string.Empty;
        }

        static void ValidatePlanOrThrow(IReadOnlyList<AssetRenameEntry> entries)
        {
            if (entries.Count == 0) throw new InvalidOperationException("没有需要重命名的资产");
            var duplicateTarget = entries
                .GroupBy(entry => entry.targetPath, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(group => group.Count() > 1);
            if (duplicateTarget != null)
                throw new InvalidOperationException($"重命名计划存在重复目标：{duplicateTarget.Key}");

            var sourcePaths = new HashSet<string>(entries.Select(entry => entry.sourcePath), StringComparer.OrdinalIgnoreCase);
            var chained = entries.FirstOrDefault(entry => sourcePaths.Contains(entry.targetPath));
            if (chained != null)
                throw new InvalidOperationException($"目标名同时是另一资产的当前名称，禁止顺序依赖重命名：{chained.targetPath}");

            var invalid = entries.FirstOrDefault(entry => !string.IsNullOrEmpty(entry.warning));
            if (invalid != null)
                throw new InvalidOperationException($"重命名计划无效：{invalid.sourcePath}\n{invalid.warning}");
        }

        static void VerifyGuidAndLoad(string expectedGuid, string path)
        {
            if (!string.Equals(AssetDatabase.AssetPathToGUID(path), expectedGuid, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"GUID 验证失败：{path}");
            if (AssetDatabase.LoadMainAssetAtPath(path) == null)
                throw new InvalidOperationException($"重命名后资产无法加载：{path}");
        }
    }
}

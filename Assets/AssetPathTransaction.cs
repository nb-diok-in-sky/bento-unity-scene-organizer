using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;

namespace Bento
{
    sealed class AssetPathTransaction
    {
        readonly IAssetPathMover _mover;

        public AssetPathTransaction() : this(new UnityAssetPathMover()) { }
        internal AssetPathTransaction(IAssetPathMover mover) => _mover = mover ?? throw new ArgumentNullException(nameof(mover));

        public void Execute(
            IEnumerable<AssetMoveHistoryEntry> operations,
            ICollection<AssetMoveHistoryEntry> completed,
            string failureMessage)
        {
            if (operations == null) throw new ArgumentNullException(nameof(operations));
            if (completed == null) throw new ArgumentNullException(nameof(completed));
            var pending = operations.ToList();
            ValidateOperations(pending);
            completed.Clear();

            try
            {
                foreach (var operation in pending)
                {
                    MoveAndVerify(operation.oldPath, operation.newPath, operation.guid);
                    completed.Add(operation);
                }
                Save();
            }
            catch (Exception operationException)
            {
                try
                {
                    Restore(completed);
                    completed.Clear();
                }
                catch (Exception rollbackException)
                {
                    throw new AggregateException(failureMessage, operationException, rollbackException);
                }
                throw;
            }
        }

        static void ValidateOperations(IReadOnlyList<AssetMoveHistoryEntry> operations)
        {
            if (operations.Count == 0) throw new InvalidOperationException("没有需要执行的资产路径操作");
            var duplicateGuid = operations.Where(operation => operation != null)
                .GroupBy(operation => operation.guid, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(group => !string.IsNullOrWhiteSpace(group.Key) && group.Count() > 1);
            if (duplicateGuid != null) throw new InvalidOperationException("同一 GUID 在本批路径操作中出现多次：" + duplicateGuid.Key);
            var duplicateSource = operations.Where(operation => operation != null)
                .GroupBy(operation => operation.oldPath, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(group => !string.IsNullOrWhiteSpace(group.Key) && group.Count() > 1);
            if (duplicateSource != null) throw new InvalidOperationException("同一源路径在本批路径操作中出现多次：" + duplicateSource.Key);
            foreach (var operation in operations)
            {
                if (operation == null || string.IsNullOrWhiteSpace(operation.guid))
                    throw new InvalidOperationException("资产路径操作缺少 GUID");
                if (string.IsNullOrWhiteSpace(operation.oldPath) || string.IsNullOrWhiteSpace(operation.newPath))
                    throw new InvalidOperationException("资产路径操作缺少源路径或目标路径");
                if (!string.Equals(AssetDatabase.AssetPathToGUID(operation.oldPath), operation.guid, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"源路径与 GUID 不匹配：{operation.oldPath}");
            }
        }

        public void Restore(IEnumerable<AssetMoveHistoryEntry> completed)
        {
            if (completed == null) return;
            foreach (var operation in completed.Reverse())
            {
                string currentPath = AssetDatabase.GUIDToAssetPath(operation.guid);
                if (string.IsNullOrEmpty(currentPath))
                    throw new InvalidOperationException($"无法找到 GUID：{operation.guid}");
                MoveAndVerify(currentPath, operation.oldPath, operation.guid);
            }
            Save();
        }

        void MoveAndVerify(string sourcePath, string targetPath, string expectedGuid)
        {
            EnsureFolder(Path.GetDirectoryName(targetPath)?.Replace('\\', '/'));
            string error = _mover.Move(sourcePath, targetPath);
            if (!string.IsNullOrEmpty(error))
                throw new InvalidOperationException($"资产路径修改失败：{sourcePath} → {targetPath}\n{error}");
            Verify(expectedGuid, targetPath);
        }

        static void EnsureFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder) || AssetDatabase.IsValidFolder(folder)) return;
            string parent = Path.GetDirectoryName(folder)?.Replace('\\', '/');
            EnsureFolder(parent);
            if (!AssetDatabase.IsValidFolder(parent))
                throw new InvalidOperationException($"无法创建目标目录，父目录不存在：{folder}");
            string guid = AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
            if (string.IsNullOrEmpty(guid))
                throw new InvalidOperationException($"无法创建目标目录：{folder}");
        }

        static void Verify(string expectedGuid, string path)
        {
            if (!string.Equals(AssetDatabase.AssetPathToGUID(path), expectedGuid, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"GUID 验证失败：{path}");
            if (!AssetDatabase.IsValidFolder(path) && AssetDatabase.LoadMainAssetAtPath(path) == null)
                throw new InvalidOperationException($"资产无法加载：{path}");
        }

        static void Save()
        {
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }
    }

    interface IAssetPathMover
    {
        string Move(string sourcePath, string targetPath);
    }

    sealed class UnityAssetPathMover : IAssetPathMover
    {
        public string Move(string sourcePath, string targetPath) => AssetDatabase.MoveAsset(sourcePath, targetPath);
    }
}

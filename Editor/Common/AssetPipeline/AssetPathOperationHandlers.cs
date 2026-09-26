using System;
using System.Collections.Generic;
using System.Linq;

namespace Bento
{
    /// <summary>
    /// Shared conversion between path-change histories (move / rename) and persisted operation records.
    /// Both operations store the same GUID + before/after path pairs and are undone by moving back.
    /// </summary>
    abstract class AssetPathOperationHandler : IRecoverableAssetOperation
    {
        public abstract string OperationType { get; }

        public void Restore(AssetOperationRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            Restore(record.items.Select(item => new AssetMoveHistoryEntry
            {
                guid = item.assetGuid,
                oldPath = item.beforePath,
                newPath = item.afterPath
            }).ToList(), record.createdAtUtc);
        }

        protected abstract void Restore(List<AssetMoveHistoryEntry> entries, string createdAtUtc);

        protected static AssetOperationRecord CreateRecord(
            string operationType,
            string label,
            IReadOnlyList<AssetMoveHistoryEntry> entries,
            string createdAtUtc)
        {
            if (entries == null || entries.Count == 0)
                throw new InvalidOperationException($"没有可记录的{label}历史。");
            return new AssetOperationRecord
            {
                id = Guid.NewGuid().ToString("N"),
                operationType = operationType,
                displayName = $"{label}（{entries.Count} 项）",
                createdAtUtc = string.IsNullOrEmpty(createdAtUtc) ? DateTime.UtcNow.ToString("O") : createdAtUtc,
                items = entries.Select(entry => new AssetOperationItem
                {
                    assetGuid = entry.guid,
                    beforePath = entry.oldPath,
                    afterPath = entry.newPath
                }).ToList()
            };
        }
    }

    sealed class AssetMoveOperationHandler : AssetPathOperationHandler
    {
        public const string Type = "asset_move";
        public override string OperationType => Type;

        protected override void Restore(List<AssetMoveHistoryEntry> entries, string createdAtUtc) =>
            SafeAssetMoveService.Restore(new AssetMoveHistory { createdAtUtc = createdAtUtc, moves = entries });

        public static AssetOperationRecord CreateRecord(AssetMoveHistory history) =>
            CreateRecord(Type, "资产移动", history?.moves, history?.createdAtUtc);
    }

    sealed class AssetRenameOperationHandler : AssetPathOperationHandler
    {
        public const string Type = "asset_rename";
        public override string OperationType => Type;

        protected override void Restore(List<AssetMoveHistoryEntry> entries, string createdAtUtc) =>
            SafeAssetRenameService.Restore(new AssetRenameHistory { renames = entries });

        public static AssetOperationRecord CreateRecord(AssetRenameHistory history) =>
            CreateRecord(Type, "资产重命名", history?.renames, null);
    }
}

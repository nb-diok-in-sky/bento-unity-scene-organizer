using System;
using System.Linq;

namespace Bento
{
    public sealed class AssetRenameOperationHandler : IRecoverableAssetOperation
    {
        public const string Type = "asset_rename";
        public string OperationType => Type;

        public void Restore(AssetOperationRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            SafeAssetRenameService.Restore(new AssetRenameHistory
            {
                renames = record.items.Select(item => new AssetMoveHistoryEntry
                {
                    guid = item.assetGuid,
                    oldPath = item.beforePath,
                    newPath = item.afterPath
                }).ToList()
            });
        }

        public static AssetOperationRecord CreateRecord(AssetRenameHistory history)
        {
            if (history?.renames == null || history.renames.Count == 0)
                throw new InvalidOperationException("没有可记录的重命名历史。");
            return new AssetOperationRecord
            {
                id = Guid.NewGuid().ToString("N"),
                operationType = Type,
                displayName = $"资产重命名（{history.renames.Count} 项）",
                createdAtUtc = DateTime.UtcNow.ToString("O"),
                items = history.renames.Select(rename => new AssetOperationItem
                {
                    assetGuid = rename.guid,
                    beforePath = rename.oldPath,
                    afterPath = rename.newPath
                }).ToList()
            };
        }
    }
}

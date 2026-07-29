using System;
using System.Linq;

namespace Bento
{
    public sealed class AssetMoveOperationHandler : IRecoverableAssetOperation
    {
        public const string Type = "asset_move";
        public string OperationType => Type;

        public void Restore(AssetOperationRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            var history = new AssetMoveHistory
            {
                createdAtUtc = record.createdAtUtc,
                moves = record.items.Select(item => new AssetMoveHistoryEntry
                {
                    guid = item.assetGuid,
                    oldPath = item.beforePath,
                    newPath = item.afterPath
                }).ToList()
            };
            SafeAssetMoveService.Restore(history);
        }

        public static AssetOperationRecord CreateRecord(AssetMoveHistory history)
        {
            if (history?.moves == null || history.moves.Count == 0)
                throw new InvalidOperationException("没有可记录的移动历史。");
            return new AssetOperationRecord
            {
                id = Guid.NewGuid().ToString("N"),
                operationType = Type,
                displayName = $"资产移动（{history.moves.Count} 项）",
                createdAtUtc = string.IsNullOrEmpty(history.createdAtUtc) ? DateTime.UtcNow.ToString("O") : history.createdAtUtc,
                items = history.moves.Select(move => new AssetOperationItem
                {
                    assetGuid = move.guid,
                    beforePath = move.oldPath,
                    afterPath = move.newPath
                }).ToList()
            };
        }
    }
}

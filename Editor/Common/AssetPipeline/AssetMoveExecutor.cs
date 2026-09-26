using System;
using System.Collections.Generic;
using System.Linq;

namespace Bento
{
    sealed class AssetMoveExecutor
    {
        readonly CompositeAssetMovePolicy _policy;
        readonly AssetPathTransaction _transaction;

        public AssetMoveExecutor(CompositeAssetMovePolicy policy, AssetPathTransaction transaction)
        {
            _policy = policy;
            _transaction = transaction;
        }

        public void Execute(IReadOnlyList<AssetMoveEntry> entries, AssetMoveHistory history)
        {
            List<AssetMoveEntry> included = entries.Where(entry => entry.include).ToList();
            _policy.ValidatePlan(included);
            if (history == null) throw new ArgumentNullException(nameof(history));

            history.createdAtUtc = DateTime.UtcNow.ToString("O");
            IEnumerable<AssetMoveHistoryEntry> operations = included.Select(entry => new AssetMoveHistoryEntry
            {
                guid = entry.sourceGuid,
                oldPath = entry.sourcePath,
                newPath = entry.targetPath
            });
            _transaction.Execute(
                operations,
                history.moves,
                "资源移动失败，且自动回滚未完成。历史已保留，请立即人工恢复。");
        }

        public void Restore(AssetMoveHistory history)
        {
            _transaction.Restore(history?.moves);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;

namespace Bento
{
    public interface IRecoverableAssetOperation
    {
        string OperationType { get; }
        void Restore(AssetOperationRecord record);
    }

    public sealed class AssetOperationHistoryService
    {
        readonly IAssetOperationHistoryRepository _repository;
        readonly Dictionary<string, IRecoverableAssetOperation> _handlers;

        public AssetOperationHistoryService(
            IAssetOperationHistoryRepository repository,
            IEnumerable<IRecoverableAssetOperation> handlers)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _handlers = handlers.ToDictionary(handler => handler.OperationType, StringComparer.OrdinalIgnoreCase);
        }

        public IReadOnlyList<AssetOperationRecord> LoadAll() => _repository.LoadAll();

        public void Record(AssetOperationRecord record) => _repository.Save(record);

        public void Restore(string id)
        {
            var record = _repository.Load(id) ?? throw new InvalidOperationException("找不到操作历史：" + id);
            if (record.restored) throw new InvalidOperationException("该操作已经恢复过。不能重复恢复");
            if (!_handlers.TryGetValue(record.operationType, out var handler))
                throw new InvalidOperationException("当前版本不支持恢复此操作：" + record.operationType);

            handler.Restore(record);
            record.restored = true;
            record.restoredAtUtc = DateTime.UtcNow.ToString("O");
            _repository.Save(record);
        }
    }

    public static class AssetOperationHistory
    {
        static readonly AssetOperationHistoryService Service = new AssetOperationHistoryService(
            new AssetOperationHistoryRepository(),
            new IRecoverableAssetOperation[] { new AssetMoveOperationHandler() });

        public static IReadOnlyList<AssetOperationRecord> LoadAll() => Service.LoadAll();
        public static void Record(AssetOperationRecord record) => Service.Record(record);
        public static void Restore(string id) => Service.Restore(id);
    }
}

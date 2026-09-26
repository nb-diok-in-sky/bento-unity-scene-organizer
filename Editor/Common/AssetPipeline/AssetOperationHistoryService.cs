using System;
using System.Collections.Generic;
using System.Linq;

namespace Bento
{
    interface IRecoverableAssetOperation
    {
        string OperationType { get; }
        void Restore(AssetOperationRecord record);
    }

    sealed class AssetOperationHistoryService
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

        public bool TryRecord(AssetOperationRecord record, out string warning)
        {
            try
            {
                Record(record);
                warning = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                warning = "操作历史保存失败，请不要关闭工具箱；当前窗口仍可恢复上次操作。";
                UnityEngine.Debug.LogError("[Bento] 资产操作已成功，但持久历史保存失败：\n" + exception);
                return false;
            }
        }

        public void Restore(string id)
        {
            var record = _repository.Load(id) ?? throw new InvalidOperationException("找不到操作历史：" + id);
            if (record.restored) throw new InvalidOperationException("该操作已经恢复过。不能重复恢复");
            if (!_handlers.TryGetValue(record.operationType, out var handler))
                throw new InvalidOperationException("当前版本不支持恢复此操作：" + record.operationType);

            handler.Restore(record);
            record.restored = true;
            record.restoredAtUtc = DateTime.UtcNow.ToString("O");
            try
            {
                _repository.Save(record);
            }
            catch (Exception exception)
            {
                throw new AssetOperationHistoryStateException(exception, record);
            }
        }
    }

    sealed class AssetOperationHistoryStateException : Exception
    {
        public AssetOperationRecord Record { get; }

        public AssetOperationHistoryStateException(Exception innerException, AssetOperationRecord record)
            : base("资产已恢复成功，但操作历史状态保存失败。请刷新历史；如果仍显示未恢复，不要重复执行，先检查资产路径。\n" +
                   innerException.Message,
                innerException)
        {
            Record = record;
        }
    }

    static class AssetOperationHistory
    {
        static readonly AssetOperationHistoryService Service = new AssetOperationHistoryService(
            new AssetOperationHistoryRepository(),
            new IRecoverableAssetOperation[]
            {
                new AssetMoveOperationHandler(),
                new AssetRenameOperationHandler()
            });

        public static IReadOnlyList<AssetOperationRecord> LoadAll() => Service.LoadAll();
        public static void Record(AssetOperationRecord record) => Service.Record(record);
        public static bool TryRecord(AssetOperationRecord record, out string warning) => Service.TryRecord(record, out warning);
        public static void Restore(string id) => Service.Restore(id);
    }
}

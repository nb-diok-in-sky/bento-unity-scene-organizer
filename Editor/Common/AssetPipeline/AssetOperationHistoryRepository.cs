using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Bento
{
    interface IAssetOperationHistoryRepository
    {
        void Save(AssetOperationRecord record);
        IReadOnlyList<AssetOperationRecord> LoadAll();
        AssetOperationRecord Load(string id);
    }

    sealed class AssetOperationHistoryRepository : IAssetOperationHistoryRepository
    {
        static readonly string DirectoryPath = Path.GetFullPath(Path.Combine(Application.dataPath, "../Library/Bento/OperationHistory"));

        public void Save(AssetOperationRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            if (string.IsNullOrEmpty(record.id)) record.id = Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(DirectoryPath);
            WriteAtomically(GetPath(record.id), JsonUtility.ToJson(record, true));
        }

        public IReadOnlyList<AssetOperationRecord> LoadAll()
        {
            if (!Directory.Exists(DirectoryPath)) return Array.Empty<AssetOperationRecord>();
            return Directory.GetFiles(DirectoryPath, "*.json")
                .Select(LoadFile)
                .Where(record => record != null)
                .OrderByDescending(record => record.createdAtUtc)
                .ToList();
        }

        public AssetOperationRecord Load(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            string path = GetPath(id);
            return File.Exists(path) ? LoadFile(path) : null;
        }

        static AssetOperationRecord LoadFile(string path)
        {
            try { return JsonUtility.FromJson<AssetOperationRecord>(File.ReadAllText(path)); }
            catch (Exception exception)
            {
                Debug.LogWarning($"[Bento] 无法读取资产操作历史：{path}\n{exception.Message}");
                return null;
            }
        }

        static string GetPath(string id)
        {
            string safeId = BentoUtility.SanitizeFileName(id, "history");
            return Path.Combine(DirectoryPath, safeId + ".json");
        }

        static void WriteAtomically(string path, string content)
        {
            string temporaryPath = path + ".tmp";
            File.WriteAllText(temporaryPath, content, new System.Text.UTF8Encoding(false));
            try
            {
                if (File.Exists(path)) File.Replace(temporaryPath, path, null);
                else File.Move(temporaryPath, path);
            }
            finally
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
        }
    }
}

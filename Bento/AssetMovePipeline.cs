using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Bento
{
    interface IAssetMovePlanner
    {
        List<AssetMoveEntry> Build(
            IEnumerable<UnityEngine.Object> assets,
            IReadOnlyDictionary<AssetMoveKind, string> targetFolders,
            AssetNamingOptions naming);
    }

    interface IAssetMovePolicy
    {
        string ValidateEntry(AssetMoveEntry entry);
        void ValidatePlan(IReadOnlyList<AssetMoveEntry> entries);
    }

    interface IAssetMoveExecutor
    {
        void Execute(IReadOnlyList<AssetMoveEntry> entries, AssetMoveHistory history);
        void Restore(AssetMoveHistory history);
    }

    sealed class AssetMovePlanner : IAssetMovePlanner
    {
        readonly IAssetMovePolicy _policy;

        public AssetMovePlanner(IAssetMovePolicy policy) => _policy = policy;

        public List<AssetMoveEntry> Build(
            IEnumerable<UnityEngine.Object> assets,
            IReadOnlyDictionary<AssetMoveKind, string> targetFolders,
            AssetNamingOptions naming)
        {
            var entries = new List<AssetMoveEntry>();
            var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var asset in assets.Where(asset => asset != null))
                AddSelectedAsset(asset, AssetDatabase.GetAssetPath(asset), entries, seenPaths);

            ApplyTargets(entries, targetFolders, naming);
            foreach (var entry in entries)
            {
                entry.warning = _policy.ValidateEntry(entry);
                if (entry.isDependency && !string.IsNullOrEmpty(entry.warning)) entry.include = false;
            }
            return entries;
        }

        public static void ApplyTargets(
            IReadOnlyList<AssetMoveEntry> entries,
            IReadOnlyDictionary<AssetMoveKind, string> targetFolders,
            AssetNamingOptions naming)
        {
            var reservedTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < entries.Count; index++)
            {
                var entry = entries[index];
                entry.targetFolder = targetFolders.TryGetValue(entry.kind, out string folder) ? folder : string.Empty;
                string extension = Path.GetExtension(entry.sourcePath);
                string originalName = Path.GetFileNameWithoutExtension(entry.sourcePath);
                string targetName = AssetNaming.BuildName(originalName, 0, WithoutNumber(naming));
                entry.targetPath = BuildTargetPath(entry, folder, targetName, extension);
                if (naming?.appendNumber == true)
                    entry.targetPath = FindAvailableNumberedPath(entry, folder, targetName, extension, reservedTargets);
                reservedTargets.Add(entry.targetPath);
            }
        }

        static AssetNamingOptions WithoutNumber(AssetNamingOptions source)
        {
            return new AssetNamingOptions
            {
                prefix = source?.prefix ?? string.Empty,
                baseName = source?.baseName ?? string.Empty,
                suffix = source?.suffix ?? string.Empty,
                appendNumber = false
            };
        }

        static string FindAvailableNumberedPath(
            AssetMoveEntry entry,
            string folder,
            string baseName,
            string extension,
            ISet<string> reservedTargets)
        {
            if (string.IsNullOrEmpty(folder)) return string.Empty;
            for (int number = 1; number < 1000000; number++)
            {
                string candidate = BuildTargetPath(entry, folder, baseName + number.ToString("D2"), extension);
                if (!reservedTargets.Contains(candidate) && string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(candidate)))
                    return candidate;
            }
            throw new InvalidOperationException($"无法为资源生成可用编号：{entry.sourcePath}");
        }

        static string BuildTargetPath(AssetMoveEntry entry, string folder, string targetName, string extension)
        {
            if (string.IsNullOrEmpty(folder)) return string.Empty;
            return entry.kind == AssetMoveKind.Folder
                ? $"{folder.TrimEnd('/')}/{targetName}"
                : $"{folder.TrimEnd('/')}/{targetName}{extension}";
        }

        static void AddSelectedAsset(
            UnityEngine.Object asset,
            string path,
            ICollection<AssetMoveEntry> entries,
            ISet<string> seenPaths)
        {
            if (string.IsNullOrEmpty(path) || !seenPaths.Add(path)) return;
            AssetMoveKind kind = Classify(asset, path);
            entries.Add(new AssetMoveEntry
            {
                asset = asset,
                sourcePath = path,
                sourceGuid = AssetDatabase.AssetPathToGUID(path),
                kind = kind,
                isDependency = false,
                ownerPath = string.Empty
            });
        }

        static AssetMoveKind Classify(UnityEngine.Object asset, string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return AssetMoveKind.Folder;
            if (asset is Material) return AssetMoveKind.Material;
            if (asset is Texture) return AssetMoveKind.Texture;
            if (asset is GameObject && new[] { ".prefab", ".fbx", ".obj" }.Any(extension => path.EndsWith(extension, StringComparison.OrdinalIgnoreCase)))
                return AssetMoveKind.PrefabOrModel;
            return AssetMoveKind.Other;
        }
    }

    sealed class AssetMoveSafetyPolicy : IAssetMovePolicy
    {
        enum AddressablePathStatus { Safe, PathBased, Unknown }
        public string ValidateEntry(AssetMoveEntry entry)
        {
            if (string.IsNullOrEmpty(entry.sourceGuid)) return "资源没有有效 GUID";
            if (!entry.sourcePath.StartsWith("Assets/", StringComparison.Ordinal)) return "只允许移动 Assets/ 下资源";
            if (string.Equals(entry.sourcePath, "Assets", StringComparison.OrdinalIgnoreCase)) return "禁止移动 Assets 根目录";
            var mainAsset = AssetDatabase.LoadMainAssetAtPath(entry.sourcePath);
            if (entry.asset != null && mainAsset != null && entry.asset != mainAsset) return "FBX/模型内嵌子资源不能单独移动，请移动模型主资产";
            string modelMaterialWarning = ValidateModelMaterialRemaps(entry.sourcePath);
            if (!string.IsNullOrEmpty(modelMaterialWarning)) return modelMaterialWarning;
            if (!BentoUtility.IsProjectFolder(entry.targetFolder)) return "目标文件夹无效";
            if (entry.kind == AssetMoveKind.Folder && entry.targetFolder.StartsWith(entry.sourcePath.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase))
                return "禁止把文件夹移动到自己的子目录";
            if (string.Equals(entry.sourcePath, entry.targetPath, StringComparison.OrdinalIgnoreCase)) return "源路径和目标路径相同";
            if (!string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(entry.targetPath))) return "目标路径已存在资源";
            AddressablePathStatus addressable = GetAddressablePathStatus(entry.sourceGuid, entry.sourcePath);
            if (addressable == AddressablePathStatus.PathBased) return "Addressables 地址等于资产路径，移动会改变运行时地址";
            if (addressable == AddressablePathStatus.Unknown) return "Addressables 风险检查失败，安全起见禁止移动";
            if (new[] { ".unity", ".cs", ".asmdef" }.Any(extension => entry.sourcePath.EndsWith(extension, StringComparison.OrdinalIgnoreCase)))
                return "禁止移动场景、脚本或程序集定义";
            return string.Empty;
        }

        static string ValidateModelMaterialRemaps(string sourcePath)
        {
            if (!sourcePath.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase) &&
                !sourcePath.EndsWith(".obj", StringComparison.OrdinalIgnoreCase))
                return string.Empty;

            var importer = AssetImporter.GetAtPath(sourcePath) as ModelImporter;
            if (importer == null) return "无法读取模型导入设置，禁止移动";

            foreach (var pair in importer.GetExternalObjectMap())
            {
                if (pair.Key.type != typeof(Material)) continue;
                if (pair.Value == null || string.IsNullOrEmpty(AssetDatabase.GetAssetPath(pair.Value)))
                    return $"模型材质映射已失效（槽位：{pair.Key.name}）。移动会触发重导入并可能生成错误材质，请先修复材质映射";
            }

            return string.Empty;
        }

        public void ValidatePlan(IReadOnlyList<AssetMoveEntry> entries)
        {
            if (entries.Count == 0) throw new InvalidOperationException("没有勾选任何要移动的资源");
            var duplicate = entries.GroupBy(entry => entry.targetPath, StringComparer.OrdinalIgnoreCase).FirstOrDefault(group => group.Count() > 1);
            if (duplicate != null) throw new InvalidOperationException($"计划中存在重复目标路径：{duplicate.Key}");
            var sources = new HashSet<string>(entries.Select(entry => entry.sourcePath), StringComparer.OrdinalIgnoreCase);
            var chained = entries.FirstOrDefault(entry => !string.Equals(entry.sourcePath, entry.targetPath, StringComparison.OrdinalIgnoreCase) && sources.Contains(entry.targetPath));
            if (chained != null) throw new InvalidOperationException($"目标路径同时是本批次另一资源的源路径，禁止顺序依赖移动：{chained.targetPath}");
            var invalid = entries.FirstOrDefault(entry => !string.IsNullOrEmpty(entry.warning));
            if (invalid != null) throw new InvalidOperationException($"移动计划无效：{invalid.sourcePath}\n{invalid.warning}");
        }

        static AddressablePathStatus GetAddressablePathStatus(string guid, string sourcePath)
        {
            try
            {
                var defaultType = Type.GetType("UnityEditor.AddressableAssets.Settings.AddressableAssetSettingsDefaultObject, Unity.Addressables.Editor");
                if (defaultType == null) return AddressablePathStatus.Safe;
                var settings = defaultType.GetProperty("Settings", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                if (settings == null) return AddressablePathStatus.Safe;
                var find = settings.GetType().GetMethod("FindAssetEntry", new[] { typeof(string) });
                if (find == null) return AddressablePathStatus.Unknown;
                var entry = find.Invoke(settings, new object[] { guid });
                if (entry == null) return AddressablePathStatus.Safe;
                string address = entry.GetType().GetProperty("address")?.GetValue(entry) as string;
                return string.Equals(address, sourcePath, StringComparison.OrdinalIgnoreCase) ? AddressablePathStatus.PathBased : AddressablePathStatus.Safe;
            }
            catch { return AddressablePathStatus.Unknown; }
        }
    }

    sealed class AssetMoveExecutor : IAssetMoveExecutor
    {
        readonly IAssetMovePolicy _policy;
        public AssetMoveExecutor(IAssetMovePolicy policy) => _policy = policy;

        public void Execute(IReadOnlyList<AssetMoveEntry> entries, AssetMoveHistory history)
        {
            var included = entries.Where(entry => entry.include).ToList();
            _policy.ValidatePlan(included);
            if (history == null) throw new ArgumentNullException(nameof(history));
            history.createdAtUtc = DateTime.UtcNow.ToString("O");
            history.moves.Clear();
            try
            {
                foreach (var entry in included)
                {
                    Move(entry.sourcePath, entry.targetPath);
                    history.moves.Add(new AssetMoveHistoryEntry { guid = entry.sourceGuid, oldPath = entry.sourcePath, newPath = entry.targetPath });
                    Verify(entry.sourceGuid, entry.targetPath);
                }
                Save();
            }
            catch (Exception moveException)
            {
                try { Restore(history); history.moves.Clear(); }
                catch (Exception rollbackException) { throw new AggregateException("资源移动失败，且自动回滚未完成。历史已保留，请立即人工恢复。", moveException, rollbackException); }
                throw;
            }
        }

        public void Restore(AssetMoveHistory history)
        {
            if (history?.moves == null) return;
            foreach (var move in history.moves.AsEnumerable().Reverse())
            {
                string currentPath = AssetDatabase.GUIDToAssetPath(move.guid);
                if (string.IsNullOrEmpty(currentPath)) throw new InvalidOperationException($"无法找到 GUID：{move.guid}");
                EnsureFolder(Path.GetDirectoryName(move.oldPath)?.Replace('\\', '/'));
                Move(currentPath, move.oldPath);
                Verify(move.guid, move.oldPath);
            }
            Save();
        }

        static void Move(string source, string target)
        {
            string error = AssetDatabase.MoveAsset(source, target);
            if (!string.IsNullOrEmpty(error)) throw new InvalidOperationException($"移动失败：{source} → {target}\n{error}");
        }

        static void Verify(string guid, string path)
        {
            if (!string.Equals(AssetDatabase.AssetPathToGUID(path), guid, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException($"GUID 验证失败：{path}");
            if (AssetDatabase.IsValidFolder(path)) return;
            if (AssetDatabase.LoadMainAssetAtPath(path) == null) throw new InvalidOperationException($"移动后资源无法加载：{path}");
        }

        static void EnsureFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder) || AssetDatabase.IsValidFolder(folder)) return;
            string parent = Path.GetDirectoryName(folder)?.Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }

        static void Save() { AssetDatabase.SaveAssets(); AssetDatabase.Refresh(); }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Bento
{
    sealed class AssetMovePlanner
    {
        readonly CompositeAssetMovePolicy _policy;

        public AssetMovePlanner(CompositeAssetMovePolicy policy) => _policy = policy;

        public List<AssetMoveEntry> Build(
            IEnumerable<UnityEngine.Object> assets,
            IReadOnlyDictionary<AssetMoveKind, string> targetFolders,
            AssetNamingOptions naming)
        {
            var entries = new List<AssetMoveEntry>();
            var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var asset in assets.Where(asset => asset != null))
                AddSelectedAsset(asset, AssetDatabase.GetAssetPath(asset), entries, seenPaths);

            RefreshTargets(entries, targetFolders, naming);
            return entries;
        }

        public void RefreshTargets(
            IReadOnlyList<AssetMoveEntry> entries,
            IReadOnlyDictionary<AssetMoveKind, string> targetFolders,
            AssetNamingOptions naming)
        {
            ApplyTargets(entries, targetFolders, naming);
            foreach (var entry in entries) entry.warning = _policy.ValidateEntry(entry);
        }

        static void ApplyTargets(
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
                kind = kind
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

    interface IAssetMoveRule
    {
        string Validate(AssetMoveEntry entry);
    }

    interface IAssetMovePlanRule
    {
        void Validate(IReadOnlyList<AssetMoveEntry> entries);
    }

    sealed class CompositeAssetMovePolicy
    {
        readonly IReadOnlyList<IAssetMoveRule> _entryRules;
        readonly IReadOnlyList<IAssetMovePlanRule> _planRules;

        public CompositeAssetMovePolicy(
            IEnumerable<IAssetMoveRule> entryRules,
            IEnumerable<IAssetMovePlanRule> planRules)
        {
            _entryRules = entryRules.ToArray();
            _planRules = planRules.ToArray();
        }

        public string ValidateEntry(AssetMoveEntry entry)
        {
            foreach (var rule in _entryRules)
            {
                string warning = rule.Validate(entry);
                if (!string.IsNullOrEmpty(warning)) return warning;
            }
            return string.Empty;
        }

        public void ValidatePlan(IReadOnlyList<AssetMoveEntry> entries)
        {
            foreach (AssetMoveEntry entry in entries)
            {
                string warning = ValidateEntry(entry);
                if (!string.IsNullOrEmpty(warning))
                    throw new InvalidOperationException($"移动计划已过期：{entry.sourcePath}\n{warning}");
            }
            foreach (var rule in _planRules) rule.Validate(entries);
        }
    }

    sealed class AssetMovePathRule : IAssetMoveRule
    {
        static readonly string[] ForbiddenExtensions = { ".unity", ".cs", ".asmdef" };

        public string Validate(AssetMoveEntry entry)
        {
            if (string.IsNullOrEmpty(entry.sourceGuid)) return "资源没有有效 GUID";
            if (!entry.sourcePath.StartsWith("Assets/", StringComparison.Ordinal)) return "只允许移动 Assets/ 下资源";
            if (string.Equals(entry.sourcePath, "Assets", StringComparison.OrdinalIgnoreCase)) return "禁止移动 Assets 根目录";
            var mainAsset = AssetDatabase.LoadMainAssetAtPath(entry.sourcePath);
            if (entry.asset != null && mainAsset != null && entry.asset != mainAsset) return "FBX/模型内嵌子资源不能单独移动，请移动模型主资产";
            if (!BentoUtility.IsProjectFolder(entry.targetFolder)) return "目标文件夹无效";
            if (entry.kind == AssetMoveKind.Folder && entry.targetFolder.StartsWith(entry.sourcePath.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase))
                return "禁止把文件夹移动到自己的子目录";
            if (string.Equals(entry.sourcePath, entry.targetPath, StringComparison.OrdinalIgnoreCase)) return "源路径和目标路径相同";
            if (!string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(entry.targetPath))) return "目标路径已存在资源";
            if (ForbiddenExtensions.Any(extension => entry.sourcePath.EndsWith(extension, StringComparison.OrdinalIgnoreCase)))
                return "禁止移动场景、脚本或程序集定义";
            return string.Empty;
        }
    }


    sealed class AssetMoveModelMaterialRule : IAssetMoveRule
    {
        public string Validate(AssetMoveEntry entry)
        {
            string sourcePath = entry.sourcePath;
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
    }

    sealed class AssetMoveAddressablesRule : IAssetMoveRule
    {
        readonly IAddressableAssetPathInspector _inspector;

        public AssetMoveAddressablesRule(IAddressableAssetPathInspector inspector) => _inspector = inspector;

        public string Validate(AssetMoveEntry entry)
        {
            switch (_inspector.Inspect(entry.sourceGuid, entry.sourcePath))
            {
                case AddressableAssetPathStatus.PathBased:
                    return "Addressables 地址等于资产路径，移动会改变运行时地址";
                case AddressableAssetPathStatus.Unknown:
                    return "Addressables 风险检查失败，安全起见禁止移动";
                default:
                    return string.Empty;
            }
        }
    }

    enum AddressableAssetPathStatus { Safe, PathBased, Unknown }

    interface IAddressableAssetPathInspector
    {
        AddressableAssetPathStatus Inspect(string guid, string sourcePath);
    }

    sealed class ReflectionAddressableAssetPathInspector : IAddressableAssetPathInspector
    {
        public AddressableAssetPathStatus Inspect(string guid, string sourcePath)
        {
            try
            {
                var defaultType = Type.GetType("UnityEditor.AddressableAssets.Settings.AddressableAssetSettingsDefaultObject, Unity.Addressables.Editor");
                if (defaultType == null) return AddressableAssetPathStatus.Safe;
                var settings = defaultType.GetProperty("Settings", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                if (settings == null) return AddressableAssetPathStatus.Safe;
                var find = settings.GetType().GetMethod("FindAssetEntry", new[] { typeof(string) });
                if (find == null) return AddressableAssetPathStatus.Unknown;
                var entry = find.Invoke(settings, new object[] { guid });
                if (entry == null) return AddressableAssetPathStatus.Safe;
                string address = entry.GetType().GetProperty("address")?.GetValue(entry) as string;
                return string.Equals(address, sourcePath, StringComparison.OrdinalIgnoreCase)
                    ? AddressableAssetPathStatus.PathBased
                    : AddressableAssetPathStatus.Safe;
            }
            catch { return AddressableAssetPathStatus.Unknown; }
        }
    }

    sealed class AssetMovePlanConsistencyRule : IAssetMovePlanRule
    {
        public void Validate(IReadOnlyList<AssetMoveEntry> entries)
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
    }

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
            var included = entries.Where(entry => entry.include).ToList();
            _policy.ValidatePlan(included);
            if (history == null) throw new ArgumentNullException(nameof(history));
            history.createdAtUtc = DateTime.UtcNow.ToString("O");
            var operations = included.Select(entry => new AssetMoveHistoryEntry
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

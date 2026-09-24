using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Bento
{
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
            foreach (IAssetMoveRule rule in _entryRules)
            {
                string warning = rule.Validate(entry);
                if (!string.IsNullOrEmpty(warning))
                    return warning;
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

            foreach (IAssetMovePlanRule rule in _planRules)
                rule.Validate(entries);
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

            UnityEngine.Object mainAsset = AssetDatabase.LoadMainAssetAtPath(entry.sourcePath);
            if (entry.asset != null && mainAsset != null && entry.asset != mainAsset)
                return "FBX/模型内嵌子资源不能单独移动，请移动模型主资源";
            if (!BentoUtility.IsProjectFolder(entry.targetFolder)) return "目标文件夹无效";
            if (entry.kind == AssetMoveKind.Folder &&
                entry.targetFolder.StartsWith(entry.sourcePath.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase))
                return "禁止把文件夹移动到自己的子目录";
            if (string.Equals(entry.sourcePath, entry.targetPath, StringComparison.OrdinalIgnoreCase))
                return "源路径和目标路径相同";
            if (!string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(entry.targetPath)))
                return "目标路径已存在资源";
            if (ForbiddenExtensions.Any(extension =>
                    entry.sourcePath.EndsWith(extension, StringComparison.OrdinalIgnoreCase)))
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
            if (importer == null)
                return "无法读取模型导入设置，禁止移动";

            foreach (KeyValuePair<AssetImporter.SourceAssetIdentifier, UnityEngine.Object> pair in importer.GetExternalObjectMap())
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

    enum AddressableAssetPathStatus
    {
        Safe,
        PathBased,
        Unknown
    }

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
                Type defaultType = Type.GetType(
                    "UnityEditor.AddressableAssets.Settings.AddressableAssetSettingsDefaultObject, Unity.Addressables.Editor");
                if (defaultType == null) return AddressableAssetPathStatus.Safe;

                object settings = defaultType.GetProperty("Settings", BindingFlags.Public | BindingFlags.Static)
                    ?.GetValue(null);
                if (settings == null) return AddressableAssetPathStatus.Safe;

                MethodInfo find = settings.GetType().GetMethod("FindAssetEntry", new[] { typeof(string) });
                if (find == null) return AddressableAssetPathStatus.Unknown;

                object entry = find.Invoke(settings, new object[] { guid });
                if (entry == null) return AddressableAssetPathStatus.Safe;

                string address = entry.GetType().GetProperty("address")?.GetValue(entry) as string;
                return string.Equals(address, sourcePath, StringComparison.OrdinalIgnoreCase)
                    ? AddressableAssetPathStatus.PathBased
                    : AddressableAssetPathStatus.Safe;
            }
            catch
            {
                return AddressableAssetPathStatus.Unknown;
            }
        }
    }

    sealed class AssetMovePlanConsistencyRule : IAssetMovePlanRule
    {
        public void Validate(IReadOnlyList<AssetMoveEntry> entries)
        {
            if (entries.Count == 0)
                throw new InvalidOperationException("没有勾选任何要移动的资源");

            IGrouping<string, AssetMoveEntry> duplicate = entries
                .GroupBy(entry => entry.targetPath, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(group => group.Count() > 1);
            if (duplicate != null)
                throw new InvalidOperationException($"计划中存在重复目标路径：{duplicate.Key}");

            var sources = new HashSet<string>(
                entries.Select(entry => entry.sourcePath),
                StringComparer.OrdinalIgnoreCase);
            AssetMoveEntry chained = entries.FirstOrDefault(entry =>
                !string.Equals(entry.sourcePath, entry.targetPath, StringComparison.OrdinalIgnoreCase) &&
                sources.Contains(entry.targetPath));
            if (chained != null)
                throw new InvalidOperationException(
                    $"目标路径同时是本批次另一资源的源路径，禁止顺序依赖移动：{chained.targetPath}");

            AssetMoveEntry invalid = entries.FirstOrDefault(entry => !string.IsNullOrEmpty(entry.warning));
            if (invalid != null)
                throw new InvalidOperationException($"移动计划无效：{invalid.sourcePath}\n{invalid.warning}");
        }
    }
}

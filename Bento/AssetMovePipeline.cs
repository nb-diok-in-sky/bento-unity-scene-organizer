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
                AddAssetAndDependencies(asset, AssetDatabase.GetAssetPath(asset), false, string.Empty, entries, seenPaths);

            ApplyTargets(entries, targetFolders, naming);
            AssetReferenceAuditService.Audit(entries);
            ApplyTargets(entries, targetFolders, naming);
            foreach (var entry in entries)
            {
                entry.warning = _policy.ValidateEntry(entry);
                if (entry.isDependency && !string.IsNullOrEmpty(entry.warning)) entry.include = false;
            }
            return entries;
        }

        static void ApplyTargets(
            IReadOnlyList<AssetMoveEntry> entries,
            IReadOnlyDictionary<AssetMoveKind, string> targetFolders,
            AssetNamingOptions naming)
        {
            for (int index = 0; index < entries.Count; index++)
            {
                var entry = entries[index];
                entry.targetFolder = targetFolders.TryGetValue(entry.kind, out string folder) ? folder : string.Empty;
                string extension = Path.GetExtension(entry.sourcePath);
                string originalName = Path.GetFileNameWithoutExtension(entry.sourcePath);
                string targetName = entry.keepOriginalNameBecauseShared ? originalName : AssetNaming.BuildName(originalName, index, naming);
                entry.targetPath = string.IsNullOrEmpty(folder) ? string.Empty : $"{folder.TrimEnd('/')}/{targetName}{extension}";
            }
        }

        static void AddAssetAndDependencies(
            UnityEngine.Object asset,
            string path,
            bool dependency,
            string ownerPath,
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
                isDependency = dependency,
                ownerPath = ownerPath
            });

            if (kind == AssetMoveKind.PrefabOrModel && asset is GameObject gameObject)
            {
                foreach (var material in gameObject.GetComponentsInChildren<Renderer>(true).SelectMany(renderer => renderer.sharedMaterials).Where(material => material != null))
                    AddAssetAndDependencies(material, AssetDatabase.GetAssetPath(material), true, path, entries, seenPaths);
            }
            else if (kind == AssetMoveKind.Material && asset is Material material && material.shader != null)
            {
                for (int index = 0; index < ShaderUtil.GetPropertyCount(material.shader); index++)
                {
                    if (ShaderUtil.GetPropertyType(material.shader, index) != ShaderUtil.ShaderPropertyType.TexEnv) continue;
                    var texture = material.GetTexture(ShaderUtil.GetPropertyName(material.shader, index));
                    if (texture != null) AddAssetAndDependencies(texture, AssetDatabase.GetAssetPath(texture), true, path, entries, seenPaths);
                }
            }
        }

        static AssetMoveKind Classify(UnityEngine.Object asset, string path)
        {
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
            if (AssetDatabase.IsValidFolder(entry.sourcePath)) return "禁止通过该工具移动整个文件夹";
            var mainAsset = AssetDatabase.LoadMainAssetAtPath(entry.sourcePath);
            if (entry.asset != null && mainAsset != null && entry.asset != mainAsset) return "FBX/模型内嵌子资源不能单独移动，请移动模型主资产";
            if (!BentoUtility.IsProjectFolder(entry.targetFolder)) return "目标文件夹无效";
            if (string.Equals(entry.sourcePath, entry.targetPath, StringComparison.OrdinalIgnoreCase)) return "源路径和目标路径相同";
            if (!string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(entry.targetPath))) return "目标路径已存在资源";
            AddressablePathStatus addressable = GetAddressablePathStatus(entry.sourceGuid, entry.sourcePath);
            if (addressable == AddressablePathStatus.PathBased) return "Addressables 地址等于资产路径，移动会改变运行时地址";
            if (addressable == AddressablePathStatus.Unknown) return "Addressables 风险检查失败，安全起见禁止移动";
            if (new[] { ".unity", ".cs", ".asmdef" }.Any(extension => entry.sourcePath.EndsWith(extension, StringComparison.OrdinalIgnoreCase)))
                return "禁止移动场景、脚本或程序集定义";
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

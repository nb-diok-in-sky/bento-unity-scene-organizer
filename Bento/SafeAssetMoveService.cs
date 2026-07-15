using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Bento
{
    public static class SafeAssetMoveService
    {
        enum AddressablePathStatus { Safe, PathBased, Unknown }

        public static List<AssetMoveEntry> BuildPlan(
            IEnumerable<UnityEngine.Object> selectedAssets,
            string prefabFolder,
            string materialFolder,
            string textureFolder,
            AssetNamingOptions naming)
        {
            var entries = new List<AssetMoveEntry>();
            var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var asset in selectedAssets.Where(asset => asset != null))
            {
                string sourcePath = AssetDatabase.GetAssetPath(asset);
                AddAssetAndDependencies(asset, sourcePath, false, string.Empty, entries, seenPaths);
            }

            int index = 0;
            foreach (var entry in entries)
            {
                entry.targetFolder = ResolveTargetFolder(entry.kind, prefabFolder, materialFolder, textureFolder);
                ApplyTargetPath(entry, naming, index++);
            }

            AssetReferenceAuditService.Audit(entries);
            index = 0;
            foreach (var entry in entries)
            {
                ApplyTargetPath(entry, naming, index++);
                entry.warning = ValidateEntry(entry);
                if (entry.isDependency && !string.IsNullOrEmpty(entry.warning))
                    entry.include = false;
            }
            return entries;
        }

        static void ApplyTargetPath(AssetMoveEntry entry, AssetNamingOptions naming, int index)
        {
            string extension = Path.GetExtension(entry.sourcePath);
            string originalName = Path.GetFileNameWithoutExtension(entry.sourcePath);
            string targetName = entry.keepOriginalNameBecauseShared
                ? originalName
                : AssetNaming.BuildName(originalName, index, naming);
            entry.targetPath = string.IsNullOrEmpty(entry.targetFolder)
                ? string.Empty
                : $"{entry.targetFolder.TrimEnd('/')}/{targetName}{extension}";
        }

        public static void Execute(IReadOnlyList<AssetMoveEntry> entries, AssetMoveHistory history)
        {
            var included = entries.Where(entry => entry.include).ToList();
            ValidatePlanOrThrow(included);
            if (history == null) throw new ArgumentNullException(nameof(history));
            history.createdAtUtc = DateTime.UtcNow.ToString("O");
            history.moves.Clear();

            try
            {
                foreach (var entry in included)
                {
                    string error = AssetDatabase.MoveAsset(entry.sourcePath, entry.targetPath);
                    if (!string.IsNullOrEmpty(error))
                        throw new InvalidOperationException($"移动失败：{entry.sourcePath} → {entry.targetPath}\n{error}");

                    history.moves.Add(new AssetMoveHistoryEntry
                    {
                        guid = entry.sourceGuid,
                        oldPath = entry.sourcePath,
                        newPath = entry.targetPath
                    });

                    string movedGuid = AssetDatabase.AssetPathToGUID(entry.targetPath);
                    if (!string.Equals(entry.sourceGuid, movedGuid, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException($"GUID 验证失败：{entry.sourcePath} → {entry.targetPath}");
                    if (AssetDatabase.LoadMainAssetAtPath(entry.targetPath) == null)
                        throw new InvalidOperationException($"移动后资源无法加载：{entry.targetPath}");
                }
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
            catch (Exception moveException)
            {
                try
                {
                    Restore(history);
                    history.moves.Clear();
                }
                catch (Exception rollbackException)
                {
                    throw new AggregateException(
                        "资源移动失败，且自动回滚未完成。历史已保留，请立即人工恢复。",
                        moveException,
                        rollbackException);
                }
                throw;
            }
        }

        public static void Restore(AssetMoveHistory history)
        {
            if (history?.moves == null) return;
            foreach (var move in history.moves.AsEnumerable().Reverse())
            {
                string currentPath = AssetDatabase.GUIDToAssetPath(move.guid);
                if (string.IsNullOrEmpty(currentPath))
                    throw new InvalidOperationException($"无法找到 GUID：{move.guid}");
                EnsureFolderExists(Path.GetDirectoryName(move.oldPath)?.Replace('\\', '/'));
                string error = AssetDatabase.MoveAsset(currentPath, move.oldPath);
                if (!string.IsNullOrEmpty(error))
                    throw new InvalidOperationException($"恢复失败：{currentPath} → {move.oldPath}\n{error}");
                if (!string.Equals(AssetDatabase.AssetPathToGUID(move.oldPath), move.guid, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"恢复后 GUID 验证失败：{move.oldPath}");
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        static void AddAssetAndDependencies(
            UnityEngine.Object asset,
            string path,
            bool dependency,
            string ownerPath,
            List<AssetMoveEntry> entries,
            HashSet<string> seenPaths)
        {
            if (string.IsNullOrEmpty(path) || !seenPaths.Add(path)) return;
            var kind = Classify(asset, path);
            entries.Add(CreateEntry(asset, path, kind, dependency, ownerPath));

            if (kind == AssetMoveKind.PrefabOrModel)
            {
                var gameObject = asset as GameObject;
                if (gameObject == null) return;
                foreach (var renderer in gameObject.GetComponentsInChildren<Renderer>(true))
                foreach (var material in renderer.sharedMaterials)
                {
                    if (material == null) continue;
                    string materialPath = AssetDatabase.GetAssetPath(material);
                    AddAssetAndDependencies(material, materialPath, true, path, entries, seenPaths);
                }
            }
            else if (kind == AssetMoveKind.Material)
            {
                var material = asset as Material;
                if (material?.shader == null) return;
                int propertyCount = ShaderUtil.GetPropertyCount(material.shader);
                for (int propertyIndex = 0; propertyIndex < propertyCount; propertyIndex++)
                {
                    if (ShaderUtil.GetPropertyType(material.shader, propertyIndex) != ShaderUtil.ShaderPropertyType.TexEnv) continue;
                    var texture = material.GetTexture(ShaderUtil.GetPropertyName(material.shader, propertyIndex));
                    if (texture == null) continue;
                    string texturePath = AssetDatabase.GetAssetPath(texture);
                    AddAssetAndDependencies(texture, texturePath, true, path, entries, seenPaths);
                }
            }
        }

        static AssetMoveEntry CreateEntry(UnityEngine.Object asset, string path, AssetMoveKind kind, bool dependency, string ownerPath)
        {
            return new AssetMoveEntry
            {
                asset = asset,
                sourcePath = path,
                sourceGuid = AssetDatabase.AssetPathToGUID(path),
                kind = kind,
                isDependency = dependency,
                ownerPath = ownerPath
            };
        }

        static AssetMoveKind Classify(UnityEngine.Object asset, string path)
        {
            if (asset is Material) return AssetMoveKind.Material;
            if (asset is Texture) return AssetMoveKind.Texture;
            if (asset is GameObject && (path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase) ||
                                        path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase) ||
                                        path.EndsWith(".obj", StringComparison.OrdinalIgnoreCase)))
                return AssetMoveKind.PrefabOrModel;
            return AssetMoveKind.Other;
        }

        static string ResolveTargetFolder(AssetMoveKind kind, string prefabFolder, string materialFolder, string textureFolder)
        {
            switch (kind)
            {
                case AssetMoveKind.PrefabOrModel: return prefabFolder;
                case AssetMoveKind.Material: return materialFolder;
                case AssetMoveKind.Texture: return textureFolder;
                default: return prefabFolder;
            }
        }

        static string ValidateEntry(AssetMoveEntry entry)
        {
            if (string.IsNullOrEmpty(entry.sourceGuid)) return "资源没有有效 GUID";
            if (!entry.sourcePath.StartsWith("Assets/", StringComparison.Ordinal)) return "只允许移动 Assets/ 下资源";
            if (AssetDatabase.IsValidFolder(entry.sourcePath)) return "禁止通过该工具移动整个文件夹";
            var mainAsset = AssetDatabase.LoadMainAssetAtPath(entry.sourcePath);
            if (entry.asset != null && mainAsset != null && entry.asset != mainAsset) return "FBX/模型内嵌子资源不能单独移动，请移动模型主资产";
            if (!BentoUtility.IsProjectFolder(entry.targetFolder)) return "目标文件夹无效";
            if (string.Equals(entry.sourcePath, entry.targetPath, StringComparison.OrdinalIgnoreCase)) return "源路径和目标路径相同";
            if (!string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(entry.targetPath))) return "目标路径已存在资源";
            var addressableStatus = GetAddressablePathStatus(entry.sourceGuid, entry.sourcePath);
            if (addressableStatus == AddressablePathStatus.PathBased) return "Addressables 地址等于资产路径，移动会改变运行时地址";
            if (addressableStatus == AddressablePathStatus.Unknown) return "Addressables 风险检查失败，安全起见禁止移动";
            if (entry.sourcePath.EndsWith(".unity", StringComparison.OrdinalIgnoreCase) ||
                entry.sourcePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                entry.sourcePath.EndsWith(".asmdef", StringComparison.OrdinalIgnoreCase))
                return "禁止移动场景、脚本或程序集定义";
            return string.Empty;
        }


        static AddressablePathStatus GetAddressablePathStatus(string guid, string sourcePath)
        {
            try
            {
                var settingsDefaultType = Type.GetType(
                    "UnityEditor.AddressableAssets.Settings.AddressableAssetSettingsDefaultObject, Unity.Addressables.Editor");
                if (settingsDefaultType == null) return AddressablePathStatus.Safe;
                var settings = settingsDefaultType?.GetProperty("Settings", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                if (settings == null) return AddressablePathStatus.Safe;
                var findAssetEntry = settings.GetType().GetMethod("FindAssetEntry", new[] { typeof(string) });
                if (findAssetEntry == null) return AddressablePathStatus.Unknown;
                var entry = findAssetEntry?.Invoke(settings, new object[] { guid });
                if (entry == null) return AddressablePathStatus.Safe;
                string address = entry.GetType().GetProperty("address")?.GetValue(entry) as string;
                return string.Equals(address, sourcePath, StringComparison.OrdinalIgnoreCase)
                    ? AddressablePathStatus.PathBased
                    : AddressablePathStatus.Safe;
            }
            catch
            {
                return AddressablePathStatus.Unknown;
            }
        }

        static void ValidatePlanOrThrow(IReadOnlyList<AssetMoveEntry> entries)
        {
            if (entries.Count == 0) throw new InvalidOperationException("没有勾选任何要移动的资源");
            var duplicateTargets = entries.GroupBy(entry => entry.targetPath, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1).ToList();
            if (duplicateTargets.Count > 0) throw new InvalidOperationException($"计划中存在重复目标路径：{duplicateTargets[0].Key}");
            var sourcePaths = new HashSet<string>(entries.Select(entry => entry.sourcePath), StringComparer.OrdinalIgnoreCase);
            var chainedMove = entries.FirstOrDefault(entry =>
                !string.Equals(entry.sourcePath, entry.targetPath, StringComparison.OrdinalIgnoreCase) &&
                sourcePaths.Contains(entry.targetPath));
            if (chainedMove != null)
                throw new InvalidOperationException($"目标路径同时是本批次另一资源的源路径，禁止顺序依赖移动：{chainedMove.targetPath}");
            var invalid = entries.FirstOrDefault(entry => !string.IsNullOrEmpty(entry.warning));
            if (invalid != null) throw new InvalidOperationException($"移动计划无效：{invalid.sourcePath}\n{invalid.warning}");
        }

        static void EnsureFolderExists(string folder)
        {
            if (string.IsNullOrEmpty(folder) || AssetDatabase.IsValidFolder(folder)) return;
            string parent = Path.GetDirectoryName(folder)?.Replace('\\', '/');
            EnsureFolderExists(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }
    }
}

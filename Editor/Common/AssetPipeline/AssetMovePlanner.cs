using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
            var entries = CollectEntries(assets);
            RefreshTargets(entries, targetFolders, naming);
            return entries;
        }

        public List<AssetMoveEntry> Build(
            IEnumerable<UnityEngine.Object> assets,
            IReadOnlyDictionary<AssetMoveKind, string> targetFolders,
            Func<AssetMoveKind, AssetNamingOptions> namingByKind)
        {
            var entries = CollectEntries(assets);
            RefreshTargets(entries, targetFolders, namingByKind);
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

        public void RefreshTargets(
            IReadOnlyList<AssetMoveEntry> entries,
            IReadOnlyDictionary<AssetMoveKind, string> targetFolders,
            Func<AssetMoveKind, AssetNamingOptions> namingByKind)
        {
            ApplyTargets(entries, targetFolders, namingByKind);
            foreach (var entry in entries) entry.warning = _policy.ValidateEntry(entry);
        }

        static List<AssetMoveEntry> CollectEntries(IEnumerable<UnityEngine.Object> assets)
        {
            var entries = new List<AssetMoveEntry>();
            var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var asset in assets.Where(asset => asset != null))
                AddSelectedAsset(asset, AssetDatabase.GetAssetPath(asset), entries, seenPaths);
            return entries;
        }

        static void ApplyTargets(
            IReadOnlyList<AssetMoveEntry> entries,
            IReadOnlyDictionary<AssetMoveKind, string> targetFolders,
            Func<AssetMoveKind, AssetNamingOptions> namingByKind)
        {
            var reservedTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var group in entries.GroupBy(entry => entry.kind))
            {
                AssetNamingOptions naming = namingByKind?.Invoke(group.Key) ?? new AssetNamingOptions();
                int index = 0;
                foreach (var entry in group)
                {
                    entry.targetFolder = targetFolders.TryGetValue(entry.kind, out string folder) ? folder : string.Empty;
                    string extension = Path.GetExtension(entry.sourcePath);
                    string originalName = Path.GetFileNameWithoutExtension(entry.sourcePath);
                    string targetName = AssetNaming.BuildName(originalName, index, naming);
                    entry.targetPath = BuildTargetPath(entry, folder, targetName, extension);
                    if (naming.appendNumber && !string.IsNullOrEmpty(folder))
                        entry.targetPath = FindAvailablePath(entry, folder, targetName, extension, reservedTargets);
                    reservedTargets.Add(entry.targetPath);
                    index++;
                }
            }
        }

        static string FindAvailablePath(
            AssetMoveEntry entry,
            string folder,
            string targetName,
            string extension,
            ISet<string> reservedTargets)
        {
            string candidate = BuildTargetPath(entry, folder, targetName, extension);
            if (!reservedTargets.Contains(candidate) && string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(candidate)))
                return candidate;
            return AssetDatabase.GenerateUniqueAssetPath(candidate);
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
}

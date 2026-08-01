using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Bento
{
    enum FbxPrefabFolderMode { Flat, PreserveFolders }
    enum FbxPrefabConflictMode { Skip, UniqueName, Overwrite }

    sealed class FbxPrefabBatchEntry
    {
        public GameObject fbx;
        public string sourcePath;
        public string prefabPath;
        public bool include = true;
        public bool exists;
        public string note;
    }

    sealed class FbxPrefabBatchResult
    {
        public int created;
        public int overwritten;
        public int skipped;
        public readonly List<string> failures = new();
    }

    static class FbxPrefabCreationService
    {
        public static GameObject CreatePrefab(GameObject fbx, string selectedFolder)
        {
            if (!FbxUrpMaterialBindingService.IsFbx(fbx))
                throw new InvalidOperationException("请选择 Project 中的 .fbx 资产。");

            string fbxPath = AssetDatabase.GetAssetPath(fbx);
            string folder = FbxUrpMaterialBindingService.ResolveOutputFolder(selectedFolder, fbxPath);
            var instance = UnityEngine.Object.Instantiate(fbx);
            instance.name = fbx.name;
            try
            {
                string prefabPath = BentoUtility.GenerateUniqueAssetPath(folder, fbx.name, ".prefab");
                var prefab = PrefabUtility.SaveAsPrefabAsset(instance, prefabPath);
                if (prefab == null) throw new InvalidOperationException("Prefab 保存失败。");
                return prefab;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        public static List<FbxPrefabBatchEntry> BuildBatchPlan(
            string sourceFolder, string outputFolder, bool createSourceNamedFolder,
            FbxPrefabFolderMode folderMode, FbxPrefabConflictMode conflictMode)
        {
            ValidateFolder(sourceFolder, "来源文件夹");
            ValidateFolder(outputFolder, "输出文件夹");
            string resolvedOutputFolder = ResolveBatchOutputFolder(sourceFolder, outputFolder, createSourceNamedFolder);
            return AssetDatabase.FindAssets("t:GameObject", new[] { sourceFolder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Select(path => BuildEntry(path, sourceFolder, resolvedOutputFolder, folderMode, conflictMode))
                .ToList();
        }

        public static string ResolveBatchOutputFolder(string sourceFolder, string outputFolder, bool createSourceNamedFolder)
        {
            if (!createSourceNamedFolder) return outputFolder.TrimEnd('/');
            string sourceName = Path.GetFileName(sourceFolder.TrimEnd('/'));
            sourceName = BentoUtility.SanitizeFileName(sourceName, "FBX_Prefabs");
            return outputFolder.TrimEnd('/') + "/" + sourceName;
        }

        public static FbxPrefabBatchResult ExecuteBatch(
            IEnumerable<FbxPrefabBatchEntry> entries, FbxPrefabConflictMode conflictMode)
        {
            var result = new FbxPrefabBatchResult();
            foreach (var entry in entries.Where(item => item.include && item.fbx != null))
            {
                try
                {
                    if (entry.exists && conflictMode == FbxPrefabConflictMode.Skip)
                    {
                        result.skipped++;
                        continue;
                    }
                    EnsureFolder(Path.GetDirectoryName(entry.prefabPath)?.Replace('\\', '/'));
                    string targetPath = conflictMode == FbxPrefabConflictMode.UniqueName
                        ? AssetDatabase.GenerateUniqueAssetPath(entry.prefabPath)
                        : entry.prefabPath;
                    bool overwriting = AssetDatabase.LoadAssetAtPath<GameObject>(targetPath) != null;
                    SavePrefab(entry.fbx, targetPath);
                    if (overwriting) result.overwritten++; else result.created++;
                }
                catch (Exception exception)
                {
                    result.failures.Add($"{entry.sourcePath}: {exception.Message}");
                }
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return result;
        }

        static FbxPrefabBatchEntry BuildEntry(string sourcePath, string sourceFolder, string outputFolder,
            FbxPrefabFolderMode folderMode, FbxPrefabConflictMode conflictMode)
        {
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            string targetFolder = outputFolder.TrimEnd('/');
            if (folderMode == FbxPrefabFolderMode.PreserveFolders)
            {
                string sourceDirectory = Path.GetDirectoryName(sourcePath)?.Replace('\\', '/') ?? sourceFolder;
                string relative = sourceDirectory.Length > sourceFolder.TrimEnd('/').Length
                    ? sourceDirectory.Substring(sourceFolder.TrimEnd('/').Length).TrimStart('/')
                    : string.Empty;
                if (!string.IsNullOrEmpty(relative)) targetFolder += "/" + relative;
            }
            string prefabPath = $"{targetFolder}/{Path.GetFileNameWithoutExtension(sourcePath)}.prefab";
            bool exists = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null;
            string note = exists
                ? conflictMode switch
                {
                    FbxPrefabConflictMode.Skip => "已存在，将跳过",
                    FbxPrefabConflictMode.UniqueName => "已存在，将生成唯一名称",
                    _ => "已存在，将覆盖"
                }
                : "准备创建";
            return new FbxPrefabBatchEntry { fbx = fbx, sourcePath = sourcePath, prefabPath = prefabPath, exists = exists, include = true, note = note };
        }

        static GameObject SavePrefab(GameObject fbx, string prefabPath)
        {
            var instance = UnityEngine.Object.Instantiate(fbx);
            instance.name = fbx.name;
            try
            {
                var prefab = PrefabUtility.SaveAsPrefabAsset(instance, prefabPath);
                if (prefab == null) throw new InvalidOperationException("Prefab 保存失败。");
                return prefab;
            }
            finally { UnityEngine.Object.DestroyImmediate(instance); }
        }

        static void ValidateFolder(string path, string label)
        {
            if (!BentoUtility.IsProjectFolder(path)) throw new InvalidOperationException($"{label}不是有效的 Project 文件夹。");
        }

        static void EnsureFolder(string path)
        {
            if (string.IsNullOrEmpty(path) || AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}

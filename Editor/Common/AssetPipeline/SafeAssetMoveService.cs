using System.Collections.Generic;
using System;
using System.Linq;

namespace Bento
{
    static class SafeAssetMoveService
    {
        static readonly CompositeAssetMovePolicy Policy = new CompositeAssetMovePolicy(
            new IAssetMoveRule[]
            {
                new AssetMovePathRule(),
                new AssetMoveModelMaterialRule(),
                new AssetMoveAddressablesRule(new ReflectionAddressableAssetPathInspector())
            },
            new IAssetMovePlanRule[] { new AssetMovePlanConsistencyRule() });
        static readonly AssetMovePlanner Planner = new AssetMovePlanner(Policy);
        static readonly AssetPathTransaction Transaction = new AssetPathTransaction();

        public static List<AssetMoveEntry> BuildPlan(
            IEnumerable<UnityEngine.Object> selectedAssets,
            string prefabFolder,
            string materialFolder,
            string textureFolder,
            AssetNamingOptions naming)
            => BuildPlan(selectedAssets, prefabFolder, materialFolder, textureFolder, prefabFolder, naming);

        public static List<AssetMoveEntry> BuildPlan(
            IEnumerable<UnityEngine.Object> selectedAssets,
            string prefabFolder,
            string materialFolder,
            string textureFolder,
            string otherFolder,
            AssetNamingOptions naming)
        {
            return Planner.Build(selectedAssets, BuildTargetFolders(prefabFolder, materialFolder, textureFolder, otherFolder), naming ?? new AssetNamingOptions());
        }

        public static List<AssetMoveEntry> BuildPlan(
            IEnumerable<UnityEngine.Object> selectedAssets,
            string modelFolder,
            string materialFolder,
            string textureFolder,
            Func<AssetMoveKind, AssetNamingOptions> namingByKind)
        {
            return Planner.Build(
                selectedAssets,
                BuildTargetFolders(modelFolder, materialFolder, textureFolder, modelFolder),
                namingByKind);
        }

        public static void RefreshTargets(
            IReadOnlyList<AssetMoveEntry> entries,
            string prefabFolder,
            string materialFolder,
            string textureFolder,
            string otherFolder,
            AssetNamingOptions naming)
        {
            Planner.RefreshTargets(entries, BuildTargetFolders(prefabFolder, materialFolder, textureFolder, otherFolder), naming ?? new AssetNamingOptions());
        }

        static IReadOnlyDictionary<AssetMoveKind, string> BuildTargetFolders(
            string prefabFolder,
            string materialFolder,
            string textureFolder,
            string otherFolder) => new Dictionary<AssetMoveKind, string>
        {
            { AssetMoveKind.PrefabOrModel, prefabFolder },
            { AssetMoveKind.Material, materialFolder },
            { AssetMoveKind.Texture, textureFolder },
            { AssetMoveKind.Folder, otherFolder },
            { AssetMoveKind.Other, otherFolder }
        };

        public static void Execute(IReadOnlyList<AssetMoveEntry> entries, AssetMoveHistory history)
        {
            List<AssetMoveEntry> included = entries.Where(entry => entry.include).ToList();
            Policy.ValidatePlan(included);
            if (history == null) throw new ArgumentNullException(nameof(history));

            history.createdAtUtc = DateTime.UtcNow.ToString("O");
            Transaction.Execute(
                included.Select(entry => new AssetMoveHistoryEntry
                {
                    guid = entry.sourceGuid,
                    oldPath = entry.sourcePath,
                    newPath = entry.targetPath
                }),
                history.moves,
                "资源移动失败，且自动回滚未完成。历史已保留，请立即人工恢复。");
        }

        public static void Restore(AssetMoveHistory history) => Transaction.Restore(history?.moves);

        public static List<AssetMoveEntry> BuildFlatExtractionPlan(IEnumerable<UnityEngine.Object> assets, string targetFolder)
        {
            var entries = assets.Where(asset => asset != null).Select(asset =>
            {
                string sourcePath = UnityEditor.AssetDatabase.GetAssetPath(asset);
                return new AssetMoveEntry
                {
                    asset = asset,
                    sourcePath = sourcePath,
                    sourceGuid = UnityEditor.AssetDatabase.AssetPathToGUID(sourcePath),
                    targetFolder = targetFolder,
                    targetPath = $"{targetFolder.TrimEnd('/')}/{System.IO.Path.GetFileName(sourcePath)}",
                    kind = ClassifyForExtraction(asset, sourcePath),
                    include = true
                };
            }).ToList();
            foreach (var entry in entries) entry.warning = Policy.ValidateEntry(entry);
            return entries;
        }

        static AssetMoveKind ClassifyForExtraction(UnityEngine.Object asset, string path)
        {
            if (asset is UnityEngine.Material) return AssetMoveKind.Material;
            if (asset is UnityEngine.Texture) return AssetMoveKind.Texture;
            if (asset is UnityEngine.GameObject && (path.EndsWith(".prefab", System.StringComparison.OrdinalIgnoreCase) || path.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase))) return AssetMoveKind.PrefabOrModel;
            return AssetMoveKind.Other;
        }
    }
}

using System.Collections.Generic;

namespace Bento
{
    public static class SafeAssetMoveService
    {
        static readonly CompositeAssetMovePolicy Policy = new CompositeAssetMovePolicy(
            new IAssetMoveRule[]
            {
                new AssetMovePathRule(),
                new AssetMoveRepositoryBoundaryRule(),
                new AssetMoveModelMaterialRule(),
                new AssetMoveAddressablesRule(new ReflectionAddressableAssetPathInspector())
            },
            new IAssetMovePlanRule[] { new AssetMovePlanConsistencyRule() });
        static readonly AssetMovePlanner Planner = new AssetMovePlanner(Policy);
        static readonly AssetMoveExecutor Executor = new AssetMoveExecutor(Policy, new AssetPathTransaction());

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

        public static void Execute(IReadOnlyList<AssetMoveEntry> entries, AssetMoveHistory history) => Executor.Execute(entries, history);
        public static void Restore(AssetMoveHistory history) => Executor.Restore(history);
    }
}

using System.Collections.Generic;

namespace Bento
{
    public static class SafeAssetMoveService
    {
        static readonly IAssetMovePolicy Policy = new AssetMoveSafetyPolicy();
        static readonly IAssetMovePlanner Planner = new AssetMovePlanner(Policy);
        static readonly IAssetMoveExecutor Executor = new AssetMoveExecutor(Policy);

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
            return Planner.Build(selectedAssets, new Dictionary<AssetMoveKind, string>
            {
                { AssetMoveKind.PrefabOrModel, prefabFolder },
                { AssetMoveKind.Material, materialFolder },
                { AssetMoveKind.Texture, textureFolder },
                { AssetMoveKind.Other, otherFolder }
            }, naming ?? new AssetNamingOptions());
        }

        public static void Execute(IReadOnlyList<AssetMoveEntry> entries, AssetMoveHistory history) => Executor.Execute(entries, history);
        public static void Restore(AssetMoveHistory history) => Executor.Restore(history);
    }
}

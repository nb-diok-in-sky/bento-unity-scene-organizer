using System;
using System.Collections.Generic;
using System.Linq;

namespace Bento
{
    [Serializable]
    public class AIAssetOrganizerConfig
    {
        public string sourceRoot;
        public string prefabFolder;
        public string materialFolder;
        public string textureFolder;
        public string otherFolder;
    }

    public class AIAssetOrganizerPlan
    {
        public AIAssetOrganizerConfig config;
        public readonly List<AssetMoveEntry> entries = new List<AssetMoveEntry>();
        public readonly List<string> warnings = new List<string>();
    }

    public sealed class AIAssetOrganizerExecutor
    {
        public AssetMoveHistory LastHistory { get; private set; }

        public void Execute(AIAssetOrganizerPlan plan)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            AIAssetOrganizerPlanner.ValidateConfig(plan.config);
            if (!plan.entries.Any(entry => entry.include)) throw new InvalidOperationException("计划中没有勾选任何资产");
            var outsideSource = plan.entries.FirstOrDefault(entry => entry.include &&
                !AIAssetOrganizerPlanner.IsInside(entry.sourcePath, plan.config.sourceRoot));
            if (outsideSource != null)
                throw new InvalidOperationException("计划中的资产已不在当前来源范围内，请重新扫描：" + outsideSource.sourcePath);
            LastHistory = new AssetMoveHistory();
            SafeAssetMoveService.Execute(plan.entries, LastHistory);
        }

        public void Restore()
        {
            if (LastHistory == null || LastHistory.moves.Count == 0) return;
            SafeAssetMoveService.Restore(LastHistory);
            LastHistory = null;
        }
    }
}

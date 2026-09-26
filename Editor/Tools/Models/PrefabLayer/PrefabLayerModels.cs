using System;
using System.Collections.Generic;
using UnityEngine;

namespace Bento
{
    public enum PrefabLayerApplyMode
    {
        DefaultNodesOnly,
        RootOnly,
        AllNodes
    }

    [Serializable]
    public class PrefabLayerPlanEntry
    {
        public string assetPath;
        public string assetGuid;
        public string currentRootLayer;
        public string suggestedLayer;
        public string reason;
        public float confidence;
        public bool include = true;
        public int nodeCount;
        public int defaultLayerNodeCount;
        public int specialLayerNodeCount;
        public List<string> existingLayers = new List<string>();
    }

    [Serializable]
    public class PrefabLayerChangeRecord
    {
        public string hierarchyPath;
        public string siblingIndexPath;
        public int oldLayer;
        public int newLayer;
    }

    [Serializable]
    public class PrefabLayerHistoryEntry
    {
        public string assetGuid;
        public string assetPath;
        public List<PrefabLayerChangeRecord> changes = new List<PrefabLayerChangeRecord>();
    }

    [Serializable]
    public class PrefabLayerHistory
    {
        public List<PrefabLayerHistoryEntry> entries = new List<PrefabLayerHistoryEntry>();
    }

    public static class ProjectLayerCatalog
    {
        public static List<string> GetDefinedNames()
        {
            var result = new List<string>();
            for (int index = 0; index < 32; index++)
            {
                string name = LayerMask.LayerToName(index);
                if (!string.IsNullOrEmpty(name)) result.Add(name);
            }
            return result;
        }

        public static bool IsDefined(string name) => !string.IsNullOrEmpty(name) && LayerMask.NameToLayer(name) >= 0;
    }
}

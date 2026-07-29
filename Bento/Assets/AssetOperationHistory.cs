using System;
using System.Collections.Generic;

namespace Bento
{
    [Serializable]
    public sealed class AssetOperationItem
    {
        public string assetGuid;
        public string beforePath;
        public string afterPath;
    }

    [Serializable]
    public sealed class AssetOperationRecord
    {
        public string id;
        public string operationType;
        public string displayName;
        public string createdAtUtc;
        public bool restored;
        public string restoredAtUtc;
        public List<AssetOperationItem> items = new List<AssetOperationItem>();
    }
}

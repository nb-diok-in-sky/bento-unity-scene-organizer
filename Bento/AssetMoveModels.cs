using System;
using System.Collections.Generic;
using UnityEngine;

namespace Bento
{
    public enum AssetMoveKind
    {
        PrefabOrModel,
        Material,
        Texture,
        Other
    }

    [Serializable]
    public class AssetMoveEntry
    {
        public UnityEngine.Object asset;
        public string sourcePath;
        public string sourceGuid;
        public string targetFolder;
        public string targetPath;
        public AssetMoveKind kind;
        public bool include = true;
        public bool isDependency;
        public string ownerPath;
        public string warning;
        public int prefabReferenceCount;
        public int materialReferenceCount;
        public int sceneReferenceCount;
        public bool keepOriginalNameBecauseShared;
        public List<string> referenceSamples = new List<string>();
    }

    [Serializable]
    public class AssetMoveHistoryEntry
    {
        public string guid;
        public string oldPath;
        public string newPath;
    }

    [Serializable]
    public class AssetMoveHistory
    {
        public string createdAtUtc;
        public List<AssetMoveHistoryEntry> moves = new List<AssetMoveHistoryEntry>();
    }
}

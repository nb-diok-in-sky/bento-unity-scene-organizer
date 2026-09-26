using System;
using System.Collections.Generic;
using UnityEngine;

namespace Bento
{
    enum AssetMoveKind
    {
        PrefabOrModel,
        Material,
        Texture,
        Folder,
        Other
    }

    [Serializable]
    class AssetMoveEntry
    {
        public UnityEngine.Object asset;
        public string sourcePath;
        public string sourceGuid;
        public string targetFolder;
        public string targetPath;
        public AssetMoveKind kind;
        public bool include = true;
        public string warning;
        public int prefabReferenceCount;
        public int materialReferenceCount;
        public int sceneReferenceCount;
        public bool keepOriginalNameBecauseShared;
        public List<string> referenceSamples = new List<string>();
    }

    [Serializable]
    class AssetMoveHistoryEntry
    {
        public string guid;
        public string oldPath;
        public string newPath;
    }

    [Serializable]
    class AssetMoveHistory
    {
        public string createdAtUtc;
        public List<AssetMoveHistoryEntry> moves = new List<AssetMoveHistoryEntry>();
    }

    [Serializable]
    class AssetMoveManifest
    {
        public string createdAtUtc;
        public string projectRoot;
        public List<AssetMoveHistoryEntry> moves = new List<AssetMoveHistoryEntry>();
    }
}

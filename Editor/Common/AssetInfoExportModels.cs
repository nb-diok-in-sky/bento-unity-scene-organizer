using System;
using System.Collections.Generic;
using UnityEngine;

namespace Bento
{
    [Serializable]
    class AssetInfoExportDocument
    {
        public string schemaVersion = "bento.asset-info.v1";
        public string exportedAtUtc;
        public List<AssetInfoRecord> assets = new List<AssetInfoRecord>();
    }

    [Serializable]
    class AssetInfoRecord
    {
        public string originalName;
        public string inferredObject = "环境资产/待 AI 或人工补充语义";
        public string format;
        public AssetInfoBounds boundsSize;
        public string path;
        public string sourceAsset;
        public bool skippedBecauseSameNamePrefabExists;
        public string verification = "loaded_by_unity_assetdatabase_and_bounds_from_renderers";
        public int nodeCount;
        public int rendererCount;
        public int materialCount;
        public int textureCount;
        public int colliderCount;
        public List<AssetInfoNode> nodes = new List<AssetInfoNode>();
    }

    [Serializable]
    class AssetInfoBounds
    {
        public float x;
        public float y;
        public float z;
        public string unit = "Unity world unit";
        public string source = "Renderer.bounds.size";
    }

    [Serializable]
    class AssetInfoNode
    {
        public string name;
        public string hierarchyPath;
        public bool activeSelf;
        public Vector3 localPosition;
        public Vector3 localEulerAngles;
        public Vector3 localScale;
        public string mesh;
        public string meshAssetPath;
        public string rendererType;
        public List<AssetInfoMaterial> materials = new List<AssetInfoMaterial>();
        public List<AssetInfoCollider> colliders = new List<AssetInfoCollider>();
        public List<string> components = new List<string>();
    }

    [Serializable]
    class AssetInfoMaterial
    {
        public string name;
        public string assetPath;
        public string shader;
        public List<AssetInfoTexture> textures = new List<AssetInfoTexture>();
    }

    [Serializable]
    class AssetInfoTexture
    {
        public string property;
        public string name;
        public string assetPath;
    }

    [Serializable]
    class AssetInfoCollider
    {
        public string type;
        public bool enabled;
        public bool isTrigger;
    }
}

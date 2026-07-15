using System;
using System.Collections.Generic;
using UnityEngine;

namespace Bento
{
    enum VariantTextureKind
    {
        BaseColor,
        Normal,
        Metallic,
        Occlusion,
        Emission,
        Height,
        Mask,
        Other
    }

    [Serializable]
    class ResourceVariantPreset
    {
        public string name = "默认预设";
        public bool copyPrefab = true;
        public bool copyMaterials = true;
        public bool copyTextures;
        public bool replaceSceneObjects = true;
        public string prefabFolder = "Assets/Variants/Prefabs";
        public string materialFolder = "Assets/Variants/Materials";
        public string textureFolder = "Assets/Variants/Textures";
        public string prefix = "";
        public string suffix = "_Variant";
        public string baseName = "";
        public bool appendNumber;
        public int startNumber = 1;
        public int numberDigits = 2;
        public bool copyBaseColor = true;
        public bool copyNormal = true;
        public bool copyMetallic = true;
        public bool copyOcclusion = true;
        public bool copyEmission = true;
        public bool copyHeight = true;
        public bool copyMask = true;
        public bool copyOtherTextures = true;
        public List<string> excludedFolders = new List<string>();
    }

    [Serializable]
    class ResourceVariantPresetList
    {
        public List<ResourceVariantPreset> items = new List<ResourceVariantPreset>();
    }

    [Serializable]
    class ResourceVariantSourceRecord
    {
        public string version = "1";
        public string sourceObjectName;
        public string sourceAssetGuid;
        public string sourceAssetPath;
        public string generatedPrefabGuid;
        public string generatedPrefabPath;
        public string presetName;
        public string generatedAtUtc;
        public List<string> materialPaths = new List<string>();
        public List<string> texturePaths = new List<string>();
    }

    [Serializable]
    class ResourceVariantSourceRecordList
    {
        public List<ResourceVariantSourceRecord> items = new List<ResourceVariantSourceRecord>();
    }

    class ResourceVariantTexturePlan
    {
        public Texture source;
        public string sourcePath;
        public string propertyName;
        public string propertyDescription;
        public VariantTextureKind kind;
        public bool willCopy;
        public string reason;
        public bool isRuntimeTexture;
    }

    class ResourceVariantMaterialPlan
    {
        public Material source;
        public string sourcePath;
        public bool willCopy;
        public string reason;
        public readonly List<ResourceVariantTexturePlan> textures = new List<ResourceVariantTexturePlan>();
    }

    class ResourceVariantObjectPlan
    {
        public GameObject sourceObject;
        public string sourceAssetPath;
        public string prefabOutputName;
        public int namingIndex;
        public readonly List<ResourceVariantMaterialPlan> materials = new List<ResourceVariantMaterialPlan>();
    }

    class ResourceVariantPlan
    {
        public readonly List<ResourceVariantObjectPlan> objects = new List<ResourceVariantObjectPlan>();
        public readonly List<string> warnings = new List<string>();

        public int MaterialCopyCount => CountUniqueMaterials(plan => plan.willCopy);
        public int TextureCopyCount => CountUniqueTextures(plan => plan.willCopy);

        int CountUniqueMaterials(Func<ResourceVariantMaterialPlan, bool> predicate)
        {
            var seen = new HashSet<Material>();
            foreach (var objectPlan in objects)
            foreach (var materialPlan in objectPlan.materials)
            {
                if (materialPlan.source != null && predicate(materialPlan))
                    seen.Add(materialPlan.source);
            }
            return seen.Count;
        }

        int CountUniqueTextures(Func<ResourceVariantTexturePlan, bool> predicate)
        {
            var seen = new HashSet<Texture>();
            foreach (var objectPlan in objects)
            foreach (var materialPlan in objectPlan.materials)
            foreach (var texturePlan in materialPlan.textures)
            {
                if (texturePlan.source != null && predicate(texturePlan))
                    seen.Add(texturePlan.source);
            }
            return seen.Count;
        }
    }

    class ResourceVariantGenerationResult
    {
        public readonly List<string> createdAssets = new List<string>();
        public readonly List<string> createdPrefabs = new List<string>();
        public readonly List<string> createdMaterials = new List<string>();
        public readonly List<string> createdTextures = new List<string>();
        public readonly List<GameObject> replacements = new List<GameObject>();
        public readonly List<string> healthWarnings = new List<string>();
        public int processedObjectCount;
        public readonly List<ResourceVariantSourceRecord> sourceRecords = new List<ResourceVariantSourceRecord>();
    }

    class ResourceVariantGenerationException : Exception
    {
        public readonly ResourceVariantGenerationResult result;

        public ResourceVariantGenerationException(Exception innerException, ResourceVariantGenerationResult result)
            : base(innerException.Message, innerException)
        {
            this.result = result;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
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

    enum VariantTextureDisposition
    {
        CopyCandidate,
        SharedShaderSupport,
        RuntimeInternal
    }

    [Serializable]
    class ResourceVariantPreset
    {
        public string name = "默认预设";
        public bool copyPrefab = true;
        public bool copyModels;
        public bool copyMaterials = true;
        public bool copyTextures;
        public bool replaceSceneObjects = true;
        public string prefabFolder = "Assets/Variants/Prefabs";
        public string modelFolder = "Assets/Variants/Models";
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

        public AssetNamingOptions GetNamingOptions() => new AssetNamingOptions
        {
            prefix = prefix,
            baseName = baseName,
            suffix = suffix,
            appendNumber = appendNumber,
            startNumber = startNumber,
            numberDigits = numberDigits
        };

        public void ApplyNaming(AssetNamingOptions options)
        {
            if (options == null) return;
            prefix = options.prefix;
            baseName = options.baseName;
            suffix = options.suffix;
            appendNumber = options.appendNumber;
            startNumber = options.startNumber;
            numberDigits = options.numberDigits;
        }

        public bool HasSameGenerationSettings(ResourceVariantPreset other)
        {
            if (other == null) return false;
            return copyPrefab == other.copyPrefab &&
                   copyModels == other.copyModels &&
                   copyMaterials == other.copyMaterials &&
                   copyTextures == other.copyTextures &&
                   replaceSceneObjects == other.replaceSceneObjects &&
                   string.Equals(prefabFolder, other.prefabFolder, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(modelFolder, other.modelFolder, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(materialFolder, other.materialFolder, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(textureFolder, other.textureFolder, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(prefix, other.prefix, StringComparison.Ordinal) &&
                   string.Equals(suffix, other.suffix, StringComparison.Ordinal) &&
                   string.Equals(baseName, other.baseName, StringComparison.Ordinal) &&
                   appendNumber == other.appendNumber &&
                   startNumber == other.startNumber &&
                   numberDigits == other.numberDigits &&
                   copyBaseColor == other.copyBaseColor &&
                   copyNormal == other.copyNormal &&
                   copyMetallic == other.copyMetallic &&
                   copyOcclusion == other.copyOcclusion &&
                   copyEmission == other.copyEmission &&
                   copyHeight == other.copyHeight &&
                   copyMask == other.copyMask &&
                   copyOtherTextures == other.copyOtherTextures &&
                   FolderListsEqual(excludedFolders, other.excludedFolders);
        }

        static bool FolderListsEqual(IReadOnlyList<string> first, IReadOnlyList<string> second)
        {
            first = first ?? Array.Empty<string>();
            second = second ?? Array.Empty<string>();
            return new HashSet<string>(first, StringComparer.OrdinalIgnoreCase)
                .SetEquals(second);
        }
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
        public List<string> modelPaths = new List<string>();
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
        public VariantTextureDisposition disposition;
        public bool willCopy;
        public string reason;
    }

    class ResourceVariantMaterialPlan
    {
        public Material source;
        public string sourcePath;
        public bool willCopy;
        public string reason;
        public readonly List<ResourceVariantTexturePlan> textures = new List<ResourceVariantTexturePlan>();
    }

    class ResourceVariantModelPlan
    {
        public UnityEngine.Object source;
        public string sourcePath;
        public bool willCopy;
        public string reason;
    }

    class ResourceVariantObjectPlan
    {
        public GameObject sourceObject;
        public readonly List<GameObject> sourceObjects = new List<GameObject>();
        public string sourceAssetPath;
        public string prefabOutputName;
        public int namingIndex;
        public readonly List<ResourceVariantModelPlan> models = new List<ResourceVariantModelPlan>();
        public readonly List<ResourceVariantMaterialPlan> materials = new List<ResourceVariantMaterialPlan>();
    }

    class ResourceVariantPlan
    {
        public ResourceVariantPreset settingsSnapshot;
        public readonly List<ResourceVariantObjectPlan> objects = new List<ResourceVariantObjectPlan>();
        public readonly List<string> warnings = new List<string>();

        public int ModelCopyCount => objects
            .SelectMany(item => item.models)
            .Where(item => item.willCopy)
            .Select(item => item.sourcePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        public int MaterialCopyCount => CountUniqueMaterials(plan => plan.willCopy);
        public int TextureCopyCount => CountUniqueTextures(plan => plan.willCopy);
        public int SelectedInstanceCount => objects.Sum(item =>
            item.sourceObjects.Count > 0 ? item.sourceObjects.Count : 1);

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
        public readonly List<string> createdModels = new List<string>();
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

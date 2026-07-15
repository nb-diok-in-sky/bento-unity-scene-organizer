using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Bento
{
    class ResourceVariantPlanner
    {
        readonly ResourceVariantPreset _settings;

        public ResourceVariantPlanner(ResourceVariantPreset settings)
        {
            _settings = settings;
        }

        public ResourceVariantPlan Build(IReadOnlyList<GameObject> selectedObjects)
        {
            var plan = new ResourceVariantPlan();
            for (int index = 0; index < selectedObjects.Count; index++)
            {
                var sourceObject = selectedObjects[index];
                var objectPlan = new ResourceVariantObjectPlan
                {
                    sourceObject = sourceObject,
                    sourceAssetPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(sourceObject),
                    prefabOutputName = AssetNaming.BuildName(sourceObject.name, index, GetNamingOptions()),
                    namingIndex = index
                };
                AnalyzeMaterials(sourceObject, objectPlan, plan);
                plan.objects.Add(objectPlan);
            }
            return plan;
        }

        void AnalyzeMaterials(GameObject sourceObject, ResourceVariantObjectPlan objectPlan, ResourceVariantPlan plan)
        {
            var seenMaterials = new HashSet<Material>();
            foreach (var renderer in sourceObject.GetComponentsInChildren<Renderer>(true))
            foreach (var material in renderer.sharedMaterials)
            {
                if (material == null || !seenMaterials.Add(material)) continue;
                string materialPath = AssetDatabase.GetAssetPath(material);
                var materialPlan = new ResourceVariantMaterialPlan
                {
                    source = material,
                    sourcePath = materialPath,
                    willCopy = _settings.copyMaterials && IsCopyableAssetPath(materialPath) && !IsExcluded(materialPath)
                };
                materialPlan.reason = GetSkipReason(_settings.copyMaterials, materialPath);
                AnalyzeTextures(material, materialPlan, plan);
                objectPlan.materials.Add(materialPlan);
            }
        }

        void AnalyzeTextures(Material material, ResourceVariantMaterialPlan materialPlan, ResourceVariantPlan plan)
        {
            if (material.shader == null) return;
            int propertyCount = ShaderUtil.GetPropertyCount(material.shader);
            for (int propertyIndex = 0; propertyIndex < propertyCount; propertyIndex++)
            {
                if (ShaderUtil.GetPropertyType(material.shader, propertyIndex) != ShaderUtil.ShaderPropertyType.TexEnv) continue;
                string propertyName = ShaderUtil.GetPropertyName(material.shader, propertyIndex);
                var texture = material.GetTexture(propertyName);
                if (texture == null) continue;

                string description = ShaderUtil.GetPropertyDescription(material.shader, propertyIndex);
                string texturePath = AssetDatabase.GetAssetPath(texture);
                bool isRuntimeTexture = string.IsNullOrEmpty(texturePath);
                var kind = ResourceVariantTextureClassifier.Classify(material.shader, propertyIndex, propertyName, description, texture);
                bool kindEnabled = IsTextureKindEnabled(kind);
                bool willCopy = _settings.copyTextures && materialPlan.willCopy && kindEnabled && IsCopyableAssetPath(texturePath) && !IsExcluded(texturePath);
                materialPlan.textures.Add(new ResourceVariantTexturePlan
                {
                    source = texture,
                    sourcePath = texturePath,
                    propertyName = propertyName,
                    propertyDescription = description,
                    kind = kind,
                    willCopy = willCopy,
                    reason = willCopy ? string.Empty : GetTextureSkipReason(materialPlan, kindEnabled, texturePath),
                    isRuntimeTexture = isRuntimeTexture
                });
                if (kind == VariantTextureKind.Other && !isRuntimeTexture)
                    plan.warnings.Add($"无法确定贴图语义：{material.name}.{propertyName} → {texture.name}，按“其他”设置处理。");
            }
        }

        bool IsTextureKindEnabled(VariantTextureKind kind)
        {
            switch (kind)
            {
                case VariantTextureKind.BaseColor: return _settings.copyBaseColor;
                case VariantTextureKind.Normal: return _settings.copyNormal;
                case VariantTextureKind.Metallic: return _settings.copyMetallic;
                case VariantTextureKind.Occlusion: return _settings.copyOcclusion;
                case VariantTextureKind.Emission: return _settings.copyEmission;
                case VariantTextureKind.Height: return _settings.copyHeight;
                case VariantTextureKind.Mask: return _settings.copyMask;
                default: return _settings.copyOtherTextures;
            }
        }

        bool IsExcluded(string path)
        {
            if (string.IsNullOrEmpty(path) || _settings.excludedFolders == null) return false;
            return _settings.excludedFolders.Any(folder =>
                path.Equals(folder, StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith(folder.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase));
        }

        string GetSkipReason(bool enabled, string path)
        {
            if (!enabled) return "未勾选";
            if (IsExcluded(path)) return "公共目录排除";
            if (!IsCopyableAssetPath(path)) return "内置/Package/运行时资源";
            return string.Empty;
        }

        string GetTextureSkipReason(ResourceVariantMaterialPlan materialPlan, bool kindEnabled, string path)
        {
            if (!_settings.copyTextures) return "未勾选贴图复制";
            if (!materialPlan.willCopy) return "材质保持共享";
            if (!kindEnabled) return "该贴图类型未勾选";
            if (IsExcluded(path)) return "公共目录排除";
            if (string.IsNullOrEmpty(path)) return "Shader 运行时内部纹理，保留引用";
            if (!IsCopyableAssetPath(path)) return "内置/Package/运行时资源";
            return string.Empty;
        }

        AssetNamingOptions GetNamingOptions()
        {
            return new AssetNamingOptions
            {
                prefix = _settings.prefix,
                baseName = _settings.baseName,
                suffix = _settings.suffix,
                appendNumber = _settings.appendNumber,
                startNumber = _settings.startNumber,
                numberDigits = _settings.numberDigits
            };
        }

        static bool IsCopyableAssetPath(string path)
        {
            return !string.IsNullOrEmpty(path) && path.StartsWith("Assets/", StringComparison.Ordinal);
        }
    }
}

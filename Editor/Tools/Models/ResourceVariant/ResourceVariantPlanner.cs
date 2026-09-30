using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
            var plan = new ResourceVariantPlan
            {
                settingsSnapshot = JsonUtility.FromJson<ResourceVariantPreset>(JsonUtility.ToJson(_settings))
            };
            var groupedPlans = new Dictionary<string, ResourceVariantObjectPlan>(
                StringComparer.Ordinal);
            for (int index = 0; index < selectedObjects.Count; index++)
            {
                var sourceObject = selectedObjects[index];
                string sourceAssetPath =
                    PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(sourceObject);
                string groupKey = BuildPrefabVariantGroupKey(
                    sourceObject,
                    sourceAssetPath,
                    index);
                if (groupedPlans.TryGetValue(groupKey, out ResourceVariantObjectPlan existing))
                {
                    existing.sourceObjects.Add(sourceObject);
                    continue;
                }

                var objectPlan = new ResourceVariantObjectPlan
                {
                    sourceObject = sourceObject,
                    sourceAssetPath = sourceAssetPath,
                    prefabOutputName = AssetNaming.BuildName(
                        sourceObject.name,
                        groupedPlans.Count,
                        _settings.GetNamingOptions()),
                    namingIndex = groupedPlans.Count
                };
                objectPlan.sourceObjects.Add(sourceObject);
                AnalyzeModels(sourceObject, objectPlan);
                AnalyzeMaterials(sourceObject, objectPlan, plan);
                groupedPlans.Add(groupKey, objectPlan);
            }
            plan.objects.AddRange(groupedPlans.Values);
            return plan;
        }

        static string BuildPrefabVariantGroupKey(
            GameObject sourceObject,
            string sourceAssetPath,
            int uniqueIndex)
        {
            GameObject instanceRoot =
                PrefabUtility.GetNearestPrefabInstanceRoot(sourceObject);
            if (instanceRoot == null ||
                instanceRoot != sourceObject ||
                string.IsNullOrEmpty(sourceAssetPath))
                return $"unique:{uniqueIndex}";

            var signature = new StringBuilder(sourceAssetPath);
            PropertyModification[] modifications =
                PrefabUtility.GetPropertyModifications(instanceRoot);
            if (modifications != null)
            {
                foreach (PropertyModification modification in modifications
                             .Where(IsContentOverride)
                             .OrderBy(GetModificationSortKey, StringComparer.Ordinal))
                {
                    signature.Append("|p:")
                        .Append(GetRelativeTargetPath(instanceRoot, modification.target))
                        .Append(':')
                        .Append(modification.propertyPath)
                        .Append('=')
                        .Append(modification.value)
                        .Append('@')
                        .Append(modification.objectReference == null
                            ? string.Empty
                            : GlobalObjectId.GetGlobalObjectIdSlow(
                                modification.objectReference).ToString());
                }
            }

            foreach (var added in PrefabUtility
                         .GetAddedComponents(instanceRoot)
                         .OrderBy(item => GetRelativeTargetPath(
                             instanceRoot,
                             item.instanceComponent),
                             StringComparer.Ordinal))
            {
                signature.Append("|ac:")
                    .Append(GetRelativeTargetPath(
                        instanceRoot,
                        added.instanceComponent))
                    .Append(':')
                    .Append(added.instanceComponent == null
                        ? string.Empty
                        : added.instanceComponent.GetType().AssemblyQualifiedName);
            }
            foreach (var added in PrefabUtility
                         .GetAddedGameObjects(instanceRoot)
                         .OrderBy(item => GetRelativeTargetPath(
                             instanceRoot,
                             item.instanceGameObject),
                             StringComparer.Ordinal))
            {
                signature.Append("|ag:")
                    .Append(GetRelativeTargetPath(
                        instanceRoot,
                        added.instanceGameObject));
            }
            foreach (var removed in PrefabUtility
                         .GetRemovedComponents(instanceRoot)
                         .OrderBy(item => GetRelativeTargetPath(
                             instanceRoot,
                             item.assetComponent),
                             StringComparer.Ordinal))
            {
                signature.Append("|rc:")
                    .Append(GetRelativeTargetPath(
                        instanceRoot,
                        removed.assetComponent))
                    .Append(':')
                    .Append(removed.assetComponent == null
                        ? string.Empty
                        : removed.assetComponent.GetType().AssemblyQualifiedName);
            }
            foreach (var removed in PrefabUtility
                         .GetRemovedGameObjects(instanceRoot)
                         .OrderBy(item => GetRelativeTargetPath(
                             instanceRoot,
                             item.assetGameObject),
                             StringComparer.Ordinal))
            {
                signature.Append("|rg:")
                    .Append(GetRelativeTargetPath(
                        instanceRoot,
                        removed.assetGameObject));
            }
            return signature.ToString();
        }

        static bool IsContentOverride(PropertyModification modification)
        {
            if (modification == null || modification.target == null) return false;
            string path = modification.propertyPath ?? string.Empty;
            if (!(modification.target is Transform)) return true;
            return path != "m_LocalPosition.x" &&
                   path != "m_LocalPosition.y" &&
                   path != "m_LocalPosition.z" &&
                   path != "m_LocalRotation.x" &&
                   path != "m_LocalRotation.y" &&
                   path != "m_LocalRotation.z" &&
                   path != "m_LocalRotation.w" &&
                   path != "m_LocalEulerAnglesHint.x" &&
                   path != "m_LocalEulerAnglesHint.y" &&
                   path != "m_LocalEulerAnglesHint.z" &&
                   path != "m_LocalScale.x" &&
                   path != "m_LocalScale.y" &&
                   path != "m_LocalScale.z" &&
                   path != "m_RootOrder";
        }

        static string GetModificationSortKey(PropertyModification modification)
        {
            return $"{modification.target?.name}|{modification.propertyPath}|{modification.value}";
        }

        static string GetRelativeTargetPath(
            GameObject instanceRoot,
            UnityEngine.Object target)
        {
            Transform transform = null;
            if (target is GameObject gameObject)
                transform = gameObject.transform;
            else if (target is Component component)
                transform = component.transform;
            if (transform == null) return target == null ? string.Empty : target.name;
            string path = AnimationUtility.CalculateTransformPath(
                transform,
                instanceRoot.transform);
            return target is Component componentTarget
                ? $"{path}#{componentTarget.GetType().FullName}"
                : path;
        }

        void AnalyzeModels(
            GameObject sourceObject,
            ResourceVariantObjectPlan objectPlan)
        {
            var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Renderer renderer in sourceObject.GetComponentsInChildren<Renderer>(true))
            {
                Mesh mesh = renderer is SkinnedMeshRenderer skinned
                    ? skinned.sharedMesh
                    : renderer.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh == null) continue;

                string path = AssetDatabase.GetAssetPath(mesh);
                if (string.IsNullOrEmpty(path) || !seenPaths.Add(path))
                    continue;

                UnityEngine.Object mainAsset = AssetDatabase.LoadMainAssetAtPath(path);
                bool isModelAsset =
                    mainAsset is GameObject &&
                    (path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase) ||
                     path.EndsWith(".obj", StringComparison.OrdinalIgnoreCase)) ||
                    mainAsset is Mesh &&
                    path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase);
                bool willCopy = _settings.copyModels &&
                                isModelAsset &&
                                IsCopyableAssetPath(path) &&
                                !IsExcluded(path);
                objectPlan.models.Add(new ResourceVariantModelPlan
                {
                    source = mainAsset,
                    sourcePath = path,
                    willCopy = willCopy,
                    reason = willCopy
                        ? string.Empty
                        : !_settings.copyModels
                            ? "未勾选模型复制"
                            : IsExcluded(path)
                                ? "公共目录排除"
                                : "不是可复制的 FBX / OBJ / Mesh .asset 模型主资产"
                });
            }
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
                    willCopy = true,
                    reason = string.Empty
                };
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
                string propertyDescription = ShaderUtil.GetPropertyDescription(material.shader, propertyIndex);
                var texture = material.GetTexture(propertyName);
                if (texture == null) continue;

                string texturePath = AssetDatabase.GetAssetPath(texture);
                VariantTextureDisposition disposition = ResourceVariantTextureClassifier.GetDisposition(texture);
                VariantTextureKind kind = ResourceVariantTextureClassifier.Classify(
                    material.shader,
                    propertyIndex,
                    propertyName,
                    propertyDescription,
                    texture);
                bool willCopy = disposition == VariantTextureDisposition.CopyCandidate &&
                                _settings.copyTextures &&
                                materialPlan.willCopy &&
                                ShouldCopyTextureKind(kind) &&
                                IsCopyableAssetPath(texturePath) && !IsExcluded(texturePath);
                materialPlan.textures.Add(new ResourceVariantTexturePlan
                {
                    source = texture,
                    sourcePath = texturePath,
                    propertyName = propertyName,
                    propertyDescription = propertyDescription,
                    kind = kind,
                    disposition = disposition,
                    willCopy = willCopy,
                    reason = willCopy
                        ? string.Empty
                        : GetTextureSkipReason(materialPlan, texturePath, kind, disposition)
                });
            }
        }

        bool IsExcluded(string path)
        {
            if (string.IsNullOrEmpty(path) || _settings.excludedFolders == null) return false;
            return _settings.excludedFolders.Any(folder =>
                path.Equals(folder, StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith(folder.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase));
        }

        string GetTextureSkipReason(
            ResourceVariantMaterialPlan materialPlan,
            string path,
            VariantTextureKind kind,
            VariantTextureDisposition disposition)
        {
            if (disposition == VariantTextureDisposition.RuntimeInternal)
                return "运行时/内部贴图";
            if (!_settings.copyTextures) return "未勾选贴图复制";
            if (!materialPlan.willCopy) return "材质保持共享";
            if (!ShouldCopyTextureKind(kind))
                return $"未勾选{ResourceVariantTextureClassifier.GetDisplayName(kind)}贴图复制";
            if (IsExcluded(path)) return "公共目录排除";
            if (!IsCopyableAssetPath(path)) return "内置/Package/运行时资源";
            return string.Empty;
        }

        bool ShouldCopyTextureKind(VariantTextureKind kind)
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

        static bool IsCopyableAssetPath(string path)
        {
            return !string.IsNullOrEmpty(path) && path.StartsWith("Assets/", StringComparison.Ordinal);
        }
    }
}

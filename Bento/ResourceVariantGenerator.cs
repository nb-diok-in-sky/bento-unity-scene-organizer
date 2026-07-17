using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Bento
{
    class ResourceVariantGenerator
    {
        readonly ResourceVariantPreset _settings;

        public ResourceVariantGenerator(ResourceVariantPreset settings)
        {
            _settings = settings;
        }

        public ResourceVariantGenerationResult Generate(ResourceVariantPlan plan)
        {
            var result = new ResourceVariantGenerationResult();
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("生成场景资源变体");
            try
            {
                foreach (var objectPlan in plan.objects) ProcessObject(objectPlan, result);
                result.processedObjectCount = plan.objects.Count;
                RunHealthCheck(result);
            }
            catch (Exception exception)
            {
                throw new ResourceVariantGenerationException(exception, result);
            }
            finally
            {
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Undo.CollapseUndoOperations(undoGroup);
            }
            return result;
        }

        void ProcessObject(ResourceVariantObjectPlan objectPlan, ResourceVariantGenerationResult result)
        {
            int materialStartIndex = result.createdMaterials.Count;
            int textureStartIndex = result.createdTextures.Count;
            var temporaryObject = UnityEngine.Object.Instantiate(objectPlan.sourceObject);
            temporaryObject.name = objectPlan.prefabOutputName;
            try
            {
                RelinkMaterials(temporaryObject, objectPlan, result);
                AssetDatabase.SaveAssets();
                string prefabPath = BentoUtility.GenerateUniqueAssetPath(_settings.prefabFolder, temporaryObject.name, ".prefab");
                var savedPrefab = PrefabUtility.SaveAsPrefabAsset(temporaryObject, prefabPath);
                if (savedPrefab == null) throw new InvalidOperationException($"Prefab 保存失败：{prefabPath}");
                AddCreatedAsset(result, prefabPath, result.createdPrefabs);
                ValidateMaterialIsolation(savedPrefab, objectPlan);
                result.sourceRecords.Add(BuildSourceRecord(objectPlan, result, prefabPath, materialStartIndex, textureStartIndex));
                if (_settings.replaceSceneObjects) result.replacements.Add(ReplaceSceneObject(objectPlan.sourceObject, savedPrefab));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(temporaryObject);
            }
        }

        void RelinkMaterials(GameObject temporaryObject, ResourceVariantObjectPlan objectPlan, ResourceVariantGenerationResult result)
        {
            var materialCopies = new Dictionary<Material, Material>();
            var textureCopies = new Dictionary<Texture, Texture>();
            var materialPlans = objectPlan.materials.ToDictionary(plan => plan.source);
            foreach (var renderer in temporaryObject.GetComponentsInChildren<Renderer>(true))
            {
                var materials = renderer.sharedMaterials;
                for (int slot = 0; slot < materials.Length; slot++)
                {
                    var sourceMaterial = materials[slot];
                    if (sourceMaterial == null || !materialPlans.TryGetValue(sourceMaterial, out var materialPlan) || !materialPlan.willCopy) continue;
                    if (!materialCopies.TryGetValue(sourceMaterial, out var copiedMaterial))
                    {
                        copiedMaterial = CopyMaterial(materialPlan, objectPlan.prefabOutputName, textureCopies, result);
                        materialCopies[sourceMaterial] = copiedMaterial;
                    }
                    materials[slot] = copiedMaterial;
                }
                renderer.sharedMaterials = materials;
            }
        }

        Material CopyMaterial(ResourceVariantMaterialPlan plan, string ownerName, Dictionary<Texture, Texture> textureCopies, ResourceVariantGenerationResult result)
        {
            var copy = new Material(plan.source) { name = BuildDependentName(ownerName, plan.source.name, "Material") };
            string materialPath = BentoUtility.GenerateUniqueAssetPath(_settings.materialFolder, copy.name, ".mat");
            AssetDatabase.CreateAsset(copy, materialPath);
            AddCreatedAsset(result, materialPath, result.createdMaterials);
            foreach (var texturePlan in plan.textures.Where(texturePlan => texturePlan.willCopy))
            {
                if (!textureCopies.TryGetValue(texturePlan.source, out var copiedTexture))
                {
                    copiedTexture = CopyTexture(texturePlan, ownerName, result);
                    textureCopies[texturePlan.source] = copiedTexture;
                }
                copy.SetTexture(texturePlan.propertyName, copiedTexture);
            }
            EditorUtility.SetDirty(copy);
            return copy;
        }

        Texture CopyTexture(ResourceVariantTexturePlan plan, string ownerName, ResourceVariantGenerationResult result)
        {
            string extension = Path.GetExtension(plan.sourcePath);
            string targetPath = BentoUtility.GenerateUniqueAssetPath(
                _settings.textureFolder,
                BuildDependentName(ownerName, plan.source.name, "Texture"),
                extension);
            if (!AssetDatabase.CopyAsset(plan.sourcePath, targetPath))
                throw new InvalidOperationException($"贴图复制失败：{plan.sourcePath} → {targetPath}");
            AddCreatedAsset(result, targetPath, result.createdTextures);
            return AssetDatabase.LoadAssetAtPath<Texture>(targetPath);
        }

        static GameObject ReplaceSceneObject(GameObject oldObject, GameObject prefabAsset)
        {
            var oldTransform = oldObject.transform;
            var replacement = PrefabUtility.InstantiatePrefab(prefabAsset, oldObject.scene) as GameObject;
            if (replacement == null) throw new InvalidOperationException($"新 Prefab 实例化失败：{prefabAsset.name}");
            Undo.RegisterCreatedObjectUndo(replacement, "创建资源变体实例");
            Undo.SetTransformParent(replacement.transform, oldTransform.parent, "设置资源变体父节点");
            Undo.RecordObject(replacement.transform, "恢复资源变体 Transform");
            replacement.transform.SetSiblingIndex(oldTransform.GetSiblingIndex());
            replacement.transform.localPosition = oldTransform.localPosition;
            replacement.transform.localRotation = oldTransform.localRotation;
            replacement.transform.localScale = oldTransform.localScale;
            Undo.RecordObject(replacement, "恢复资源变体激活状态");
            replacement.SetActive(oldObject.activeSelf);
            Undo.DestroyObjectImmediate(oldObject);
            EditorSceneManager.MarkSceneDirty(replacement.scene);
            return replacement;
        }

        static ResourceVariantSourceRecord BuildSourceRecord(
            ResourceVariantObjectPlan objectPlan,
            ResourceVariantGenerationResult result,
            string prefabPath,
            int materialStartIndex,
            int textureStartIndex)
        {
            return new ResourceVariantSourceRecord
            {
                sourceObjectName = objectPlan.sourceObject.name,
                sourceAssetGuid = AssetDatabase.AssetPathToGUID(objectPlan.sourceAssetPath),
                sourceAssetPath = objectPlan.sourceAssetPath,
                generatedPrefabGuid = AssetDatabase.AssetPathToGUID(prefabPath),
                generatedPrefabPath = prefabPath,
                generatedAtUtc = DateTime.UtcNow.ToString("O"),
                materialPaths = result.createdMaterials.Skip(materialStartIndex).ToList(),
                texturePaths = result.createdTextures.Skip(textureStartIndex).ToList()
            };
        }

        static void ValidateMaterialIsolation(GameObject prefab, ResourceVariantObjectPlan objectPlan)
        {
            var sourceMaterials = new HashSet<Material>(objectPlan.materials.Select(plan => plan.source));
            foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true))
            foreach (var material in renderer.sharedMaterials)
            {
                if (material != null && sourceMaterials.Contains(material))
                {
                    throw new InvalidOperationException(
                        $"材质隔离失败：新 Prefab {prefab.name}/{renderer.name} 仍引用原材质 {material.name}。");
                }
            }
        }

        static void RunHealthCheck(ResourceVariantGenerationResult result)
        {
            foreach (string prefabPath in result.createdPrefabs)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefab == null) { result.healthWarnings.Add($"Prefab 无法加载：{prefabPath}"); continue; }
                foreach (var component in prefab.GetComponentsInChildren<Component>(true))
                    if (component == null) result.healthWarnings.Add($"Missing Script：{prefabPath}");
                foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true))
                foreach (var material in renderer.sharedMaterials)
                {
                    if (material == null) result.healthWarnings.Add($"Missing Material：{prefabPath}/{renderer.name}");
                    else if (material.shader == null) result.healthWarnings.Add($"Missing Shader：{AssetDatabase.GetAssetPath(material)}");
                }
            }
        }

        static void AddCreatedAsset(ResourceVariantGenerationResult result, string path, List<string> typedList)
        {
            result.createdAssets.Add(path);
            typedList.Add(path);
        }

        public static string BuildDependentName(string ownerName, string sourceName, string fallback)
        {
            string roleName = BentoUtility.SanitizeFileName(sourceName, fallback);
            return BentoUtility.SanitizeFileName($"{ownerName}_{roleName}");
        }
    }
}

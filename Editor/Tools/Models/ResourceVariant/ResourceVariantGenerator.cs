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
        readonly Dictionary<string, CopiedModelAsset> _copiedModels =
            new Dictionary<string, CopiedModelAsset>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<Material, Material> _copiedMaterials =
            new Dictionary<Material, Material>();
        readonly Dictionary<Texture, Texture> _copiedTextures =
            new Dictionary<Texture, Texture>();

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
                for (int index = 0; index < plan.objects.Count; index++)
                {
                    ResourceVariantObjectPlan objectPlan = plan.objects[index];
                    EditorUtility.DisplayProgressBar(
                        "生成资源变体",
                        $"正在处理 {index + 1}/{plan.objects.Count}：{objectPlan.sourceObject.name}",
                        plan.objects.Count == 0 ? 1f : (float)index / plan.objects.Count);
                    ProcessObject(objectPlan, result);
                }
                result.processedObjectCount = plan.objects.Count;
                RunHealthCheck(result);
            }
            catch (Exception exception)
            {
                throw new ResourceVariantGenerationException(exception, result);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Undo.CollapseUndoOperations(undoGroup);
            }
            return result;
        }

        void ProcessObject(ResourceVariantObjectPlan objectPlan, ResourceVariantGenerationResult result)
        {
            int modelStartIndex = result.createdModels.Count;
            int materialStartIndex = result.createdMaterials.Count;
            int textureStartIndex = result.createdTextures.Count;
            var temporaryObject = UnityEngine.Object.Instantiate(objectPlan.sourceObject);
            temporaryObject.name = objectPlan.prefabOutputName;
            try
            {
                RelinkModels(temporaryObject, objectPlan, result);
                RelinkMaterials(temporaryObject, objectPlan, result);
                string prefabPath = BentoUtility.GenerateUniqueAssetPath(_settings.prefabFolder, temporaryObject.name, ".prefab");
                var savedPrefab = PrefabUtility.SaveAsPrefabAsset(temporaryObject, prefabPath);
                if (savedPrefab == null) throw new InvalidOperationException($"Prefab 保存失败：{prefabPath}");
                AddCreatedAsset(result, prefabPath, result.createdPrefabs);
                ValidateModelIsolation(savedPrefab, objectPlan);
                ValidateMaterialIsolation(savedPrefab, objectPlan);
                result.sourceRecords.Add(BuildSourceRecord(
                    objectPlan,
                    result,
                    prefabPath,
                    modelStartIndex,
                    materialStartIndex,
                    textureStartIndex));
                if (_settings.replaceSceneObjects)
                {
                    IReadOnlyList<GameObject> sources = objectPlan.sourceObjects.Count > 0
                        ? objectPlan.sourceObjects
                        : new[] { objectPlan.sourceObject };
                    foreach (GameObject source in sources)
                    {
                        ValidateReplacementSource(source, objectPlan);
                        result.replacements.Add(ReplaceSceneObject(source, savedPrefab));
                    }
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(temporaryObject);
            }
        }

        void RelinkModels(
            GameObject temporaryObject,
            ResourceVariantObjectPlan objectPlan,
            ResourceVariantGenerationResult result)
        {
            var plansByPath = objectPlan.models
                .Where(plan => plan.willCopy)
                .ToDictionary(plan => plan.sourcePath, StringComparer.OrdinalIgnoreCase);
            if (plansByPath.Count == 0) return;

            foreach (Renderer renderer in temporaryObject.GetComponentsInChildren<Renderer>(true))
            {
                Mesh sourceMesh = renderer is SkinnedMeshRenderer skinned
                    ? skinned.sharedMesh
                    : renderer.GetComponent<MeshFilter>()?.sharedMesh;
                if (sourceMesh == null) continue;

                string sourcePath = AssetDatabase.GetAssetPath(sourceMesh);
                if (!plansByPath.ContainsKey(sourcePath)) continue;
                if (!_copiedModels.TryGetValue(sourcePath, out CopiedModelAsset copiedModel))
                {
                    string targetPath = BentoUtility.GenerateUniqueAssetPath(
                        _settings.modelFolder,
                        BuildAssetName(
                            Path.GetFileNameWithoutExtension(sourcePath),
                            objectPlan.namingIndex,
                            _settings),
                        Path.GetExtension(sourcePath));
                    if (!AssetDatabase.CopyAsset(sourcePath, targetPath))
                        throw new InvalidOperationException(
                            $"模型复制失败：{sourcePath} → {targetPath}");

                    AddCreatedAsset(result, targetPath, result.createdModels);
                    AssetDatabase.ImportAsset(
                        targetPath,
                        ImportAssetOptions.ForceSynchronousImport);
                    Mesh[] copiedMeshes = AssetDatabase.LoadAllAssetsAtPath(targetPath)
                        .OfType<Mesh>()
                        .ToArray();
                    if (copiedMeshes.Length == 0)
                        throw new InvalidOperationException(
                            $"复制后的模型不包含可用 Mesh：{targetPath}");
                    copiedModel = new CopiedModelAsset(copiedMeshes);
                    _copiedModels[sourcePath] = copiedModel;
                }

                Mesh copiedMesh = FindMatchingMesh(copiedModel, sourceMesh);
                if (copiedMesh == null)
                    throw new InvalidOperationException(
                        $"复制后的模型找不到对应 Mesh：{sourceMesh.name}\n来源：{sourcePath}");

                if (renderer is SkinnedMeshRenderer targetSkinned)
                    targetSkinned.sharedMesh = copiedMesh;
                else
                {
                    MeshFilter filter = renderer.GetComponent<MeshFilter>();
                    if (filter != null) filter.sharedMesh = copiedMesh;
                }
            }
        }

        static Mesh FindMatchingMesh(CopiedModelAsset copiedModel, Mesh sourceMesh)
        {
            Mesh[] candidates = copiedModel.Meshes;
            return candidates.FirstOrDefault(mesh =>
                       string.Equals(mesh.name, sourceMesh.name, StringComparison.Ordinal) &&
                       mesh.vertexCount == sourceMesh.vertexCount &&
                       mesh.subMeshCount == sourceMesh.subMeshCount)
                   ?? candidates.FirstOrDefault(mesh =>
                       string.Equals(mesh.name, sourceMesh.name, StringComparison.Ordinal));
        }

        void RelinkMaterials(GameObject temporaryObject, ResourceVariantObjectPlan objectPlan, ResourceVariantGenerationResult result)
        {
            var materialPlans = objectPlan.materials.ToDictionary(plan => plan.source);
            foreach (var renderer in temporaryObject.GetComponentsInChildren<Renderer>(true))
            {
                var materials = renderer.sharedMaterials;
                for (int slot = 0; slot < materials.Length; slot++)
                {
                    var sourceMaterial = materials[slot];
                    if (sourceMaterial == null || !materialPlans.TryGetValue(sourceMaterial, out var materialPlan) || !materialPlan.willCopy) continue;
                    if (!_copiedMaterials.TryGetValue(sourceMaterial, out var copiedMaterial))
                    {
                        copiedMaterial = CopyMaterial(materialPlan, objectPlan, result);
                        _copiedMaterials[sourceMaterial] = copiedMaterial;
                    }
                    materials[slot] = copiedMaterial;
                }
                renderer.sharedMaterials = materials;
            }
        }

        Material CopyMaterial(
            ResourceVariantMaterialPlan plan,
            ResourceVariantObjectPlan objectPlan,
            ResourceVariantGenerationResult result)
        {
            var copy = new Material(plan.source)
            {
                name = BuildAssetName(plan.source.name, objectPlan.namingIndex, _settings)
            };
            string materialPath = BentoUtility.GenerateUniqueAssetPath(_settings.materialFolder, copy.name, ".mat");
            AssetDatabase.CreateAsset(copy, materialPath);
            AddCreatedAsset(result, materialPath, result.createdMaterials);
            foreach (var texturePlan in plan.textures.Where(texturePlan => texturePlan.willCopy))
            {
                if (!_copiedTextures.TryGetValue(texturePlan.source, out var copiedTexture))
                {
                    copiedTexture = CopyTexture(texturePlan, objectPlan, result);
                    _copiedTextures[texturePlan.source] = copiedTexture;
                }
                copy.SetTexture(texturePlan.propertyName, copiedTexture);
            }
            EditorUtility.SetDirty(copy);
            return copy;
        }

        Texture CopyTexture(
            ResourceVariantTexturePlan plan,
            ResourceVariantObjectPlan objectPlan,
            ResourceVariantGenerationResult result)
        {
            string extension = Path.GetExtension(plan.sourcePath);
            string targetPath = BentoUtility.GenerateUniqueAssetPath(
                _settings.textureFolder,
                BuildAssetName(plan.source.name, objectPlan.namingIndex, _settings),
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

        static void ValidateReplacementSource(
            GameObject source,
            ResourceVariantObjectPlan objectPlan)
        {
            if (source == null || !source.scene.IsValid())
                throw new InvalidOperationException(
                    $"待替换实例已删除或离开场景：{objectPlan.sourceObject?.name}");
            string currentSourcePath =
                PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(source);
            if (!string.Equals(
                    currentSourcePath,
                    objectPlan.sourceAssetPath,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"待替换实例的源 Prefab 已变化，已停止避免错位替换：\n" +
                    $"{source.name}\n计划：{objectPlan.sourceAssetPath}\n当前：{currentSourcePath}");
        }

        static ResourceVariantSourceRecord BuildSourceRecord(
            ResourceVariantObjectPlan objectPlan,
            ResourceVariantGenerationResult result,
            string prefabPath,
            int modelStartIndex,
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
                modelPaths = CollectReferencedAssetPaths<GameObject>(
                    prefabPath,
                    result.createdModels,
                    result.createdModels.Skip(modelStartIndex)),
                materialPaths = CollectReferencedAssetPaths<Material>(
                    prefabPath,
                    result.createdMaterials,
                    result.createdMaterials.Skip(materialStartIndex)),
                texturePaths = CollectReferencedTexturePaths(
                    prefabPath,
                    result.createdTextures,
                    result.createdTextures.Skip(textureStartIndex)),
                generatedAssetPaths = result.createdAssets.ToList(),
                generatedAssetGuids = result.createdAssets.Select(path => result.createdAssetGuids[path]).ToList()
            };
        }

        static List<string> CollectReferencedAssetPaths<T>(
            string prefabPath,
            IReadOnlyCollection<string> generatedPaths,
            IEnumerable<string> newlyCreatedPaths)
            where T : UnityEngine.Object
        {
            var paths = new HashSet<string>(
                newlyCreatedPaths,
                StringComparer.OrdinalIgnoreCase);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null) return paths.ToList();

            if (typeof(T) == typeof(Material))
            {
                foreach (Renderer renderer in prefab.GetComponentsInChildren<Renderer>(true))
                foreach (Material material in renderer.sharedMaterials)
                {
                    string path = material == null ? string.Empty : AssetDatabase.GetAssetPath(material);
                    if (generatedPaths.Contains(path, StringComparer.OrdinalIgnoreCase)) paths.Add(path);
                }
            }
            else
            {
                foreach (Renderer renderer in prefab.GetComponentsInChildren<Renderer>(true))
                {
                    Mesh mesh = renderer is SkinnedMeshRenderer skinned
                        ? skinned.sharedMesh
                        : renderer.GetComponent<MeshFilter>()?.sharedMesh;
                    string path = mesh == null ? string.Empty : AssetDatabase.GetAssetPath(mesh);
                    if (generatedPaths.Contains(path, StringComparer.OrdinalIgnoreCase)) paths.Add(path);
                }
            }
            return paths.ToList();
        }

        static List<string> CollectReferencedTexturePaths(
            string prefabPath,
            IReadOnlyCollection<string> generatedPaths,
            IEnumerable<string> newlyCreatedPaths)
        {
            var paths = new HashSet<string>(
                newlyCreatedPaths,
                StringComparer.OrdinalIgnoreCase);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null) return paths.ToList();

            foreach (Renderer renderer in prefab.GetComponentsInChildren<Renderer>(true))
            foreach (Material material in renderer.sharedMaterials)
            {
                if (material == null || material.shader == null) continue;
                int count = ShaderUtil.GetPropertyCount(material.shader);
                for (int index = 0; index < count; index++)
                {
                    if (ShaderUtil.GetPropertyType(material.shader, index) != ShaderUtil.ShaderPropertyType.TexEnv)
                        continue;
                    Texture texture = material.GetTexture(ShaderUtil.GetPropertyName(material.shader, index));
                    string path = texture == null ? string.Empty : AssetDatabase.GetAssetPath(texture);
                    if (generatedPaths.Contains(path, StringComparer.OrdinalIgnoreCase)) paths.Add(path);
                }
            }
            return paths.ToList();
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

        static void ValidateModelIsolation(
            GameObject prefab,
            ResourceVariantObjectPlan objectPlan)
        {
            var sourceModelPaths = new HashSet<string>(
                objectPlan.models
                    .Where(plan => plan.willCopy)
                    .Select(plan => plan.sourcePath),
                StringComparer.OrdinalIgnoreCase);
            if (sourceModelPaths.Count == 0) return;

            foreach (Renderer renderer in prefab.GetComponentsInChildren<Renderer>(true))
            {
                Mesh mesh = renderer is SkinnedMeshRenderer skinned
                    ? skinned.sharedMesh
                    : renderer.GetComponent<MeshFilter>()?.sharedMesh;
                string path = mesh == null
                    ? string.Empty
                    : AssetDatabase.GetAssetPath(mesh);
                if (sourceModelPaths.Contains(path))
                    throw new InvalidOperationException(
                        $"模型隔离失败：新 Prefab {prefab.name}/{renderer.name} 仍引用原模型 {path}。");
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
            result.createdAssetGuids[path] = AssetDatabase.AssetPathToGUID(path);
            typedList.Add(path);
        }

        public static string BuildAssetName(string originalName, int index, ResourceVariantPreset settings)
        {
            return AssetNaming.BuildName(originalName, index, settings.GetNamingOptions());
        }

        sealed class CopiedModelAsset
        {
            public CopiedModelAsset(Mesh[] meshes)
            {
                Meshes = meshes ?? Array.Empty<Mesh>();
            }

            public Mesh[] Meshes { get; }
        }
    }
}

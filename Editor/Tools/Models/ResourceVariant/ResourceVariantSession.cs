using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Bento
{
    sealed class ResourceVariantSession
    {
        readonly ResourceVariantPresetStore _store = new ResourceVariantPresetStore();
        readonly ResourceVariantCleanupService _cleanup = new ResourceVariantCleanupService();

        public ResourceVariantSession()
        {
            Presets = _store.LoadPresets();
            Settings = new ResourceVariantPreset();
            Normalize(false);
        }

        public ResourceVariantPreset Settings { get; private set; }
        public ResourceVariantPresetList Presets { get; }
        public ResourceVariantPlan Plan { get; private set; }
        public ResourceVariantGenerationResult LastResult { get; private set; }

        public ResourceVariantPlan Analyze(IReadOnlyList<GameObject> selected)
        {
            Plan = new ResourceVariantPlanner(Settings).Build(selected);
            return Plan;
        }

        public ResourceVariantGenerationResult Generate()
        {
            if (Plan == null) throw new InvalidOperationException("请先生成资源变体计划");
            if (!Settings.HasSameGenerationSettings(Plan.settingsSnapshot))
                throw new InvalidOperationException("资源变体设置已变化，请重新分析后再生成。");
            if (Plan.objects.Any(item => item.sourceObject == null || !item.sourceObject.scene.IsValid()))
                throw new InvalidOperationException("资源变体来源对象已删除或离开场景，请重新分析。");
            try
            {
                LastResult = new ResourceVariantGenerator(Settings).Generate(Plan);
            }
            catch (ResourceVariantGenerationException exception)
            {
                LastResult = exception.result;
                throw;
            }
            foreach (var record in LastResult.sourceRecords) record.presetName = Settings.name;
            _store.AddSourceRecords(LastResult.sourceRecords);
            return LastResult;
        }

        public bool TryRestoreLatestGeneration()
        {
            ResourceVariantSourceRecordList records = _store.LoadSourceRecords();
            List<ResourceVariantSourceRecord> valid = records.items
                .Where(record => record != null &&
                                 !string.IsNullOrEmpty(record.generatedPrefabPath) &&
                                 !string.IsNullOrEmpty(record.generatedAtUtc))
                .OrderBy(record => record.generatedAtUtc, StringComparer.Ordinal)
                .ToList();
            if (valid.Count == 0) return false;

            DateTime latest = DateTime.Parse(
                valid[valid.Count - 1].generatedAtUtc,
                null,
                global::System.Globalization.DateTimeStyles.RoundtripKind);
            DateTime batchStart = latest.AddMinutes(-15);
            List<ResourceVariantSourceRecord> batch = valid
                .Where(record =>
                {
                    DateTime generated = DateTime.Parse(
                        record.generatedAtUtc,
                        null,
                        global::System.Globalization.DateTimeStyles.RoundtripKind);
                    return generated >= batchStart && generated <= latest;
                })
                .ToList();

            LastResult = BuildRecoveredResult(batch);
            return LastResult.createdAssets.Count > 0;
        }

        public void SavePreset()
        {
            var copy = Clone(Settings);
            int index = Presets.items.FindIndex(item => string.Equals(item.name, copy.name, StringComparison.OrdinalIgnoreCase));
            if (index >= 0) Presets.items[index] = copy; else Presets.items.Add(copy);
            _store.SavePresets(Presets);
        }

        public void LoadPreset(int index)
        {
            if (Presets.items.Count == 0) return;
            Settings = Clone(Presets.items[Mathf.Clamp(index, 0, Presets.items.Count - 1)]);
            Normalize(true);
            InvalidatePlan();
        }

        public void DeletePreset(int index)
        {
            if (Presets.items.Count == 0) return;
            Presets.items.RemoveAt(Mathf.Clamp(index, 0, Presets.items.Count - 1));
            _store.SavePresets(Presets);
        }

        public bool TryPrepareLastGenerationCleanup(out string blockingReference)
        {
            blockingReference = string.Empty;
            if (LastResult == null || LastResult.createdAssets.Count == 0) return false;
            blockingReference = _cleanup.FindLoadedSceneReference(LastResult.createdPrefabs);
            if (!string.IsNullOrEmpty(blockingReference) && LastResult.replacements.Count > 0)
            {
                Undo.PerformUndo();
                blockingReference = _cleanup.FindLoadedSceneReference(LastResult.createdPrefabs);
            }
            if (!string.IsNullOrEmpty(blockingReference) && LastResult.sourceRecords.Count > 0)
            {
                _cleanup.RestoreSceneInstances(LastResult.sourceRecords);
                blockingReference = _cleanup.FindLoadedSceneReference(LastResult.createdPrefabs);
            }
            return string.IsNullOrEmpty(blockingReference);
        }

        public ResourceVariantCleanupResult DeleteGeneratedAssets()
        {
            if (LastResult == null || LastResult.createdAssets.Count == 0)
                throw new InvalidOperationException("没有可清理的生成资产。");

            ClearGeneratedAssetSelection(LastResult.createdAssets);
            var deleted = _cleanup.Delete(LastResult);
            RemoveDeletedPaths(LastResult, deleted.Paths);
            if (deleted.FailedPaths.Count > 0)
                throw new ResourceVariantCleanupException(deleted);
            if (LastResult.createdAssets.Count == 0)
            {
                _store.RemoveSourceRecords(
                    new HashSet<string>(
                        LastResult.sourceRecords.Select(record => record.generatedPrefabPath),
                        StringComparer.OrdinalIgnoreCase));
                LastResult = null;
            }
            return deleted;
        }

        public void InvalidatePlan() => Plan = null;

        public bool Validate(IReadOnlyList<GameObject> selected, out string message)
        {
            if (selected.Count == 0) { message = "请在 Hierarchy 选择对象。"; return false; }
            if (!BentoUtility.IsProjectFolder(Settings.prefabFolder)) { message = "请选择 Prefab 输出文件夹。"; return false; }
            if (Settings.copyModels && !BentoUtility.IsProjectFolder(Settings.modelFolder)) { message = "请选择 Model 输出文件夹。"; return false; }
            if (!BentoUtility.IsProjectFolder(Settings.materialFolder)) { message = "请选择 Material 输出文件夹。"; return false; }
            if (Settings.copyTextures && !BentoUtility.IsProjectFolder(Settings.textureFolder)) { message = "请选择 Texture 输出文件夹。"; return false; }
            message = string.Empty;
            return true;
        }

        void Normalize(bool migrateLegacy)
        {
            Settings.copyMaterials = true;
            Settings.excludedFolders = Settings.excludedFolders ?? new List<string>();
            if (!migrateLegacy) return;
            bool any = Settings.copyBaseColor || Settings.copyNormal || Settings.copyMetallic || Settings.copyOcclusion ||
                       Settings.copyEmission || Settings.copyHeight || Settings.copyMask || Settings.copyOtherTextures;
            if (!any) Settings.copyBaseColor = Settings.copyNormal = Settings.copyMetallic = Settings.copyOcclusion =
                Settings.copyEmission = Settings.copyHeight = Settings.copyMask = Settings.copyOtherTextures = true;
        }

        static ResourceVariantPreset Clone(ResourceVariantPreset value) =>
            JsonUtility.FromJson<ResourceVariantPreset>(JsonUtility.ToJson(value));

        static ResourceVariantGenerationResult BuildRecoveredResult(
            IEnumerable<ResourceVariantSourceRecord> records)
        {
            var result = new ResourceVariantGenerationResult();
            foreach (ResourceVariantSourceRecord record in records)
            {
                AddExisting(record.generatedPrefabPath, result.createdPrefabs, result.createdAssets);
                foreach (string path in record.modelPaths)
                    AddExisting(path, result.createdModels, result.createdAssets);
                foreach (string path in record.materialPaths)
                    AddExisting(path, result.createdMaterials, result.createdAssets);
                foreach (string path in record.texturePaths)
                    AddExisting(path, result.createdTextures, result.createdAssets);
                result.sourceRecords.Add(record);
            }
            result.processedObjectCount = result.createdPrefabs.Count;
            return result;
        }

        static void AddExisting(
            string path,
            List<string> typedPaths,
            List<string> allPaths)
        {
            if (string.IsNullOrEmpty(path) ||
                string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(path)))
                return;
            if (!typedPaths.Contains(path, StringComparer.OrdinalIgnoreCase))
                typedPaths.Add(path);
            if (!allPaths.Contains(path, StringComparer.OrdinalIgnoreCase))
                allPaths.Add(path);
        }

        static void ClearGeneratedAssetSelection(
            IEnumerable<string> generatedPaths)
        {
            var paths = new HashSet<string>(
                generatedPaths ?? Enumerable.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);
            bool selectionContainsGeneratedAsset = Selection.objects.Any(selected =>
            {
                if (selected == null) return false;
                string path = AssetDatabase.GetAssetPath(selected);
                return !string.IsNullOrEmpty(path) && paths.Contains(path);
            });
            if (selectionContainsGeneratedAsset)
                Selection.objects = Array.Empty<UnityEngine.Object>();
        }

        static void RemoveDeletedPaths(ResourceVariantGenerationResult result, HashSet<string> deletedPaths)
        {
            result.createdAssets.RemoveAll(deletedPaths.Contains);
            result.createdPrefabs.RemoveAll(deletedPaths.Contains);
            result.createdModels.RemoveAll(deletedPaths.Contains);
            result.createdMaterials.RemoveAll(deletedPaths.Contains);
            result.createdTextures.RemoveAll(deletedPaths.Contains);
            if (result.createdAssets.Count == 0) result.replacements.Clear();
        }
    }

    sealed class ResourceVariantCleanupService
    {
        public void RestoreSceneInstances(
            IEnumerable<ResourceVariantSourceRecord> sourceRecords)
        {
            var recordsByGeneratedPath = sourceRecords
                .Where(record => record != null &&
                                 !string.IsNullOrEmpty(record.generatedPrefabPath) &&
                                 !string.IsNullOrEmpty(record.sourceAssetPath))
                .GroupBy(
                    record => record.generatedPrefabPath,
                    StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.First(),
                    StringComparer.OrdinalIgnoreCase);
            if (recordsByGeneratedPath.Count == 0) return;

            var instances = new List<GameObject>();
            foreach (Scene scene in Enumerable.Range(0, SceneManager.sceneCount)
                         .Select(SceneManager.GetSceneAt)
                         .Where(scene => scene.isLoaded))
            foreach (GameObject root in scene.GetRootGameObjects())
            foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
            {
                GameObject gameObject = transform.gameObject;
                if (PrefabUtility.GetNearestPrefabInstanceRoot(gameObject) != gameObject)
                    continue;
                string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(gameObject);
                if (recordsByGeneratedPath.ContainsKey(path))
                    instances.Add(gameObject);
            }

            if (instances.Count == 0) return;
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("恢复资源变体原始实例");
            try
            {
                foreach (GameObject generatedInstance in instances)
                {
                    string generatedPath =
                        PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(generatedInstance);
                    ResourceVariantSourceRecord record = recordsByGeneratedPath[generatedPath];
                    GameObject sourcePrefab =
                        AssetDatabase.LoadAssetAtPath<GameObject>(record.sourceAssetPath);
                    if (sourcePrefab == null)
                        throw new InvalidOperationException(
                            $"无法加载原始 Prefab，已停止恢复：{record.sourceAssetPath}");

                    ReplaceWithSourcePrefab(generatedInstance, sourcePrefab);
                }
            }
            finally
            {
                Undo.CollapseUndoOperations(undoGroup);
            }
        }

        static void ReplaceWithSourcePrefab(
            GameObject generatedInstance,
            GameObject sourcePrefab)
        {
            Transform oldTransform = generatedInstance.transform;
            Transform parent = oldTransform.parent;
            int siblingIndex = oldTransform.GetSiblingIndex();
            Vector3 worldPosition = oldTransform.position;
            Quaternion worldRotation = oldTransform.rotation;
            Vector3 worldScale = oldTransform.lossyScale;
            bool activeSelf = generatedInstance.activeSelf;
            Scene scene = generatedInstance.scene;

            var replacement =
                PrefabUtility.InstantiatePrefab(sourcePrefab, scene) as GameObject;
            if (replacement == null)
                throw new InvalidOperationException(
                    $"原始 Prefab 实例化失败：{AssetDatabase.GetAssetPath(sourcePrefab)}");

            Undo.RegisterCreatedObjectUndo(replacement, "恢复原始 Prefab 实例");
            Undo.SetTransformParent(replacement.transform, parent, "恢复原始父节点");
            Undo.RecordObject(replacement.transform, "恢复原始 Transform");
            replacement.transform.SetPositionAndRotation(worldPosition, worldRotation);
            replacement.transform.localScale = ResolveLocalScale(
                replacement.transform,
                worldScale);
            replacement.transform.SetSiblingIndex(siblingIndex);
            Undo.RecordObject(replacement, "恢复原始激活状态");
            replacement.SetActive(activeSelf);
            Undo.DestroyObjectImmediate(generatedInstance);
            EditorSceneManager.MarkSceneDirty(scene);
        }

        static Vector3 ResolveLocalScale(
            Transform transform,
            Vector3 desiredWorldScale)
        {
            Transform parent = transform.parent;
            if (parent == null) return desiredWorldScale;
            Vector3 parentScale = parent.lossyScale;
            return new Vector3(
                SafeDivide(desiredWorldScale.x, parentScale.x),
                SafeDivide(desiredWorldScale.y, parentScale.y),
                SafeDivide(desiredWorldScale.z, parentScale.z));
        }

        static float SafeDivide(float value, float divisor)
        {
            return Mathf.Abs(divisor) < 0.000001f ? value : value / divisor;
        }

        public ResourceVariantCleanupResult Delete(ResourceVariantGenerationResult result)
        {
            if (result == null || result.createdAssets.Count == 0)
                throw new InvalidOperationException("没有可删除的生成资源");
            string blockingReference = FindLoadedSceneReference(result.createdPrefabs);
            if (!string.IsNullOrEmpty(blockingReference))
                throw new InvalidOperationException($"已加载场景仍在引用生成 Prefab：{blockingReference}");

            var cleanup = new ResourceVariantCleanupResult();
            foreach (string path in OrderedPaths(result))
            {
                if (string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(path))) continue;
                if (AssetDatabase.MoveAssetToTrash(path))
                {
                    cleanup.Paths.Add(path);
                    if (result.createdPrefabs.Contains(path, StringComparer.OrdinalIgnoreCase))
                        cleanup.PrefabPaths.Add(path);
                }
                else
                {
                    cleanup.FailedPaths.Add(path);
                }
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return cleanup;
        }

        static IEnumerable<string> OrderedPaths(ResourceVariantGenerationResult result)
        {
            return result.createdPrefabs
                .Concat(result.createdModels)
                .Concat(result.createdMaterials)
                .Concat(result.createdTextures)
                .Concat(result.createdAssets)
                .Where(path => !string.IsNullOrEmpty(path))
                .Distinct(StringComparer.OrdinalIgnoreCase);
        }

        public string FindLoadedSceneReference(IEnumerable<string> prefabPaths)
        {
            var paths = new HashSet<string>(prefabPaths ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            foreach (var scene in Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt).Where(scene => scene.isLoaded))
            foreach (var root in scene.GetRootGameObjects())
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            {
                string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(transform.gameObject);
                if (paths.Contains(path)) return path;
            }
            return string.Empty;
        }
    }

    sealed class ResourceVariantCleanupResult
    {
        public readonly HashSet<string> Paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public readonly HashSet<string> PrefabPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public readonly List<string> FailedPaths = new List<string>();
    }

    sealed class ResourceVariantCleanupException : Exception
    {
        public readonly ResourceVariantCleanupResult Result;

        public ResourceVariantCleanupException(ResourceVariantCleanupResult result)
            : base("部分生成资源未能移入系统回收站：\n" + string.Join("\n", result.FailedPaths) +
                   "\n\n已成功删除的路径已从本次生成记录中移除，可修正问题后再次清理剩余资源。")
        {
            Result = result;
        }
    }
}

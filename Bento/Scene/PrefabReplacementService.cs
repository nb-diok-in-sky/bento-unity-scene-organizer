using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Bento
{
    sealed class PrefabReplacementOptions
    {
        public bool preserveName = true;
        public bool preserveActiveState = true;
        public bool preservePositionX = true;
        public bool preservePositionY = true;
        public bool preservePositionZ = true;
        public bool preserveRotation = true;
        public bool preserveScale = true;
        public bool deepCopyChildTransforms = true;
        public bool deepCopyComponents;
    }

    sealed class PrefabReplacementResult
    {
        public readonly List<GameObject> replacements = new List<GameObject>();
        public readonly List<string> failures = new List<string>();
        public int Count => replacements.Count;
    }

    sealed class PrefabBuildSceneReplacementResult
    {
        public int ReplacementCount;
        public readonly List<string> ModifiedScenes = new List<string>();
    }

    static class PrefabReplacementService
    {
        public static bool IsValidPrefabAsset(GameObject prefab)
        {
            return prefab != null && !string.IsNullOrEmpty(AssetDatabase.GetAssetPath(prefab)) &&
                   PrefabUtility.GetPrefabAssetType(prefab) != PrefabAssetType.NotAPrefab;
        }

        public static PrefabReplacementResult ReplaceSelection(
            IReadOnlyList<GameObject> selectedObjects,
            GameObject prefab,
            PrefabReplacementOptions options)
        {
            var result = new PrefabReplacementResult();
            using (var undo = new BentoUndoScope("替换选中的预制体"))
            {
                foreach (var oldObject in selectedObjects.Where(item => item != null))
                {
                    var replacement = ReplaceOne(oldObject.transform, prefab, options, true, preserveNameAndState: true);
                    if (replacement == null) result.failures.Add(oldObject.name);
                    else result.replacements.Add(replacement);
                }
                undo.Complete();
            }
            return result;
        }

        public static int ReplaceCurrentScene(string searchName, GameObject prefab, PrefabReplacementOptions options)
        {
            var scene = SceneManager.GetActiveScene();
            int count;
            using (var undo = new BentoUndoScope("按名称替换当前场景对象"))
            {
                count = ReplaceInScene(scene, searchName, prefab, options, true);
                undo.Complete();
            }
            if (count > 0) EditorSceneManager.MarkSceneDirty(scene);
            return count;
        }

        public static PrefabBuildSceneReplacementResult ReplaceBuildScenes(
            string searchName,
            GameObject prefab,
            PrefabReplacementOptions options)
        {
            var result = new PrefabBuildSceneReplacementResult();
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return result;
            string currentScene = SceneManager.GetActiveScene().path;
            try
            {
                foreach (var entry in EditorBuildSettings.scenes.Where(scene => scene.enabled))
                {
                    if (!File.Exists(entry.path)) continue;
                    var scene = EditorSceneManager.OpenScene(entry.path, OpenSceneMode.Single);
                    int count = ReplaceInScene(scene, searchName, prefab, options, false);
                    if (count == 0) continue;
                    EditorSceneManager.MarkSceneDirty(scene);
                    if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException($"场景保存失败：{entry.path}");
                    result.ReplacementCount += count;
                    result.ModifiedScenes.Add(entry.path);
                }
                return result;
            }
            catch (Exception exception)
            {
                throw new PrefabBuildSceneReplacementException(exception, result);
            }
            finally
            {
                if (!string.IsNullOrEmpty(currentScene) && File.Exists(currentScene))
                    EditorSceneManager.OpenScene(currentScene, OpenSceneMode.Single);
            }
        }

        static int ReplaceInScene(Scene scene, string searchName, GameObject prefab, PrefabReplacementOptions options, bool useUndo)
        {
            var matches = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
                .Where(transform => transform.gameObject.name == searchName)
                .ToList();
            int count = 0;
            foreach (var oldTransform in matches)
                if (oldTransform != null && ReplaceOne(oldTransform, prefab, options, useUndo, preserveNameAndState: false) != null) count++;
            return count;
        }

        static GameObject ReplaceOne(
            Transform oldTransform,
            GameObject prefab,
            PrefabReplacementOptions options,
            bool useUndo,
            bool preserveNameAndState)
        {
            var oldObject = oldTransform.gameObject;
            var replacement = PrefabUtility.InstantiatePrefab(prefab, oldObject.scene) as GameObject;
            if (replacement == null) return null;

            if (useUndo)
            {
                Undo.RegisterCreatedObjectUndo(replacement, "创建替换预制体");
                Undo.SetTransformParent(replacement.transform, oldTransform.parent, "设置替换预制体父节点");
                Undo.RecordObject(replacement.transform, "应用原对象 Transform");
            }
            else replacement.transform.SetParent(oldTransform.parent, false);

            replacement.transform.SetSiblingIndex(oldTransform.GetSiblingIndex());
            ApplyTransform(replacement.transform, oldTransform, options);
            if (options.deepCopyChildTransforms || options.deepCopyComponents) DeepCopy(oldTransform, replacement.transform, options);

            if (preserveNameAndState && options.preserveName) replacement.name = oldObject.name;
            if (preserveNameAndState && options.preserveActiveState) replacement.SetActive(oldObject.activeSelf);
            if (useUndo) Undo.DestroyObjectImmediate(oldObject); else Object.DestroyImmediate(oldObject);
            EditorSceneManager.MarkSceneDirty(replacement.scene);
            return replacement;
        }

        static void ApplyTransform(Transform target, Transform source, PrefabReplacementOptions options)
        {
            Vector3 position = target.localPosition;
            if (options.preservePositionX) position.x = source.localPosition.x;
            if (options.preservePositionY) position.y = source.localPosition.y;
            if (options.preservePositionZ) position.z = source.localPosition.z;
            target.localPosition = position;
            if (options.preserveRotation) target.localRotation = source.localRotation;
            if (options.preserveScale) target.localScale = source.localScale;
        }

        static void DeepCopy(Transform sourceRoot, Transform targetRoot, PrefabReplacementOptions options)
        {
            var targets = targetRoot.GetComponentsInChildren<Transform>(true)
                .GroupBy(transform => RelativePath(targetRoot, transform))
                .ToDictionary(group => group.Key, group => group.First());
            foreach (var source in sourceRoot.GetComponentsInChildren<Transform>(true).Where(item => item != sourceRoot))
            {
                if (!targets.TryGetValue(RelativePath(sourceRoot, source), out var target)) continue;
                if (options.deepCopyChildTransforms) ApplyTransform(target, source, options);
                if (options.deepCopyComponents) CopyComponentValues(source.gameObject, target.gameObject);
            }
        }

        static string RelativePath(Transform root, Transform current)
        {
            if (current == root) return string.Empty;
            string path = current.name;
            while (current.parent != null && current.parent != root) { current = current.parent; path = current.name + "/" + path; }
            return path;
        }

        static void CopyComponentValues(GameObject source, GameObject target)
        {
            foreach (var component in source.GetComponents<Component>())
            {
                if (component == null || component is Transform) continue;
                var targetComponent = target.GetComponent(component.GetType());
                if (targetComponent != null && ComponentUtility.CopyComponent(component)) ComponentUtility.PasteComponentValues(targetComponent);
            }
        }
    }

    sealed class PrefabBuildSceneReplacementException : Exception
    {
        public PrefabBuildSceneReplacementResult Result { get; }

        public PrefabBuildSceneReplacementException(Exception innerException, PrefabBuildSceneReplacementResult result)
            : base(innerException.Message + BuildWarning(result), innerException)
        {
            Result = result;
        }

        static string BuildWarning(PrefabBuildSceneReplacementResult result)
        {
            return result.ModifiedScenes.Count == 0
                ? string.Empty
                : "\n\n失败前已有场景保存成功，无法用 Ctrl+Z 跨场景恢复。请从版本控制检查或还原：\n" +
                  string.Join("\n", result.ModifiedScenes);
        }
    }
}

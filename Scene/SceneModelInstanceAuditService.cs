using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Bento
{
    public static class SceneModelInstanceAuditService
    {
        static readonly string[] ModelExtensions = { ".fbx", ".obj" };

        public static SceneModelInstanceAuditResult Scan(SceneModelInstanceAuditScope scope)
        {
            List<GameObject> roots = scope == SceneModelInstanceAuditScope.Selection
                ? BentoUtility.GetTopLevelSelectedSceneObjects()
                : LoadedSceneRoots().ToList();
            List<GameObject> candidates = roots
                .SelectMany(EnumerateHierarchy)
                .Where(gameObject => gameObject != null && gameObject.scene.IsValid())
                .Distinct()
                .ToList();
            var result = new SceneModelInstanceAuditResult
            {
                scannedAtUtc = DateTime.UtcNow.ToString("O"),
                scope = scope,
                inspectedObjectCount = candidates.Count
            };

            foreach (GameObject gameObject in candidates)
            {
                if (!TryGetDirectModelInstancePath(gameObject, out string modelPath))
                    continue;
                result.instances.Add(new SceneModelInstanceAuditRecord
                {
                    instanceId = gameObject.GetInstanceID(),
                    objectName = gameObject.name,
                    hierarchyPath = HierarchyPath(gameObject.transform),
                    scenePath = gameObject.scene.path,
                    modelAssetPath = modelPath,
                    modelExtension = Path.GetExtension(modelPath).ToLowerInvariant()
                });
            }

            result.instances = result.instances
                .OrderBy(record => record.scenePath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(record => record.hierarchyPath, StringComparer.OrdinalIgnoreCase)
                .ToList();
            SceneModelInstanceAuditSession.Replace(result);
            return result;
        }

        static bool TryGetDirectModelInstancePath(GameObject gameObject, out string modelPath)
        {
            modelPath = string.Empty;
            if (!PrefabUtility.IsPartOfPrefabInstance(gameObject))
                return false;
            if (PrefabUtility.GetNearestPrefabInstanceRoot(gameObject) != gameObject)
                return false;

            string assetPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(gameObject);
            string extension = Path.GetExtension(assetPath);
            if (string.IsNullOrEmpty(assetPath) ||
                !ModelExtensions.Any(value => string.Equals(value, extension, StringComparison.OrdinalIgnoreCase)))
                return false;

            modelPath = assetPath;
            return true;
        }

        static IEnumerable<GameObject> LoadedSceneRoots()
        {
            for (int index = 0; index < SceneManager.sceneCount; index++)
            {
                Scene scene = SceneManager.GetSceneAt(index);
                if (!scene.IsValid() || !scene.isLoaded) continue;
                foreach (GameObject root in scene.GetRootGameObjects())
                    yield return root;
            }
        }

        static IEnumerable<GameObject> EnumerateHierarchy(GameObject root)
        {
            if (root == null) yield break;
            foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
                yield return transform.gameObject;
        }

        static string HierarchyPath(Transform transform)
        {
            string path = transform.name;
            for (Transform parent = transform.parent; parent != null; parent = parent.parent)
                path = parent.name + "/" + path;
            return path;
        }
    }
}

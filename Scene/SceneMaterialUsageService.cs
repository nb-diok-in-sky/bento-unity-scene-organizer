using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Bento
{
    sealed class SceneMaterialUsageRecord
    {
        public int gameObjectInstanceId;
        public string hierarchyPath;
        public string scenePath;
        public string rendererType;
        public int[] materialSlots;
    }

    static class SceneMaterialUsageService
    {
        public static List<SceneMaterialUsageRecord> Scan(
            Material material,
            Transform scopeRoot,
            bool includeInactive)
        {
            if (material == null) throw new ArgumentNullException(nameof(material));

            IEnumerable<GameObject> roots = scopeRoot != null
                ? new[] { scopeRoot.gameObject }
                : Enumerable.Range(0, SceneManager.sceneCount)
                    .Select(SceneManager.GetSceneAt)
                    .Where(scene => scene.IsValid() && scene.isLoaded)
                    .SelectMany(scene => scene.GetRootGameObjects());

            return roots
                .SelectMany(root => root.GetComponentsInChildren<Renderer>(includeInactive))
                .Where(renderer => renderer != null)
                .Distinct()
                .Select(renderer => BuildRecord(renderer, material))
                .Where(record => record != null)
                .OrderBy(record => record.scenePath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(record => record.hierarchyPath, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        static SceneMaterialUsageRecord BuildRecord(Renderer renderer, Material material)
        {
            Material[] materials = renderer.sharedMaterials;
            int[] slots = Enumerable.Range(0, materials.Length)
                .Where(index => materials[index] == material)
                .ToArray();
            if (slots.Length == 0) return null;

            return new SceneMaterialUsageRecord
            {
                gameObjectInstanceId = renderer.gameObject.GetInstanceID(),
                hierarchyPath = BentoUtility.GetHierarchyPath(renderer.transform),
                scenePath = string.IsNullOrEmpty(renderer.gameObject.scene.path)
                    ? renderer.gameObject.scene.name
                    : renderer.gameObject.scene.path,
                rendererType = renderer.GetType().Name,
                materialSlots = slots
            };
        }
    }
}

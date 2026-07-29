using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Bento
{
    static class McpSceneAnalysisFacade
    {
        public static object GetSelection()
        {
            var selected = BentoUtility.GetTopLevelSelectedSceneObjects(sortByHierarchy: true);
            return new
            {
                count = selected.Count,
                objects = selected.Select(gameObject => new
                {
                    name = gameObject.name,
                    scene = gameObject.scene.path,
                    hierarchy_path = HierarchyPath(gameObject.transform),
                    prefab_asset_path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(gameObject),
                    renderer_count = gameObject.GetComponentsInChildren<Renderer>(true).Length
                }).ToArray()
            };
        }

        public static object InspectLoadedScenes()
        {
            var scenes = Enumerable.Range(0, SceneManager.sceneCount)
                .Select(SceneManager.GetSceneAt)
                .Where(scene => scene.IsValid() && scene.isLoaded)
                .Select(scene => new
                {
                    name = scene.name,
                    path = scene.path,
                    is_dirty = scene.isDirty,
                    root_count = scene.rootCount,
                    object_count = scene.GetRootGameObjects().Sum(root => root.GetComponentsInChildren<Transform>(true).Length),
                    renderer_count = scene.GetRootGameObjects().Sum(root => root.GetComponentsInChildren<Renderer>(true).Length)
                }).ToArray();
            return new { count = scenes.Length, scenes };
        }

        public static object AnalyzeHierarchyOrganization()
        {
            var snapshot = SceneHierarchySnapshotService.CaptureLoadedScenes();
            return new
            {
                scenes = snapshot.scenes.Select(scene => new
                {
                    scene.name,
                    scene.path,
                    root_candidates = scene.nodes.Where(node => node.depth == 0).Select(node => new
                    {
                        instance_id = node.instanceId,
                        node.name,
                        node.hierarchyPath,
                        node.prefabAssetPath,
                        node.layer,
                        node.childCount
                    }).ToArray(),
                    parent_candidates = scene.nodes.Where(node => node.childCount > 0 && node.depth <= 4).Select(node => new
                    {
                        instance_id = node.instanceId,
                        node.hierarchyPath,
                        node.depth,
                        node.childCount
                    }).ToArray()
                }).ToArray()
            };
        }

        public static object ValidateLoadedScenes()
        {
            int missingScripts = 0;
            int missingMaterials = 0;
            int missingShaders = 0;
            var samples = new List<string>();

            foreach (var scene in Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt).Where(scene => scene.isLoaded))
            foreach (var root in scene.GetRootGameObjects())
            foreach (var gameObject in root.GetComponentsInChildren<Transform>(true).Select(transform => transform.gameObject))
            {
                int missing = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(gameObject);
                if (missing > 0) { missingScripts += missing; AddSample(samples, gameObject, $"missing_scripts={missing}"); }
                foreach (var renderer in gameObject.GetComponents<Renderer>())
                foreach (var material in renderer.sharedMaterials)
                {
                    if (material == null) { missingMaterials++; AddSample(samples, gameObject, "missing_material"); }
                    else if (material.shader == null) { missingShaders++; AddSample(samples, gameObject, $"missing_shader={material.name}"); }
                }
            }

            return new
            {
                healthy = missingScripts == 0 && missingMaterials == 0 && missingShaders == 0,
                missing_scripts = missingScripts,
                missing_material_slots = missingMaterials,
                missing_shaders = missingShaders,
                disconnected_prefab_objects = 0,
                samples = samples.ToArray()
            };
        }

        static void AddSample(ICollection<string> samples, GameObject gameObject, string issue)
        {
            if (samples.Count < 40) samples.Add($"{gameObject.scene.path} :: {HierarchyPath(gameObject.transform)} :: {issue}");
        }

        static string HierarchyPath(Transform transform)
        {
            string path = transform.name;
            for (var parent = transform.parent; parent != null; parent = parent.parent) path = parent.name + "/" + path;
            return path;
        }
    }
}

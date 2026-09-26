using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Bento
{
    [Serializable]
    sealed class SceneHierarchySnapshot
    {
        public string createdAtUtc;
        public List<SceneHierarchySceneInfo> scenes = new List<SceneHierarchySceneInfo>();
    }

    [Serializable]
    sealed class SceneHierarchySceneInfo
    {
        public string name;
        public string path;
        public bool isDirty;
        public List<SceneHierarchyNodeInfo> nodes = new List<SceneHierarchyNodeInfo>();
    }

    [Serializable]
    sealed class SceneHierarchyNodeInfo
    {
        public int instanceId;
        public string name;
        public string hierarchyPath;
        public string parentPath;
        public int depth;
        public bool activeSelf;
        public bool activeInHierarchy;
        public string tag;
        public string layer;
        public string prefabAssetPath;
        public string prefabStatus;
        public SceneHierarchyVector3 localPosition;
        public SceneHierarchyVector3 localEulerAngles;
        public SceneHierarchyVector3 localScale;
        public int childCount;
        public int rendererCount;
        public string[] components;
    }

    [Serializable]
    struct SceneHierarchyVector3
    {
        public float x;
        public float y;
        public float z;

        public static SceneHierarchyVector3 From(Vector3 value) => new SceneHierarchyVector3
        {
            x = value.x,
            y = value.y,
            z = value.z
        };
    }

    static class SceneHierarchySnapshotService
    {
        const string OutputFolder = "Bento_Hierarchy快照";

        public static SceneHierarchySnapshot CaptureLoadedScenes()
        {
            var snapshot = new SceneHierarchySnapshot { createdAtUtc = DateTime.UtcNow.ToString("O") };
            for (int sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++)
            {
                Scene scene = SceneManager.GetSceneAt(sceneIndex);
                if (!scene.IsValid() || !scene.isLoaded) continue;
                var sceneInfo = new SceneHierarchySceneInfo
                {
                    name = scene.name,
                    path = scene.path,
                    isDirty = scene.isDirty
                };
                foreach (GameObject root in scene.GetRootGameObjects())
                    AddNode(root.transform, string.Empty, 0, sceneInfo.nodes);
                snapshot.scenes.Add(sceneInfo);
            }
            return snapshot;
        }

        public static string ExportLoadedScenes()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string outputRoot = Path.Combine(projectRoot, OutputFolder);
            Directory.CreateDirectory(outputRoot);
            string filePath = Path.Combine(outputRoot, DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_Hierarchy.json");
            File.WriteAllText(filePath, JsonUtility.ToJson(CaptureLoadedScenes(), true), new UTF8Encoding(true));
            EditorUtility.RevealInFinder(filePath);
            return filePath;
        }

        static void AddNode(Transform transform, string parentPath, int depth, ICollection<SceneHierarchyNodeInfo> nodes)
        {
            string path = string.IsNullOrEmpty(parentPath) ? transform.name : parentPath + "/" + transform.name;
            GameObject gameObject = transform.gameObject;
            nodes.Add(new SceneHierarchyNodeInfo
            {
                instanceId = gameObject.GetInstanceID(),
                name = gameObject.name,
                hierarchyPath = path,
                parentPath = parentPath,
                depth = depth,
                activeSelf = gameObject.activeSelf,
                activeInHierarchy = gameObject.activeInHierarchy,
                tag = gameObject.tag,
                layer = LayerMask.LayerToName(gameObject.layer),
                prefabAssetPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(gameObject),
                prefabStatus = PrefabUtility.GetPrefabInstanceStatus(gameObject).ToString(),
                localPosition = SceneHierarchyVector3.From(transform.localPosition),
                localEulerAngles = SceneHierarchyVector3.From(transform.localEulerAngles),
                localScale = SceneHierarchyVector3.From(transform.localScale),
                childCount = transform.childCount,
                rendererCount = gameObject.GetComponents<Renderer>().Length,
                components = gameObject.GetComponents<Component>()
                    .Select(component => component == null ? "Missing Script" : component.GetType().FullName)
                    .ToArray()
            });
            for (int childIndex = 0; childIndex < transform.childCount; childIndex++)
                AddNode(transform.GetChild(childIndex), path, depth + 1, nodes);
        }
    }
}

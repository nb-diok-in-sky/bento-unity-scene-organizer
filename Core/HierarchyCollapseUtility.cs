using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Bento
{
    readonly struct HierarchyCollapseResult
    {
        public HierarchyCollapseResult(int parentObjectCount, int hierarchyWindowCount)
        {
            ParentObjectCount = parentObjectCount;
            HierarchyWindowCount = hierarchyWindowCount;
        }

        public int ParentObjectCount { get; }
        public int HierarchyWindowCount { get; }
    }

    static class HierarchyCollapseUtility
    {
        const BindingFlags InstanceFlags =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        const BindingFlags StaticFlags =
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        static readonly Type HierarchyWindowType =
            typeof(EditorWindow).Assembly.GetType("UnityEditor.SceneHierarchyWindow");

        static readonly MethodInfo GetAllHierarchyWindowsMethod =
            HierarchyWindowType?.GetMethod(
                "GetAllSceneHierarchyWindows",
                StaticFlags,
                null,
                Type.EmptyTypes,
                null);

        static readonly MethodInfo SetExpandedRecursiveMethod =
            HierarchyWindowType?.GetMethod(
                "SetExpandedRecursive",
                InstanceFlags,
                null,
                new[] { typeof(int), typeof(bool) },
                null);

        public static HierarchyCollapseResult CollapseAllLoadedScenes()
        {
            EnsureApiAvailable();
            UnityEngine.Object[] selectedObjects = Selection.objects;
            UnityEngine.Object activeObject = Selection.activeObject;

            try
            {
                List<EditorWindow> hierarchyWindows = GetHierarchyWindows();
                if (hierarchyWindows.Count == 0)
                    throw new InvalidOperationException("当前没有打开的 Unity Hierarchy 窗口。");

                List<GameObject> sceneRoots = GetCurrentHierarchyRoots();
                int parentObjectCount = CountParentObjects(sceneRoots);
                foreach (EditorWindow hierarchyWindow in hierarchyWindows)
                {
                    foreach (GameObject root in sceneRoots)
                    {
                        if (root == null || root.transform.childCount == 0) continue;
                        SetExpandedRecursiveMethod.Invoke(
                            hierarchyWindow,
                            new object[] { root.GetInstanceID(), false });
                    }
                    hierarchyWindow.Repaint();
                }

                return new HierarchyCollapseResult(
                    parentObjectCount,
                    hierarchyWindows.Count);
            }
            finally
            {
                RestoreSelectionIfChanged(selectedObjects, activeObject);
            }
        }

        static void EnsureApiAvailable()
        {
            if (HierarchyWindowType == null ||
                GetAllHierarchyWindowsMethod == null ||
                SetExpandedRecursiveMethod == null)
                throw new NotSupportedException(
                    "当前 Unity 版本没有找到 Hierarchy 递归折叠接口。");
        }

        static List<EditorWindow> GetHierarchyWindows()
        {
            var windows = new List<EditorWindow>();
            if (!(GetAllHierarchyWindowsMethod.Invoke(null, null) is IEnumerable values))
                return windows;
            foreach (object value in values)
                if (value is EditorWindow window) windows.Add(window);
            return windows;
        }

        static List<GameObject> GetCurrentHierarchyRoots()
        {
            PrefabStage prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            if (prefabStage != null && prefabStage.prefabContentsRoot != null)
                return new List<GameObject> { prefabStage.prefabContentsRoot };

            var roots = new List<GameObject>();
            for (int sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++)
            {
                Scene scene = SceneManager.GetSceneAt(sceneIndex);
                if (!scene.IsValid() || !scene.isLoaded) continue;
                roots.AddRange(scene.GetRootGameObjects());
            }
            return roots;
        }

        static int CountParentObjects(IEnumerable<GameObject> roots)
        {
            int count = 0;
            var pending = new Stack<Transform>(
                roots.Where(root => root != null).Select(root => root.transform));
            while (pending.Count > 0)
            {
                Transform current = pending.Pop();
                if (current.childCount > 0) count++;
                for (int childIndex = 0; childIndex < current.childCount; childIndex++)
                    pending.Push(current.GetChild(childIndex));
            }
            return count;
        }

        static void RestoreSelectionIfChanged(
            UnityEngine.Object[] selectedObjects,
            UnityEngine.Object activeObject)
        {
            if (Selection.activeObject == activeObject &&
                Selection.objects.SequenceEqual(selectedObjects))
                return;
            Selection.objects = selectedObjects;
            Selection.activeObject = activeObject;
        }
    }
}

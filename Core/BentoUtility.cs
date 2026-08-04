using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Bento
{
    public static class BentoUtility
    {
        public static string GetProjectEditorPrefsKey(string prefix)
        {
            return prefix + Application.dataPath.GetHashCode();
        }

        public static List<GameObject> GetTopLevelSelectedSceneObjects(bool sortByHierarchy = false)
        {
            var selectedObjects = Selection.gameObjects
                .Where(IsLoadedSceneObject)
                .Distinct()
                .ToList();
            var selectedSet = new HashSet<GameObject>(selectedObjects);

            var result = selectedObjects
                .Where(gameObject => !HasSelectedAncestor(gameObject.transform, selectedSet));

            return sortByHierarchy
                ? result.OrderBy(GetHierarchySortKey).ToList()
                : result.ToList();
        }

        public static string GetHierarchyPath(Transform transform)
        {
            if (transform == null) return string.Empty;
            string path = transform.name;
            for (Transform parent = transform.parent; parent != null; parent = parent.parent)
                path = parent.name + "/" + path;
            return path;
        }

        public static bool IsProjectFolder(string path)
        {
            return !string.IsNullOrEmpty(path) &&
                   path.StartsWith("Assets", StringComparison.Ordinal) &&
                   AssetDatabase.IsValidFolder(path);
        }

        public static string GenerateUniqueAssetPath(string folder, string name, string extension)
        {
            return AssetDatabase.GenerateUniqueAssetPath($"{folder.TrimEnd('/')}/{name}{extension}");
        }

        public static string SanitizeFileName(string value, string fallback = "Variant")
        {
            foreach (char invalidCharacter in Path.GetInvalidFileNameChars())
                value = value.Replace(invalidCharacter, '_');
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }

        static bool IsLoadedSceneObject(GameObject gameObject)
        {
            return gameObject != null && gameObject.scene.IsValid() && gameObject.scene.isLoaded;
        }

        static bool HasSelectedAncestor(Transform transform, HashSet<GameObject> selectedObjects)
        {
            for (var parent = transform.parent; parent != null; parent = parent.parent)
            {
                if (selectedObjects.Contains(parent.gameObject))
                    return true;
            }
            return false;
        }

        static string GetHierarchySortKey(GameObject gameObject)
        {
            var siblingIndices = new Stack<int>();
            for (var current = gameObject.transform; current != null; current = current.parent)
                siblingIndices.Push(current.GetSiblingIndex());
            return string.Join("/", siblingIndices.Select(index => index.ToString("D8")));
        }
    }
}

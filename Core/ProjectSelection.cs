using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Bento
{
    public static class ProjectSelection
    {
        public static UnityEngine.Object[] CurrentAssets => Selection.objects
            .Where(asset => asset != null && !string.IsNullOrEmpty(AssetDatabase.GetAssetPath(asset)))
            .ToArray();

        public static IEnumerable<UnityEngine.Object> SupportedAssets(Func<UnityEngine.Object, bool> predicate)
        {
            return CurrentAssets.Where(predicate);
        }

        public static bool TryGetActiveProjectFolder(out DefaultAsset folder)
        {
            folder = null;
            string path = Selection.activeObject == null ? string.Empty : AssetDatabase.GetAssetPath(Selection.activeObject);
            if (string.IsNullOrEmpty(path)) return false;

            string folderPath = AssetDatabase.IsValidFolder(path)
                ? path
                : Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(folderPath) || !AssetDatabase.IsValidFolder(folderPath)) return false;

            folder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(folderPath);
            return folder != null;
        }
    }
}

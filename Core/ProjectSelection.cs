using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Bento
{
    static class ProjectSelection
    {
        public static UnityEngine.Object[] CurrentAssets => Selection.objects
            .Where(asset => asset != null && !string.IsNullOrEmpty(AssetDatabase.GetAssetPath(asset)))
            .ToArray();

        public static IEnumerable<UnityEngine.Object> SupportedAssets(Func<UnityEngine.Object, bool> predicate)
        {
            return CurrentAssets.Where(predicate);
        }
    }
}

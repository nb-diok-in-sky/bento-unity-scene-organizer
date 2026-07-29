using System;
using UnityEditor;
using UnityEngine;

namespace Bento
{
    static class FbxPrefabCreationService
    {
        public static GameObject CreatePrefab(GameObject fbx, string selectedFolder)
        {
            if (!FbxUrpMaterialBindingService.IsFbx(fbx))
                throw new InvalidOperationException("请选择 Project 中的 .fbx 资产。");

            string fbxPath = AssetDatabase.GetAssetPath(fbx);
            string folder = FbxUrpMaterialBindingService.ResolveOutputFolder(selectedFolder, fbxPath);
            var instance = UnityEngine.Object.Instantiate(fbx);
            instance.name = fbx.name;
            try
            {
                string prefabPath = BentoUtility.GenerateUniqueAssetPath(folder, fbx.name, ".prefab");
                var prefab = PrefabUtility.SaveAsPrefabAsset(instance, prefabPath);
                if (prefab == null) throw new InvalidOperationException("Prefab 保存失败。");
                return prefab;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }
    }
}

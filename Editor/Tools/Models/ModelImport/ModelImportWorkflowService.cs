using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Bento
{
    static class ModelImportWorkflowService
    {
        public static ModelImportWorkflowResult Execute(IEnumerable<ModelImportWorkflowEntry> entries, ModelImportWorkflowOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            var selected = entries.Where(entry => entry.include).ToList();
            var result = new ModelImportWorkflowResult();
            if (options.createMaterials) result.materials = FbxUrpMaterialBindingService.ExecuteBatchMaterials(selected.Select(entry => entry.material), options.maskLayout);
            if (!options.createPrefabs) return result;
            var refreshedPrefabs = new List<FbxPrefabBatchEntry>();
            foreach (var entry in selected)
            {
                GameObject refreshedFbx = AssetDatabase.LoadAssetAtPath<GameObject>(entry.sourcePath);
                if (refreshedFbx == null) { result.failures.Add(entry.sourcePath + ": FBX 重导入后无法重新加载。"); continue; }
                refreshedPrefabs.Add(new FbxPrefabBatchEntry { fbx = refreshedFbx, sourcePath = entry.sourcePath, prefabPath = entry.prefab.prefabPath, include = true, exists = entry.prefab.exists, note = entry.prefab.note });
            }
            result.prefabs = FbxPrefabCreationService.ExecuteBatch(refreshedPrefabs, options.conflictMode);
            return result;
        }
    }
}

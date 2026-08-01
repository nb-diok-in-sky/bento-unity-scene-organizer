using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace Bento
{
    static class ModelImportWorkflowPlanner
    {
        public static List<ModelImportWorkflowEntry> Build(IEnumerable<string> sourceFolders, ModelImportWorkflowOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            var plan = new List<ModelImportWorkflowEntry>();
            foreach (string source in sourceFolders.Where(BentoUtility.IsProjectFolder).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var materials = FbxUrpMaterialBindingService.BuildBatchMaterialPlan(source);
                var prefabs = options.createPrefabs ? FbxPrefabCreationService.BuildBatchPlan(source, options.prefabOutputFolder, options.createSourceNamedFolder, options.folderMode, options.conflictMode) : new List<FbxPrefabBatchEntry>();
                var prefabByPath = prefabs.ToDictionary(entry => entry.sourcePath, StringComparer.OrdinalIgnoreCase);
                foreach (var material in materials)
                {
                    string sourcePath = AssetDatabase.GetAssetPath(material.fbx);
                    prefabByPath.TryGetValue(sourcePath, out var prefab);
                    var entry = new ModelImportWorkflowEntry { rootFolder = source, sourcePath = sourcePath, material = material, prefab = prefab };
                    entry.include = CanExecute(entry, options);
                    plan.Add(entry);
                }
            }
            return plan;
        }

        public static bool CanExecute(ModelImportWorkflowEntry entry, ModelImportWorkflowOptions options)
        {
            if (entry == null || string.IsNullOrEmpty(entry.sourcePath) || options == null) return false;
            if (options.createMaterials && (entry.material?.baseColor == null || entry.material.normal == null || entry.material.packedMask == null)) return false;
            return !options.createPrefabs || entry.prefab != null;
        }
    }
}

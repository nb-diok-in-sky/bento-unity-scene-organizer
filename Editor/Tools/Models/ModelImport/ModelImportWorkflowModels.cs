using System.Collections.Generic;
using UnityEditor;

namespace Bento
{
    sealed class ModelImportWorkflowSource { public DefaultAsset folder; public bool include = true; }
    sealed class ModelImportWorkflowOptions
    {
        public bool createMaterials = true;
        public bool createPrefabs = true;
        public string prefabOutputFolder;
        public bool createSourceNamedFolder = true;
        public FbxPrefabFolderMode folderMode = FbxPrefabFolderMode.PreserveFolders;
        public FbxPrefabConflictMode conflictMode = FbxPrefabConflictMode.Skip;
        public PackedMaskLayout maskLayout;
    }
    sealed class ModelImportWorkflowEntry
    {
        public string rootFolder;
        public string sourcePath;
        public FbxBatchMaterialEntry material;
        public FbxPrefabBatchEntry prefab;
        public bool include = true;
    }
    sealed class ModelImportWorkflowResult
    {
        public FbxBatchMaterialResult materials;
        public FbxPrefabBatchResult prefabs;
        public readonly List<string> failures = new();
    }
}

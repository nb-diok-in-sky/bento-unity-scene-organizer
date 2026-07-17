using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Bento
{
    sealed class ResourceVariantSession
    {
        readonly ResourceVariantPresetStore _store = new ResourceVariantPresetStore();

        public ResourceVariantSession()
        {
            Presets = _store.LoadPresets();
            Settings = new ResourceVariantPreset();
            Normalize(false);
        }

        public ResourceVariantPreset Settings { get; private set; }
        public ResourceVariantPresetList Presets { get; }
        public ResourceVariantPlan Plan { get; private set; }
        public ResourceVariantGenerationResult LastResult { get; private set; }

        public ResourceVariantPlan Analyze(IReadOnlyList<GameObject> selected)
        {
            Plan = new ResourceVariantPlanner(Settings).Build(selected);
            return Plan;
        }

        public ResourceVariantGenerationResult Generate()
        {
            if (Plan == null) throw new InvalidOperationException("请先生成资源变体计划");
            LastResult = new ResourceVariantGenerator(Settings).Generate(Plan);
            foreach (var record in LastResult.sourceRecords) record.presetName = Settings.name;
            _store.AddSourceRecords(LastResult.sourceRecords);
            return LastResult;
        }

        public void SavePreset()
        {
            var copy = Clone(Settings);
            int index = Presets.items.FindIndex(item => string.Equals(item.name, copy.name, StringComparison.OrdinalIgnoreCase));
            if (index >= 0) Presets.items[index] = copy; else Presets.items.Add(copy);
            _store.SavePresets(Presets);
        }

        public void LoadPreset(int index)
        {
            if (Presets.items.Count == 0) return;
            Settings = Clone(Presets.items[Mathf.Clamp(index, 0, Presets.items.Count - 1)]);
            Normalize(true);
            InvalidatePlan();
        }

        public void DeletePreset(int index)
        {
            if (Presets.items.Count == 0) return;
            Presets.items.RemoveAt(Mathf.Clamp(index, 0, Presets.items.Count - 1));
            _store.SavePresets(Presets);
        }

        public bool TryUndoLastGeneration(out string blockingReference)
        {
            blockingReference = string.Empty;
            if (LastResult == null || LastResult.createdAssets.Count == 0) return false;
            blockingReference = FindLoadedSceneReference(LastResult.createdPrefabs);
            if (!string.IsNullOrEmpty(blockingReference) && LastResult.replacements.Count > 0)
            {
                Undo.PerformUndo();
                blockingReference = FindLoadedSceneReference(LastResult.createdPrefabs);
            }
            return string.IsNullOrEmpty(blockingReference);
        }

        public void DeleteGeneratedAssets()
        {
            foreach (string path in LastResult.createdPrefabs.Concat(LastResult.createdMaterials).Concat(LastResult.createdTextures).Distinct())
                if (!string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(path))) AssetDatabase.DeleteAsset(path);
            _store.RemoveSourceRecords(new HashSet<string>(LastResult.createdPrefabs, StringComparer.OrdinalIgnoreCase));
            LastResult.createdAssets.Clear();
            LastResult.createdPrefabs.Clear();
            LastResult.createdMaterials.Clear();
            LastResult.createdTextures.Clear();
            LastResult.replacements.Clear();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        public void InvalidatePlan() => Plan = null;

        public bool Validate(IReadOnlyList<GameObject> selected, out string message)
        {
            if (selected.Count == 0) { message = "请在 Hierarchy 选择对象。"; return false; }
            if (!BentoUtility.IsProjectFolder(Settings.prefabFolder)) { message = "请选择 Prefab 输出文件夹。"; return false; }
            if (!BentoUtility.IsProjectFolder(Settings.materialFolder)) { message = "请选择 Material 输出文件夹。"; return false; }
            if (Settings.copyTextures && !BentoUtility.IsProjectFolder(Settings.textureFolder)) { message = "请选择 Texture 输出文件夹。"; return false; }
            message = string.Empty;
            return true;
        }

        void Normalize(bool migrateLegacy)
        {
            Settings.copyMaterials = true;
            Settings.excludedFolders = Settings.excludedFolders ?? new List<string>();
            if (!migrateLegacy) return;
            bool any = Settings.copyBaseColor || Settings.copyNormal || Settings.copyMetallic || Settings.copyOcclusion ||
                       Settings.copyEmission || Settings.copyHeight || Settings.copyMask || Settings.copyOtherTextures;
            if (!any) Settings.copyBaseColor = Settings.copyNormal = Settings.copyMetallic = Settings.copyOcclusion =
                Settings.copyEmission = Settings.copyHeight = Settings.copyMask = Settings.copyOtherTextures = true;
        }

        static string FindLoadedSceneReference(IEnumerable<string> prefabPaths)
        {
            var paths = new HashSet<string>(prefabPaths, StringComparer.OrdinalIgnoreCase);
            foreach (var gameObject in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (gameObject == null || !gameObject.scene.IsValid() || !gameObject.scene.isLoaded) continue;
                string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(gameObject);
                if (paths.Contains(path)) return path;
            }
            return string.Empty;
        }

        static ResourceVariantPreset Clone(ResourceVariantPreset value) =>
            JsonUtility.FromJson<ResourceVariantPreset>(JsonUtility.ToJson(value));
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Bento
{
    public sealed class ModelMaterialRemapSnapshot
    {
        public string modelPath;
        public ModelImporterMaterialImportMode importMode;
        public ModelImporterMaterialLocation materialLocation;
        public readonly Dictionary<string, Material> materials = new Dictionary<string, Material>(StringComparer.Ordinal);
    }

    public static class ModelMaterialRemapService
    {
        public static ModelMaterialRemapSnapshot Capture(string modelPath)
        {
            var importer = AssetImporter.GetAtPath(modelPath) as ModelImporter ??
                           throw new InvalidOperationException("无法读取模型导入设置：" + modelPath);
            var snapshot = new ModelMaterialRemapSnapshot
            {
                modelPath = modelPath,
                importMode = importer.materialImportMode,
                materialLocation = importer.materialLocation
            };
            foreach (var pair in importer.GetExternalObjectMap())
                if (pair.Key.type == typeof(Material)) snapshot.materials[pair.Key.name] = pair.Value as Material;
            return snapshot;
        }

        public static void Restore(ModelMaterialRemapSnapshot snapshot)
        {
            if (snapshot == null || string.IsNullOrEmpty(snapshot.modelPath))
                throw new ArgumentException("材质映射快照无效。", nameof(snapshot));
            var importer = AssetImporter.GetAtPath(snapshot.modelPath) as ModelImporter ??
                           throw new InvalidOperationException("无法读取模型导入设置：" + snapshot.modelPath);
            foreach (var identifier in importer.GetExternalObjectMap().Keys.ToArray())
                if (identifier.type == typeof(Material)) importer.RemoveRemap(identifier);
            foreach (var pair in snapshot.materials)
                if (pair.Value != null)
                    importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), pair.Key), pair.Value);
            importer.materialImportMode = snapshot.importMode;
            importer.materialLocation = snapshot.materialLocation;
            importer.SaveAndReimport();
        }

        public static IReadOnlyList<string> GetSlotNames(string modelPath)
        {
            var importer = AssetImporter.GetAtPath(modelPath) as ModelImporter ??
                           throw new InvalidOperationException("无法读取模型导入设置：" + modelPath);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath) ??
                        throw new InvalidOperationException("无法加载模型资产：" + modelPath);
            return CollectSlotNames(model, importer).OrderBy(name => name, StringComparer.Ordinal).ToArray();
        }

        public static int RemapSlots(string modelPath, IReadOnlyDictionary<string, Material> slotMaterials)
        {
            if (slotMaterials == null) throw new ArgumentNullException(nameof(slotMaterials));
            var importer = AssetImporter.GetAtPath(modelPath) as ModelImporter ??
                           throw new InvalidOperationException("无法读取模型导入设置：" + modelPath);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath) ??
                        throw new InvalidOperationException("无法加载模型资产：" + modelPath);
            var slotNames = CollectSlotNames(model, importer);
            var mappings = slotMaterials
                .Where(pair => slotNames.Contains(pair.Key) && pair.Value != null)
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            if (mappings.Count == 0)
                throw new InvalidOperationException($"目标 FBX 没有可写入的有效材质映射。\n目标：{modelPath}\n请回到匹配列表检查槽名为空、未勾选或未指定材质的条目，并使用定位按钮检查对应对象。");

            foreach (var identifier in importer.GetExternalObjectMap().Keys.ToArray())
                if (identifier.type == typeof(Material) && mappings.ContainsKey(identifier.name)) importer.RemoveRemap(identifier);
            foreach (var pair in mappings)
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), pair.Key), pair.Value);

            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            importer.materialLocation = ModelImporterMaterialLocation.External;
            importer.SaveAndReimport();
            Verify(modelPath, mappings);
            return mappings.Count;
        }

        public static int RemapAllSlots(string modelPath, Material targetMaterial)
        {
            if (targetMaterial == null) throw new ArgumentNullException(nameof(targetMaterial));
            var importer = AssetImporter.GetAtPath(modelPath) as ModelImporter ??
                           throw new InvalidOperationException("无法读取模型导入设置：" + modelPath);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath) ??
                        throw new InvalidOperationException("无法加载模型资产：" + modelPath);

            var slotNames = CollectSlotNames(model, importer);
            if (slotNames.Count == 0) throw new InvalidOperationException("模型中没有可映射的材质槽：" + modelPath);

            return RemapSlots(modelPath, slotNames.ToDictionary(name => name, _ => targetMaterial, StringComparer.Ordinal));
        }


        static HashSet<string> CollectSlotNames(GameObject model, ModelImporter importer)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
            foreach (var material in renderer.sharedMaterials)
                if (material != null && !string.IsNullOrWhiteSpace(material.name)) names.Add(material.name);
            foreach (var identifier in importer.GetExternalObjectMap().Keys)
                if (identifier.type == typeof(Material) && !string.IsNullOrWhiteSpace(identifier.name)) names.Add(identifier.name);
            return names;
        }

        static void Verify(string modelPath, IReadOnlyDictionary<string, Material> expected)
        {
            var importer = AssetImporter.GetAtPath(modelPath) as ModelImporter ??
                           throw new InvalidOperationException("重导入后无法读取模型导入设置：" + modelPath);
            var external = importer.GetExternalObjectMap();
            var invalid = expected.Where(pair =>
            {
                var identifier = new AssetImporter.SourceAssetIdentifier(typeof(Material), pair.Key);
                return !external.TryGetValue(identifier, out var mapped) || mapped != pair.Value;
            }).Select(pair => pair.Key).ToArray();
            if (invalid.Length > 0)
                throw new InvalidOperationException($"目标 FBX 重导入后，以下材质槽映射未生效：\n- {string.Join("\n- ", invalid)}\n目标：{modelPath}\n请在工具中定位目标 FBX，并检查 Inspector > Materials 中的槽名。");
        }
    }
}

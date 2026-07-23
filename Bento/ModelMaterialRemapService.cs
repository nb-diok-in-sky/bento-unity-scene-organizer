using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Bento
{
    public static class ModelMaterialRemapService
    {
        public static int RemapAllSlots(string modelPath, Material targetMaterial)
        {
            if (targetMaterial == null) throw new ArgumentNullException(nameof(targetMaterial));
            var importer = AssetImporter.GetAtPath(modelPath) as ModelImporter ??
                           throw new InvalidOperationException("无法读取模型导入设置：" + modelPath);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath) ??
                        throw new InvalidOperationException("无法加载模型资产：" + modelPath);

            var slotNames = CollectSlotNames(model, importer);
            if (slotNames.Count == 0) throw new InvalidOperationException("模型中没有可映射的材质槽：" + modelPath);

            foreach (var identifier in importer.GetExternalObjectMap().Keys.ToArray())
                if (identifier.type == typeof(Material)) importer.RemoveRemap(identifier);
            foreach (string slotName in slotNames)
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), slotName), targetMaterial);

            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            importer.materialLocation = ModelImporterMaterialLocation.External;
            importer.SaveAndReimport();
            Verify(modelPath, targetMaterial);
            return slotNames.Count;
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

        static void Verify(string modelPath, Material targetMaterial)
        {
            var reloaded = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath) ??
                           throw new InvalidOperationException("重导入后无法加载模型：" + modelPath);
            var invalid = reloaded.GetComponentsInChildren<Renderer>(true)
                .Where(renderer => renderer.sharedMaterials.Any(material => material != targetMaterial))
                .Select(renderer => renderer.name)
                .ToArray();
            if (invalid.Length > 0)
                throw new InvalidOperationException("模型重导入后仍存在错误材质：" + string.Join("、", invalid));
        }
    }
}

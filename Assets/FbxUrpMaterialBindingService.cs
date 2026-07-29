using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Bento
{
    static class FbxUrpMaterialBindingService
    {
        public static bool IsFbx(GameObject asset)
        {
            string path = asset == null ? string.Empty : AssetDatabase.GetAssetPath(asset);
            return path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase);
        }

        public static List<FbxTextureBinding> BuildBindings(IEnumerable<Texture2D> textures)
        {
            return textures.Where(texture => texture != null)
                .Distinct()
                .Select(texture =>
                {
                    string suffix = TexRules.ExtractSuffix(texture.name, new HashSet<string> { texture.name }).suffix;
                    return new FbxTextureBinding { texture = texture, type = TexRules.GuessType(suffix) };
                })
                .ToList();
        }

        public static Material CreateAndBind(GameObject fbx, IEnumerable<FbxTextureBinding> bindings, string selectedFolder)
        {
            if (!IsFbx(fbx)) throw new InvalidOperationException("请选择 Project 中的 .fbx 资产。");
            string fbxPath = AssetDatabase.GetAssetPath(fbx);
            string folder = ResolveOutputFolder(selectedFolder, fbxPath);
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("找不到 Universal Render Pipeline/Lit。");

            var material = new Material(shader) { name = fbx.name + "_Material" };
            foreach (var binding in bindings.Where(item => item.texture != null && item.type != TexType.Unknown))
            {
                if (TexRules.URPLitProps.TryGetValue(binding.type, out string property) && material.HasProperty(property))
                    material.SetTexture(property, binding.texture);
            }
            SetKeyword(material, "_NORMALMAP", "_BumpMap");
            SetKeyword(material, "_METALLICSPECGLOSSMAP", "_MetallicGlossMap");
            SetKeyword(material, "_OCCLUSIONMAP", "_OcclusionMap");
            SetKeyword(material, "_EMISSION", "_EmissionMap");

            string materialPath = BentoUtility.GenerateUniqueAssetPath(folder, material.name, ".mat");
            AssetDatabase.CreateAsset(material, materialPath);
            try
            {
                RemapAllMaterialSlots(fbxPath, material);
            }
            catch
            {
                AssetDatabase.DeleteAsset(materialPath);
                throw;
            }
            return material;
        }

        public static string ResolveOutputFolder(string selectedFolder, string assetPath)
        {
            if (BentoUtility.IsProjectFolder(selectedFolder)) return selectedFolder;
            string folder = Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(folder) || !AssetDatabase.IsValidFolder(folder))
                throw new InvalidOperationException("无法确定输出目录。");
            return folder;
        }

        static void RemapAllMaterialSlots(string fbxPath, Material material)
        {
            var importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
            if (importer == null) throw new InvalidOperationException("无法读取 FBX ModelImporter。");
            var identifiers = AssetDatabase.LoadAllAssetsAtPath(fbxPath)
                .OfType<Material>()
                .Select(source => new AssetImporter.SourceAssetIdentifier(typeof(Material), source.name))
                .ToList();
            if (identifiers.Count == 0)
                throw new InvalidOperationException("FBX 没有可映射的材质槽。请检查 Import Settings > Materials。");

            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            foreach (var identifier in identifiers) importer.AddRemap(identifier, material);
            importer.SaveAndReimport();
        }

        static void SetKeyword(Material material, string keyword, string property)
        {
            if (material.HasProperty(property) && material.GetTexture(property) != null) material.EnableKeyword(keyword);
            else material.DisableKeyword(keyword);
        }
    }
}

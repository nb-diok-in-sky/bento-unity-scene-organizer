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
                    return new FbxTextureBinding { texture = texture, role = GuessRole(suffix) };
                })
                .ToList();
        }

        public static Material CreateAndBind(GameObject fbx, IEnumerable<FbxTextureBinding> bindings, PackedMaskLayout maskLayout, string selectedFolder)
        {
            if (!IsFbx(fbx)) throw new InvalidOperationException("请选择 Project 中的 .fbx 资产。");
            string fbxPath = AssetDatabase.GetAssetPath(fbx);
            string folder = ResolveOutputFolder(selectedFolder, fbxPath);
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("找不到 Universal Render Pipeline/Lit。");

            var bindingList = bindings.Where(item => item.texture != null && item.role != FbxTextureRole.Ignore).ToList();
            ConfigureImporters(bindingList);
            var material = new Material(shader) { name = fbx.name + "_Material" };
            foreach (var binding in bindingList.Where(item => item.role != FbxTextureRole.PackedMask))
            {
                if (TryGetUrpProperty(binding.role, out string property) && material.HasProperty(property))
                    material.SetTexture(property, binding.texture);
            }

            var packedMask = bindingList.FirstOrDefault(item => item.role == FbxTextureRole.PackedMask)?.texture;
            var generatedTextures = new List<string>();
            if (packedMask != null)
            {
                var generated = PackForUrp(packedMask, maskLayout ?? new PackedMaskLayout(), folder, fbx.name);
                generatedTextures.Add(generated.metallicPath);
                generatedTextures.Add(generated.occlusionPath);
                material.SetTexture("_MetallicGlossMap", generated.metallicSmoothness);
                material.SetTexture("_OcclusionMap", generated.occlusion);
            }
            if (material.HasProperty("_SmoothnessTextureChannel")) material.SetFloat("_SmoothnessTextureChannel", 0f);
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
                foreach (string path in generatedTextures) AssetDatabase.DeleteAsset(path);
                throw;
            }
            return material;
        }

        static FbxTextureRole GuessRole(string suffix)
        {
            if (suffix.Equals("msa", StringComparison.OrdinalIgnoreCase) ||
                suffix.Equals("mask", StringComparison.OrdinalIgnoreCase) ||
                suffix.Equals("packed", StringComparison.OrdinalIgnoreCase))
                return FbxTextureRole.PackedMask;

            return TexRules.GuessType(suffix) switch
            {
                TexType.BaseColor => FbxTextureRole.BaseColor,
                TexType.Normal => FbxTextureRole.Normal,
                TexType.Metallic => FbxTextureRole.Metallic,
                TexType.AO => FbxTextureRole.AO,
                TexType.Height => FbxTextureRole.Height,
                TexType.Emission => FbxTextureRole.Emission,
                _ => FbxTextureRole.Ignore
            };
        }

        static bool TryGetUrpProperty(FbxTextureRole role, out string property)
        {
            property = role switch
            {
                FbxTextureRole.BaseColor => "_BaseMap",
                FbxTextureRole.Normal => "_BumpMap",
                FbxTextureRole.Metallic => "_MetallicGlossMap",
                FbxTextureRole.AO => "_OcclusionMap",
                FbxTextureRole.Height => "_ParallaxMap",
                FbxTextureRole.Emission => "_EmissionMap",
                _ => null
            };
            return property != null;
        }

        static void ConfigureImporters(IEnumerable<FbxTextureBinding> bindings)
        {
            foreach (var binding in bindings)
            {
                string path = AssetDatabase.GetAssetPath(binding.texture);
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) continue;
                bool dirty = false;
                if (binding.role == FbxTextureRole.Normal && importer.textureType != TextureImporterType.NormalMap)
                {
                    importer.textureType = TextureImporterType.NormalMap;
                    dirty = true;
                }
                bool shouldUseSrgb = binding.role == FbxTextureRole.BaseColor || binding.role == FbxTextureRole.Emission;
                if (importer.sRGBTexture != shouldUseSrgb)
                {
                    importer.sRGBTexture = shouldUseSrgb;
                    dirty = true;
                }
                if (dirty) importer.SaveAndReimport();
            }
        }

        static (Texture2D metallicSmoothness, Texture2D occlusion, string metallicPath, string occlusionPath) PackForUrp(
            Texture2D source, PackedMaskLayout layout, string folder, string outputName)
        {
            var pixels = ReadPixels(source);
            var metallicPixels = new Color32[pixels.Length];
            var occlusionPixels = new Color32[pixels.Length];
            for (int i = 0; i < pixels.Length; i++)
            {
                byte metallic = ReadChannel(pixels[i], layout.metallic);
                byte smoothness = ReadChannel(pixels[i], layout.smoothness);
                if (layout.invertSmoothness) smoothness = (byte)(255 - smoothness);
                byte ao = ReadChannel(pixels[i], layout.occlusion);
                metallicPixels[i] = new Color32(metallic, 0, 0, smoothness);
                occlusionPixels[i] = new Color32(ao, ao, ao, 255);
            }

            string metallicPath = BentoUtility.GenerateUniqueAssetPath(folder, outputName + "_MetallicSmoothness", ".png");
            string occlusionPath = BentoUtility.GenerateUniqueAssetPath(folder, outputName + "_Occlusion", ".png");
            WritePng(metallicPath, source.width, source.height, metallicPixels);
            WritePng(occlusionPath, source.width, source.height, occlusionPixels);
            AssetDatabase.ImportAsset(metallicPath, ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.ImportAsset(occlusionPath, ImportAssetOptions.ForceSynchronousImport);
            ConfigureDataTexture(metallicPath);
            ConfigureDataTexture(occlusionPath);
            return (AssetDatabase.LoadAssetAtPath<Texture2D>(metallicPath), AssetDatabase.LoadAssetAtPath<Texture2D>(occlusionPath), metallicPath, occlusionPath);
        }

        static Color32[] ReadPixels(Texture2D source)
        {
            string path = AssetDatabase.GetAssetPath(source);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) throw new InvalidOperationException($"无法读取贴图导入设置：{path}");
            bool wasReadable = importer.isReadable;
            try
            {
                if (!wasReadable)
                {
                    importer.isReadable = true;
                    importer.SaveAndReimport();
                    source = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                }
                return source.GetPixels32();
            }
            finally
            {
                if (!wasReadable)
                {
                    importer = AssetImporter.GetAtPath(path) as TextureImporter;
                    if (importer != null)
                    {
                        importer.isReadable = false;
                        importer.SaveAndReimport();
                    }
                }
            }
        }

        static byte ReadChannel(Color32 pixel, PackedMaskChannel channel) => channel switch
        {
            PackedMaskChannel.R => pixel.r,
            PackedMaskChannel.G => pixel.g,
            PackedMaskChannel.B => pixel.b,
            PackedMaskChannel.A => pixel.a,
            PackedMaskChannel.One => 255,
            _ => 0
        };

        static void WritePng(string assetPath, int width, int height, Color32[] pixels)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
            try
            {
                texture.SetPixels32(pixels);
                texture.Apply();
                string absolutePath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetPath));
                File.WriteAllBytes(absolutePath, texture.EncodeToPNG());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        static void ConfigureDataTexture(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) return;
            importer.sRGBTexture = false;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.SaveAndReimport();
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

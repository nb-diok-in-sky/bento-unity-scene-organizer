using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Formats.Fbx.Exporter;
using UnityEngine;

namespace Bento
{
    [Serializable]
    public sealed class ObjToFbxConversionRequest
    {
        public string objAssetPath;
        public string mtlAssetPath;
        public string outputFolder;
        public string outputName;
        public string materialFolder;
    }

    public static class ObjToFbxConversionService
    {

        public static string Convert(ObjToFbxConversionRequest request)
        {
            Validate(request);
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string mtlAbsolute = Path.Combine(projectRoot, request.mtlAssetPath);
            string fbxAssetPath = AssetDatabase.GenerateUniqueAssetPath(
                $"{request.outputFolder.TrimEnd('/')}/{BentoUtility.SanitizeFileName(request.outputName, "Model")}.fbx");
            string fbxAbsolute = Path.Combine(projectRoot, fbxAssetPath);
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(request.objAssetPath);
            if (source == null) throw new InvalidOperationException("Unity 无法加载所选 OBJ。");

            GameObject instance = null;
            try
            {
                instance = UnityEngine.Object.Instantiate(source);
                instance.name = request.outputName;
                var options = new ExportModelOptions
                {
                    ExportFormat = ExportFormat.Binary,
                    ModelAnimIncludeOption = Include.Model,
                    ObjectPosition = ObjectPosition.Reset,
                    UseMayaCompatibleNames = false,
                    ExportUnrendered = true,
                    KeepInstances = true,
                    EmbedTextures = false
                };
                string exported = ModelExporter.ExportObject(fbxAbsolute, instance, options);
                if (string.IsNullOrEmpty(exported) || !File.Exists(fbxAbsolute) || new FileInfo(fbxAbsolute).Length == 0)
                    throw new InvalidOperationException("Unity FBX Exporter 未生成有效 FBX。");

                AssetDatabase.Refresh();
                var definitions = ObjMaterialParser.Parse(mtlAbsolute);
                var materials = CreateMaterials(definitions, request.mtlAssetPath, request.materialFolder);
                RemapFbxMaterials(fbxAssetPath, materials);
                ValidateFbx(fbxAssetPath, materials);
                return fbxAssetPath;
            }
            finally
            {
                if (instance != null) UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        static void Validate(ObjToFbxConversionRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (string.IsNullOrEmpty(request.objAssetPath) || !request.objAssetPath.EndsWith(".obj", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("请选择 OBJ 资产。");
            if (string.IsNullOrEmpty(request.mtlAssetPath) || !request.mtlAssetPath.EndsWith(".mtl", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("请选择 MTL 文件。");
            if (!BentoUtility.IsProjectFolder(request.outputFolder) || !BentoUtility.IsProjectFolder(request.materialFolder))
                throw new InvalidOperationException("输出文件夹无效。");
        }

        static Dictionary<string, Material> CreateMaterials(IEnumerable<ObjMaterialDefinition> definitions, string mtlAssetPath, string materialFolder)
        {
            string sourceFolder = Path.GetDirectoryName(mtlAssetPath)?.Replace('\\', '/');
            var result = new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);
            foreach (ObjMaterialDefinition definition in definitions)
            {
                string path = AssetDatabase.GenerateUniqueAssetPath(
                    $"{materialFolder.TrimEnd('/')}/{BentoUtility.SanitizeFileName(definition.name, "Material")}.mat");
                var material = new Material(shader) { name = Path.GetFileNameWithoutExtension(path) };

                if (definition.opacity < 0.999f)
                {
                }
                AssetDatabase.CreateAsset(material, path);
                result[definition.name] = material;
            }
            AssetDatabase.SaveAssets();
            return result;
        }

        static Texture LoadTexture(string folder, string file)
        {
            if (string.IsNullOrEmpty(file)) return null;
            return AssetDatabase.LoadAssetAtPath<Texture>($"{folder}/{file}".Replace("//", "/"));
        }

        static string BuildTexturePath(string folder, string file)
        {
            return string.IsNullOrEmpty(file) ? string.Empty : $"{folder}/{file}".Replace("//", "/");
        }

        static void SetTexture(Material material, string property, Texture texture)
        {
            if (texture != null && material.HasProperty(property)) material.SetTexture(property, texture);
        }

        static void SetFloat(Material material, string property, float value)
        {
            if (material.HasProperty(property)) material.SetFloat(property, value);
        }

        static void RemapFbxMaterials(string fbxPath, IReadOnlyDictionary<string, Material> materials)
        {
            var importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter ??
                           throw new InvalidOperationException("无法读取 FBX ModelImporter。");
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            importer.materialLocation = ModelImporterMaterialLocation.External;
            foreach (var pair in materials)
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), pair.Key), pair.Value);
            importer.SaveAndReimport();
        }

        static void ValidateFbx(string fbxPath, IReadOnlyDictionary<string, Material> materials)
        {
            AssetDatabase.ImportAsset(fbxPath, ImportAssetOptions.ForceSynchronousImport);
            var importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter ??
                           throw new InvalidOperationException("无法读取转换后的 FBX ModelImporter。");

            foreach (var pair in materials)
            {
                var sourceIdentifier = new AssetImporter.SourceAssetIdentifier(typeof(Material), pair.Key);
                UnityEngine.Object remapped = importer.GetExternalObjectMap()
                    .TryGetValue(sourceIdentifier, out UnityEngine.Object mapped)
                    ? mapped
                    : null;
                if (remapped != pair.Value)
                    throw new InvalidOperationException($"FBX 材质槽“{pair.Key}”未成功映射到生成的独立材质。");
            }

            GameObject fbx = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            if (fbx == null) throw new InvalidOperationException("转换后的 FBX 无法加载。");

            var expected = new HashSet<Material>(materials.Values);
            foreach (Renderer renderer in fbx.GetComponentsInChildren<Renderer>(true))
            foreach (Material assigned in renderer.sharedMaterials)
            {
                if (assigned != null && expected.Contains(assigned))
                    expected.Remove(assigned);
            }

            if (expected.Count > 0)
                throw new InvalidOperationException("FBX 导入结果尚未应用全部独立材质，请重新导入后再试。");
        }
    }
}

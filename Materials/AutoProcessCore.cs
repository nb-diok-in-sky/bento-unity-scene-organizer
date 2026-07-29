using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Bento
{
    sealed class AutoProcessGroup
    {
        public string category;
        public string folderPath;
        public List<TexInfo> textures = new List<TexInfo>();
        public List<Material> materials = new List<Material>();
        public MaterialTextureCatalog catalog = new MaterialTextureCatalog();
        public List<(Material material, string groupName, Dictionary<TexType, Texture2D> slots)> matches =
            new List<(Material, string, Dictionary<TexType, Texture2D>)>();
    }

    sealed class AutoProcessOptions
    {
        public bool rename = true;
        public bool fill = true;
        public bool replaceShader;
        public bool repair = true;
        public Shader targetShader;
    }

    sealed class AutoProcessResult
    {
        public int renamed;
        public int shaderReplaced;
        public int repaired;
        public int filled;
        public bool cancelled;
    }

    sealed class AutoProcessException : Exception
    {
        public AutoProcessResult Result { get; }

        public AutoProcessException(Exception innerException, AutoProcessResult result)
            : base(innerException.Message +
                   $"\n\n中断前已完成：改名 {result.renamed}、Shader 替换 {result.shaderReplaced}、材质修复 {result.repaired}、贴图填充 {result.filled}。" +
                   "\n请检查结果后，只重试尚未完成的阶段。",
                innerException)
        {
            Result = result;
        }
    }

    static class AutoProcessPlanner
    {
        public static List<AutoProcessGroup> Build(string rootFolder, SharedContext context)
        {
            var folders = AssetDatabase.GetSubFolders(rootFolder).ToList();
            if (folders.Count == 0) folders.Add(rootFolder);
            return folders.Select(folder => BuildGroup(rootFolder, folder, context)).Where(group => group != null).ToList();
        }

        public static void Refresh(AutoProcessGroup group, SharedContext context)
        {
            var refreshed = BuildGroup(group.folderPath, group.folderPath, context);
            if (refreshed == null) return;
            group.textures = refreshed.textures;
            group.catalog = refreshed.catalog;
            group.matches = Match(group.materials, group.catalog, context);
        }

        static AutoProcessGroup BuildGroup(string root, string folder, SharedContext context)
        {
            var textureGuids = AssetDatabase.FindAssets("t:Texture2D", new[] { folder });
            var materialGuids = AssetDatabase.FindAssets("t:Material", new[] { folder });
            if (textureGuids.Length == 0 && materialGuids.Length == 0) return null;
            var group = new AutoProcessGroup
            {
                category = folder == root ? Path.GetFileName(root) : folder.Substring(root.Length + 1),
                folderPath = folder,
                catalog = MaterialTextureMatchService.BuildCatalog(folder)
            };
            foreach (string guid in textureGuids) AddTexture(group, AssetDatabase.GUIDToAssetPath(guid), context);
            InferUnknownTextureTypes(group);
            group.materials = materialGuids.Select(AssetDatabase.GUIDToAssetPath).Select(AssetDatabase.LoadAssetAtPath<Material>).Where(material => material != null).ToList();
            group.matches = Match(group.materials, group.catalog, context);
            return group;
        }

        static void AddTexture(AutoProcessGroup group, string path, SharedContext context)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            var (baseName, suffix) = TexRules.ExtractSuffix(name, group.catalog.textureNames);
            TexType type = context.suffixMap.TryGetValue(suffix, out var overridden) ? overridden : TexRules.GuessType(suffix);
            group.textures.Add(new TexInfo { assetPath = path, nameNoExt = name, baseName = baseName, suffix = suffix, type = type });
        }

        static void InferUnknownTextureTypes(AutoProcessGroup group)
        {
            foreach (string suffix in group.textures.Where(texture => texture.type == TexType.Unknown).Select(texture => texture.suffix).Distinct())
            {
                var sample = group.textures.First(texture => texture.suffix == suffix);
                TexType guessed = TexRules.GuessTypeFromContent(sample.assetPath);
                if (guessed == TexType.Unknown) continue;
                foreach (var texture in group.textures.Where(texture => texture.suffix == suffix)) { texture.type = guessed; texture.guessedByContent = true; }
            }
        }

        static List<(Material, string, Dictionary<TexType, Texture2D>)> Match(
            IEnumerable<Material> materials,
            MaterialTextureCatalog catalog,
            SharedContext context)
        {
            return MaterialTextureMatchService.Match(materials, catalog, context.suffixMap)
                .Select(info => (info.material, info.matchedGroupName, info.matched)).ToList();
        }
    }

    static class AutoProcessService
    {
        public static AutoProcessResult Execute(
            string rootFolder,
            SharedContext context,
            AutoProcessOptions options)
        {
            if (!BentoUtility.IsProjectFolder(rootFolder))
                throw new InvalidOperationException("资产根文件夹已失效，请重新选择并扫描。");

            // 预览仅用于人工确认；执行前重新构建，避免沿用已经过期的资源和匹配结果。
            List<AutoProcessGroup> groups = AutoProcessPlanner.Build(rootFolder, context);
            var result = new AutoProcessResult();
            try
            {
                var materials = groups.SelectMany(group => group.materials).ToArray();
                if (options.rename) result.renamed = Rename(groups.SelectMany(group => group.textures).ToList(), context);
                if (options.rename && result.renamed > 0) foreach (var group in groups) AutoProcessPlanner.Refresh(group, context);
                if (options.replaceShader && options.targetShader != null) result.shaderReplaced = MaterialShaderOperations.ReplaceShaders(materials, options.targetShader);
                if (options.repair) result.repaired = MaterialRepairService.RepairFolder(rootFolder);
                if (options.fill) result.filled = Fill(groups, result);
                return result;
            }
            catch (Exception exception)
            {
                throw new AutoProcessException(exception, result);
            }
        }

        static int Rename(IReadOnlyList<TexInfo> textures, SharedContext context)
        {
            var sourceTextures = new List<TexInfo>();
            var plan = new List<AssetRenameEntry>();
            foreach (var texture in textures)
            {
                if (texture.type == TexType.Unknown || !context.outputSuffixes.TryGetValue(texture.type, out string suffix)) continue;
                string newName = string.IsNullOrEmpty(suffix) ? texture.baseName : texture.baseName + "_" + suffix;
                if (newName == texture.nameNoExt) continue;
                sourceTextures.Add(texture);
                plan.Add(SafeAssetRenameService.BuildEntry(texture.assetPath, newName));
            }
            if (plan.Count == 0) return 0;
            SafeAssetRenameService.Execute(plan, new AssetRenameHistory());
            for (int index = 0; index < sourceTextures.Count; index++)
            {
                sourceTextures[index].assetPath = plan[index].targetPath;
                sourceTextures[index].nameNoExt = Path.GetFileNameWithoutExtension(plan[index].targetPath);
            }
            return plan.Count;
        }

        static int Fill(IEnumerable<AutoProcessGroup> groups, AutoProcessResult result)
        {
            var candidates = groups.SelectMany(group => group.matches).ToList();
            var matches = new List<MatFillInfo>();
            try
            {
                for (int index = 0; index < candidates.Count; index++)
                {
                    var match = candidates[index];
                    if (EditorUtility.DisplayCancelableProgressBar(
                            "批量处理 - 填充材质球",
                            match.material.name,
                            (float)index / Math.Max(1, candidates.Count)))
                    {
                        result.cancelled = true;
                        break;
                    }
                    matches.Add(new MatFillInfo { material = match.material, matchedGroupName = match.groupName, matched = match.slots });
                }
            }
            finally { EditorUtility.ClearProgressBar(); }
            return MaterialTextureMatchService.Fill(matches, "AutoProcess Fill Textures");
        }
    }
}

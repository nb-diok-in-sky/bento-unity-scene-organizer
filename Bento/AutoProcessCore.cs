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
        public Dictionary<string, Dictionary<string, string>> textureGroups = new Dictionary<string, Dictionary<string, string>>();
        public HashSet<string> textureNames = new HashSet<string>();
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
            group.textureGroups = refreshed.textureGroups;
            group.textureNames = refreshed.textureNames;
            group.matches = Match(group.materials, group.textureGroups, group.textureNames, context);
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
                textureNames = new HashSet<string>(textureGuids.Select(guid => Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(guid))))
            };
            foreach (string guid in textureGuids) AddTexture(group, AssetDatabase.GUIDToAssetPath(guid), context);
            TexRules.ConsolidateTexGroups(group.textureGroups);
            InferUnknownTextureTypes(group);
            group.materials = materialGuids.Select(AssetDatabase.GUIDToAssetPath).Select(AssetDatabase.LoadAssetAtPath<Material>).Where(material => material != null).ToList();
            group.matches = Match(group.materials, group.textureGroups, group.textureNames, context);
            return group;
        }

        static void AddTexture(AutoProcessGroup group, string path, SharedContext context)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            var (baseName, suffix) = TexRules.ExtractSuffix(name, group.textureNames);
            TexType type = context.suffixMap.TryGetValue(suffix, out var overridden) ? overridden : TexRules.GuessType(suffix);
            group.textures.Add(new TexInfo { assetPath = path, nameNoExt = name, baseName = baseName, suffix = suffix, type = type });
            if (!group.textureGroups.TryGetValue(baseName, out var suffixes))
                group.textureGroups[baseName] = suffixes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            suffixes[suffix] = path;
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
            Dictionary<string, Dictionary<string, string>> groups,
            HashSet<string> names,
            SharedContext context)
        {
            return materials.Select(material =>
            {
                var (groupName, textureGroup) = TexRules.FindTexGroup(material.name, groups, names);
                var slots = textureGroup != null ? TexRules.MatchTexSlots(material, textureGroup, context.suffixMap) : new Dictionary<TexType, Texture2D>();
                return (material, groupName, slots);
            }).ToList();
        }
    }

    static class AutoProcessService
    {
        public static AutoProcessResult Execute(
            IReadOnlyList<AutoProcessGroup> groups,
            string rootFolder,
            SharedContext context,
            AutoProcessOptions options)
        {
            var result = new AutoProcessResult();
            var materials = groups.SelectMany(group => group.materials).ToArray();
            if (options.rename) result.renamed = Rename(groups.SelectMany(group => group.textures).ToList(), context);
            if (options.rename && result.renamed > 0) foreach (var group in groups) AutoProcessPlanner.Refresh(group, context);
            if (options.replaceShader && options.targetShader != null) result.shaderReplaced = TexRules.ReplaceShaders(materials, options.targetShader);
            if (options.repair) result.repaired = MaterialRepairService.RepairFolder(rootFolder);
            if (options.fill) result.filled = Fill(groups, materials, result);
            return result;
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

        static int Fill(IEnumerable<AutoProcessGroup> groups, Material[] materials, AutoProcessResult result)
        {
            Undo.RecordObjects(materials, "AutoProcess Fill Textures");
            int count = 0;
            foreach (var match in groups.SelectMany(group => group.matches))
            {
                if (EditorUtility.DisplayCancelableProgressBar("批量处理 - 填充材质球", match.material.name, 0f)) { result.cancelled = true; break; }
                int slotCount = match.slots.Count(pair => pair.Value != null);
                if (slotCount == 0) continue;
                TexRules.FillMaterialSlots(match.material, match.slots);
                EditorUtility.SetDirty(match.material);
                count += slotCount;
            }
            EditorUtility.ClearProgressBar();
            AssetDatabase.SaveAssets();
            return count;
        }
    }
}

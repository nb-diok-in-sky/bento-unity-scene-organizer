using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Bento
{
    sealed class MaterialTextureCatalog
    {
        public readonly Dictionary<string, Dictionary<string, string>> groups =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        public readonly HashSet<string> textureNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    static class MaterialTextureMatchService
    {
        public static MaterialTextureCatalog BuildCatalog(string folder)
        {
            var catalog = new MaterialTextureCatalog();
            string[] paths = AssetDatabase.FindAssets("t:Texture2D", new[] { folder })
                .Select(AssetDatabase.GUIDToAssetPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            foreach (string path in paths) catalog.textureNames.Add(Path.GetFileNameWithoutExtension(path));
            foreach (string path in paths)
            {
                string name = Path.GetFileNameWithoutExtension(path);
                var extracted = TexRules.ExtractSuffix(name, catalog.textureNames);
                if (!catalog.groups.TryGetValue(extracted.baseName, out var suffixes))
                    catalog.groups[extracted.baseName] = suffixes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                suffixes[extracted.suffix] = path;
            }
            TexRules.ConsolidateTexGroups(catalog.groups);
            return catalog;
        }

        public static List<MatFillInfo> Match(
            IEnumerable<Material> materials,
            MaterialTextureCatalog catalog,
            IReadOnlyDictionary<string, TexType> suffixOverrides)
        {
            var result = new List<MatFillInfo>();
            foreach (var material in materials.Where(material => material != null))
            {
                var info = new MatFillInfo { material = material };
                var match = TexRules.FindTexGroup(material.name, catalog.groups, catalog.textureNames);
                if (match.group != null)
                {
                    info.matchedGroupName = match.groupName;
                    info.matched = TexRules.MatchTexSlots(material, match.group,
                        suffixOverrides as Dictionary<string, TexType> ?? new Dictionary<string, TexType>(suffixOverrides));
                }
                result.Add(info);
            }
            return result;
        }

        public static int Fill(IReadOnlyList<MatFillInfo> matches, string undoName)
        {
            var materials = matches.Where(match => match.material != null).Select(match => (UnityEngine.Object)match.material).ToArray();
            if (materials.Length == 0) return 0;
            int count = 0;
            using (var undo = new BentoUndoScope(undoName))
            {
                Undo.RecordObjects(materials, undoName);
                foreach (var match in matches)
                {
                    int slots = match.matched.Count(pair => pair.Value != null);
                    if (slots == 0) continue;
                    MaterialShaderOperations.FillMaterialSlots(match.material, match.matched);
                    EditorUtility.SetDirty(match.material);
                    count += slots;
                }
                undo.Complete();
            }
            AssetDatabase.SaveAssets();
            return count;
        }
    }
}

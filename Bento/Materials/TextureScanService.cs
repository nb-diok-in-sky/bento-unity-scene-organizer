using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Bento
{
    sealed class TextureScanResult
    {
        public readonly List<TexInfo> textures = new List<TexInfo>();
        public readonly Dictionary<string, List<TexInfo>> groups = new Dictionary<string, List<TexInfo>>();
        public readonly Dictionary<string, TexType> suffixMap = new Dictionary<string, TexType>();
        public readonly Dictionary<string, int> suffixCounts = new Dictionary<string, int>();
        public readonly HashSet<string> guessedSuffixes = new HashSet<string>();
    }

    static class TextureScanService
    {
        public static TextureScanResult Scan(IEnumerable<UnityEngine.Object> selection)
        {
            var paths = CollectTexturePaths(selection);
            var result = new TextureScanResult();
            if (paths.Count == 0) return result;
            var names = new HashSet<string>(paths.Select(Path.GetFileNameWithoutExtension));
            foreach (string path in paths)
            {
                string name = Path.GetFileNameWithoutExtension(path);
                var extracted = TexRules.ExtractSuffix(name, names);
                result.textures.Add(new TexInfo
                {
                    assetPath = path,
                    nameNoExt = name,
                    baseName = extracted.baseName,
                    suffix = extracted.suffix,
                    type = TexRules.GuessType(extracted.suffix)
                });
            }
            foreach (var group in result.textures.GroupBy(texture => texture.baseName)) result.groups[group.Key] = group.ToList();
            foreach (var texture in result.textures) if (!result.suffixMap.ContainsKey(texture.suffix)) result.suffixMap[texture.suffix] = texture.type;
            InferUnknownSuffixes(result);
            foreach (var texture in result.textures)
            {
                result.suffixCounts.TryGetValue(texture.suffix, out int count);
                result.suffixCounts[texture.suffix] = count + 1;
                if (texture.guessedByContent) result.guessedSuffixes.Add(texture.suffix);
            }
            return result;
        }

        static HashSet<string> CollectTexturePaths(IEnumerable<UnityEngine.Object> selection)
        {
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var asset in selection ?? Enumerable.Empty<UnityEngine.Object>())
            {
                string path = AssetDatabase.GetAssetPath(asset);
                if (AssetDatabase.IsValidFolder(path))
                    foreach (string guid in AssetDatabase.FindAssets("t:Texture", new[] { path })) paths.Add(AssetDatabase.GUIDToAssetPath(guid));
                else if (asset is Texture && !string.IsNullOrEmpty(path)) paths.Add(path);
            }
            return paths;
        }

        static void InferUnknownSuffixes(TextureScanResult result)
        {
            foreach (string suffix in result.suffixMap.Where(pair => pair.Value == TexType.Unknown).Select(pair => pair.Key).ToArray())
            {
                var sample = result.textures.FirstOrDefault(texture => texture.suffix == suffix);
                if (sample == null) continue;
                TexType guessed = TexRules.GuessTypeFromContent(sample.assetPath);
                if (guessed == TexType.Unknown) continue;
                result.suffixMap[suffix] = guessed;
                foreach (var texture in result.textures.Where(texture => texture.suffix == suffix))
                {
                    texture.type = guessed;
                    texture.guessedByContent = true;
                }
            }
        }
    }

    sealed class TexturePresetStore
    {
        readonly string _directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Bento", "Presets");

        public string[] ListNames()
        {
            return Directory.Exists(_directory)
                ? Directory.GetFiles(_directory, "*.json").Select(Path.GetFileNameWithoutExtension).OrderBy(name => name).ToArray()
                : Array.Empty<string>();
        }

        public void Save(string name, IReadOnlyDictionary<string, TexType> suffixMap, IReadOnlyDictionary<TexType, string> outputSuffixes)
        {
            string safeName = BentoUtility.SanitizeFileName(name, "Preset");
            Directory.CreateDirectory(_directory);
            var data = new PresetData { presetName = safeName };
            foreach (var pair in suffixMap) data.suffixRules.Add(new SuffixRuleEntry { suffix = pair.Key, type = (int)pair.Value });
            foreach (var pair in outputSuffixes) data.outputNames.Add(new OutputNameEntry { type = (int)pair.Key, outputSuffix = pair.Value });
            File.WriteAllText(Path.Combine(_directory, safeName + ".json"), JsonUtility.ToJson(data, true));
        }

        public PresetData Load(string name)
        {
            string path = GetPath(name);
            return File.Exists(path) ? JsonUtility.FromJson<PresetData>(File.ReadAllText(path)) : null;
        }

        public void Delete(string name)
        {
            string path = GetPath(name);
            if (File.Exists(path)) File.Delete(path);
        }

        string GetPath(string name) => Path.Combine(_directory, BentoUtility.SanitizeFileName(name, "Preset") + ".json");
    }
}

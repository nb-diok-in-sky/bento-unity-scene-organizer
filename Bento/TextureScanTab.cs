using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Bento
{
    class TextureScanTab
    {
        readonly SharedContext _ctx;
        Vector2 _scroll;

        Dictionary<string, int> _suffixCounts = new();
        HashSet<string> _guessedSuffixes = new();

        string _presetDir;
        int _selectedPreset;
        string[] _presetNames = {};
        string _newPresetName = "MyPreset";

        public TextureScanTab(SharedContext ctx) => _ctx = ctx;

        public void OnEnable()
        {
            _presetDir = Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
                "Bento", "Presets");
            _ctx.outputSuffixes = new Dictionary<TexType, string>(TexRules.DefaultOutput);
            RefreshPresetList();
        }

        public void Draw()
        {
            EditorGUILayout.LabelField("选中贴图文件或文件夹，然后点击扫描", EditorStyles.miniLabel);
            if (GUILayout.Button("扫描选中的贴图", GUILayout.Height(28)))
                ScanTextures();

            if (!_ctx.scanDone)
            {
                EditorGUILayout.HelpBox("在 Project 面板选中贴图或贴图所在文件夹", MessageType.Info);
                DrawPresetSection();
                return;
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField($"发现 {_ctx.groups.Count} 组贴图，{_ctx.suffixMap.Count} 种后缀", EditorStyles.boldLabel);
            EditorGUILayout.Space(2);

            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.MaxHeight(220));

            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            EditorGUILayout.LabelField("后缀", EditorStyles.boldLabel, GUILayout.Width(140));
            EditorGUILayout.LabelField("次数", EditorStyles.boldLabel, GUILayout.Width(45));
            EditorGUILayout.LabelField("类型", EditorStyles.boldLabel);
            EditorGUILayout.EndHorizontal();

            foreach (var suffix in _ctx.suffixMap.Keys.OrderByDescending(s => _suffixCounts.GetValueOrDefault(s, 0)))
            {
                int count = _suffixCounts.GetValueOrDefault(suffix, 0);
                EditorGUILayout.BeginHorizontal();

                var label = string.IsNullOrEmpty(suffix) ? "(无后缀)" : "_" + suffix;
                bool isGuessed = _guessedSuffixes.Contains(suffix);
                if (isGuessed) label += " (像素)";

                var prevColor = GUI.contentColor;
                if (_ctx.suffixMap[suffix] == TexType.Unknown)
                    GUI.contentColor = new Color(1f, 0.7f, 0.2f);
                else if (isGuessed)
                    GUI.contentColor = new Color(0.4f, 0.8f, 1f);

                EditorGUILayout.LabelField(label, GUILayout.Width(140));
                GUI.contentColor = prevColor;
                EditorGUILayout.LabelField(count.ToString(), GUILayout.Width(45));

                var newType = (TexType)EditorGUILayout.EnumPopup(_ctx.suffixMap[suffix]);
                if (newType != _ctx.suffixMap[suffix])
                {
                    _ctx.suffixMap[suffix] = newType;
                    foreach (var t in _ctx.textures.Where(t => t.suffix == suffix))
                        t.type = newType;
                }

                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(6);

            EditorGUILayout.LabelField("输出命名规范（改名后的后缀）", EditorStyles.boldLabel);
            foreach (TexType type in Enum.GetValues(typeof(TexType)))
            {
                if (type == TexType.Unknown) continue;
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(type.ToString(), GUILayout.Width(110));
                EditorGUILayout.LabelField("{名}_", GUILayout.Width(35));
                _ctx.outputSuffixes[type] = EditorGUILayout.TextField(
                    _ctx.outputSuffixes.GetValueOrDefault(type, type.ToString()));
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.Space(6);
            DrawPresetSection();
        }

        void ScanTextures()
        {
            var paths = new HashSet<string>();
            foreach (var obj in Selection.objects)
            {
                var p = AssetDatabase.GetAssetPath(obj);
                if (AssetDatabase.IsValidFolder(p))
                {
                    foreach (var guid in AssetDatabase.FindAssets("t:Texture", new[] { p }))
                        paths.Add(AssetDatabase.GUIDToAssetPath(guid));
                }
                else if (obj is Texture)
                    paths.Add(p);
            }
            if (paths.Count == 0) return;

            var allNames = new HashSet<string>(paths.Select(p => Path.GetFileNameWithoutExtension(p)));

            _ctx.textures.Clear();
            foreach (var path in paths)
            {
                var nameNoExt = Path.GetFileNameWithoutExtension(path);
                var (baseName, suffix) = TexRules.ExtractSuffix(nameNoExt, allNames);
                _ctx.textures.Add(new TexInfo
                {
                    assetPath = path, nameNoExt = nameNoExt,
                    baseName = baseName, suffix = suffix, type = TexRules.GuessType(suffix),
                });
            }

            _ctx.groups = _ctx.textures.GroupBy(t => t.baseName).ToDictionary(g => g.Key, g => g.ToList());
            _ctx.suffixMap.Clear();
            foreach (var t in _ctx.textures) _ctx.suffixMap.TryAdd(t.suffix, t.type);

            // Pixel analysis for unknown suffixes
            var unknownSuffixes = _ctx.suffixMap
                .Where(kv => kv.Value == TexType.Unknown)
                .Select(kv => kv.Key).ToList();
            foreach (var suffix in unknownSuffixes)
            {
                var sample = _ctx.textures.FirstOrDefault(t => t.suffix == suffix);
                if (sample == null) continue;
                var guessed = TexRules.GuessTypeFromContent(sample.assetPath);
                if (guessed != TexType.Unknown)
                {
                    _ctx.suffixMap[suffix] = guessed;
                    foreach (var t in _ctx.textures.Where(t => t.suffix == suffix))
                    {
                        t.type = guessed;
                        t.guessedByContent = true;
                    }
                }
            }

            RebuildSuffixCache();
            _ctx.scanDone = true;
        }

        void RebuildSuffixCache()
        {
            _suffixCounts.Clear();
            _guessedSuffixes.Clear();
            foreach (var t in _ctx.textures)
            {
                _suffixCounts.TryGetValue(t.suffix, out int c);
                _suffixCounts[t.suffix] = c + 1;
                if (t.guessedByContent) _guessedSuffixes.Add(t.suffix);
            }
        }

        // ── Presets ──
        void DrawPresetSection()
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("预设管理", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            if (_presetNames.Length > 0)
            {
                _selectedPreset = Mathf.Clamp(_selectedPreset, 0, _presetNames.Length - 1);
                _selectedPreset = EditorGUILayout.Popup("加载预设", _selectedPreset, _presetNames);
                if (GUILayout.Button("加载", GUILayout.Width(50)))
                    LoadPreset(_presetNames[_selectedPreset]);
                if (GUILayout.Button("删除", GUILayout.Width(50)))
                {
                    var path = Path.Combine(_presetDir, _presetNames[_selectedPreset] + ".json");
                    if (File.Exists(path)) File.Delete(path);
                    RefreshPresetList();
                }
            }
            else
                EditorGUILayout.LabelField("暂无预设");
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            _newPresetName = EditorGUILayout.TextField("保存为", _newPresetName);
            if (GUILayout.Button("保存", GUILayout.Width(50)))
            {
                SavePreset(_newPresetName);
                RefreshPresetList();
            }
            EditorGUILayout.EndHorizontal();
        }

        void SavePreset(string pName)
        {
            if (!Directory.Exists(_presetDir)) Directory.CreateDirectory(_presetDir);
            var data = new PresetData { presetName = pName };
            foreach (var kv in _ctx.suffixMap)
                data.suffixRules.Add(new SuffixRuleEntry { suffix = kv.Key, type = (int)kv.Value });
            foreach (var kv in _ctx.outputSuffixes)
                data.outputNames.Add(new OutputNameEntry { type = (int)kv.Key, outputSuffix = kv.Value });
            File.WriteAllText(Path.Combine(_presetDir, pName + ".json"), JsonUtility.ToJson(data, true));
            Debug.Log($"[Bento] 预设已保存: {pName}");
        }

        void LoadPreset(string pName)
        {
            var path = Path.Combine(_presetDir, pName + ".json");
            if (!File.Exists(path)) return;
            var data = JsonUtility.FromJson<PresetData>(File.ReadAllText(path));
            foreach (var r in data.suffixRules)
                _ctx.suffixMap[r.suffix] = (TexType)r.type;
            _ctx.outputSuffixes.Clear();
            foreach (var r in data.outputNames)
                _ctx.outputSuffixes[(TexType)r.type] = r.outputSuffix;
            foreach (var t in _ctx.textures)
                if (_ctx.suffixMap.TryGetValue(t.suffix, out var type))
                    t.type = type;
            Debug.Log($"[Bento] 预设已加载: {pName}");
        }

        void RefreshPresetList()
        {
            _presetNames = Directory.Exists(_presetDir)
                ? Directory.GetFiles(_presetDir, "*.json").Select(Path.GetFileNameWithoutExtension).ToArray()
                : Array.Empty<string>();
        }
    }
}

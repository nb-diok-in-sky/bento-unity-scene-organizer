using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Bento
{
    class MaterialFillTab
    {
        readonly SharedContext _ctx;
        Vector2 _scroll;

        List<MatFillInfo> _matInfos = new();
        List<Material> _cachedMats = new();
        DefaultAsset _textureFolderAsset;
        string _textureFolder = "";
        Dictionary<string, Dictionary<string, string>> _folderTexGroups = new();
        HashSet<string> _allTexNamesInFolder = new();

        public MaterialFillTab(SharedContext ctx) => _ctx = ctx;

        public void Draw()
        {
            EditorGUILayout.LabelField("① 指定贴图文件夹", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            var newFolder = EditorGUILayout.ObjectField("贴图文件夹", _textureFolderAsset,
                typeof(DefaultAsset), false) as DefaultAsset;
            if (newFolder != _textureFolderAsset)
            {
                _textureFolderAsset = newFolder;
                _textureFolder = newFolder != null ? AssetDatabase.GetAssetPath(newFolder) : "";
                _folderTexGroups.Clear();
            }
            EditorGUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(_textureFolder))
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("  路径: " + _textureFolder, EditorStyles.miniLabel);
                if (_folderTexGroups.Count > 0)
                    EditorGUILayout.LabelField($"({_folderTexGroups.Count} 组贴图)", EditorStyles.miniLabel, GUILayout.Width(80));
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.Space(4);

            EditorGUILayout.LabelField("② 选中材质球后锁定", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            var selectedMats = Selection.objects.OfType<Material>().ToArray();
            if (selectedMats.Length > 0)
            {
                if (GUILayout.Button($"锁定当前选中的 {selectedMats.Length} 个材质球", GUILayout.Height(24)))
                {
                    _cachedMats.Clear();
                    _cachedMats.AddRange(selectedMats);
                }
            }
            else
            {
                EditorGUI.BeginDisabledGroup(true);
                GUILayout.Button("先在 Project 面板选中材质球", GUILayout.Height(24));
                EditorGUI.EndDisabledGroup();
            }
            EditorGUILayout.EndHorizontal();

            if (_cachedMats.Count > 0)
                EditorGUILayout.LabelField(
                    $"  已锁定 {_cachedMats.Count} 个: {string.Join(", ", _cachedMats.Take(5).Select(m => m.name))}{(_cachedMats.Count > 5 ? "..." : "")}",
                    EditorStyles.miniLabel);

            EditorGUILayout.Space(4);

            EditorGUILayout.LabelField("③ 扫描匹配", EditorStyles.boldLabel);
            EditorGUI.BeginDisabledGroup(_cachedMats.Count == 0 || string.IsNullOrEmpty(_textureFolder));
            if (GUILayout.Button("扫描匹配贴图", GUILayout.Height(28)))
                ScanMaterials();
            EditorGUI.EndDisabledGroup();

            if (_matInfos.Count == 0) return;

            EditorGUILayout.Space(4);
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            foreach (var info in _matInfos)
            {
                int matchCount = info.matched.Count(kv => kv.Value != null);
                var propMap = TexRules.GetPropertyMap(info.material);

                EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
                EditorGUILayout.LabelField($"■ {info.material.name}  [{matchCount}/{propMap.Count}]", EditorStyles.boldLabel);
                if (!string.IsNullOrEmpty(info.matchedGroupName) && info.matchedGroupName != info.material.name)
                    EditorGUILayout.LabelField("← 组: " + info.matchedGroupName, EditorStyles.miniLabel, GUILayout.Width(200));
                EditorGUILayout.EndHorizontal();

                foreach (var kv in propMap)
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("  " + kv.Key, GUILayout.Width(100));

                    if (info.matched.TryGetValue(kv.Key, out var tex) && tex != null)
                    {
                        GUI.contentColor = new Color(0.5f, 1f, 0.5f);
                        EditorGUILayout.ObjectField(tex, typeof(Texture2D), false);
                        GUI.contentColor = Color.white;
                    }
                    else
                    {
                        GUI.contentColor = Color.gray;
                        EditorGUILayout.LabelField("(未找到)");
                        GUI.contentColor = Color.white;
                    }
                    EditorGUILayout.EndHorizontal();
                }
                EditorGUILayout.Space(2);
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.Space(6);

            int fillCount = _matInfos.Sum(m => m.matched.Count(kv => kv.Value != null));
            EditorGUI.BeginDisabledGroup(fillCount == 0);
            if (GUILayout.Button($"执行填充（{fillCount} 个贴图槽位）", GUILayout.Height(30)))
                ExecuteFill();
            EditorGUI.EndDisabledGroup();
        }

        void ScanMaterials()
        {
            _matInfos.Clear();
            if (_cachedMats.Count == 0 || string.IsNullOrEmpty(_textureFolder)) return;

            var folderGuids = AssetDatabase.FindAssets("t:Texture2D", new[] { _textureFolder });
            _folderTexGroups.Clear();

            _allTexNamesInFolder = new HashSet<string>(
                folderGuids.Select(g => Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(g))));

            foreach (var guid in folderGuids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var nameNoExt = Path.GetFileNameWithoutExtension(path);
                var (baseName, suffix) = TexRules.ExtractSuffix(nameNoExt, _allTexNamesInFolder);

                if (!_folderTexGroups.ContainsKey(baseName))
                    _folderTexGroups[baseName] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                _folderTexGroups[baseName][suffix] = path;
            }

            TexRules.ConsolidateTexGroups(_folderTexGroups);

            foreach (var mat in _cachedMats)
            {
                var info = new MatFillInfo { material = mat };
                var (groupName, group) = FindTexGroupForMaterial(mat.name);

                if (group == null) { _matInfos.Add(info); continue; }

                info.matchedGroupName = groupName;
                info.matched = TexRules.MatchTexSlots(mat, group, _ctx.suffixMap);
                _matInfos.Add(info);
            }
        }

        (string groupName, Dictionary<string, string> group) FindTexGroupForMaterial(string matName)
        {
            return TexRules.FindTexGroup(matName, _folderTexGroups, _allTexNamesInFolder);
        }

        void ExecuteFill()
        {
            Undo.RecordObjects(
                _matInfos.Select(m => (UnityEngine.Object)m.material).ToArray(),
                "Fill Material Textures");

            int count = 0;
            foreach (var info in _matInfos)
            {
                int before = info.matched.Count(kv => kv.Value != null);
                TexRules.FillMaterialSlots(info.material, info.matched);
                count += before;
                EditorUtility.SetDirty(info.material);
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[Bento] 已填充 {count} 个贴图槽位");
        }
    }
}

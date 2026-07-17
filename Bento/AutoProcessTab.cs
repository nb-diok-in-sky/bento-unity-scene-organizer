using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Bento
{
    class AutoProcessTab
    {
        readonly SharedContext _ctx;
        Vector2 _scroll;

        DefaultAsset _rootFolderAsset;
        string _rootFolder = "";

        bool _doRename = true;
        bool _doFill = true;
        bool _doShaderReplace;
        bool _doRepair = true;
        Shader _targetShader;

        List<AutoProcessGroup> _groups = new();
        bool _scanned;

        public AutoProcessTab(SharedContext ctx) => _ctx = ctx;

        public void Draw()
        {
            EditorGUILayout.LabelField("一键批量处理", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("选中资产根文件夹，自动发现贴图与材质球并批量处理", EditorStyles.miniLabel);
            EditorGUILayout.Space(4);

            var newFolder = EditorGUILayout.ObjectField("资产根文件夹", _rootFolderAsset,
                typeof(DefaultAsset), false) as DefaultAsset;
            if (newFolder != _rootFolderAsset)
            {
                _rootFolderAsset = newFolder;
                _rootFolder = newFolder != null ? AssetDatabase.GetAssetPath(newFolder) : "";
                _scanned = false;
                _groups.Clear();
            }

            if (string.IsNullOrEmpty(_rootFolder))
            {
                EditorGUILayout.HelpBox("将资产根文件夹拖入上方（如 Coniferous forest）", MessageType.Info);
                return;
            }

            EditorGUILayout.Space(4);
            if (GUILayout.Button("扫描文件夹结构", GUILayout.Height(26)))
                ScanFolder();
            if (!_scanned) return;

            // Summary
            int totalTex = _groups.Sum(g => g.textures.Count);
            int totalMat = _groups.Sum(g => g.materials.Count);
            int totalSlots = _groups.Sum(g => g.matches.Sum(m => m.slots.Count(kv => kv.Value != null)));
            EditorGUILayout.Space(4);
            EditorGUILayout.HelpBox(
                $"发现 {_groups.Count} 个子分组 | {totalTex} 张贴图 | {totalMat} 个材质球 | {totalSlots} 个可填充槽位",
                MessageType.None);

            // Preview
            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.MaxHeight(350));
            foreach (var g in _groups)
            {
                int matched = g.matches.Sum(m => m.slots.Count(kv => kv.Value != null));
                EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
                EditorGUILayout.LabelField($"■ {g.category}", EditorStyles.boldLabel, GUILayout.Width(160));
                EditorGUILayout.LabelField($"{g.textures.Count} 贴图", GUILayout.Width(60));
                EditorGUILayout.LabelField($"{g.materials.Count} 材质", GUILayout.Width(60));
                EditorGUILayout.LabelField($"{matched} 匹配", GUILayout.Width(60));
                EditorGUILayout.EndHorizontal();

                foreach (var (mat, groupName, slots) in g.matches)
                {
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Space(16);
                    EditorGUILayout.LabelField(mat.name, GUILayout.Width(180));
                    int sc = slots.Count(kv => kv.Value != null);
                    if (sc > 0)
                    {
                        GUI.contentColor = new Color(0.5f, 1f, 0.5f);
                        EditorGUILayout.LabelField($"← {string.Join(", ", slots.Where(kv => kv.Value != null).Select(kv => kv.Key))}");
                    }
                    else
                    {
                        GUI.contentColor = new Color(1f, 0.7f, 0.2f);
                        EditorGUILayout.LabelField(string.IsNullOrEmpty(groupName) ? "(未找到匹配贴图组)" : "(贴图组无可用类型)");
                    }
                    GUI.contentColor = Color.white;
                    EditorGUILayout.EndHorizontal();
                }

                var unknowns = g.textures.Where(t => t.type == TexType.Unknown).ToList();
                if (unknowns.Count > 0)
                {
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Space(16);
                    GUI.contentColor = new Color(1f, 0.6f, 0.2f);
                    EditorGUILayout.LabelField(
                        $"⚠ {unknowns.Count} 张后缀无法识别: {string.Join(", ", unknowns.Take(5).Select(t => "_" + t.suffix))}{(unknowns.Count > 5 ? "..." : "")}",
                        EditorStyles.miniLabel);
                    GUI.contentColor = Color.white;
                    EditorGUILayout.EndHorizontal();
                }
            }
            EditorGUILayout.EndScrollView();

            // Options
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("执行选项", EditorStyles.boldLabel);
            _doRename = EditorGUILayout.Toggle("贴图改名（使用当前预设）", _doRename);
            _doFill = EditorGUILayout.Toggle("材质球填充贴图", _doFill);
            _doRepair = EditorGUILayout.Toggle("URP 材质修复（重影/keyword缺失）", _doRepair);
            _doShaderReplace = EditorGUILayout.Toggle("Shader 替换", _doShaderReplace);
            if (_doShaderReplace)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PrefixLabel("目标 Shader");
                if (EditorGUILayout.DropdownButton(
                        new GUIContent(_targetShader != null ? _targetShader.name : "(选择 Shader)"),
                        FocusType.Keyboard))
                    TexRules.ShowShaderDropdown(s => _targetShader = s, _targetShader);
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.Space(6);
            bool canExecute = _doRename || _doFill || _doRepair || (_doShaderReplace && _targetShader != null);
            EditorGUI.BeginDisabledGroup(!canExecute);
            if (GUILayout.Button("执行批量处理", GUILayout.Height(32)))
                Execute();
            EditorGUI.EndDisabledGroup();
        }

        void ScanFolder()
        {
            _groups = AutoProcessPlanner.Build(_rootFolder, _ctx);
            _scanned = true;
            Debug.Log($"[AutoProcess] 扫描完成: {_groups.Count} 组, {_groups.Sum(g => g.textures.Count)} 贴图, {_groups.Sum(g => g.materials.Count)} 材质球");
        }

        void Execute()
        {
            AutoProcessResult result = null;
            BentoCommand.Run("批量处理中断", () => result = AutoProcessService.Execute(_groups, _rootFolder, _ctx, new AutoProcessOptions
            {
                rename = _doRename,
                fill = _doFill,
                repair = _doRepair,
                replaceShader = _doShaderReplace,
                targetShader = _targetShader
            }), () => $"改名: {result.renamed}\nShader替换: {result.shaderReplaced}\nURP材质修复: {result.repaired}\n贴图填充: {result.filled}" +
                       (result.cancelled ? "\n\n已取消部分操作" : string.Empty));
        }
    }
}

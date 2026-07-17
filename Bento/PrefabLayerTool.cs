using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Bento
{
    class PrefabLayerTool
    {
        List<PrefabLayerPlanEntry> _plan = new List<PrefabLayerPlanEntry>();
        PrefabLayerHistory _history;
        PrefabLayerApplyMode _mode = PrefabLayerApplyMode.DefaultNodesOnly;
        Vector2 _scroll;
        string[] _layers;

        public void Draw()
        {
            EnsureLayers();
            EditorGUILayout.LabelField("Prefab Layer 规划", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("AI/程序只负责建议，实际写入必须在这里确认。默认仅修改 Default 节点，已有的特殊 Layer 会保留。", MessageType.Info);
            _mode = (PrefabLayerApplyMode)EditorGUILayout.EnumPopup("子节点应用策略", _mode);
            if (GUILayout.Button("分析 Project 选择", GUILayout.Height(34))) Analyze();
            DrawPlan();
            DrawActions();
        }

        void Analyze()
        {
            _plan = PrefabLayerPlanner.Build(ProjectSelection.CurrentAssets);
            if (_plan.Count == 0) EditorUtility.DisplayDialog("没有 Prefab/FBX", "请在 Project 窗口选择 Prefab、FBX 或文件夹。", "确定");
        }

        void DrawPlan()
        {
            if (_plan.Count == 0) return;
            _scroll = EditorGUILayout.BeginScrollView(_scroll, EditorStyles.helpBox, GUILayout.MinHeight(220));
            foreach (var entry in _plan)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    entry.include = EditorGUILayout.ToggleLeft(entry.assetPath, entry.include);
                    int selected = Mathf.Max(0, Array.IndexOf(_layers, entry.suggestedLayer));
                    selected = EditorGUILayout.Popup("目标 Layer", selected, _layers);
                    entry.suggestedLayer = _layers[selected];
                    EditorGUILayout.LabelField($"当前根节点：{entry.currentRootLayer}　Default节点：{entry.defaultLayerNodeCount}　特殊节点：{entry.specialLayerNodeCount}", EditorStyles.miniLabel);
                    EditorGUILayout.LabelField($"建议依据：{entry.reason}　置信度：{entry.confidence:P0}", EditorStyles.miniLabel);
                }
            }
            EditorGUILayout.EndScrollView();
        }

        void DrawActions()
        {
            using (new EditorGUI.DisabledScope(!_plan.Any(entry => entry.include)))
                if (GUILayout.Button("确认应用 Layer 计划", GUILayout.Height(38)) && BentoCommand.Confirm(
                        "确认修改 Prefab Layer",
                        $"将修改 {_plan.Count(entry => entry.include)} 个 Prefab。策略：{_mode}。Prefab GUID 和资源引用保持不变。",
                        "应用")) Apply();
            using (new EditorGUI.DisabledScope(_history == null || _history.entries.Count == 0))
                if (GUILayout.Button("恢复上次 Layer 修改")) Restore();
        }

        void Apply()
        {
            BentoCommand.Run(
                "Layer 修改中断",
                () => _history = PrefabLayerService.Apply(_plan, _mode),
                () => $"已修改 {_history.entries.Count} 个 Prefab。");
        }

        void Restore()
        {
            if (BentoCommand.Run("恢复中断", () => PrefabLayerService.Restore(_history), () => "上次 Prefab Layer 修改已恢复。"))
                _history = null;
        }

        void EnsureLayers() => _layers = _layers ?? ProjectLayerCatalog.GetDefinedNames().ToArray();
    }
}

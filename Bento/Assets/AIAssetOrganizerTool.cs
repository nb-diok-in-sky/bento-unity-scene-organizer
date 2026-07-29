using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Bento
{
    class AIAssetOrganizerTool
    {
        readonly AIAssetOrganizerConfig _config = new AIAssetOrganizerConfig();
        readonly AIAssetOrganizerExecutor _executor = new AIAssetOrganizerExecutor();
        AIAssetOrganizerPlan _plan;
        DefaultAsset _sourceRoot;
        DefaultAsset _prefabFolder;
        DefaultAsset _materialFolder;
        DefaultAsset _textureFolder;
        DefaultAsset _otherFolder;
        Vector2 _scroll;

        public void Draw()
        {
            EditorGUILayout.LabelField("AI 资产整理", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("AI 只能读取这里指定的来源范围并生成计划。实际移动仍需你在本页确认，底层统一使用安全移动服务。", MessageType.Info);
            DrawFolders();
            if (GUILayout.Button("扫描并生成整理计划", GUILayout.Height(34))) BuildPlan();
            DrawPlan();
            DrawActions();
        }

        void DrawFolders()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawFolder("待整理资产根目录", ref _sourceRoot, path => _config.sourceRoot = path);
                DrawFolder("Prefab/模型移动至", ref _prefabFolder, path => _config.prefabFolder = path);
                DrawFolder("材质球移动至", ref _materialFolder, path => _config.materialFolder = path);
                DrawFolder("贴图移动至", ref _textureFolder, path => _config.textureFolder = path);
                DrawFolder("其他资产移动至", ref _otherFolder, path => _config.otherFolder = path);
            }
        }

        void DrawPlan()
        {
            if (_plan == null) return;
            EditorGUILayout.LabelField($"待审批计划：{_plan.entries.Count} 项 / 勾选 {_plan.entries.Count(entry => entry.include)} 项", EditorStyles.boldLabel);
            _scroll = EditorGUILayout.BeginScrollView(_scroll, EditorStyles.helpBox, GUILayout.MinHeight(220));
            foreach (var entry in _plan.entries)
            {
                using (new EditorGUI.DisabledScope(!string.IsNullOrEmpty(entry.warning)))
                    entry.include = EditorGUILayout.ToggleLeft($"{entry.kind}: {entry.sourcePath}", entry.include);
                EditorGUILayout.LabelField($"→ {entry.targetPath}", EditorStyles.miniLabel);
                if (!string.IsNullOrEmpty(entry.warning)) EditorGUILayout.HelpBox(entry.warning, MessageType.Warning);
            }
            EditorGUILayout.EndScrollView();
        }

        void DrawActions()
        {
            bool canExecute = _plan != null && _plan.entries.Any(entry => entry.include) &&
                              _plan.entries.Where(entry => entry.include).All(entry => string.IsNullOrEmpty(entry.warning));
            using (new EditorGUI.DisabledScope(!canExecute))
            {
                if (GUILayout.Button("确认执行勾选计划", GUILayout.Height(38)) && BentoCommand.Confirm(
                        "确认 AI 资产整理计划",
                        $"将安全移动 {_plan.entries.Count(entry => entry.include)} 个资产。GUID 将保持不变，AI 无法绕过本次确认。",
                        "执行"))
                    Execute();
            }

            using (new EditorGUI.DisabledScope(_executor.LastHistory == null))
                if (GUILayout.Button("恢复上次整理批次")) Restore();
        }

        void BuildPlan()
        {
            try { _plan = AIAssetOrganizerPlanner.Build(_config); }
            catch (Exception exception) { _plan = null; EditorUtility.DisplayDialog("无法生成计划", exception.Message, "确定"); }
        }

        void Execute()
        {
            BentoCommand.Run("整理中断", () => _executor.Execute(_plan), () => "勾选资产已安全移动，GUID 验证通过。");
        }

        void Restore()
        {
            BentoCommand.Run("恢复中断", _executor.Restore, () => "上次整理批次已恢复。");
        }

        void DrawFolder(string label, ref DefaultAsset field, Action<string> setPath)
        {
            var next = BentoGUI.DrawProjectFolder(label, field, out string path);
            if (next == field) return;
            field = next;
            setPath(path);
            _plan = null;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Bento
{
    sealed class HierarchyOrganizationTool
    {
        readonly List<HierarchyOrganizationEntry> _entries = new List<HierarchyOrganizationEntry>();
        Transform _targetParent;
        Vector2 _scroll;
        string _search = string.Empty;
        float _listHeight = 520f;

        public void Draw()
        {
            LoadPendingDraft();
            EditorGUILayout.LabelField("Hierarchy 分类整理", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "先在 Hierarchy 选择要归类的对象，再指定目标父节点。执行只改变父子关系，保持世界坐标、旋转和缩放，支持整批 Ctrl+Z。",
                MessageType.Info);
            _targetParent = (Transform)EditorGUILayout.ObjectField("目标父节点", _targetParent, typeof(Transform), true);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("从当前选择生成计划")) BuildFromSelection();
                using (new EditorGUI.DisabledScope(_entries.Count == 0))
                    if (GUILayout.Button("清空计划")) _entries.Clear();
            }

            var visibleEntries = FilteredEntries().ToList();
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                _search = GUILayout.TextField(_search, GUI.skin.FindStyle("ToolbarSearchTextField") ?? EditorStyles.toolbarTextField, GUILayout.MinWidth(180));
                if (!string.IsNullOrEmpty(_search) && GUILayout.Button("清除", EditorStyles.toolbarButton, GUILayout.Width(48))) _search = string.Empty;
                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField($"显示 {visibleEntries.Count} / {_entries.Count}", EditorStyles.miniLabel, GUILayout.Width(100));
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("筛选结果全选")) SetVisibleIncluded(visibleEntries, true);
                if (GUILayout.Button("筛选结果全不选")) SetVisibleIncluded(visibleEntries, false);
                EditorGUILayout.LabelField("列表高度", GUILayout.Width(58));
                _listHeight = EditorGUILayout.Slider(_listHeight, 220f, 1000f, GUILayout.MinWidth(180));
            }

            EditorGUILayout.LabelField($"整理计划：{_entries.Count} 项", EditorStyles.boldLabel);
            _scroll = EditorGUILayout.BeginScrollView(_scroll, EditorStyles.helpBox, GUILayout.Height(_listHeight));
            foreach (var entry in visibleEntries)
            {
                HierarchyOrganizationService.Validate(entry);
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        entry.include = EditorGUILayout.ToggleLeft(entry.objectPath, entry.include);
                        using (new EditorGUI.DisabledScope(EditorUtility.InstanceIDToObject(entry.objectInstanceId) == null))
                        {
                            if (GUILayout.Button("定位对象", GUILayout.Width(72)))
                                FocusObject(entry.objectInstanceId);
                        }
                    }
                    EditorGUILayout.LabelField("→ " + entry.targetParentPath, EditorStyles.miniLabel);
                    if (!string.IsNullOrEmpty(entry.warning)) EditorGUILayout.HelpBox(entry.warning, MessageType.Warning);
                }
            }
            EditorGUILayout.EndScrollView();

            bool canExecute = _entries.Any(entry => entry.include) &&
                              _entries.Where(entry => entry.include).All(entry => string.IsNullOrEmpty(HierarchyOrganizationService.Validate(entry).warning));
            using (new EditorGUI.DisabledScope(!canExecute))
            {
                if (GUILayout.Button("确认并执行整理", GUILayout.Height(38)) && BentoCommand.Confirm(
                    "确认整理 Hierarchy",
                    $"将调整 {_entries.Count(entry => entry.include)} 个对象的父节点。\n保持世界 Transform，支持 Ctrl+Z 撤销。",
                    "执行整理"))
                {
                    BentoCommand.Run("Hierarchy 整理失败", () =>
                    {
                        int count = HierarchyOrganizationService.Execute(_entries);
                        _entries.Clear();
                        EditorUtility.DisplayDialog("整理完成", $"已整理 {count} 个对象。", "确定");
                    });
                }
            }
        }

        void LoadPendingDraft()
        {
            var draft = HierarchyOrganizationSession.TakeDraft();
            if (draft.Count == 0) return;
            _entries.Clear();
            _entries.AddRange(draft);
        }

        void BuildFromSelection()
        {
            _entries.Clear();
            _entries.AddRange(HierarchyOrganizationService.BuildFromSelection(_targetParent));
        }

        IEnumerable<HierarchyOrganizationEntry> FilteredEntries()
        {
            if (string.IsNullOrWhiteSpace(_search)) return _entries;
            string search = _search.Trim();
            return _entries.Where(entry =>
                (entry.objectPath ?? string.Empty).IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 ||
                (entry.targetParentPath ?? string.Empty).IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        static void SetVisibleIncluded(IEnumerable<HierarchyOrganizationEntry> entries, bool included)
        {
            foreach (var entry in entries) entry.include = included;
        }

        static void FocusObject(int instanceId)
        {
            var gameObject = EditorUtility.InstanceIDToObject(instanceId) as GameObject;
            if (gameObject == null) return;
            Selection.activeGameObject = gameObject;
            EditorGUIUtility.PingObject(gameObject);
            SceneView.lastActiveSceneView?.FrameSelected();
        }
    }
}

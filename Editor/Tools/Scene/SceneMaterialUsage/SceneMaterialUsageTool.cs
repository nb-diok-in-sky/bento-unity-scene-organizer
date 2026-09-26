using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Bento
{
    [BentoPage("scene_material_usage", BentoGroups.Scene, "查找材质使用对象", Order = 20)]
    sealed class SceneMaterialUsageTool : IBentoTool
    {
        Material _material;
        Transform _scopeRoot;
        bool _includeInactive = true;
        string _search = string.Empty;
        Vector2 _scroll;
        readonly List<SceneMaterialUsageRecord> _records = new List<SceneMaterialUsageRecord>();

        public void Draw()
        {
            EditorGUILayout.LabelField("查找场景材质使用对象", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "选择一个 Project 材质球，扫描当前已加载场景中 sharedMaterials 实际引用它的 Renderer。只读操作，不修改场景、Prefab 或材质。",
                MessageType.Info);

            Material nextMaterial = (Material)EditorGUILayout.ObjectField(
                "目标材质球", _material, typeof(Material), false);
            if (nextMaterial != _material)
            {
                _material = nextMaterial;
                _records.Clear();
            }

            _scopeRoot = (Transform)EditorGUILayout.ObjectField(
                "限制 Hierarchy 层级（可空）", _scopeRoot, typeof(Transform), true);
            _includeInactive = EditorGUILayout.Toggle("包含未激活对象", _includeInactive);

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(_material == null))
                    if (GUILayout.Button("扫描材质使用对象", GUILayout.Height(34)))
                        Scan();
                using (new EditorGUI.DisabledScope(_records.Count == 0))
                    if (GUILayout.Button($"全选结果（{_records.Count}）", GUILayout.Height(34)))
                        SelectRecords(_records);
            }

            if (_material == null)
            {
                EditorGUILayout.HelpBox("请拖入需要查找的材质球。", MessageType.Warning);
                return;
            }

            if (_records.Count == 0)
            {
                EditorGUILayout.HelpBox("尚未扫描，或者当前扫描范围没有对象引用该材质。", MessageType.None);
                return;
            }

            List<SceneMaterialUsageRecord> visible = FilteredRecords();
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                _search = GUILayout.TextField(
                    _search,
                    GUI.skin.FindStyle("ToolbarSearchTextField") ?? EditorStyles.toolbarTextField);
                if (!string.IsNullOrEmpty(_search) &&
                    GUILayout.Button("清除", EditorStyles.toolbarButton, GUILayout.Width(48)))
                    _search = string.Empty;
                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField(
                    $"显示 {visible.Count} / {_records.Count}",
                    EditorStyles.miniLabel,
                    GUILayout.Width(100));
            }

            _scroll = EditorGUILayout.BeginScrollView(
                _scroll,
                EditorStyles.helpBox,
                GUILayout.MinHeight(260),
                GUILayout.MaxHeight(760));
            foreach (SceneMaterialUsageRecord record in visible)
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(record.hierarchyPath, EditorStyles.boldLabel);
                    if (GUILayout.Button("定位", GUILayout.Width(58)))
                        BentoGUI.FocusSceneObject(record.gameObjectInstanceId);
                }

                EditorGUILayout.LabelField(
                    $"{record.rendererType} / 材质槽：{string.Join(", ", record.materialSlots)}",
                    EditorStyles.miniLabel);
                EditorGUILayout.LabelField(record.scenePath, EditorStyles.wordWrappedMiniLabel);
            }
            EditorGUILayout.EndScrollView();
        }

        void Scan()
        {
            _records.Clear();
            _records.AddRange(SceneMaterialUsageService.Scan(
                _material,
                _scopeRoot,
                _includeInactive));
            Debug.Log(
                $"[Bento] 材质“{_material.name}”在当前扫描范围内命中 {_records.Count} 个场景对象。");
        }

        List<SceneMaterialUsageRecord> FilteredRecords()
        {
            if (string.IsNullOrWhiteSpace(_search)) return _records;
            string search = _search.Trim();
            return _records.Where(record =>
                    Contains(record.hierarchyPath, search) ||
                    Contains(record.scenePath, search) ||
                    Contains(record.rendererType, search))
                .ToList();
        }

        static bool Contains(string value, string search)
        {
            return (value ?? string.Empty).IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static void SelectRecords(IEnumerable<SceneMaterialUsageRecord> records)
        {
            Selection.objects = records
                .Select(record => EditorUtility.InstanceIDToObject(record.gameObjectInstanceId))
                .Where(target => target != null)
                .Distinct()
                .ToArray();
            SceneView.lastActiveSceneView?.FrameSelected();
        }
    }
}

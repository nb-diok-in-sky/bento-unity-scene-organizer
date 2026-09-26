using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Bento
{
    sealed class SceneModelInstanceAuditTool
    {
        SceneModelInstanceAuditScope _scope = SceneModelInstanceAuditScope.LoadedScenes;
        Vector2 _scroll;
        string _search = string.Empty;

        public void Draw()
        {
            EditorGUILayout.LabelField("场景模型实例检查", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "检查场景中直接拖入的 FBX/OBJ 模型实例。Prefab 内部只是引用模型 Mesh 的正常对象不会被误报。本工具只读，不会修改场景。",
                MessageType.Info);
            _scope = (SceneModelInstanceAuditScope)EditorGUILayout.EnumPopup("检查范围", _scope);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("开始检查", GUILayout.Height(34)))
                    SceneModelInstanceAuditService.Scan(_scope);
                using (new EditorGUI.DisabledScope(SceneModelInstanceAuditSession.Current == null))
                    if (GUILayout.Button("清空结果", GUILayout.Height(34)))
                        SceneModelInstanceAuditSession.Clear();
            }

            SceneModelInstanceAuditResult result = SceneModelInstanceAuditSession.Current;
            if (result == null)
            {
                EditorGUILayout.HelpBox("尚未执行检查。", MessageType.None);
                return;
            }

            List<SceneModelInstanceAuditRecord> visible = Filter(result.instances);
            EditorGUILayout.LabelField(
                $"已检查 {result.inspectedObjectCount} 个对象，发现 {result.instances.Count} 个直接模型实例。",
                EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                _search = GUILayout.TextField(
                    _search,
                    GUI.skin.FindStyle("ToolbarSearchTextField") ?? EditorStyles.toolbarTextField);
                if (!string.IsNullOrEmpty(_search) &&
                    GUILayout.Button("清除", EditorStyles.toolbarButton, GUILayout.Width(48)))
                    _search = string.Empty;
                using (new EditorGUI.DisabledScope(visible.Count == 0))
                    if (GUILayout.Button($"选中结果（{visible.Count}）", EditorStyles.toolbarButton, GUILayout.Width(110)))
                        SelectRecords(visible);
            }

            _scroll = EditorGUILayout.BeginScrollView(
                _scroll,
                EditorStyles.helpBox,
                GUILayout.MinHeight(260),
                GUILayout.MaxHeight(720));
            foreach (SceneModelInstanceAuditRecord record in visible)
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(record.hierarchyPath, EditorStyles.boldLabel);
                    if (GUILayout.Button("定位", GUILayout.Width(58)))
                        Focus(record.instanceId);
                }
                EditorGUILayout.LabelField($"场景：{record.scenePath}", EditorStyles.miniLabel);
                EditorGUILayout.LabelField($"模型：{record.modelAssetPath}", EditorStyles.miniLabel);
            }
            EditorGUILayout.EndScrollView();
        }

        List<SceneModelInstanceAuditRecord> Filter(IEnumerable<SceneModelInstanceAuditRecord> records)
        {
            if (string.IsNullOrWhiteSpace(_search))
                return records.ToList();
            string search = _search.Trim();
            return records.Where(record =>
                    Contains(record.hierarchyPath, search) ||
                    Contains(record.modelAssetPath, search) ||
                    Contains(record.scenePath, search))
                .ToList();
        }

        static bool Contains(string value, string search) =>
            (value ?? string.Empty).IndexOf(search, System.StringComparison.OrdinalIgnoreCase) >= 0;

        static void SelectRecords(IEnumerable<SceneModelInstanceAuditRecord> records)
        {
            Selection.objects = records
                .Select(record => EditorUtility.InstanceIDToObject(record.instanceId))
                .Where(value => value != null)
                .ToArray();
        }

        static void Focus(int instanceId)
        {
            Object target = EditorUtility.InstanceIDToObject(instanceId);
            if (target == null) return;
            Selection.activeObject = target;
            EditorGUIUtility.PingObject(target);
            SceneView.lastActiveSceneView?.FrameSelected();
        }
    }
}

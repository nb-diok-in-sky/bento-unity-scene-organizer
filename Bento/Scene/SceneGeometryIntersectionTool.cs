using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Bento
{
    sealed class SceneGeometryIntersectionTool
    {
        readonly SceneGeometryScanOptions _options = new SceneGeometryScanOptions();
        Vector2 _scroll;

        public void Draw()
        {
            EditorGUILayout.LabelField("场景几何相交审查", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "只读扫描，不修改场景。结果仅保存在编辑器内存中，重新扫描会覆盖，关闭 Unity 或点击清空后消失。Collider 结果较可靠；包围盒结果只是供 AI 继续判断的疑似项。",
                MessageType.Info);

            _options.scope = (SceneGeometryScanScope)EditorGUILayout.EnumPopup("扫描范围", _options.scope);
            _options.includeInactive = EditorGUILayout.Toggle("包含未激活对象", _options.includeInactive);
            _options.ignoreSameHierarchy = EditorGUILayout.Toggle("忽略父子层级内部", _options.ignoreSameHierarchy);
            _options.includeBoundsOnlyCandidates = EditorGUILayout.Toggle("保留无 Collider 疑似项", _options.includeBoundsOnlyCandidates);
            _options.minimumBoundsOverlap = EditorGUILayout.FloatField("最小包围盒交叠", _options.minimumBoundsOverlap);
            _options.minimumPenetration = EditorGUILayout.FloatField("最小 Collider 穿透", _options.minimumPenetration);
            _options.maximumResults = EditorGUILayout.IntSlider("最大结果数", _options.maximumResults, 10, 2000);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("执行只读扫描", GUILayout.Height(32)))
                    BentoCommand.Run("几何相交扫描失败", () => SceneGeometryIntersectionService.Scan(_options));
                using (new EditorGUI.DisabledScope(SceneGeometryIntersectionSession.Current == null))
                    if (GUILayout.Button("清空内存结果", GUILayout.Height(32))) SceneGeometryIntersectionSession.Clear();
            }

            var result = SceneGeometryIntersectionSession.Current;
            if (result == null)
            {
                EditorGUILayout.HelpBox("尚未扫描。AI 可通过 bento 的 scan_geometry_intersections / get_geometry_intersections 调用。", MessageType.None);
                return;
            }

            EditorGUILayout.LabelField(
                $"对象 {result.candidateObjectCount}，候选对 {result.testedPairCount}，发现 {result.intersections.Count}" +
                (result.truncated ? "（结果已截断）" : string.Empty), EditorStyles.boldLabel);
            _scroll = EditorGUILayout.BeginScrollView(_scroll, EditorStyles.helpBox, GUILayout.MinHeight(240));
            foreach (var item in result.intersections)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField(item.kind == SceneGeometryIntersectionKind.ColliderPenetration ? "Collider 穿透" : "包围盒疑似", EditorStyles.boldLabel);
                    EditorGUILayout.LabelField(item.firstPath, EditorStyles.wordWrappedMiniLabel);
                    EditorGUILayout.LabelField("↔ " + item.secondPath, EditorStyles.wordWrappedMiniLabel);
                    EditorGUILayout.LabelField($"深度/最小交叠：{item.depth:F4}，交叠体积：{item.boundsOverlapVolume:F4}", EditorStyles.miniLabel);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("定位 A")) Focus(item.firstInstanceId);
                        if (GUILayout.Button("定位 B")) Focus(item.secondInstanceId);
                        if (GUILayout.Button("同时选中")) SelectBoth(item.firstInstanceId, item.secondInstanceId);
                    }
                }
            }
            EditorGUILayout.EndScrollView();
        }

        static void Focus(int instanceId)
        {
            var target = EditorUtility.InstanceIDToObject(instanceId) as GameObject;
            if (target == null) return;
            Selection.activeGameObject = target;
            EditorGUIUtility.PingObject(target);
            SceneView.lastActiveSceneView?.FrameSelected();
        }

        static void SelectBoth(params int[] instanceIds)
        {
            Selection.objects = instanceIds.Select(EditorUtility.InstanceIDToObject).Where(value => value != null).ToArray();
            SceneView.lastActiveSceneView?.FrameSelected();
        }
    }
}

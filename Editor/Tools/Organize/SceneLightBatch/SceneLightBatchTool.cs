using UnityEditor;
using UnityEngine;

namespace Bento
{
    sealed class SceneLightBatchTool : IBentoPageLifecycle
    {
        LightShadows _targetShadows = LightShadows.Soft;
        readonly SelectionPlanCache<SceneLightBatchPlan> _selection =
            new SelectionPlanCache<SceneLightBatchPlan>(SceneLightBatchService.BuildPlan);

        public void Draw()
        {
            EditorGUILayout.LabelField("场景光源批处理", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "在 Hierarchy 中选择光源或包含光源的父对象。工具会递归查找 Light，只修改当前场景实例的阴影模式，不改 Prefab 源。支持 Ctrl+Z。",
                MessageType.Info);

            var roots = _selection.Roots;
            var plan = _selection.Plan;
            _targetShadows = (LightShadows)EditorGUILayout.EnumPopup("目标阴影模式", _targetShadows);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField($"选中根对象：{roots.Count} 个", EditorStyles.miniLabel);
                EditorGUILayout.LabelField($"找到 Light：{plan.lights.Count} 个", EditorStyles.boldLabel);
                EditorGUILayout.LabelField($"直接选中：{plan.directLightCount}　父节点下找到：{plan.childLightCount}", EditorStyles.miniLabel);
                EditorGUILayout.LabelField($"关闭：{plan.noneCount}　硬阴影：{plan.hardCount}　软阴影：{plan.softCount}", EditorStyles.miniLabel);
            }

            if (roots.Count == 0)
                EditorGUILayout.HelpBox("请先在 Hierarchy 中选择要处理的光源或父对象。", MessageType.Warning);
            else if (plan.lights.Count == 0)
                EditorGUILayout.HelpBox("选中对象及其子物体中没有 Light 组件。", MessageType.Warning);

            int changeCount = plan.lights.FindAll(light => light != null && light.shadows != _targetShadows).Count;
            using (new EditorGUI.DisabledScope(changeCount == 0))
            if (GUILayout.Button($"修改 {changeCount} 个光源", GUILayout.Height(36)) && BentoCommand.Confirm(
                    "确认修改场景光源",
                    $"将把 {changeCount} 个光源设置为“{GetLabel(_targetShadows)}”。\n只修改场景实例，支持 Ctrl+Z。",
                    "开始修改"))
            {
                int changed = 0;
                if (BentoCommand.Run(
                        "光源批处理失败",
                        () => changed = SceneLightBatchService.ApplyShadows(plan, _targetShadows),
                        () => $"已修改 {changed} 个光源，可按 Ctrl+Z 撤销。"))
                    SceneView.RepaintAll();
            }
        }

        public void OnActivated() => _selection.Invalidate();
        public void OnDeactivated() { }
        public void OnSelectionChanged() => _selection.Invalidate();

        static string GetLabel(LightShadows shadows)
        {
            switch (shadows)
            {
                case LightShadows.Hard: return "硬阴影";
                case LightShadows.Soft: return "软阴影";
                default: return "关闭阴影";
            }
        }
    }
}

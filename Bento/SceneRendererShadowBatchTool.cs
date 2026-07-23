using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Bento
{
    sealed class SceneRendererShadowBatchTool
    {
        int _castingOption;
        RendererShadowReceiveOption _receiveOption;
        static readonly string[] CastingLabels = { "不修改", "关闭", "开启", "双面", "仅显示阴影" };

        public void Draw()
        {
            EditorGUILayout.LabelField("场景物体阴影批处理", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "在 Hierarchy 中选择物体或父节点，递归处理其子物体 Renderer。投射阴影与接收阴影可独立设置，只修改场景实例并支持 Ctrl+Z。",
                MessageType.Info);

            _castingOption = EditorGUILayout.Popup("投射阴影", _castingOption, CastingLabels);
            _receiveOption = (RendererShadowReceiveOption)EditorGUILayout.EnumPopup("接收阴影", _receiveOption);

            var roots = BentoUtility.GetTopLevelSelectedSceneObjects(sortByHierarchy: true);
            var plan = SceneRendererShadowBatchService.BuildPlan(roots);
            ShadowCastingMode? casting = GetCastingMode(_castingOption);
            int changeCount = SceneRendererShadowBatchService.CountChanges(plan, casting, _receiveOption);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField($"选中根对象：{roots.Count} 个", EditorStyles.miniLabel);
                EditorGUILayout.LabelField($"找到 Renderer：{plan.renderers.Count} 个", EditorStyles.boldLabel);
                EditorGUILayout.LabelField($"直接选中：{plan.directRendererCount}　父节点下找到：{plan.childRendererCount}", EditorStyles.miniLabel);
                EditorGUILayout.LabelField($"投射关闭：{plan.castingOffCount}　投射开启：{plan.castingOnCount}", EditorStyles.miniLabel);
                EditorGUILayout.LabelField($"接收关闭：{plan.receiveOffCount}　接收开启：{plan.receiveOnCount}", EditorStyles.miniLabel);
            }

            if (roots.Count == 0) EditorGUILayout.HelpBox("请先在 Hierarchy 中选择要处理的物体或父节点。", MessageType.Warning);
            else if (plan.renderers.Count == 0) EditorGUILayout.HelpBox("选中对象及其子物体中没有 Renderer。", MessageType.Warning);
            else if (!casting.HasValue && _receiveOption == RendererShadowReceiveOption.NoChange)
                EditorGUILayout.HelpBox("请至少选择一项需要修改的阴影设置。", MessageType.Warning);

            using (new EditorGUI.DisabledScope(changeCount == 0))
            if (GUILayout.Button($"修改 {changeCount} 个 Renderer", GUILayout.Height(36)) && BentoCommand.Confirm(
                    "确认修改场景物体阴影",
                    $"将修改 {changeCount} 个 Renderer。\n只修改场景实例，支持 Ctrl+Z。",
                    "开始修改"))
            {
                int changed = 0;
                if (BentoCommand.Run(
                        "Renderer 阴影批处理失败",
                        () => changed = SceneRendererShadowBatchService.Apply(plan, casting, _receiveOption),
                        () => $"已修改 {changed} 个 Renderer，可按 Ctrl+Z 撤销。"))
                    SceneView.RepaintAll();
            }
        }

        static ShadowCastingMode? GetCastingMode(int option)
        {
            switch (option)
            {
                case 1: return ShadowCastingMode.Off;
                case 2: return ShadowCastingMode.On;
                case 3: return ShadowCastingMode.TwoSided;
                case 4: return ShadowCastingMode.ShadowsOnly;
                default: return null;
            }
        }
    }
}

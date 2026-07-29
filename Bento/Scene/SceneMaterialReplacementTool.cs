using UnityEditor;
using UnityEngine;

namespace Bento
{
    sealed class SceneMaterialReplacementTool : IBentoPageLifecycle
    {
        Material _replacement;
        Material _sourceMaterial;
        SceneMaterialReplaceMode _mode = SceneMaterialReplaceMode.AllSlots;
        SelectionPlanCache<SceneMaterialReplacementPlan> _selection;

        public SceneMaterialReplacementTool()
        {
            _selection = new SelectionPlanCache<SceneMaterialReplacementPlan>(
                roots => SceneMaterialReplacementService.BuildPlan(roots, _mode, _sourceMaterial));
        }

        public void Draw()
        {
            EditorGUILayout.LabelField("场景材质替换", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "在 Hierarchy 中选择一个或多个场景对象，递归处理其子物体 Renderer。整批操作支持 Ctrl+Z，不修改 Prefab 或材质资产本身。",
                MessageType.Info);
            _replacement = (Material)EditorGUILayout.ObjectField("替换为", _replacement, typeof(Material), false);
            var nextMode = (SceneMaterialReplaceMode)EditorGUILayout.EnumPopup("替换范围", _mode);
            if (nextMode != _mode) { _mode = nextMode; _selection.Invalidate(); }
            if (_mode == SceneMaterialReplaceMode.MatchingMaterial)
            {
                var nextSource = (Material)EditorGUILayout.ObjectField("仅替换旧材质", _sourceMaterial, typeof(Material), false);
                if (nextSource != _sourceMaterial) { _sourceMaterial = nextSource; _selection.Invalidate(); }
            }

            var roots = _selection.Roots;
            var plan = _selection.Plan;
            EditorGUILayout.LabelField($"选中根对象：{roots.Count} 个", EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"将处理 Renderer：{plan.renderers.Count} 个 / 材质槽：{plan.slotCount} 个", EditorStyles.miniLabel);
            if (roots.Count == 0) EditorGUILayout.HelpBox("请先在 Hierarchy 中选择要处理的场景对象。", MessageType.Warning);
            else if (_mode == SceneMaterialReplaceMode.MatchingMaterial && _sourceMaterial == null)
                EditorGUILayout.HelpBox("请选择需要被替换的旧材质球。", MessageType.Warning);

            bool valid = roots.Count > 0 && _replacement != null && plan.slotCount > 0 &&
                         (_mode == SceneMaterialReplaceMode.AllSlots || _sourceMaterial != null);
            using (new EditorGUI.DisabledScope(!valid))
            if (GUILayout.Button($"替换 {plan.slotCount} 个材质槽", GUILayout.Height(36)) && BentoCommand.Confirm(
                    "确认替换场景材质",
                    $"将修改 {plan.renderers.Count} 个 Renderer、{plan.slotCount} 个材质槽。\n操作支持 Ctrl+Z 撤销。",
                    "开始替换"))
            {
                int changed = 0;
                if (BentoCommand.Run("场景材质替换失败",
                        () => changed = SceneMaterialReplacementService.Execute(plan, _replacement, _mode, _sourceMaterial),
                        () => $"已替换 {changed} 个材质槽，可按 Ctrl+Z 撤销。"))
                    SceneView.RepaintAll();
            }
        }

        public void OnActivated() => _selection.Invalidate();
        public void OnDeactivated() { }
        public void OnSelectionChanged() => _selection.Invalidate();
    }
}

using UnityEditor;
using UnityEngine;

namespace Bento
{
    sealed class SceneMaterialReplacementTool : IBentoPageLifecycle
    {
        Material _replacement;
        Material _sourceMaterial;
        SceneMaterialReplaceMode _mode = SceneMaterialReplaceMode.AllSlots;
        SceneMaterialReplaceScope _scope = SceneMaterialReplaceScope.SelectedHierarchy;
        SelectionPlanCache<SceneMaterialReplacementPlan> _selection;
        SceneMaterialReplacementPlan _loadedScenesPlan = new SceneMaterialReplacementPlan();

        public SceneMaterialReplacementTool()
        {
            _selection = new SelectionPlanCache<SceneMaterialReplacementPlan>(
                roots => SceneMaterialReplacementService.BuildPlan(roots, _mode, _sourceMaterial));
        }

        public void Draw()
        {
            EditorGUILayout.LabelField("场景材质替换", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "可处理 Hierarchy 选中范围，也可用 Project 中选中的旧材质扫描全部已加载场景。整批操作支持 Ctrl+Z，不修改 Prefab 或材质资产本身。",
                MessageType.Info);

            int nextScopeIndex = EditorGUILayout.Popup(
                "处理范围",
                _scope == SceneMaterialReplaceScope.SelectedHierarchy ? 0 : 1,
                new[] { "Hierarchy 选中范围", "当前已加载场景" });
            var nextScope = nextScopeIndex == 0
                ? SceneMaterialReplaceScope.SelectedHierarchy
                : SceneMaterialReplaceScope.LoadedScenes;
            if (nextScope != _scope)
            {
                _scope = nextScope;
                InvalidatePlans();
            }

            _replacement = (Material)EditorGUILayout.ObjectField("替换为", _replacement, typeof(Material), false);
            if (_scope == SceneMaterialReplaceScope.SelectedHierarchy)
            {
                DrawHierarchyControls();
                DrawExecute(_selection.Plan, _mode, _sourceMaterial);
            }
            else
                DrawLoadedSceneControls();
        }

        void DrawHierarchyControls()
        {
            int nextModeIndex = EditorGUILayout.Popup(
                "替换方式",
                _mode == SceneMaterialReplaceMode.AllSlots ? 0 : 1,
                new[] { "全部材质槽", "仅指定旧材质" });
            var nextMode = nextModeIndex == 0
                ? SceneMaterialReplaceMode.AllSlots
                : SceneMaterialReplaceMode.MatchingMaterial;
            if (nextMode != _mode)
            {
                _mode = nextMode;
                _selection.Invalidate();
            }
            if (_mode == SceneMaterialReplaceMode.MatchingMaterial)
                DrawSourceMaterialField();

            var roots = _selection.Roots;
            var plan = _selection.Plan;
            EditorGUILayout.LabelField($"选中根对象：{roots.Count} 个", EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"将处理 Renderer：{plan.renderers.Count} 个 / 材质槽：{plan.slotCount} 个", EditorStyles.miniLabel);
            if (roots.Count == 0) EditorGUILayout.HelpBox("请先在 Hierarchy 中选择要处理的场景对象。", MessageType.Warning);
            else if (_mode == SceneMaterialReplaceMode.MatchingMaterial && _sourceMaterial == null)
                EditorGUILayout.HelpBox("请选择需要被替换的旧材质球。", MessageType.Warning);
        }

        void DrawLoadedSceneControls()
        {
            DrawSourceMaterialField();
            using (new EditorGUI.DisabledScope(_sourceMaterial == null))
            {
                if (GUILayout.Button("扫描当前已加载场景"))
                    _loadedScenesPlan = SceneMaterialReplacementService.BuildLoadedScenesPlan(_sourceMaterial);
            }

            EditorGUILayout.LabelField(
                $"命中 Renderer：{_loadedScenesPlan.renderers.Count} 个 / 材质槽：{_loadedScenesPlan.slotCount} 个",
                EditorStyles.miniLabel);
            if (_sourceMaterial == null)
                EditorGUILayout.HelpBox("请在 Project 选择旧材质，或将旧材质拖入上方字段。", MessageType.Warning);
            else if (_loadedScenesPlan.slotCount == 0)
                EditorGUILayout.HelpBox("点击扫描，确认当前已加载场景中哪些对象使用了这个材质。", MessageType.Info);

            DrawExecute(
                _loadedScenesPlan,
                SceneMaterialReplaceMode.MatchingMaterial,
                _sourceMaterial);
        }

        void DrawSourceMaterialField()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                var nextSource = (Material)EditorGUILayout.ObjectField(
                    "需要替换的旧材质",
                    _sourceMaterial,
                    typeof(Material),
                    false);
                if (nextSource != _sourceMaterial)
                {
                    _sourceMaterial = nextSource;
                    InvalidatePlans();
                }

                using (new EditorGUI.DisabledScope(!(Selection.activeObject is Material)))
                {
                    if (GUILayout.Button("使用当前选择", GUILayout.Width(96)))
                    {
                        _sourceMaterial = Selection.activeObject as Material;
                        InvalidatePlans();
                    }
                }
            }
        }

        void DrawExecute(
            SceneMaterialReplacementPlan plan,
            SceneMaterialReplaceMode mode,
            Material sourceMaterial)
        {
            bool valid = _replacement != null &&
                         plan != null &&
                         plan.slotCount > 0 &&
                         (mode == SceneMaterialReplaceMode.AllSlots || sourceMaterial != null);
            using (new EditorGUI.DisabledScope(!valid))
            if (GUILayout.Button($"替换 {plan.slotCount} 个材质槽", GUILayout.Height(36)) && BentoCommand.Confirm(
                    "确认替换场景材质",
                    $"将修改 {plan.renderers.Count} 个 Renderer、{plan.slotCount} 个材质槽。\n操作支持 Ctrl+Z 撤销。",
                    "开始替换"))
            {
                int changed = 0;
                if (BentoCommand.Run("场景材质替换失败",
                        () => changed = SceneMaterialReplacementService.Execute(plan, _replacement, mode, sourceMaterial),
                        () => $"已替换 {changed} 个材质槽，可按 Ctrl+Z 撤销。"))
                {
                    InvalidatePlans();
                    SceneView.RepaintAll();
                }
            }
        }

        void InvalidatePlans()
        {
            _selection.Invalidate();
            _loadedScenesPlan = new SceneMaterialReplacementPlan();
        }

        public void OnActivated() => InvalidatePlans();
        public void OnDeactivated() { }
        public void OnSelectionChanged()
        {
            if (_scope == SceneMaterialReplaceScope.SelectedHierarchy)
                _selection.Invalidate();
        }
    }
}

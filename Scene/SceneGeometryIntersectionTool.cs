using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Bento
{
    sealed class SceneGeometryIntersectionTool
    {
        enum BatchTarget
        {
            Second,
            First,
            Both
        }

        static readonly SceneGeometryScanScope[] ScopeValues =
        {
            SceneGeometryScanScope.BetweenLayers,
            SceneGeometryScanScope.SelectionOnly,
            SceneGeometryScanScope.SelectionAgainstScene,
            SceneGeometryScanScope.LoadedScenes
        };

        static readonly string[] ScopeLabels =
        {
            "Layer A 对 Layer B",
            "仅当前选择内部",
            "当前选择对整个场景",
            "所有已打开场景"
        };

        readonly SceneGeometryScanOptions _options = new SceneGeometryScanOptions
        {
            scope = SceneGeometryScanScope.BetweenLayers
        };
        int _firstLayer;
        int _secondLayer = 1;
        BatchTarget _batchTarget = BatchTarget.Second;
        bool _showSceneMarkers = true;
        Vector2 _scroll;

        public void Draw()
        {
            EditorGUILayout.LabelField("场景几何相交审查", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "未完工：当前检测结果和批量删除流程仍处于实验阶段。请只把它作为人工检查辅助，不要把结果直接当作最终结论。",
                MessageType.Warning);
            EditorGUILayout.HelpBox(
                "只读扫描，不修改场景。选择 Unity Layer A 和 Layer B，只检查这两个 Layer 之间。Collider 结果较可靠；没有 Collider 时会列为 Renderer 包围盒疑似项。",
                MessageType.Info);

            int scopeIndex = Mathf.Max(0, Array.IndexOf(ScopeValues, _options.scope));
            _options.scope = ScopeValues[EditorGUILayout.Popup("扫描范围", scopeIndex, ScopeLabels)];
            if (_options.scope == SceneGeometryScanScope.BetweenLayers)
                DrawLayerPair();
            _options.includeInactive = EditorGUILayout.Toggle("包含未激活对象", _options.includeInactive);
            if (_options.scope != SceneGeometryScanScope.BetweenLayers)
                _options.ignoreSameHierarchy = EditorGUILayout.Toggle("忽略父子层级内部", _options.ignoreSameHierarchy);
            _options.includeBoundsOnlyCandidates = EditorGUILayout.Toggle("保留无 Collider 疑似项", _options.includeBoundsOnlyCandidates);
            _options.minimumBoundsOverlap = EditorGUILayout.FloatField("最小包围盒交叠", _options.minimumBoundsOverlap);
            _options.minimumPenetration = EditorGUILayout.FloatField("最小 Collider 穿透", _options.minimumPenetration);
            _options.maximumResults = EditorGUILayout.IntSlider("最大结果数", _options.maximumResults, 10, 2000);

            using (new EditorGUILayout.HorizontalScope())
            {
                bool layerPairInvalid = _options.scope == SceneGeometryScanScope.BetweenLayers &&
                                        _firstLayer == _secondLayer;
                using (new EditorGUI.DisabledScope(layerPairInvalid))
                if (GUILayout.Button("执行只读扫描", GUILayout.Height(32)))
                {
                    _options.firstLayer = _firstLayer;
                    _options.secondLayer = _secondLayer;
                    if (BentoCommand.Run(
                            "几何相交扫描失败",
                            () => SceneGeometryIntersectionService.Scan(_options)))
                    {
                        _showSceneMarkers = true;
                        SceneGeometryIntersectionVisualizer.Enabled = true;
                    }
                }
                using (new EditorGUI.DisabledScope(SceneGeometryIntersectionSession.Current == null))
                    if (GUILayout.Button("清空内存结果", GUILayout.Height(32)))
                    {
                        SceneGeometryIntersectionSession.Clear();
                        SceneGeometryIntersectionVisualizer.Enabled = false;
                    }
            }

            var result = SceneGeometryIntersectionSession.Current;
            if (result == null)
            {
                EditorGUILayout.HelpBox("尚未扫描。AI 可通过 bento 的 scan_geometry_intersections / get_geometry_intersections 调用。", MessageType.None);
                return;
            }

            EditorGUILayout.LabelField(
                ResultSummary(result) +
                (result.truncated ? "（结果已截断）" : string.Empty), EditorStyles.boldLabel);
            if (result.scope == SceneGeometryScanScope.BetweenLayers)
                EditorGUILayout.LabelField(
                    $"Layer {LayerMask.LayerToName(result.firstLayer)}  ↔  Layer {LayerMask.LayerToName(result.secondLayer)}",
                    EditorStyles.wordWrappedMiniLabel);
            DrawResultActions(result);
            _scroll = EditorGUILayout.BeginScrollView(_scroll, EditorStyles.helpBox, GUILayout.MinHeight(240));
            foreach (var item in result.intersections)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        bool include = EditorGUILayout.Toggle(item.include, GUILayout.Width(18));
                        if (include != item.include)
                        {
                            item.include = include;
                            SceneGeometryIntersectionVisualizer.Repaint();
                        }
                        EditorGUILayout.LabelField(
                            item.kind == SceneGeometryIntersectionKind.ColliderPenetration
                                ? "Collider 穿透"
                                : "包围盒疑似",
                            EditorStyles.boldLabel);
                    }
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

        void DrawResultActions(SceneGeometryScanResult result)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                bool showMarkers = EditorGUILayout.ToggleLeft(
                    "在 Scene 视图显示定位框（A 青色、B 红色）",
                    _showSceneMarkers);
                if (showMarkers != _showSceneMarkers)
                {
                    _showSceneMarkers = showMarkers;
                    SceneGeometryIntersectionVisualizer.Enabled = showMarkers;
                }

                if (result.scope == SceneGeometryScanScope.BetweenLayers)
                {
                    _batchTarget = (BatchTarget)EditorGUILayout.Popup(
                        "批量操作目标",
                        (int)_batchTarget,
                        new[] { "B 组（默认清理目标）", "A 组（默认保留目标）", "A 与 B 两组" });
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("全部勾选")) SetAllIncluded(result, true);
                    if (GUILayout.Button("全部取消")) SetAllIncluded(result, false);
                }

                GameObject[] targets = GetIncludedTargets(result, _batchTarget);
                using (new EditorGUI.DisabledScope(targets.Length == 0))
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button($"选中并定位目标（{targets.Length}）", GUILayout.Height(30)))
                        SelectAndFrame(targets);
                    if (result.scope == SceneGeometryScanScope.BetweenLayers &&
                        GUILayout.Button($"删除目标（{targets.Length}）", GUILayout.Height(30)))
                        DeleteTargets(result, targets);
                }

                EditorGUILayout.LabelField(
                    "删除会按对象去重，并通过 Unity Undo 支持一次 Ctrl+Z 恢复。默认只删除 B 组，不会删除 A 组。",
                    EditorStyles.wordWrappedMiniLabel);
            }
        }

        static void SetAllIncluded(SceneGeometryScanResult result, bool include)
        {
            foreach (SceneGeometryIntersectionRecord item in result.intersections)
                item.include = include;
            SceneGeometryIntersectionVisualizer.Repaint();
        }

        static GameObject[] GetIncludedTargets(SceneGeometryScanResult result, BatchTarget target)
        {
            return result.intersections
                .Where(item => item.include)
                .SelectMany(item => TargetInstanceIds(item, target))
                .Distinct()
                .Select(EditorUtility.InstanceIDToObject)
                .OfType<GameObject>()
                .Where(gameObject => gameObject.scene.IsValid() && gameObject.scene.isLoaded)
                .OrderBy(gameObject => BentoUtility.GetHierarchyPath(gameObject.transform), StringComparer.Ordinal)
                .ToArray();
        }

        static int[] TargetInstanceIds(SceneGeometryIntersectionRecord item, BatchTarget target)
        {
            switch (target)
            {
                case BatchTarget.First:
                    return new[] { item.firstInstanceId };
                case BatchTarget.Both:
                    return new[] { item.firstInstanceId, item.secondInstanceId };
                default:
                    return new[] { item.secondInstanceId };
            }
        }

        static void SelectAndFrame(GameObject[] targets)
        {
            Selection.objects = targets;
            SceneView.lastActiveSceneView?.FrameSelected();
        }

        static void DeleteTargets(SceneGeometryScanResult result, GameObject[] targets)
        {
            if (targets.Length == 0) return;
            string preview = string.Join("\n", targets.Take(8).Select(gameObject =>
                "• " + BentoUtility.GetHierarchyPath(gameObject.transform)));
            if (targets.Length > 8) preview += $"\n……以及另外 {targets.Length - 8} 个对象";
            if (!EditorUtility.DisplayDialog(
                    "确认删除穿模对象",
                    $"将删除 {targets.Length} 个已勾选目标：\n\n{preview}\n\n支持一次 Ctrl+Z 恢复。",
                    "删除",
                    "取消"))
                return;

            var deletedIds = new HashSet<int>(targets.Select(target => target.GetInstanceID()));
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Bento 删除穿模对象");
            foreach (GameObject target in targets)
                if (target != null) Undo.DestroyObjectImmediate(target);
            Undo.CollapseUndoOperations(undoGroup);

            result.intersections.RemoveAll(item =>
                deletedIds.Contains(item.firstInstanceId) || deletedIds.Contains(item.secondInstanceId));
            Selection.objects = Array.Empty<UnityEngine.Object>();
            SceneGeometryIntersectionVisualizer.Repaint();
        }

        void DrawLayerPair()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                _firstLayer = EditorGUILayout.LayerField("Layer A（默认保留）", _firstLayer);
                _secondLayer = EditorGUILayout.LayerField("Layer B（默认清理）", _secondLayer);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("交换 A / B"))
                        (_firstLayer, _secondLayer) = (_secondLayer, _firstLayer);
                }
                EditorGUILayout.LabelField(
                    "只比较 Layer A 的 Renderer 与 Layer B 的 Renderer，不检查同 Layer 内部。",
                    EditorStyles.wordWrappedMiniLabel);
            }
        }

        static string ResultSummary(SceneGeometryScanResult result)
        {
            return result.scope == SceneGeometryScanScope.BetweenLayers
                ? $"Layer A {result.firstCandidateObjectCount}，Layer B {result.secondCandidateObjectCount}，候选对 {result.testedPairCount}，发现 {result.intersections.Count}"
                : $"对象 {result.candidateObjectCount}，候选对 {result.testedPairCount}，发现 {result.intersections.Count}";
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

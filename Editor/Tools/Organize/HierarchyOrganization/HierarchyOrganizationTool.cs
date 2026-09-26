using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Bento
{
    [BentoPage("hierarchy_organization", BentoGroups.Organize, "Hierarchy 整理", Order = 80)]
    sealed class HierarchyOrganizationTool : IBentoPageLifecycle, IBentoTool
    {
        readonly List<HierarchyOrganizationEntry> _entries = new List<HierarchyOrganizationEntry>();
        readonly List<HierarchyOrganizationEntry> _quickEntries = new List<HierarchyOrganizationEntry>();
        readonly List<HierarchyOrganizationEntry> _deformationRisks = new List<HierarchyOrganizationEntry>();
        Transform _targetParent;
        bool _quickPlanDirty = true;
        Vector2 _scroll;
        string _search = string.Empty;
        float _listHeight = 520f;
        string _newGroupName = "新建分组";

        public void Draw()
        {
            LoadPendingDraft();
            EditorGUILayout.LabelField("Hierarchy 分类整理", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "先在 Hierarchy 选择要归类的对象，再指定目标父节点。执行只改变父子关系，保持世界坐标、旋转和缩放，支持整批 Ctrl+Z。",
                MessageType.Info);

            DrawQuickParenting();
            DrawCreateGroup();
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("批量整理计划", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            _targetParent = (Transform)EditorGUILayout.ObjectField("目标父节点", _targetParent, typeof(Transform), true);
            if (EditorGUI.EndChangeCheck())
            {
                _entries.Clear();
                _quickPlanDirty = true;
            }
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
                                BentoGUI.FocusSceneObject(entry.objectInstanceId);
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

            DrawActivationControls();
        }

        static void DrawActivationControls()
        {
            GameObject[] selected = SceneObjectActivationService.GetSelectedSceneObjects();
            int count = selected.Length;

            EditorGUILayout.Space(14);
            EditorGUILayout.LabelField("低频操作：选中对象批量状态", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "对象启用控制 Inspector 顶部的 Active 勾选框；LOD 操作会递归处理选中对象及其子物体。禁用 LOD 时先固定显示 LOD0，避免远近切换。全部操作支持一次 Ctrl+Z 撤销。",
                MessageType.Info);

            using (new EditorGUI.DisabledScope(count == 0))
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button($"禁用选中对象（{count}）", GUILayout.Height(30)))
                    RunActivationCommand(false);
                if (GUILayout.Button($"启用选中对象（{count}）", GUILayout.Height(30)))
                    RunActivationCommand(true);
            }

            LODGroup[] lodGroups = SceneLodGroupBatchService.GetSelectedLodGroups();
            int lodCount = lodGroups.Length;
            using (new EditorGUI.DisabledScope(lodCount == 0))
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button($"禁用 LOD、固定 LOD0（{lodCount}）", GUILayout.Height(30)))
                    RunLodCommand(false);
                if (GUILayout.Button($"重新启用 LOD（{lodCount}）", GUILayout.Height(30)))
                    RunLodCommand(true);
            }
        }

        static void RunActivationCommand(bool active)
        {
            int changed = SceneObjectActivationService.SetSelectedActive(active);
            Debug.Log($"[Bento] 已{(active ? "启用" : "禁用")} {changed} 个场景对象，可使用 Ctrl+Z 撤销。");
        }

        static void RunLodCommand(bool enabled)
        {
            int changed = SceneLodGroupBatchService.SetSelectedLodEnabled(enabled);
            Debug.Log(
                enabled
                    ? $"[Bento] 已重新启用 {changed} 个 LODGroup，可使用 Ctrl+Z 撤销。"
                    : $"[Bento] 已禁用 {changed} 个 LODGroup，并固定显示 LOD0，可使用 Ctrl+Z 撤销。");
        }

        void DrawQuickParenting()
        {
            EditorGUILayout.LabelField("快速批量设为子物体", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "先在 Hierarchy 只选中一个目标父物体并缓存。之后可任意多选其他场景物体，一键归入该父物体；缓存不会随 Selection 改变，操作保持世界 Transform 并支持一次 Ctrl+Z 撤销。",
                MessageType.Info);

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(!TryGetSingleSelectedSceneTransform(out _)))
                {
                    if (GUILayout.Button("缓存当前选中为父物体", GUILayout.Height(32)) &&
                        TryGetSingleSelectedSceneTransform(out Transform selectedParent))
                    {
                        _targetParent = selectedParent;
                        _entries.Clear();
                        _quickPlanDirty = true;
                    }
                }

                using (new EditorGUI.DisabledScope(_targetParent == null))
                    if (GUILayout.Button("清除父物体缓存", GUILayout.Height(32)))
                    {
                        _targetParent = null;
                        _entries.Clear();
                        _quickPlanDirty = true;
                    }
            }

            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.ObjectField("已缓存父物体", _targetParent, typeof(Transform), true);

            RefreshQuickPlanIfNeeded();
            List<HierarchyOrganizationEntry> quickEntries = _quickEntries;
            int validCount = quickEntries.Count(entry => string.IsNullOrEmpty(entry.warning));
            IReadOnlyList<HierarchyOrganizationEntry> deformationRisks = _deformationRisks;
            string quickWarning = quickEntries
                .Where(entry => !string.IsNullOrEmpty(entry.warning))
                .Select(entry => $"{entry.objectPath}：{entry.warning}")
                .FirstOrDefault();

            if (_targetParent == null)
                EditorGUILayout.HelpBox("尚未缓存目标父物体。", MessageType.Warning);
            else if (quickEntries.Count == 0)
                EditorGUILayout.HelpBox("请在 Hierarchy 多选需要归入的场景物体。", MessageType.Info);
            else if (!string.IsNullOrEmpty(quickWarning))
                EditorGUILayout.HelpBox(quickWarning, MessageType.Warning);
            else if (deformationRisks.Count > 0)
                DrawDeformationRisks(deformationRisks);

            using (new EditorGUI.DisabledScope(
                       _targetParent == null || validCount == 0 ||
                       validCount != quickEntries.Count || deformationRisks.Count > 0))
            {
                if (GUILayout.Button($"将当前选中的 {validCount} 个物体归入缓存父物体", GUILayout.Height(40)))
                {
                    BentoCommand.Run("批量设置父物体失败", () =>
                    {
                        int count = HierarchyOrganizationService.Execute(quickEntries);
                        Selection.activeGameObject = _targetParent.gameObject;
                        EditorGUIUtility.PingObject(_targetParent.gameObject);
                        Debug.Log($"[Bento] 已将 {count} 个物体归入“{_targetParent.name}”，世界 Transform 保持不变，可使用 Ctrl+Z 撤销。");
                    });
                }
            }
        }

        void DrawDeformationRisks(IReadOnlyList<HierarchyOrganizationEntry> risks)
        {
            EditorGUILayout.HelpBox(
                $"检测到 {risks.Count} 个对象包含旋转与非等比缩放组合，直接换父级可能形变，已暂时阻止执行。",
                MessageType.Warning);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("定位第一个风险对象", GUILayout.Height(28)))
                    BentoGUI.FocusSceneObject(risks[0].objectInstanceId);
                if (GUILayout.Button($"选中全部风险对象（{risks.Count}）", GUILayout.Height(28)))
                    Selection.objects = risks
                        .Select(entry => EditorUtility.InstanceIDToObject(entry.objectInstanceId))
                        .Where(item => item != null)
                        .ToArray();
            }

            DrawCompensationActions(risks);

            int displayCount = Mathf.Min(risks.Count, 20);
            for (int index = 0; index < displayCount; index++)
            {
                HierarchyOrganizationEntry entry = risks[index];
                using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField(entry.objectPath, EditorStyles.miniLabel);
                    if (GUILayout.Button("定位", GUILayout.Width(58)))
                        BentoGUI.FocusSceneObject(entry.objectInstanceId);
                }
            }

            if (risks.Count > displayCount)
                EditorGUILayout.LabelField($"仅显示前 {displayCount} 个；可点击“选中全部风险对象”统一检查。", EditorStyles.miniLabel);
        }

        void DrawCompensationActions(IReadOnlyList<HierarchyOrganizationEntry> risks)
        {
            HierarchyCompensationPreview individual = HierarchyCompensationService.Preview(
                risks, _targetParent, HierarchyCompensationMode.Individual);
            HierarchyCompensationPreview shared = HierarchyCompensationService.Preview(
                risks, _targetParent, HierarchyCompensationMode.Shared);

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("安全补偿方案", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(!individual.canExecute))
                    if (GUILayout.Button(
                            $"独立补偿并归入（创建 {individual.compensationNodeCount} 个节点）",
                            GUILayout.Height(34)))
                        ExecuteCompensation(individual);

                using (new EditorGUI.DisabledScope(!shared.canExecute))
                    if (GUILayout.Button(
                            $"共享补偿并归入（创建 {shared.compensationNodeCount} 个节点）",
                            GUILayout.Height(34)))
                        ExecuteCompensation(shared);
            }

            if (!shared.canExecute && !string.IsNullOrEmpty(shared.reason))
                EditorGUILayout.HelpBox($"共享方案不可用：{shared.reason}", MessageType.Info);
        }

        void ExecuteCompensation(HierarchyCompensationPreview preview)
        {
            BentoCommand.Run("安全补偿归入失败", () =>
            {
                int compensationCount = HierarchyCompensationService.Execute(preview, _targetParent);
                Selection.activeGameObject = _targetParent.gameObject;
                EditorGUIUtility.PingObject(_targetParent.gameObject);
                _quickPlanDirty = true;
                Debug.Log(
                    $"[Bento] 已安全归入 {preview.entries.Count} 个风险对象，创建 {compensationCount} 个补偿节点，可使用 Ctrl+Z 一次撤销。");
            });
        }

        void DrawCreateGroup()
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("一键创建空父物体", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                _newGroupName = EditorGUILayout.TextField("空物体名称", _newGroupName);
                int count = BentoUtility.GetTopLevelSelectedSceneObjects(true).Count;
                using (new EditorGUI.DisabledScope(count == 0))
                {
                    if (GUILayout.Button($"创建并归入（{count}）", GUILayout.Width(150), GUILayout.Height(30)))
                    {
                        BentoCommand.Run("创建场景分组失败", () =>
                        {
                            GameObject group = HierarchyOrganizationService.CreateParentForSelection(_newGroupName);
                            Selection.activeGameObject = group;
                            EditorGUIUtility.PingObject(group);
                            Debug.Log($"[Bento] 已创建空父物体“{group.name}”并归入 {count} 个对象，可使用 Ctrl+Z 撤销。");
                        });
                    }
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

        void RefreshQuickPlanIfNeeded()
        {
            if (!_quickPlanDirty) return;
            _quickPlanDirty = false;
            _quickEntries.Clear();
            _deformationRisks.Clear();
            if (_targetParent == null) return;

            _quickEntries.AddRange(HierarchyOrganizationService.BuildFromSelection(_targetParent));
            _deformationRisks.AddRange(HierarchyTransformRiskAnalyzer.FindRisks(_quickEntries, _targetParent));
        }

        public void OnActivated() => _quickPlanDirty = true;
        public void OnDeactivated() { }
        public void OnSelectionChanged() => _quickPlanDirty = true;

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

        static bool TryGetSingleSelectedSceneTransform(out Transform transform)
        {
            transform = null;
            GameObject[] selected = Selection.gameObjects
                .Where(gameObject => gameObject != null && gameObject.scene.IsValid())
                .Distinct()
                .ToArray();
            if (selected.Length != 1) return false;
            transform = selected[0].transform;
            return true;
        }
    }
}

using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Bento
{
    class ResourceVariantTool : IBentoPageLifecycle
    {
        readonly ResourceVariantSession _session = new ResourceVariantSession();
        readonly ResourceVariantSettingsGUI _settingsGUI;
        Vector2 _scroll;
        Vector2 _planScroll;
        bool _showPlan = true;
        bool _showHealth;
        List<GameObject> _selection = new List<GameObject>();
        bool _selectionDirty = true;

        public ResourceVariantTool() => _settingsGUI = new ResourceVariantSettingsGUI(_session);

        ResourceVariantPlan Plan => _session.Plan;
        ResourceVariantGenerationResult LastResult => _session.LastResult;

        public void Draw()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorGUILayout.HelpBox("Play Mode 中禁止生成资源变体，请先退出运行模式。", MessageType.Warning);
                return;
            }

            var selectedObjects = GetSelection();
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUILayout.LabelField("场景资源变体", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "把 Hierarchy 选中的 Prefab、FBX 或普通对象保存为独立 Prefab。可选复制模型、材质和原材质实际引用的贴图，并按原属性槽自动重连。",
                MessageType.Info);
            _settingsGUI.Draw();
            DrawPlan(selectedObjects);
            DrawActions(selectedObjects);
            DrawLastResult();
            EditorGUILayout.EndScrollView();
        }

        void DrawPlan(List<GameObject> selectedObjects)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                _showPlan = EditorGUILayout.Foldout(_showPlan, "生成计划 / 干运行", true);
                if (GUILayout.Button("分析选中对象", GUILayout.Width(100))) _session.Analyze(selectedObjects);
            }
            if (!_showPlan) return;
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (Plan == null)
                {
                    EditorGUILayout.LabelField("点击分析，不会创建文件。", EditorStyles.miniLabel);
                    return;
                }
                EditorGUILayout.LabelField(
                    $"选中实例：{Plan.SelectedInstanceCount}　生成 Prefab：{Plan.objects.Count}　模型：{Plan.ModelCopyCount}　材质：{Plan.MaterialCopyCount}　贴图：{Plan.TextureCopyCount}",
                    EditorStyles.boldLabel);
                foreach (string warning in Plan.warnings.Distinct()) EditorGUILayout.HelpBox(warning, MessageType.Warning);
                _planScroll = EditorGUILayout.BeginScrollView(_planScroll, GUILayout.MinHeight(180), GUILayout.MaxHeight(360));
                foreach (var objectPlan in Plan.objects) DrawObjectPlan(objectPlan, Plan.settingsSnapshot);
                EditorGUILayout.EndScrollView();
            }
        }

        static void DrawObjectPlan(ResourceVariantObjectPlan objectPlan, ResourceVariantPreset settings)
        {
            int instanceCount = objectPlan.sourceObjects.Count > 0
                ? objectPlan.sourceObjects.Count
                : 1;
            EditorGUILayout.LabelField(
                $"● {objectPlan.sourceObject.name} × {instanceCount} → {objectPlan.prefabOutputName}.prefab",
                EditorStyles.boldLabel);
            foreach (var model in objectPlan.models)
            {
                string modelName = ResourceVariantGenerator.BuildAssetName(
                    Path.GetFileNameWithoutExtension(model.sourcePath),
                    objectPlan.namingIndex,
                    settings);
                EditorGUILayout.LabelField(
                    $"　模型：{Path.GetFileName(model.sourcePath)} → {modelName}{Path.GetExtension(model.sourcePath)} [{State(model.willCopy, model.reason)}]");
            }
            foreach (var material in objectPlan.materials)
            {
                string matName = ResourceVariantGenerator.BuildAssetName(
                    material.source.name,
                    objectPlan.namingIndex,
                    settings);
                EditorGUILayout.LabelField($"　材质：{material.source.name} → {matName}.mat [{State(material.willCopy, material.reason)}]");
                List<ResourceVariantTexturePlan> visibleTextures = material.textures
                    .Where(texture => texture.disposition == VariantTextureDisposition.CopyCandidate)
                    .ToList();
                int ignoredTextureCount = material.textures.Count - visibleTextures.Count;
                foreach (var texture in visibleTextures)
                {
                    string texName = ResourceVariantGenerator.BuildAssetName(
                        texture.source.name,
                        objectPlan.namingIndex,
                        settings);
                    string kind = ResourceVariantTextureClassifier.GetDisplayName(texture.kind);
                    EditorGUILayout.LabelField($"　　{texture.propertyName} ({kind}): {texture.source.name} → {texName}{Path.GetExtension(texture.sourcePath)} [{State(texture.willCopy, texture.reason)}]", EditorStyles.miniLabel);
                }
                if (ignoredTextureCount > 0)
                    EditorGUILayout.LabelField(
                        $"　　已忽略 {ignoredTextureCount} 个 Shader/Terrain/运行时共享贴图槽（不会复制）",
                        EditorStyles.miniLabel);
            }
        }

        void DrawActions(List<GameObject> selectedObjects)
        {
            bool valid = _session.Validate(selectedObjects, out string message);
            using (new EditorGUILayout.HorizontalScope())
            using (new EditorGUI.DisabledScope(!valid))
            {
                if (GUILayout.Button("仅干运行", GUILayout.Height(38))) _session.Analyze(selectedObjects);
                string generateLabel = Plan == null
                    ? $"生成选中的 {selectedObjects.Count} 个实例"
                    : $"生成 {Plan.objects.Count} 个 Prefab 变体并替换 {Plan.SelectedInstanceCount} 个实例";
                if (GUILayout.Button(generateLabel, GUILayout.Height(38)))
                {
                    _session.Analyze(selectedObjects);
                    if (EditorUtility.DisplayDialog(
                            "确认生成资源变体",
                            $"选中实例 {Plan.SelectedInstanceCount} / 生成 Prefab {Plan.objects.Count} / 模型 {Plan.ModelCopyCount} / 材质 {Plan.MaterialCopyCount} / 贴图 {Plan.TextureCopyCount}",
                            "开始生成",
                            "取消"))
                        Generate();
                }
            }
            if (!valid) EditorGUILayout.HelpBox(message, MessageType.Warning);
        }

        void DrawLastResult()
        {
            if (LastResult == null)
            {
                if (GUILayout.Button("恢复并清理最近一次生成记录"))
                {
                    if (!_session.TryRestoreLatestGeneration())
                        EditorUtility.DisplayDialog("没有可恢复记录", "没有找到仍然存在的最近生成资产。", "确定");
                }
                return;
            }
            _showHealth = EditorGUILayout.Foldout(_showHealth, "上次生成结果与健康检查", true);
            if (!_showHealth) return;
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(
                    $"Prefab {LastResult.createdPrefabs.Count} / 模型 {LastResult.createdModels.Count} / 材质 {LastResult.createdMaterials.Count} / 贴图 {LastResult.createdTextures.Count}");
                if (LastResult.healthWarnings.Count == 0) EditorGUILayout.HelpBox("健康检查通过。", MessageType.Info);
                else foreach (string warning in LastResult.healthWarnings) EditorGUILayout.HelpBox(warning, MessageType.Warning);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("定位新 Prefab")) PingFirst(LastResult.createdPrefabs);
                    if (GUILayout.Button("定位模型")) PingFirst(LastResult.createdModels);
                    if (GUILayout.Button("定位材质")) PingFirst(LastResult.createdMaterials);
                    if (GUILayout.Button("定位贴图")) PingFirst(LastResult.createdTextures);
                }
                if (GUILayout.Button("一键完整撤销本次生成")) UndoLastGeneration();
            }
        }

        void Generate()
        {
            try
            {
                var result = _session.Generate();
                if (result.replacements.Count > 0) Selection.objects = result.replacements.Cast<UnityEngine.Object>().ToArray();
            }
            catch (ResourceVariantGenerationException exception)
            {
                Debug.LogException(exception.InnerException ?? exception);
                EditorUtility.DisplayDialog("生成中断", $"{exception.Message}\n\n已创建 {exception.result.createdAssets.Count} 个资源，可在下方撤销清理。", "确定");
            }
            catch (System.Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("生成中断", exception.Message, "确定");
            }
        }

        void UndoLastGeneration()
        {
            if (!_session.TryPrepareLastGenerationCleanup(out string referencedPrefab))
            {
                if (string.IsNullOrEmpty(referencedPrefab)) return;
                EditorUtility.DisplayDialog("完整撤销已停止", $"当前已加载场景仍在使用：\n{referencedPrefab}\n\n为避免误删引用，工具没有删除任何 Project 资源。", "确定");
                return;
            }
            if (!EditorUtility.DisplayDialog("确认完整撤销", $"场景中的生成实例已经撤销。接下来将删除 {LastResult.createdAssets.Count} 个本次生成的 Project 资源。", "删除资源", "取消")) return;
            try
            {
                ResourceVariantCleanupResult deleted = _session.DeleteGeneratedAssets();
                _showHealth = false;
                EditorUtility.DisplayDialog(
                    "完整撤销成功",
                    $"场景实例已恢复，已删除 {deleted.Paths.Count} 个生成资产。",
                    "确定");
            }
            catch (ResourceVariantCleanupException exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog(
                    "部分资产删除失败",
                    $"已删除 {exception.Result.Paths.Count} 个资产，仍有 {exception.Result.FailedPaths.Count} 个未删除。\n\n剩余记录和撤销按钮已保留，可修复占用后继续清理。",
                    "确定");
            }
            catch (System.Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog(
                    "完整撤销失败",
                    $"{exception.Message}\n\n生成记录和撤销按钮仍然保留。",
                    "确定");
            }
        }

        List<GameObject> GetSelection()
        {
            if (!_selectionDirty) return _selection;
            _selection = BentoUtility.GetTopLevelSelectedSceneObjects();
            _selectionDirty = false;
            return _selection;
        }

        public void OnActivated() => _selectionDirty = true;
        public void OnDeactivated() { }
        public void OnSelectionChanged()
        {
            _selectionDirty = true;
            _session.InvalidatePlan();
        }

        static string State(bool enabled, string reason) => enabled ? "复制" : $"保留（{reason}）";

        static void PingFirst(List<string> paths)
        {
            if (paths.Count == 0) return;
            var asset = AssetDatabase.LoadMainAssetAtPath(paths[0]);
            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
        }
    }

    static class ResourceVariantMcpSummary
    {
        public static object Build(ResourceVariantPlan plan) => new
        {
            selected_instance_count = plan.SelectedInstanceCount,
            generated_prefab_count = plan.objects.Count,
            model_copy_count = plan.ModelCopyCount,
            material_copy_count = plan.MaterialCopyCount,
            texture_copy_count = plan.TextureCopyCount,
            warnings = plan.warnings.Distinct().ToArray(),
            objects = plan.objects.Select(item => new
            {
                source_name = item.sourceObject.name,
                source_asset_path = item.sourceAssetPath,
                prefab_output_name = item.prefabOutputName
            }).ToArray()
        };
    }
}

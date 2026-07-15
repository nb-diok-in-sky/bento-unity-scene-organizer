using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Bento
{
    class SafeAssetMoveTool
    {
        readonly AssetNamingOptions _naming = new AssetNamingOptions();
        readonly List<UnityEngine.Object> _sourceAssets = new List<UnityEngine.Object>();
        readonly List<AssetMoveEntry> _plan = new List<AssetMoveEntry>();
        UnityEngine.Object _sourceAssetToAdd;
        DefaultAsset _prefabFolder;
        DefaultAsset _materialFolder;
        DefaultAsset _textureFolder;
        Vector2 _scrollPosition;
        AssetMoveHistory _lastHistory;

        public void Draw()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorGUILayout.HelpBox("Play Mode 中禁止移动 Project 资源。", MessageType.Warning);
                return;
            }

            EditorGUILayout.LabelField("安全移动 Project 资产", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "使用 AssetDatabase.MoveAsset 移动资源并验证 GUID 不变。Prefab/FBX 会列出直接引用的材质和贴图，可逐项取消。",
                MessageType.Info);

            DrawSourceAssets();
            DrawFolders();
            DrawNaming();
            DrawPlanToolbar();
            DrawPlan();
            DrawActions();
        }

        public object AnalyzeSelectionForMcp(
            string prefabFolder = null,
            string materialFolder = null,
            string textureFolder = null,
            AssetNamingOptions naming = null)
        {
            ApplyMcpInputs(prefabFolder, materialFolder, textureFolder, naming);
            RebuildPlan();
            return new
            {
                count = _plan.Count,
                valid_count = _plan.Count(entry => string.IsNullOrEmpty(entry.warning)),
                entries = _plan.Select(entry => new
                {
                    source_path = entry.sourcePath,
                    source_guid = entry.sourceGuid,
                    target_path = entry.targetPath,
                    kind = entry.kind.ToString(),
                    is_dependency = entry.isDependency,
                    owner_path = entry.ownerPath,
                    warning = entry.warning,
                    prefab_reference_count = entry.prefabReferenceCount,
                    material_reference_count = entry.materialReferenceCount,
                    scene_reference_count = entry.sceneReferenceCount,
                    keeps_original_name = entry.keepOriginalNameBecauseShared,
                    reference_samples = entry.referenceSamples.ToArray()
                }).ToArray()
            };
        }

        void ApplyMcpInputs(string prefabFolder, string materialFolder, string textureFolder, AssetNamingOptions naming)
        {
            if (BentoUtility.IsProjectFolder(prefabFolder))
                _prefabFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(prefabFolder);
            if (BentoUtility.IsProjectFolder(materialFolder))
                _materialFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(materialFolder);
            if (BentoUtility.IsProjectFolder(textureFolder))
                _textureFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(textureFolder);
            if (naming == null) return;
            AssetNaming.Copy(naming, _naming);
        }

        void DrawFolders()
        {
            EditorGUILayout.LabelField("目标文件夹（这里只能拖 Project 文件夹）", EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUI.BeginChangeCheck();
                _prefabFolder = DrawFolder("Prefab / FBX", _prefabFolder);
                _materialFolder = DrawFolder("Material", _materialFolder);
                _textureFolder = DrawFolder("Texture", _textureFolder);
                if (EditorGUI.EndChangeCheck() && _plan.Count > 0) RebuildPlan();
            }
        }

        void DrawSourceAssets()
        {
            EditorGUILayout.LabelField("源资产", EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.HelpBox(
                    "拖入 Project 中的 Prefab、FBX、Material 或 Texture。拖入 Prefab/FBX 后会自动加入它引用的材质和贴图。",
                    MessageType.None);

                using (new EditorGUILayout.HorizontalScope())
                {
                    _sourceAssetToAdd = EditorGUILayout.ObjectField("添加源资产", _sourceAssetToAdd, typeof(UnityEngine.Object), false);
                    using (new EditorGUI.DisabledScope(!IsSupportedSourceAsset(_sourceAssetToAdd)))
                    {
                        if (GUILayout.Button("添加", GUILayout.Width(54)))
                        {
                            AddSourceAsset(_sourceAssetToAdd);
                            _sourceAssetToAdd = null;
                            RebuildPlan();
                        }
                    }
                }

                DrawDropArea();

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("使用 Project 当前选择"))
                    {
                        foreach (var asset in Selection.objects) AddSourceAsset(asset);
                        RebuildPlan();
                    }
                    using (new EditorGUI.DisabledScope(_sourceAssets.Count == 0))
                    {
                        if (GUILayout.Button("清空来源"))
                        {
                            _sourceAssets.Clear();
                            _plan.Clear();
                        }
                    }
                }

                for (int index = 0; index < _sourceAssets.Count; index++)
                {
                    var asset = _sourceAssets[index];
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.ObjectField(asset, typeof(UnityEngine.Object), false);
                        if (GUILayout.Button("移除", GUILayout.Width(48)))
                        {
                            _sourceAssets.RemoveAt(index);
                            RebuildPlan();
                            break;
                        }
                    }
                }
            }
        }

        void DrawDropArea()
        {
            Rect dropArea = GUILayoutUtility.GetRect(0, 56, GUILayout.ExpandWidth(true));
            GUI.Box(dropArea, "把一个或多个 Project 资产拖到这里", EditorStyles.helpBox);
            var currentEvent = Event.current;
            if (!dropArea.Contains(currentEvent.mousePosition) ||
                (currentEvent.type != EventType.DragUpdated && currentEvent.type != EventType.DragPerform))
                return;

            bool hasSupportedAssets = DragAndDrop.objectReferences.Any(IsSupportedSourceAsset);
            DragAndDrop.visualMode = hasSupportedAssets ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;
            if (currentEvent.type == EventType.DragPerform && hasSupportedAssets)
            {
                DragAndDrop.AcceptDrag();
                foreach (var asset in DragAndDrop.objectReferences) AddSourceAsset(asset);
                RebuildPlan();
            }
            currentEvent.Use();
        }

        void DrawNaming()
        {
            EditorGUILayout.LabelField("可选重命名", EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUI.BeginChangeCheck();
                AssetNaming.DrawFields(_naming, "统一基础名称（留空保留原名）");
                if (EditorGUI.EndChangeCheck() && _plan.Count > 0) RebuildPlan();
            }
        }

        void DrawPlanToolbar()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField($"移动计划：{_plan.Count} 项", EditorStyles.boldLabel);
                if (GUILayout.Button("扫描 Project 选择", GUILayout.Width(130))) RebuildPlan();
            }
        }

        void DrawPlan()
        {
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition, EditorStyles.helpBox, GUILayout.MinHeight(200));
            if (_plan.Count == 0)
            {
                EditorGUILayout.LabelField("在 Project 窗口选择 Prefab、FBX、材质或贴图，然后点击扫描。", EditorStyles.miniLabel);
            }
            foreach (var entry in _plan)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    entry.include = EditorGUILayout.ToggleLeft($"{entry.kind}: {entry.sourcePath}", entry.include);
                    EditorGUILayout.LabelField($"→ {entry.targetPath}", EditorStyles.miniLabel);
                    if (entry.isDependency) EditorGUILayout.LabelField($"引用来源：{entry.ownerPath}", EditorStyles.miniLabel);
                    if (entry.kind == AssetMoveKind.Material || entry.kind == AssetMoveKind.Texture)
                    {
                        EditorGUILayout.LabelField(
                            $"反向引用：Prefab {entry.prefabReferenceCount} / Material {entry.materialReferenceCount} / Scene {entry.sceneReferenceCount}",
                            EditorStyles.miniLabel);
                        if (entry.keepOriginalNameBecauseShared)
                            EditorGUILayout.HelpBox("检测到共享引用：移动时保留原文件名，不应用统一重命名。", MessageType.Info);
                        foreach (string sample in entry.referenceSamples.Take(4))
                            EditorGUILayout.LabelField($"　↳ {sample}", EditorStyles.miniLabel);
                    }
                    if (!string.IsNullOrEmpty(entry.warning)) EditorGUILayout.HelpBox(entry.warning, MessageType.Warning);
                    if (GUILayout.Button("定位源资源", GUILayout.Width(90))) Ping(entry.sourcePath);
                }
            }
            EditorGUILayout.EndScrollView();
        }

        void DrawActions()
        {
            bool canMove = _plan.Any(entry => entry.include) && _plan.Where(entry => entry.include).All(entry => string.IsNullOrEmpty(entry.warning));
            using (new EditorGUI.DisabledScope(!canMove))
            {
                if (GUILayout.Button("确认并安全移动勾选资源", GUILayout.Height(38)) && EditorUtility.DisplayDialog(
                    "确认移动 Project 资源",
                    $"将移动 {_plan.Count(entry => entry.include)} 个资源。\n使用 AssetDatabase.MoveAsset 并验证 GUID 不变。\n\n移动后请提交对应 Git move 状态。",
                    "开始移动",
                    "取消"))
                {
                    ExecuteMove();
                }
            }

            using (new EditorGUI.DisabledScope(_lastHistory == null || _lastHistory.moves.Count == 0))
            {
                if (GUILayout.Button("反向恢复上次移动") && EditorUtility.DisplayDialog(
                    "恢复上次移动",
                    "将按 GUID 把上次移动的资源恢复到原路径。确定继续吗？",
                    "恢复",
                    "取消"))
                {
                    RestoreLastMove();
                }
            }
        }

        void RebuildPlan()
        {
            _plan.Clear();
            string prefabFolder = GetFolderPath(_prefabFolder);
            string materialFolder = GetFolderPath(_materialFolder);
            string textureFolder = GetFolderPath(_textureFolder);
            _plan.AddRange(SafeAssetMoveService.BuildPlan(
                GetPlanSources(),
                prefabFolder,
                materialFolder,
                textureFolder,
                _naming));
        }

        IEnumerable<UnityEngine.Object> GetPlanSources()
        {
            if (_sourceAssets.Count > 0)
                return _sourceAssets.Where(IsSupportedSourceAsset);
            return Selection.objects.Where(IsSupportedSourceAsset);
        }

        void AddSourceAsset(UnityEngine.Object asset)
        {
            if (!IsSupportedSourceAsset(asset) || _sourceAssets.Contains(asset)) return;
            _sourceAssets.Add(asset);
        }

        static bool IsSupportedSourceAsset(UnityEngine.Object asset)
        {
            if (asset == null) return false;
            string path = AssetDatabase.GetAssetPath(asset);
            if (string.IsNullOrEmpty(path) || AssetDatabase.IsValidFolder(path) || !path.StartsWith("Assets/", StringComparison.Ordinal))
                return false;
            return asset is GameObject || asset is Material || asset is Texture;
        }

        void ExecuteMove()
        {
            try
            {
                _lastHistory = new AssetMoveHistory();
                SafeAssetMoveService.Execute(_plan, _lastHistory);
                EditorUtility.DisplayDialog("移动完成", $"已安全移动 {_lastHistory.moves.Count} 个资源，GUID 验证通过。", "确定");
                _plan.Clear();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                bool rollbackSucceeded = _lastHistory == null || _lastHistory.moves.Count == 0;
                EditorUtility.DisplayDialog(
                    "移动中断",
                    exception.Message + (rollbackSucceeded
                        ? "\n\n本批次已由移动服务自动回滚到原路径。"
                        : "\n\n自动回滚未完成，历史已保留。请使用“反向恢复上次移动”或立即联系程序处理。"),
                    "确定");
            }
        }

        void RestoreLastMove()
        {
            try
            {
                SafeAssetMoveService.Restore(_lastHistory);
                EditorUtility.DisplayDialog("恢复完成", "上次移动的资源已恢复，GUID 验证通过。", "确定");
                _lastHistory = null;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("恢复中断", exception.Message, "确定");
            }
        }

        static DefaultAsset DrawFolder(string label, DefaultAsset folder)
        {
            var newValue = (DefaultAsset)EditorGUILayout.ObjectField(label, folder, typeof(DefaultAsset), false);
            if (newValue == null) return null;
            string path = AssetDatabase.GetAssetPath(newValue);
            if (AssetDatabase.IsValidFolder(path)) return newValue;
            EditorGUILayout.HelpBox($"{label} 只能选择文件夹，不能选择贴图、材质或 Prefab。", MessageType.Warning);
            return folder;
        }

        static string GetFolderPath(DefaultAsset folder)
        {
            return folder == null ? string.Empty : AssetDatabase.GetAssetPath(folder);
        }

        static void Ping(string path)
        {
            var asset = AssetDatabase.LoadMainAssetAtPath(path);
            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
        }
    }
}

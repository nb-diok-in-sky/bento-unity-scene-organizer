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
        DefaultAsset _otherFolder;
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
                "等价于在 Unity Project 窗口手动拖动：支持任意 Project 资产和整个文件夹。模型会自动带出直接使用的材质、贴图；其他内容原样移动，不扫描整个项目。",
                MessageType.Info);

            DrawSourceAssets();
            DrawFolders();
            DrawNaming();
            DrawPlanToolbar();
            DrawPlan();
            DrawActions();
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
                _otherFolder = DrawFolder("其他资产 / 文件夹", _otherFolder);
                if (EditorGUI.EndChangeCheck() && _plan.Count > 0) RefreshTargets();
            }
        }

        void DrawSourceAssets()
        {
            EditorGUILayout.LabelField("源资产", EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.HelpBox(
                    "拖入任意 Project 资产或整个文件夹。Prefab/FBX/OBJ 会自动加入直接引用的材质和贴图；其他内容按原样移动。",
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
                        foreach (var asset in ProjectSelection.CurrentAssets) AddSourceAsset(asset);
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
                if (EditorGUI.EndChangeCheck() && _plan.Count > 0) RefreshTargets();
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
                if (GUILayout.Button("导出移动前完整清单"))
                {
                    string path = AssetMoveManifestService.ExportPlan(_plan);
                    EditorUtility.DisplayDialog("清单已导出", $"移动前预览已保存：\n{path}", "确定");
                }
            }
            using (new EditorGUI.DisabledScope(!canMove))
            {
                if (GUILayout.Button("确认并安全移动勾选资源", GUILayout.Height(38)) && BentoCommand.Confirm(
                    "确认移动 Project 资源",
                    $"将移动 {_plan.Count(entry => entry.include)} 个资源。\n使用 AssetDatabase.MoveAsset 并验证 GUID 不变。\n\n移动后请提交对应 Git move 状态。",
                    "开始移动"))
                {
                    ExecuteMove();
                }
            }

            using (new EditorGUI.DisabledScope(_lastHistory == null || _lastHistory.moves.Count == 0))
            {

                if (GUILayout.Button("反向恢复上次移动") && BentoCommand.Confirm(
                    "恢复上次移动",
                    "将按 GUID 把上次移动的资源恢复到原路径。确定继续吗？",
                    "恢复"))
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
            string otherFolder = GetFolderPath(_otherFolder);
            _plan.AddRange(SafeAssetMoveService.BuildPlan(
                GetPlanSources(),
                prefabFolder,
                materialFolder,
                textureFolder,
                otherFolder,
                _naming));
        }

        void RefreshTargets()
        {
            var targetFolders = new Dictionary<AssetMoveKind, string>
            {
                [AssetMoveKind.PrefabOrModel] = GetFolderPath(_prefabFolder),
                [AssetMoveKind.Material] = GetFolderPath(_materialFolder),
                [AssetMoveKind.Texture] = GetFolderPath(_textureFolder),
                [AssetMoveKind.Folder] = GetFolderPath(_otherFolder),
                [AssetMoveKind.Other] = GetFolderPath(_otherFolder)
            };
            AssetMovePlanner.ApplyTargets(_plan, targetFolders, _naming);
        }

        IEnumerable<UnityEngine.Object> GetPlanSources()
        {
            if (_sourceAssets.Count > 0)
                return _sourceAssets.Where(IsSupportedSourceAsset);
            return ProjectSelection.SupportedAssets(IsSupportedSourceAsset);
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
            if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/", StringComparison.Ordinal))
                return false;
            return true;
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
            return BentoGUI.DrawProjectFolder(label, folder, out _);
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

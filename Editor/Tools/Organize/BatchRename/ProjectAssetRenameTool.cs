using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Bento
{
    sealed class ProjectAssetRenameTool
    {
        enum ProjectRenameMode
        {
            ReplaceName,
            AddAffixes
        }

        static readonly string[] RenameModeLabels =
        {
            "统一替换名称",
            "保留原名并增加前后缀"
        };

        readonly AssetNamingOptions _naming = new AssetNamingOptions { appendNumber = true };
        readonly List<UnityEngine.Object> _sources = new List<UnityEngine.Object>();
        readonly List<AssetRenameEntry> _plan = new List<AssetRenameEntry>();
        Vector2 _scroll;
        AssetRenameHistory _lastHistory;
        ProjectRenameMode _renameMode;
        string _completedSelectionSignature = string.Empty;

        public void Draw()
        {
            DrawInternal(false);
        }

        public void DrawAutomaticSelection()
        {
            RefreshFromSelection();
            DrawInternal(true);
        }

        void DrawInternal(bool automaticMode)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorGUILayout.HelpBox("Play Mode 中禁止重命名 Project 资产。", MessageType.Warning);
                return;
            }
            EditorGUILayout.LabelField("Project 资产批量重命名", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("通过 AssetDatabase.MoveAsset 安全改名，保持 .meta、GUID 和现有引用。禁止重命名文件夹、场景、脚本和 asmdef。", MessageType.Info);
            if (automaticMode)
                EditorGUILayout.LabelField($"当前 Project 选择：{_sources.Count} 个资产", EditorStyles.boldLabel);
            else
                DrawSources();
            if (automaticMode && !string.IsNullOrEmpty(_completedSelectionSignature))
                DrawCompletedSelectionNotice();
            EditorGUI.BeginChangeCheck();
            _renameMode = (ProjectRenameMode)EditorGUILayout.Popup(
                "命名方式",
                (int)_renameMode,
                RenameModeLabels);
            if (_renameMode == ProjectRenameMode.ReplaceName)
            {
                _naming.baseName = EditorGUILayout.TextField("统一基础名称", _naming.baseName);
                EditorGUILayout.LabelField(
                    "输出结构：前缀 + 统一基础名称 + 编号 + 后缀",
                    EditorStyles.wordWrappedMiniLabel);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "该模式会完整保留资产当前名称；再次执行会继续叠加前后缀和编号。",
                    MessageType.Warning);
            }
            BentoGUI.DrawNamingAffixesAndNumbering(_naming);
            if (EditorGUI.EndChangeCheck() && _sources.Count > 0) RebuildPlan();
            DrawPlan();
            DrawActions();
        }

        void RefreshFromSelection()
        {
            var current = ProjectSelection.CurrentAssets.Where(IsSupported)
                .OrderBy(AssetDatabase.GetAssetPath, StringComparer.OrdinalIgnoreCase).ToList();
            string currentSignature = BuildSelectionSignature(current);
            if (_sources.Count == 0 &&
                !string.IsNullOrEmpty(_completedSelectionSignature) &&
                string.Equals(currentSignature, _completedSelectionSignature, StringComparison.Ordinal))
                return;

            if (!string.Equals(currentSignature, _completedSelectionSignature, StringComparison.Ordinal))
                _completedSelectionSignature = string.Empty;

            var currentPaths = current.Select(AssetDatabase.GetAssetPath).ToArray();
            var sourcePaths = _sources.Select(AssetDatabase.GetAssetPath).ToArray();
            if (currentPaths.SequenceEqual(sourcePaths, StringComparer.OrdinalIgnoreCase)) return;
            _sources.Clear();
            _sources.AddRange(current);
            RebuildPlan();
        }

        void DrawSources()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("使用 Project 当前选择"))
                {
                    _sources.Clear();
                    _sources.AddRange(ProjectSelection.CurrentAssets.Where(IsSupported).OrderBy(AssetDatabase.GetAssetPath, StringComparer.OrdinalIgnoreCase));
                    RebuildPlan();
                }
                if (GUILayout.Button("添加所选文件夹下的资产")) AddSelectedFolderAssets();
                using (new EditorGUI.DisabledScope(_sources.Count == 0))
                    if (GUILayout.Button("清空")) { _sources.Clear(); _plan.Clear(); }
            }
            EditorGUILayout.LabelField($"已选择 {_sources.Count} 个资产", EditorStyles.boldLabel);
        }

        void AddSelectedFolderAssets()
        {
            var folders = Selection.objects.Select(AssetDatabase.GetAssetPath).Where(AssetDatabase.IsValidFolder).Distinct().ToArray();
            foreach (string folder in folders)
            foreach (string guid in AssetDatabase.FindAssets(string.Empty, new[] { folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadMainAssetAtPath(path);
                if (IsSupported(asset) && !_sources.Contains(asset)) _sources.Add(asset);
            }
            _sources.Sort((a, b) => string.Compare(AssetDatabase.GetAssetPath(a), AssetDatabase.GetAssetPath(b), StringComparison.OrdinalIgnoreCase));
            RebuildPlan();
        }

        void RebuildPlan()
        {
            _plan.Clear();
            for (int index = 0; index < _sources.Count; index++)
            {
                string path = AssetDatabase.GetAssetPath(_sources[index]);
                string original = System.IO.Path.GetFileNameWithoutExtension(path);
                string name = AssetNaming.BuildName(
                    original,
                    index,
                    BuildNamingOptions());
                _plan.Add(SafeAssetRenameService.BuildEntry(path, name));
            }
            SafeAssetRenameService.RefreshWarnings(_plan);
        }

        AssetNamingOptions BuildNamingOptions()
        {
            if (_renameMode == ProjectRenameMode.ReplaceName) return _naming;
            return new AssetNamingOptions
            {
                prefix = _naming.prefix,
                suffix = _naming.suffix,
                appendNumber = _naming.appendNumber,
                startNumber = _naming.startNumber,
                numberDigits = _naming.numberDigits
            };
        }

        void DrawPlan()
        {
            EditorGUILayout.LabelField($"重命名预览：{_plan.Count} 项", EditorStyles.boldLabel);
            _scroll = EditorGUILayout.BeginScrollView(_scroll, EditorStyles.helpBox, GUILayout.MinHeight(220), GUILayout.MaxHeight(650));
            foreach (var entry in _plan)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    entry.include = EditorGUILayout.ToggleLeft(entry.sourcePath, entry.include);
                    EditorGUILayout.LabelField("→ " + entry.targetPath, EditorStyles.miniLabel);
                    if (!string.IsNullOrEmpty(entry.warning)) EditorGUILayout.HelpBox(entry.warning, MessageType.Warning);
                    if (GUILayout.Button("定位资产", GUILayout.Width(80))) Ping(entry.sourcePath);
                }
            }
            EditorGUILayout.EndScrollView();
        }

        void DrawActions()
        {
            int count = _plan.Count(entry => entry.include);
            bool missingBaseName = _renameMode == ProjectRenameMode.ReplaceName &&
                                   string.IsNullOrWhiteSpace(_naming.baseName);
            if (missingBaseName)
                EditorGUILayout.HelpBox("统一替换名称模式必须填写统一基础名称。", MessageType.Warning);

            bool valid = !missingBaseName &&
                         count > 0 &&
                         _plan.Where(entry => entry.include).All(entry => string.IsNullOrEmpty(entry.warning));
            using (new EditorGUI.DisabledScope(!valid))
            {
                if (GUILayout.Button($"确认重命名 {count} 个资产", GUILayout.Height(38)) && BentoCommand.Confirm(
                    "确认重命名 Project 资产",
                    $"将通过 AssetDatabase.MoveAsset 重命名 {count} 个资产，并验证 GUID 不变。\n\n确定继续吗？",
                    "开始重命名"))
                    Execute();
            }
            using (new EditorGUI.DisabledScope(_lastHistory == null || _lastHistory.renames.Count == 0))
                if (GUILayout.Button("恢复上次资产重命名") && BentoCommand.Confirm("恢复资产名称", "将按 GUID 恢复上次重命名。", "恢复")) Restore();
        }

        void Execute()
        {
            string executedSelectionSignature = BuildSelectionSignature(_sources);
            BentoCommand.Run("资产重命名失败", () =>
            {
                _lastHistory = new AssetRenameHistory();
                SafeAssetRenameService.Execute(_plan, _lastHistory);
                var record = AssetRenameOperationHandler.CreateRecord(_lastHistory);
                bool historySaved = AssetOperationHistory.TryRecord(record, out string historyWarning);
                _sources.Clear();
                _plan.Clear();
                _completedSelectionSignature = executedSelectionSignature;
                EditorUtility.DisplayDialog(
                    "重命名完成",
                    $"已安全重命名 {_lastHistory.renames.Count} 个资产，GUID 验证通过。" +
                    (historySaved ? "\n操作历史已保存。" : "\n\n警告：" + historyWarning),
                    "确定");
            });
        }

        void Restore()
        {
            BentoCommand.Run("恢复重命名失败", () =>
            {
                SafeAssetRenameService.Restore(_lastHistory);
                EditorUtility.DisplayDialog("恢复完成", "上次资产重命名已按 GUID 恢复。", "确定");
                _lastHistory = null;
                _completedSelectionSignature = string.Empty;
            });
        }

        static bool IsSupported(UnityEngine.Object asset)
        {
            if (asset == null) return false;
            string path = AssetDatabase.GetAssetPath(asset);
            if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/", StringComparison.Ordinal) || AssetDatabase.IsValidFolder(path)) return false;
            return !path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase) &&
                   !path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) &&
                   !path.EndsWith(".asmdef", StringComparison.OrdinalIgnoreCase);
        }

        static void Ping(string path)
        {
            var asset = AssetDatabase.LoadMainAssetAtPath(path);
            if (asset != null)
                EditorGUIUtility.PingObject(asset);
        }

        void DrawCompletedSelectionNotice()
        {
            EditorGUILayout.HelpBox(
                "这批资产刚刚重命名完成，已暂停自动重新载入，防止同一规则再次叠加。",
                MessageType.Info);
            if (GUILayout.Button("重新载入当前 Project 选择"))
            {
                _completedSelectionSignature = string.Empty;
                RefreshFromSelection();
            }
        }

        static string BuildSelectionSignature(IEnumerable<UnityEngine.Object> assets)
        {
            return string.Join(
                "|",
                assets.Where(asset => asset != null)
                    .Select(AssetDatabase.GetAssetPath)
                    .Where(path => !string.IsNullOrEmpty(path))
                    .Select(AssetDatabase.AssetPathToGUID)
                    .Where(guid => !string.IsNullOrEmpty(guid))
                    .OrderBy(guid => guid, StringComparer.OrdinalIgnoreCase));
        }
    }
}

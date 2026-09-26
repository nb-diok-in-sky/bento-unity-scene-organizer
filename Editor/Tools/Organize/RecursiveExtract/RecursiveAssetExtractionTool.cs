using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Bento
{
    enum AssetExtractionType { Prefab, Material, Texture, FBX, CustomSuffix }

    [BentoPage("recursive_extract", BentoGroups.Organize, "递归抽取资产", Order = 70)]
    sealed class RecursiveAssetExtractionTool : IBentoTool
    {
        DefaultAsset _sourceFolder;
        DefaultAsset _targetFolder;
        AssetExtractionType _type = AssetExtractionType.Prefab;
        string _customSuffix = ".asset";
        List<AssetMoveEntry> _plan = new();
        Vector2 _scroll;
        AssetMoveHistory _history;
        string _message;

        public void Draw()
        {
            EditorGUILayout.LabelField("递归抽取并平铺资产", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("递归查找来源文件夹全部层级中的指定资产，并移动到一个目标文件夹。使用 Unity MoveAsset，GUID 与已有引用保持不变。", MessageType.Info);
            _sourceFolder = BentoGUI.DrawProjectFolder("来源总文件夹", _sourceFolder, out string sourceFolder);
            _targetFolder = BentoGUI.DrawProjectFolder("平铺目标文件夹", _targetFolder, out string targetFolder);
            _type = (AssetExtractionType)EditorGUILayout.EnumPopup("抽取类型", _type);
            if (_type == AssetExtractionType.CustomSuffix) _customSuffix = EditorGUILayout.TextField("自定义文件名结尾", _customSuffix);
            int selected = _plan.Count(entry => entry.include);
            bool valid = BentoUtility.IsProjectFolder(sourceFolder) && BentoUtility.IsProjectFolder(targetFolder) && !targetFolder.StartsWith(sourceFolder.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(!valid)) if (GUILayout.Button("① 递归扫描", GUILayout.Height(34))) Scan(sourceFolder, targetFolder);
                bool canMove = selected > 0 && _plan.Where(entry => entry.include).All(entry => string.IsNullOrEmpty(entry.warning));
                using (new EditorGUI.DisabledScope(!canMove)) if (GUILayout.Button(selected > 0 ? $"② 平铺移动 {selected} 个" : "② 移动（请先扫描）", GUILayout.Height(34))) Execute(sourceFolder, targetFolder, selected);
            }
            if (!valid && !string.IsNullOrEmpty(sourceFolder) && !string.IsNullOrEmpty(targetFolder)) EditorGUILayout.HelpBox("目标文件夹不能位于来源文件夹内部。", MessageType.Warning);
            if (!string.IsNullOrEmpty(_message)) EditorGUILayout.HelpBox(_message, _plan.Count > 0 ? MessageType.Info : MessageType.Warning);
            if (_plan.Count == 0) return;
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("全选无冲突项", GUILayout.Width(100))) foreach (var entry in _plan) entry.include = string.IsNullOrEmpty(entry.warning);
                if (GUILayout.Button("全不选", GUILayout.Width(70))) foreach (var entry in _plan) entry.include = false;
                if (GUILayout.Button("反选", GUILayout.Width(60))) foreach (var entry in _plan) entry.include = !entry.include;
                using (new EditorGUI.DisabledScope(_history == null || _history.moves.Count == 0))
                    if (GUILayout.Button("恢复上次移动", GUILayout.Width(100)) && BentoCommand.Confirm("恢复移动", "把上次抽取的资产恢复到原目录。", "恢复"))
                        BentoCommand.Run("恢复失败", () => { SafeAssetMoveService.Restore(_history); _history = null; Scan(sourceFolder, targetFolder); });
            }
            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.MinHeight(260), GUILayout.MaxHeight(620));
            foreach (var entry in _plan)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    entry.include = EditorGUILayout.ToggleLeft(entry.sourcePath, entry.include);
                    EditorGUILayout.LabelField("→ " + entry.targetPath, EditorStyles.miniLabel);
                    if (!string.IsNullOrEmpty(entry.warning)) EditorGUILayout.HelpBox(entry.warning, MessageType.Warning);
                }
            }
            EditorGUILayout.EndScrollView();
        }

        void Scan(string sourceFolder, string targetFolder)
        {
            var assets = AssetDatabase.FindAssets(string.Empty, new[] { sourceFolder }).Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => !AssetDatabase.IsValidFolder(path) && Matches(path)).Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase).Select(AssetDatabase.LoadMainAssetAtPath).Where(asset => asset != null).ToList();
            _plan = SafeAssetMoveService.BuildFlatExtractionPlan(assets, targetFolder);
            foreach (var group in _plan.GroupBy(entry => entry.targetPath, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1))
                foreach (var entry in group) { entry.warning = "多个来源文件平铺后同名冲突"; entry.include = false; }
            foreach (var entry in _plan.Where(entry => !string.IsNullOrEmpty(entry.warning))) entry.include = false;
            _message = _plan.Count > 0 ? $"找到 {_plan.Count} 个资产，可移动 {_plan.Count(entry => entry.include)} 个。" : "没有找到匹配资产。";
        }

        bool Matches(string path) => _type switch
        {
            AssetExtractionType.Prefab => path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase),
            AssetExtractionType.Material => path.EndsWith(".mat", StringComparison.OrdinalIgnoreCase),
            AssetExtractionType.Texture => new[] { ".png", ".tga", ".jpg", ".jpeg", ".tif", ".tiff", ".exr", ".psd" }.Any(ext => path.EndsWith(ext, StringComparison.OrdinalIgnoreCase)),
            AssetExtractionType.FBX => path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase),
            _ => !string.IsNullOrWhiteSpace(_customSuffix) && path.EndsWith(_customSuffix.Trim(), StringComparison.OrdinalIgnoreCase)
        };

        void Execute(string sourceFolder, string targetFolder, int selected)
        {
            if (!BentoCommand.Confirm("平铺移动资产", $"将把 {selected} 个资产从 {sourceFolder} 的各级子目录移动到 {targetFolder}。GUID 与引用保持不变。", "开始移动")) return;
            _history = new AssetMoveHistory();
            if (BentoCommand.Run("抽取移动失败", () => SafeAssetMoveService.Execute(_plan, _history), () => $"已移动 {_history.moves.Count} 个资产。")) Scan(sourceFolder, targetFolder);
        }
    }
}

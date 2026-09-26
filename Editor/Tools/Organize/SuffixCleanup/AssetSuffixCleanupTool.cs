using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Bento
{
    sealed class AssetSuffixCleanupEntry
    {
        public string path;
        public UnityEngine.Object asset;
        public bool include = true;
    }

    [BentoPage("suffix_cleanup", BentoGroups.Organize, "按后缀清理", Order = 60)]
    sealed class AssetSuffixCleanupTool : IBentoTool
    {
        DefaultAsset _folder;
        string _suffixes = ".max";
        List<AssetSuffixCleanupEntry> _entries = new();
        Vector2 _scroll;
        string _message;

        public void Draw()
        {
            EditorGUILayout.LabelField("按后缀批量清理资产", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("递归扫描所选 Project 文件夹的全部层级。每行填写一个文件名结尾，例如 .max、.psd、_LOD0.fbx。先预览，再删除。", MessageType.Warning);
            _folder = BentoGUI.DrawProjectFolder("扫描文件夹", _folder, out string folder);
            _suffixes = EditorGUILayout.TextArea(_suffixes, GUILayout.MinHeight(62));
            bool canScan = BentoUtility.IsProjectFolder(folder) && ParseSuffixes().Count > 0;
            int selected = _entries.Count(entry => entry.include);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(!canScan)) if (GUILayout.Button("① 递归扫描", GUILayout.Height(34))) Scan(folder);
                using (new EditorGUI.DisabledScope(selected == 0)) if (GUILayout.Button(selected > 0 ? $"② 删除 {selected} 个资产" : "② 删除（请先扫描）", GUILayout.Height(34))) DeleteSelected(folder, selected);
            }
            if (!string.IsNullOrEmpty(_message)) EditorGUILayout.HelpBox(_message, _entries.Count > 0 ? MessageType.Info : MessageType.Warning);
            if (_entries.Count == 0) return;
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("全选", GUILayout.Width(60))) foreach (var entry in _entries) entry.include = true;
                if (GUILayout.Button("全不选", GUILayout.Width(70))) foreach (var entry in _entries) entry.include = false;
                if (GUILayout.Button("反选", GUILayout.Width(60))) foreach (var entry in _entries) entry.include = !entry.include;
            }
            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.MinHeight(260), GUILayout.MaxHeight(620));
            foreach (var entry in _entries)
            {
                using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
                {
                    entry.include = EditorGUILayout.Toggle(entry.include, GUILayout.Width(18));
                    EditorGUILayout.ObjectField(entry.asset, typeof(UnityEngine.Object), false, GUILayout.Width(180));
                    EditorGUILayout.SelectableLabel(entry.path, EditorStyles.miniLabel, GUILayout.Height(18));
                    if (GUILayout.Button("定位", GUILayout.Width(44))) { Selection.activeObject = entry.asset; EditorGUIUtility.PingObject(entry.asset); }
                }
            }
            EditorGUILayout.EndScrollView();
        }

        void Scan(string folder)
        {
            List<string> suffixes = ParseSuffixes();
            _entries = AssetDatabase.FindAssets(string.Empty, new[] { folder }).Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => !AssetDatabase.IsValidFolder(path) && suffixes.Any(suffix => path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)))
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Select(path => new AssetSuffixCleanupEntry { path = path, asset = AssetDatabase.LoadMainAssetAtPath(path), include = true }).ToList();
            _message = _entries.Count > 0 ? $"扫描完成：找到 {_entries.Count} 个匹配资产。" : "扫描完成：没有找到匹配文件。不需要单独填写 .meta。";
        }

        void DeleteSelected(string folder, int selected)
        {
            var targets = _entries.Where(entry => entry.include).ToList();
            if (!BentoCommand.Confirm("删除匹配资产", $"将从 {folder} 及其子目录永久删除 {selected} 个资产。Unity 会同时删除对应 .meta。此操作不支持 Ctrl+Z。", "确认删除")) return;
            int deleted = 0;
            var failures = new List<string>();
            foreach (var entry in targets) { if (AssetDatabase.DeleteAsset(entry.path)) deleted++; else failures.Add(entry.path); }
            AssetDatabase.Refresh();
            Scan(folder);
            EditorUtility.DisplayDialog("清理完成", $"删除 {deleted}，失败 {failures.Count}。", "确定");
            foreach (string failure in failures) Debug.LogError("[Bento] 删除失败：" + failure);
        }

        List<string> ParseSuffixes() => (_suffixes ?? string.Empty)
            .Split(new[] { '\r', '\n', ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(value => value.Trim()).Where(value => !string.IsNullOrEmpty(value) && !value.Equals(".meta", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
}

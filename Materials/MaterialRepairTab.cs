using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Bento
{
    class MaterialRepairTab
    {
        static readonly Dictionary<MaterialRepairIssue, string> Labels = new Dictionary<MaterialRepairIssue, string>
        {
            { MaterialRepairIssue.TransparentMissingKeyword, "透明材质缺透明 keyword" },
            { MaterialRepairIssue.TransparentWrongBlend, "透明材质 Blend/ZWrite 错误" },
            { MaterialRepairIssue.CutoutMissingKeyword, "Cutout 材质缺 AlphaTest keyword" },
            { MaterialRepairIssue.CutoutWrongBlend, "Cutout 材质 Blend/ZWrite 错误" },
            { MaterialRepairIssue.OpaqueHasTransparentState, "不透明材质残留透明状态" },
            { MaterialRepairIssue.FadeShouldBeCutout, "Fade 材质建议修为 AlphaClip" },
            { MaterialRepairIssue.MissingNormalMapKeyword, "缺法线贴图 keyword" },
            { MaterialRepairIssue.MissingEmissionKeyword, "缺自发光 keyword" },
            { MaterialRepairIssue.MissingMetallicKeyword, "缺金属度 keyword" }
        };

        DefaultAsset _folderAsset;
        List<MaterialRepairEntry> _entries = new List<MaterialRepairEntry>();
        Vector2 _scroll;
        bool _scanned;
        public void Draw()
        {
            EditorGUILayout.LabelField("URP 材质修复", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("扫描 Standard→URP 转换后遗留的状态不一致问题", EditorStyles.miniLabel);
            _folderAsset = BentoGUI.DrawProjectFolder("扫描文件夹", _folderAsset, out string folder);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("扫描全部材质", GUILayout.Height(26))) Scan(() => MaterialRepairAnalyzer.ScanFolders("Assets"));
                using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(folder)))
                    if (GUILayout.Button("扫描文件夹", GUILayout.Height(26))) Scan(() => MaterialRepairAnalyzer.ScanFolders(folder));
                if (GUILayout.Button("扫描选中", GUILayout.Height(26))) Scan(() => MaterialRepairAnalyzer.ScanSelection(ProjectSelection.CurrentAssets));
            }
            DrawResults();
        }

        void Scan(System.Func<List<MaterialRepairEntry>> scan)
        {
            _entries = scan();
            _scanned = true;
            Debug.Log($"[MaterialRepair] 扫描完成: {_entries.Count} 个材质有问题");
        }

        void DrawResults()
        {
            if (!_scanned) return;
            int totalIssues = _entries.Sum(entry => entry.issues.Count);
            if (totalIssues == 0) { EditorGUILayout.HelpBox("未发现问题。", MessageType.Info); return; }
            EditorGUILayout.HelpBox($"发现 {_entries.Count} 个材质，共 {totalIssues} 个异常", MessageType.Warning);
            foreach (var group in _entries.SelectMany(entry => entry.issues).GroupBy(issue => issue).OrderByDescending(group => group.Count()))
                EditorGUILayout.LabelField($"× {group.Count()}　{Labels[group.Key]}", EditorStyles.miniLabel);

            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.MaxHeight(350));
            foreach (var entry in _entries)
            {
                using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
                {
                    entry.selected = EditorGUILayout.Toggle(entry.selected, GUILayout.Width(18));
                    EditorGUILayout.ObjectField(entry.material, typeof(Material), false, GUILayout.Width(180));
                    EditorGUILayout.LabelField(string.Join(" | ", entry.issues.Select(issue => Labels[issue])), EditorStyles.miniLabel);
                }
            }
            EditorGUILayout.EndScrollView();

            int selectedCount = _entries.Count(entry => entry.selected);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("全选", GUILayout.Width(50))) _entries.ForEach(entry => entry.selected = true);
                if (GUILayout.Button("全不选", GUILayout.Width(60))) _entries.ForEach(entry => entry.selected = false);
            }
            using (new EditorGUI.DisabledScope(selectedCount == 0))
                if (GUILayout.Button($"修复选中的 {selectedCount} 个材质", GUILayout.Height(32))) Repair();
        }

        void Repair()
        {
            int materialCount = _entries.Count(entry => entry.selected);
            int issueCount = 0;
            if (BentoCommand.Run("修复中断", () => issueCount = MaterialRepairService.Repair(_entries),
                    () => $"已修复 {materialCount} 个材质，共 {issueCount} 个问题。可通过 Ctrl+Z 撤销。"))
                _entries.RemoveAll(entry => entry.selected);
        }
    }
}

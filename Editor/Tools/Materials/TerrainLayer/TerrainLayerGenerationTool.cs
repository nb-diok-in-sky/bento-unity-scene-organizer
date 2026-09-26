using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Bento
{
    sealed class TerrainLayerGenerationTool
    {
        DefaultAsset _outputFolder;
        string _outputFolderPath = string.Empty;
        readonly List<TerrainLayerGenerationEntry> _entries = new List<TerrainLayerGenerationEntry>();
        Vector2 _tileSize = new Vector2(15f, 15f);
        Vector2 _scroll;

        public void Draw()
        {
            EditorGUILayout.LabelField("批量生成 Terrain Layer", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "在 Project 中选择贴图或文件夹。工具按名称分组：无后缀、_BC、_BaseColor 为基础色；_N、_Normal 为法线；_MRO、_MR、_Mask 为遮罩。只读取明确选择的范围。",
                MessageType.Info);

            _outputFolder = BentoGUI.DrawProjectFolder(
                "Terrain Layer 输出文件夹",
                _outputFolder,
                out _outputFolderPath);
            _tileSize = EditorGUILayout.Vector2Field("默认平铺尺寸", _tileSize);

            using (new EditorGUI.DisabledScope(!BentoUtility.IsProjectFolder(_outputFolderPath)))
                if (GUILayout.Button("扫描选中的贴图或文件夹", GUILayout.Height(32)))
                    Scan();

            if (_entries.Count == 0)
            {
                EditorGUILayout.HelpBox("尚未生成预览。请在 Project 中选择贴图或文件夹后扫描。", MessageType.Info);
                return;
            }

            int creatableCount = _entries.Count(entry =>
                entry.diffuse != null &&
                AssetDatabase.LoadAssetAtPath<TerrainLayer>(entry.outputPath) == null);
            EditorGUILayout.LabelField(
                $"识别 {_entries.Count} 组，可生成 {creatableCount} 个",
                EditorStyles.boldLabel);

            _scroll = EditorGUILayout.BeginScrollView(_scroll, EditorStyles.helpBox, GUILayout.MinHeight(220));
            foreach (TerrainLayerGenerationEntry entry in _entries)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField(entry.name, EditorStyles.boldLabel);
                    EditorGUILayout.ObjectField("基础色", entry.diffuse, typeof(Texture2D), false);
                    EditorGUILayout.ObjectField("法线", entry.normal, typeof(Texture2D), false);
                    EditorGUILayout.ObjectField("遮罩", entry.mask, typeof(Texture2D), false);
                    EditorGUILayout.LabelField(entry.outputPath, EditorStyles.miniLabel);
                    if (!string.IsNullOrEmpty(entry.warning))
                        EditorGUILayout.HelpBox(entry.warning, MessageType.Warning);
                }
            }
            EditorGUILayout.EndScrollView();

            using (new EditorGUI.DisabledScope(creatableCount == 0))
                if (GUILayout.Button($"生成 Terrain Layer（{creatableCount}）", GUILayout.Height(38)))
                    Generate();
        }

        void Scan()
        {
            _entries.Clear();
            _entries.AddRange(TerrainLayerGenerationService.BuildFromSelection(_outputFolderPath));
            Debug.Log($"[Bento] Terrain Layer 扫描完成：识别 {_entries.Count} 组贴图。");
        }

        void Generate()
        {
            List<TerrainLayer> created = TerrainLayerGenerationService.Generate(_entries, _tileSize);
            Selection.objects = created.Cast<UnityEngine.Object>().ToArray();
            if (created.Count > 0) EditorGUIUtility.PingObject(created[0]);
            Debug.Log($"[Bento] 已生成 {created.Count} 个 Terrain Layer，原贴图未移动或修改。");
            Scan();
        }
    }
}

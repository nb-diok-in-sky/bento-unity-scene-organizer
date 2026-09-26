using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Bento
{
    [BentoPage("asset_info", BentoGroups.Organize, "资产信息", Order = 50)]
    class AssetInfoExportTool : IBentoTool
    {
        AssetInfoExportDocument _document;
        Vector2 _scroll;

        public void Draw()
        {
            EditorGUILayout.LabelField("资产信息导出", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("在 Project 中选择 Prefab、FBX 或文件夹。工具会递归读取子物体信息，只读分析，不修改资产。", MessageType.Info);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("分析 Project 选择", GUILayout.Height(34))) Analyze();
                using (new EditorGUI.DisabledScope(_document == null || _document.assets.Count == 0))
                    if (GUILayout.Button("导出 JSON", GUILayout.Height(34))) Export();
            }
            DrawResults();
        }

        void Analyze()
        {
            _document = AssetInfoCollector.Collect(ProjectSelection.CurrentAssets);
            if (_document.assets.Count == 0)
                EditorUtility.DisplayDialog("没有可分析资产", "请在 Project 窗口选择 Prefab、FBX 或包含它们的文件夹。", "确定");
        }

        void Export()
        {
            string path = EditorUtility.SaveFilePanel("导出资产信息 JSON", Application.dataPath, "资产数据表", "json");
            if (string.IsNullOrEmpty(path)) return;
            BentoCommand.Run("导出失败", () =>
            {
                AssetInfoJsonExporter.Export(_document, path);
                EditorUtility.RevealInFinder(path);
            });
        }

        void DrawResults()
        {
            if (_document == null) return;
            EditorGUILayout.LabelField($"已分析 {_document.assets.Count} 个资产", EditorStyles.boldLabel);
            _scroll = EditorGUILayout.BeginScrollView(_scroll, EditorStyles.helpBox);
            foreach (var asset in _document.assets)
            {
                EditorGUILayout.LabelField($"● {asset.originalName}", EditorStyles.boldLabel);
                EditorGUILayout.LabelField($"节点 {asset.nodeCount} / Renderer {asset.rendererCount} / 材质 {asset.materialCount} / 贴图 {asset.textureCount} / Collider {asset.colliderCount}", EditorStyles.miniLabel);
                EditorGUILayout.LabelField($"尺寸 {asset.boundsSize.x:0.###} × {asset.boundsSize.y:0.###} × {asset.boundsSize.z:0.###}", EditorStyles.miniLabel);
                foreach (var node in asset.nodes)
                    EditorGUILayout.LabelField($"　{node.hierarchyPath}　Mesh={node.mesh}　Material={node.materials.Count}", EditorStyles.miniLabel);
            }
            EditorGUILayout.EndScrollView();
        }
    }
}

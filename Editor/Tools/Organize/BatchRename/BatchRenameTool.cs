using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Bento
{
    [BentoPage("batch_rename", BentoGroups.Organize, "批量重命名", Order = 20)]
    sealed class BatchRenameTool : IBentoTool
    {
        readonly HierarchyRenameTool _hierarchyTool = new HierarchyRenameTool();
        readonly ProjectAssetRenameTool _projectTool = new ProjectAssetRenameTool();

        public void Draw()
        {
            bool hasSceneObjects = Selection.gameObjects.Any(gameObject => gameObject != null && gameObject.scene.IsValid() && gameObject.scene.isLoaded);
            bool hasProjectAssets = Selection.objects.Any(asset => asset != null && !string.IsNullOrEmpty(AssetDatabase.GetAssetPath(asset)));

            EditorGUILayout.LabelField("批量重命名", EditorStyles.boldLabel);
            if (hasSceneObjects && hasProjectAssets)
            {
                EditorGUILayout.HelpBox("检测到 Hierarchy 场景对象与 Project 资产混合选择。为避免误操作，请只保留一种选择后再重命名。", MessageType.Error);
                DrawModeSummary("混合选择（禁止执行）", hasSceneObjects, hasProjectAssets);
                return;
            }
            if (hasProjectAssets)
            {
                DrawModeSummary("Project 资产模式", false, true);
                _projectTool.DrawAutomaticSelection();
                return;
            }
            if (hasSceneObjects)
            {
                DrawModeSummary("Hierarchy 场景对象模式", true, false);
                _hierarchyTool.Draw();
                return;
            }

            EditorGUILayout.HelpBox("请在 Hierarchy 或 Project 窗口选择要重命名的对象。工具会自动判断模式。", MessageType.Info);
            DrawModeSummary("等待选择", false, false);
        }

        static void DrawModeSummary(string mode, bool scene, bool project)
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("当前模式", GUILayout.Width(70));
                EditorGUILayout.LabelField(mode, EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField(scene ? "Hierarchy ✓" : "Hierarchy —", EditorStyles.miniLabel, GUILayout.Width(85));
                EditorGUILayout.LabelField(project ? "Project ✓" : "Project —", EditorStyles.miniLabel, GUILayout.Width(75));
            }
        }
    }
}

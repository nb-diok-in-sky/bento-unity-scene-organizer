using System.IO;
using System.Linq;
using MCPForUnity.Editor.Setup;
using MCPForUnity.Editor.Services;
using MCPForUnity.Editor.Windows;
using UnityEditor;
using UnityEngine;

namespace Bento
{
    class McpIntegrationTool
    {
        static readonly string[] DangerousTools = { "execute_code", "manage_asset", "manage_script", "execute_menu_item" };
        static readonly string[] ReadOnlyTools = { "find_gameobjects", "read_console" };
        const string McpPackageId = "com.coplaydev.mcp-for-unity";
        static readonly string McpConfigPath = Path.GetFullPath(Path.Combine(Application.dataPath, "../.mcp.json"));

        public void Draw()
        {
            EditorGUILayout.LabelField("AI / MCP 集成", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "MCP for Unity 负责让外部 AI 客户端控制 Unity。Bento通过自定义 bento 命令向 AI 暴露安全的查询与干运行能力。",
                MessageType.Info);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawStatus("MCP Unity 包", IsMcpPackageInstalled(), $"{McpPackageId} v10.1.0");
                DrawStatus("本机 MCP 配置", File.Exists(McpConfigPath), ".mcp.json（已加入 Git 忽略）");
                DrawStatus("Bento AI 工具", IsBentoMcpToolAvailable(), "bento");
            }

            DrawSecuritySection();

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("AI 只读测试能力", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox(
                    "可开启官方 find_gameobjects / read_console，并使用 Bento 的场景健康检查。这些接口不创建、不删除、不移动、不保存场景。",
                    MessageType.None);
                if (GUILayout.Button("开启推荐的只读 MCP 工具"))
                    foreach (string toolName in ReadOnlyTools)
                        MCPServiceLocator.ToolDiscovery.SetToolEnabled(toolName, true);
            }

            EditorGUILayout.Space(6);
            if (GUILayout.Button("打开 MCP for Unity 主窗口", GUILayout.Height(34)))
                MCPForUnityEditorWindow.ShowWindow();
            if (GUILayout.Button("打开 MCP 本地配置向导", GUILayout.Height(30)))
                SetupWindowService.ShowSetupWindow();

            EditorGUILayout.Space(6);
            EditorGUILayout.HelpBox(
                "AI 可调用：读取 Hierarchy/Project 选择、场景健康检查、资源变体干运行、安全移动计划分析。\n" +
                "生成、移动、删除 Project 资源仍需你在 Unity 窗口内确认，AI 没有直接执行接口。",
                MessageType.None);
        }

        static void DrawSecuritySection()
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("AI 权限安全", EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                var enabledDangerousTools = DangerousTools
                    .Where(toolName => MCPServiceLocator.ToolDiscovery.IsToolEnabled(toolName))
                    .ToArray();
                if (enabledDangerousTools.Length == 0)
                {
                    EditorGUILayout.HelpBox("高危通用工具已关闭。AI 只能使用当前启用的 MCP 工具；Bento 不提供生成、移动或删除执行接口。", MessageType.Info);
                }
                else
                {
                    EditorGUILayout.HelpBox(
                        "以下高危通用 MCP 工具已启用，可绕过 Bento 限制：\n" + string.Join("、", enabledDangerousTools),
                        MessageType.Error);
                    if (GUILayout.Button("一键关闭高危通用工具"))
                    {
                        foreach (string toolName in enabledDangerousTools)
                            MCPServiceLocator.ToolDiscovery.SetToolEnabled(toolName, false);
                    }
                }
            }
        }

        static void DrawStatus(string label, bool ok, string detail)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(ok ? "●" : "○", GUILayout.Width(20));
                EditorGUILayout.LabelField(label, GUILayout.Width(120));
                EditorGUILayout.LabelField(detail, EditorStyles.miniLabel);
            }
        }

        static bool IsMcpPackageInstalled()
        {
            return UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/" + McpPackageId) != null;
        }

        static bool IsBentoMcpToolAvailable()
        {
            return typeof(BentoMcpTool) != null;
        }
    }
}

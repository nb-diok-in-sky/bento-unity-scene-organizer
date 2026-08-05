using System;
using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;

namespace Bento
{
    [InitializeOnLoad]
    static class BentoOptionalDependencySettings
    {
        const string FbxSymbol = "BENTO_ENABLE_FBX_EXPORTER";
        const string McpSymbol = "BENTO_ENABLE_MCP_FOR_UNITY";
        const string FbxPackage = "com.unity.formats.fbx";
        const string McpPackage = "com.coplaydev.unity-mcp";

        static BentoOptionalDependencySettings()
        {
            EditorApplication.delayCall += () => Synchronize(showDialog: false);
        }

        [MenuItem("Tools/Bento/刷新可选依赖模块", priority = -9900)]
        public static void Synchronize() => Synchronize(showDialog: true);

        static void Synchronize(bool showDialog)
        {
            bool hasFbx = IsInstalled(FbxPackage);
            bool hasMcp = IsInstalled(McpPackage);
            string[] symbols = PlayerSettings.GetScriptingDefineSymbolsForGroup(EditorUserBuildSettings.selectedBuildTargetGroup)
                .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(value => value.Trim())
                .Where(value => !string.IsNullOrEmpty(value))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            var next = symbols
                .Where(symbol => symbol != FbxSymbol && symbol != McpSymbol)
                .ToList();
            if (hasFbx) next.Add(FbxSymbol);
            if (hasMcp) next.Add(McpSymbol);
            string serialized = string.Join(";", next);
            string current = string.Join(";", symbols);
            if (!string.Equals(current, serialized, StringComparison.Ordinal))
            {
                PlayerSettings.SetScriptingDefineSymbolsForGroup(
                    EditorUserBuildSettings.selectedBuildTargetGroup,
                    serialized);
                AssetDatabase.Refresh();
            }

            if (showDialog)
            {
                EditorUtility.DisplayDialog(
                    "Bento 可选依赖",
                    $"Unity FBX Exporter：{(hasFbx ? "已启用" : "未安装，页面隐藏")}\n" +
                    $"MCP for Unity：{(hasMcp ? "已启用" : "未安装，页面隐藏")}",
                    "确定");
            }
        }

        static bool IsInstalled(string packageName)
        {
            return UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages()
                .Any(package => string.Equals(package.name, packageName, StringComparison.Ordinal));
        }
    }
}

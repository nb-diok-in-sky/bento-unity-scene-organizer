using UnityEditor;
using UnityEngine;

namespace Bento
{
    public class BentoWindow : EditorWindow
    {
        int _mainTab;
        int _materialTab;
        SharedContext _ctx;
        ShaderReplaceTab _shaderTab;
        TextureScanTab _scanTab;
        TextureRenameTab _renameTab;
        MaterialFillTab _fillTab;
        TagLayerTab _tagLayerTab;
        AutoProcessTab _autoTab;
        MaterialRepairTab _repairTab;
        PrefabReplacer _prefabReplacer;
        HierarchyRenameTool _renameTool;
        ProjectFolderFavoritesTool _folderFavoritesTool;
        ResourceVariantTool _resourceVariantTool;
        McpIntegrationTool _mcpIntegrationTool;
        SafeAssetMoveTool _safeAssetMoveTool;

        static readonly string[] MainTabNames = { "材质工具", "预制体替换", "资源变体", "资产移动", "批量重命名", "文件夹收藏", "AI/MCP" };
        static readonly string[] MaterialTabNames = { "Shader 替换", "贴图扫描", "贴图改名", "填充材质球", "Tag/Layer", "一键处理", "材质修复" };

        [MenuItem("Tools/Bento/工具箱")]
        static void Open()
        {
            var w = GetWindow<BentoWindow>("Bento");
            w.minSize = new Vector2(620, 520);
        }

        public static BentoWindow OpenPage(string pageName)
        {
            var window = GetWindow<BentoWindow>("Bento");
            window.minSize = new Vector2(620, 520);
            int pageIndex = System.Array.FindIndex(MainTabNames, name =>
                string.Equals(name, pageName, System.StringComparison.OrdinalIgnoreCase));
            if (pageIndex >= 0)
                window._mainTab = pageIndex;
            window.Show();
            window.Focus();
            window.Repaint();
            return window;
        }

        public string CurrentPageName => MainTabNames[Mathf.Clamp(_mainTab, 0, MainTabNames.Length - 1)];

        public object AnalyzeResourceVariantSelection(AssetNamingOptions naming = null)
        {
            return _resourceVariantTool.AnalyzeSelectionForMcp(naming);
        }

        public object AnalyzeAssetMoveSelection(
            string prefabFolder = null,
            string materialFolder = null,
            string textureFolder = null,
            AssetNamingOptions naming = null)
        {
            return _safeAssetMoveTool.AnalyzeSelectionForMcp(prefabFolder, materialFolder, textureFolder, naming);
        }

        void OnEnable()
        {
            _ctx = new SharedContext();
            _shaderTab = new ShaderReplaceTab();
            _scanTab = new TextureScanTab(_ctx);
            _renameTab = new TextureRenameTab(_ctx);
            _fillTab = new MaterialFillTab(_ctx);
            _tagLayerTab = new TagLayerTab();
            _autoTab = new AutoProcessTab(_ctx);
            _repairTab = new MaterialRepairTab();
            _prefabReplacer = new PrefabReplacer();
            _renameTool = new HierarchyRenameTool();
            _folderFavoritesTool = new ProjectFolderFavoritesTool();
            _resourceVariantTool = new ResourceVariantTool();
            _mcpIntegrationTool = new McpIntegrationTool();
            _safeAssetMoveTool = new SafeAssetMoveTool();
            _scanTab.OnEnable();
        }

        void OnSelectionChange() => Repaint();

        void OnGUI()
        {
            _mainTab = GUILayout.Toolbar(_mainTab, MainTabNames, GUILayout.Height(26));
            EditorGUILayout.Space(6);
            switch (_mainTab)
            {
                case 0: DrawMaterialTools(); break;
                case 1: _prefabReplacer.Draw(); break;
                case 2: _resourceVariantTool.Draw(); break;
                case 3: _safeAssetMoveTool.Draw(); break;
                case 4: _renameTool.Draw(); break;
                case 5: _folderFavoritesTool.Draw(); break;
                case 6: _mcpIntegrationTool.Draw(); break;
            }
        }

        void DrawMaterialTools()
        {
            _materialTab = GUILayout.Toolbar(_materialTab, MaterialTabNames);
            EditorGUILayout.Space(4);
            switch (_materialTab)
            {
                case 0: _shaderTab.Draw(); break;
                case 1: _scanTab.Draw(); break;
                case 2: _renameTab.Draw(); break;
                case 3: _fillTab.Draw(); break;
                case 4: _tagLayerTab.Draw(); break;
                case 5: _autoTab.Draw(); break;
                case 6: _repairTab.Draw(); break;
            }
        }
    }

}

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Bento
{
    public class BentoWindow : EditorWindow
    {
        int _mainTab;
        int _groupTab;
        int _materialTab;
        List<IBentoPage> _pages;
        string[] _pageNames;
        string[] _groupNames;
        List<IBentoPage> _materialPages;
        string[] _materialPageNames;
        Vector2 _pageScroll;

        [MenuItem("Tools/Bento/工具箱")]
        static void Open() => OpenPage(null);

        public static BentoWindow OpenPage(string pageName)
        {
            var window = GetWindow<BentoWindow>("Bento");
            window.minSize = new Vector2(620, 520);
            window.EnsurePages();
            int index = Array.FindIndex(window._pageNames, name =>
                string.Equals(name, pageName, StringComparison.OrdinalIgnoreCase));
            if (index < 0) index = window._pages.FindIndex(page =>
                string.Equals(page.Id, pageName, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                window._mainTab = index;
                window._groupTab = Array.IndexOf(window._groupNames, window._pages[index].Group);
            }
            window.Show();
            window.Focus();
            window.Repaint();
            return window;
        }

        public string CurrentPageName
        {
            get
            {
                EnsurePages();
                return _pageNames[Mathf.Clamp(_mainTab, 0, _pageNames.Length - 1)];
            }
        }

        void OnEnable() => BuildPages();
        void OnSelectionChange() => Repaint();

        void OnGUI()
        {
            EnsurePages();
            bool compact = position.width < 760f;
            if (compact)
            {
                DrawCompactNavigation();
                EditorGUILayout.Space(4);
                DrawCurrentPage();
            }
            else
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    DrawNavigation();
                    DrawCurrentPage();
                }
            }
        }

        void DrawCurrentPage()
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true)))
            {
                EditorGUILayout.LabelField(CurrentPageName, EditorStyles.largeLabel);
                EditorGUILayout.Space(4);
                _pageScroll = EditorGUILayout.BeginScrollView(_pageScroll, GUILayout.ExpandHeight(true));
                _pages[Mathf.Clamp(_mainTab, 0, _pages.Count - 1)].Draw();
                EditorGUILayout.EndScrollView();
            }
        }

        void DrawCompactNavigation()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                int nextGroup = EditorGUILayout.Popup(_groupTab, _groupNames, EditorStyles.toolbarPopup, GUILayout.Width(130));
                if (nextGroup != _groupTab)
                {
                    _groupTab = nextGroup;
                    _mainTab = _pages.FindIndex(page => page.Group == _groupNames[_groupTab]);
                    _pageScroll = Vector2.zero;
                }
                var groupPages = _pages.Select((page, index) => new { page, index })
                    .Where(item => item.page.Group == _groupNames[_groupTab]).ToArray();
                int localIndex = Array.FindIndex(groupPages, item => item.index == _mainTab);
                int nextPage = EditorGUILayout.Popup(Mathf.Max(0, localIndex), groupPages.Select(item => item.page.Name).ToArray(), EditorStyles.toolbarPopup);
                if (nextPage >= 0 && nextPage < groupPages.Length && groupPages[nextPage].index != _mainTab)
                {
                    _mainTab = groupPages[nextPage].index;
                    _pageScroll = Vector2.zero;
                }
            }
        }

        void BuildPages()
        {
            var context = new SharedContext();
            var scanTab = new TextureScanTab(context);
            scanTab.OnEnable();
            _materialPages = new List<IBentoPage>
            {
                Page("材质", "shader_replace", "Shader 替换", new ShaderReplaceTab().Draw),
                Page("材质", "texture_scan", "贴图扫描", scanTab.Draw),
                Page("材质", "texture_rename", "贴图改名", new TextureRenameTab(context).Draw),
                Page("材质", "material_fill", "填充材质球", new MaterialFillTab(context).Draw),
                Page("材质", "tag_layer", "Tag/Layer", new TagLayerTab().Draw),
                Page("材质", "auto_process", "一键处理", new AutoProcessTab(context).Draw),
                Page("材质", "material_repair", "材质修复", new MaterialRepairTab().Draw)
            };
            _materialPageNames = _materialPages.Select(page => page.Name).ToArray();

            _pages = new List<IBentoPage>
            {
                Page("材质", "material_tools", "材质工具", DrawMaterialTools),
                Page("场景", "hierarchy_organization", "Hierarchy 整理", new HierarchyOrganizationTool().Draw),
                Page("场景", "prefab_replace", "预制体替换", new PrefabReplacer().Draw),
                Page("场景", "hierarchy_rename", "批量重命名", new HierarchyRenameTool().Draw),
                Page("资源", "resource_variant", "资源变体", new ResourceVariantTool().Draw),
                Page("资源", "asset_move", "资产移动", new SafeAssetMoveTool().Draw),
                Page("资源", "ai_asset_organizer", "AI资产整理", new AIAssetOrganizerTool().Draw),
                Page("资源", "asset_info", "资产信息", new AssetInfoExportTool().Draw),
                Page("资源", "prefab_layer", "Prefab Layer", new PrefabLayerTool().Draw),
                Page("资源", "folder_favorites", "文件夹收藏", new ProjectFolderFavoritesTool().Draw),
                Page("AI与设置", "ai_mcp", "AI/MCP", new McpIntegrationTool().Draw)
            };
            _pageNames = _pages.Select(page => page.Name).ToArray();
            _groupNames = _pages.Select(page => page.Group).Distinct().ToArray();
        }

        void DrawMaterialTools()
        {
            _materialTab = GUILayout.Toolbar(_materialTab, _materialPageNames);
            EditorGUILayout.Space(4);
            _materialPages[Mathf.Clamp(_materialTab, 0, _materialPages.Count - 1)].Draw();
        }

        void EnsurePages()
        {
            if (_pages == null || _pages.Count == 0) BuildPages();
        }

        void DrawNavigation()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.Width(150)))
            {
                EditorGUILayout.LabelField("Bento", EditorStyles.boldLabel);
                for (int groupIndex = 0; groupIndex < _groupNames.Length; groupIndex++)
                {
                    string group = _groupNames[groupIndex];
                    bool selectedGroup = groupIndex == _groupTab;
                    if (GUILayout.Toggle(selectedGroup, group, EditorStyles.toolbarButton))
                    {
                        if (!selectedGroup)
                        {
                            _groupTab = groupIndex;
                            _mainTab = _pages.FindIndex(page => page.Group == group);
                            _pageScroll = Vector2.zero;
                        }
                    }
                    if (_groupTab != groupIndex) continue;
                    foreach (int pageIndex in _pages.Select((page, index) => new { page, index }).Where(item => item.page.Group == group).Select(item => item.index))
                        if (GUILayout.Toggle(_mainTab == pageIndex, "  " + _pages[pageIndex].Name, EditorStyles.miniButton) && _mainTab != pageIndex)
                        {
                            _mainTab = pageIndex;
                            _pageScroll = Vector2.zero;
                        }
                    EditorGUILayout.Space(4);
                }
            }
        }

        static IBentoPage Page(string group, string id, string name, Action draw) => new BentoPage(group, id, name, draw);
    }
}

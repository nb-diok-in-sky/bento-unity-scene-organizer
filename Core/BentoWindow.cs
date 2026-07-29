using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Bento
{
    public class BentoWindow : EditorWindow
    {
        const float CompactWidth = 860f;
        const float NavigationWidth = 220f;
        const float GroupButtonHeight = 34f;
        const float PageButtonHeight = 30f;

        int _mainTab;
        int _groupTab;
        List<IBentoPage> _pages;
        string[] _pageNames;
        string[] _groupNames;
        IBentoPage _activePage;
        Vector2 _pageScroll;
        string _pageError;
        GUIStyle _titleStyle;
        GUIStyle _headingStyle;
        GUIStyle _groupStyle;
        GUIStyle _pageStyle;
        GUIStyle _popupStyle;

        [MenuItem("Tools/Bento/工具箱", priority = -10000)]
        static void Open() => OpenPage(null);

        public static BentoWindow OpenPage(string pageName)
        {
            if (string.Equals(pageName, "hierarchy_rename", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(pageName, "project_asset_rename", StringComparison.OrdinalIgnoreCase))
                pageName = "batch_rename";
            var window = GetWindow<BentoWindow>("Bento");
            window.minSize = new Vector2(760, 620);
            window.EnsurePages();
            int index = Array.FindIndex(window._pageNames, name =>
                string.Equals(name, pageName, StringComparison.OrdinalIgnoreCase));
            if (index < 0) index = window._pages.FindIndex(page =>
                string.Equals(page.Id, pageName, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                window.SelectPage(index);
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
        void OnDisable() => NotifyLifecycle(_activePage, lifecycle => lifecycle.OnDeactivated(), "停用");
        void OnSelectionChange()
        {
            NotifyLifecycle(_activePage, lifecycle => lifecycle.OnSelectionChanged(), "响应选中变化");
            Repaint();
        }

        void OnGUI()
        {
            EnsurePages();
            bool compact = position.width < CompactWidth;
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
                EditorGUILayout.LabelField(CurrentPageName, TitleStyle, GUILayout.Height(28));
                EditorGUILayout.Space(8);
                _pageScroll = EditorGUILayout.BeginScrollView(_pageScroll, GUILayout.ExpandHeight(true));
                try
                {
                    if (string.IsNullOrEmpty(_pageError))
                    {
                        try
                        {
                            _pages[Mathf.Clamp(_mainTab, 0, _pages.Count - 1)].Draw();
                        }
                        catch (Exception exception)
                        {
                            _pageError = exception.ToString();
                            Debug.LogException(exception);
                        }
                    }
                    else
                    {
                        EditorGUILayout.HelpBox("当前页面绘制中断，已停止重复执行以避免 GUILayout 状态持续损坏。", MessageType.Error);
                        EditorGUILayout.TextArea(_pageError, GUILayout.MinHeight(120));
                        if (GUILayout.Button("重试当前页面")) _pageError = null;
                    }
                }
                finally
                {
                    EditorGUILayout.EndScrollView();
                }
            }
        }

        void DrawCompactNavigation()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                int nextGroup = EditorGUILayout.Popup(_groupTab, _groupNames, PopupStyle, GUILayout.Width(180), GUILayout.Height(28));
                if (nextGroup != _groupTab)
                {
                    _groupTab = nextGroup;
                    SelectPage(_pages.FindIndex(page => page.Group == _groupNames[_groupTab]));
                }
                var groupPages = _pages.Select((page, index) => new { page, index })
                    .Where(item => item.page.Group == _groupNames[_groupTab]).ToArray();
                int localIndex = Array.FindIndex(groupPages, item => item.index == _mainTab);
                int nextPage = EditorGUILayout.Popup(Mathf.Max(0, localIndex), groupPages.Select(item => item.page.Name).ToArray(), PopupStyle, GUILayout.Height(28));
                if (nextPage >= 0 && nextPage < groupPages.Length && groupPages[nextPage].index != _mainTab)
                {
                    SelectPage(groupPages[nextPage].index);
                }
            }
        }

        void BuildPages()
        {
            NotifyLifecycle(_activePage, lifecycle => lifecycle.OnDeactivated(), "重建前停用");
            var catalog = new BentoCatalog();
            _pages = catalog.Pages.ToList();
            _pageNames = _pages.Select(page => page.Name).ToArray();
            _groupNames = _pages.Select(page => page.Group).Distinct().ToArray();
            _mainTab = Mathf.Clamp(_mainTab, 0, _pages.Count - 1);
            _activePage = _pages[_mainTab];
            NotifyLifecycle(_activePage, lifecycle => lifecycle.OnActivated(), "激活");
        }

        void EnsurePages()
        {
            if (_pages == null || _pages.Count == 0) BuildPages();
        }

        void DrawNavigation()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.Width(NavigationWidth)))
            {
                EditorGUILayout.LabelField("Bento", HeadingStyle, GUILayout.Height(28));
                EditorGUILayout.Space(4);
                for (int groupIndex = 0; groupIndex < _groupNames.Length; groupIndex++)
                {
                    string group = _groupNames[groupIndex];
                    bool selectedGroup = groupIndex == _groupTab;
                    if (GUILayout.Toggle(selectedGroup, group, GroupStyle, GUILayout.Height(GroupButtonHeight)))
                    {
                        if (!selectedGroup)
                        {
                            SelectPage(_pages.FindIndex(page => page.Group == group));
                        }
                    }
                    if (_groupTab != groupIndex) continue;
                    foreach (int pageIndex in _pages.Select((page, index) => new { page, index }).Where(item => item.page.Group == group).Select(item => item.index))
                        if (GUILayout.Toggle(_mainTab == pageIndex, _pages[pageIndex].Name, PageStyle, GUILayout.Height(PageButtonHeight)) && _mainTab != pageIndex)
                        {
                            SelectPage(pageIndex);
                        }
                    EditorGUILayout.Space(8);
                }
            }
        }

        GUIStyle TitleStyle => _titleStyle ?? (_titleStyle = new GUIStyle(EditorStyles.largeLabel)
        {
            fontSize = 18,
            fontStyle = FontStyle.Bold
        });

        GUIStyle HeadingStyle => _headingStyle ?? (_headingStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 16
        });

        GUIStyle GroupStyle => _groupStyle ?? (_groupStyle = new GUIStyle(EditorStyles.toolbarButton)
        {
            fontSize = 14,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft,
            padding = new RectOffset(12, 8, 4, 4)
        });

        GUIStyle PageStyle => _pageStyle ?? (_pageStyle = new GUIStyle(EditorStyles.miniButton)
        {
            fontSize = 13,
            alignment = TextAnchor.MiddleLeft,
            padding = new RectOffset(20, 8, 3, 3)
        });

        GUIStyle PopupStyle => _popupStyle ?? (_popupStyle = new GUIStyle(EditorStyles.toolbarPopup)
        {
            fontSize = 13,
            fixedHeight = 28
        });

        void SelectPage(int index)
        {
            if (index < 0 || index >= _pages.Count || index == _mainTab && _activePage == _pages[index]) return;
            NotifyLifecycle(_activePage, lifecycle => lifecycle.OnDeactivated(), "切换前停用");
            _mainTab = index;
            _groupTab = Array.IndexOf(_groupNames, _pages[index].Group);
            _activePage = _pages[index];
            _pageScroll = Vector2.zero;
            _pageError = null;
            NotifyLifecycle(_activePage, lifecycle => lifecycle.OnActivated(), "激活");
        }

        static void NotifyLifecycle(IBentoPage page, Action<IBentoPageLifecycle> action, string phase)
        {
            if (!(page is IBentoPageLifecycle lifecycle)) return;
            try
            {
                action(lifecycle);
            }
            catch (Exception exception)
            {
                Debug.LogError($"[Bento] 页面“{page.Name}”生命周期阶段“{phase}”失败。\n{exception}");
            }
        }
    }
}

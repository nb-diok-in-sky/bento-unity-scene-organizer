using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Bento
{
    public class BentoWindow : EditorWindow
    {
        const string FavoritesGroup = "常用";
        const float CompactWidth = 860f;
        const float NavigationWidth = 220f;
        const float GroupButtonHeight = 34f;
        const float PageButtonHeight = 30f;

        int _mainTab;
        int _groupTab;
        List<IBentoPage> _pages;
        string[] _pageNames;
        string[] _groupNames;
        IReadOnlyList<IBentoPage> _favoritePages;
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
            else if (string.Equals(pageName, "material_tools", StringComparison.OrdinalIgnoreCase))
                pageName = "select_used_assets";
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
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(CurrentPageName, TitleStyle, GUILayout.Height(28));
                    GUILayout.FlexibleSpace();
                    DrawFavoriteButton(_activePage, GUILayout.Width(42), GUILayout.Height(28));
                }
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
                        catch (ExitGUIException)
                        {
                            throw;
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
                    SelectFirstPageInGroup(_groupNames[_groupTab]);
                }

                IBentoPage[] groupPages = GetPagesForGroup(_groupNames[_groupTab]).ToArray();
                if (groupPages.Length == 0)
                {
                    EditorGUILayout.LabelField("暂无收藏", EditorStyles.miniLabel);
                }
                else
                {
                    int localIndex = Array.IndexOf(groupPages, _activePage);
                    int nextPage = EditorGUILayout.Popup(
                        Mathf.Max(0, localIndex),
                        groupPages.Select(page => page.Name).ToArray(),
                        PopupStyle,
                        GUILayout.Height(28));
                    if (nextPage >= 0 && nextPage < groupPages.Length && groupPages[nextPage] != _activePage)
                        SelectPage(_pages.IndexOf(groupPages[nextPage]));
                }
                DrawFavoriteButton(_activePage, GUILayout.Width(34), GUILayout.Height(28));
            }
        }

        void BuildPages()
        {
            NotifyLifecycle(_activePage, lifecycle => lifecycle.OnDeactivated(), "重建前停用");
            var catalog = new BentoCatalog();
            _pages = catalog.Pages.ToList();
            _pageNames = _pages.Select(page => page.Name).ToArray();
            RefreshFavorites();
            _groupNames = new[] { FavoritesGroup }
                .Concat(_pages.Select(page => page.Group).Distinct())
                .ToArray();
            _mainTab = Mathf.Clamp(_mainTab, 0, _pages.Count - 1);
            _activePage = _pages[_mainTab];
            _groupTab = Math.Max(0, Array.IndexOf(_groupNames, _activePage.Group));
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
                            SelectFirstPageInGroup(group);
                    }
                    if (_groupTab != groupIndex) continue;

                    IBentoPage[] groupPages = GetPagesForGroup(group).ToArray();
                    if (groupPages.Length == 0)
                    {
                        EditorGUILayout.HelpBox(
                            "还没有常用功能。进入任意页面后点击右上角的 ☆ 即可收藏。",
                            MessageType.None);
                    }
                    foreach (IBentoPage page in groupPages)
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            int pageIndex = _pages.IndexOf(page);
                            if (GUILayout.Toggle(
                                    _mainTab == pageIndex,
                                    page.Name,
                                    PageStyle,
                                    GUILayout.Height(PageButtonHeight)) &&
                                _mainTab != pageIndex)
                                SelectPage(pageIndex);
                            DrawFavoriteButton(page, GUILayout.Width(30), GUILayout.Height(PageButtonHeight));
                        }
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
            if (_groupTab != Array.IndexOf(_groupNames, FavoritesGroup) ||
                !_favoritePages.Contains(_pages[index]))
                _groupTab = Array.IndexOf(_groupNames, _pages[index].Group);
            _activePage = _pages[index];
            _pageScroll = Vector2.zero;
            _pageError = null;
            NotifyLifecycle(_activePage, lifecycle => lifecycle.OnActivated(), "激活");
        }

        IEnumerable<IBentoPage> GetPagesForGroup(string group)
        {
            return string.Equals(group, FavoritesGroup, StringComparison.Ordinal)
                ? _favoritePages
                : _pages.Where(page => page.Group == group);
        }

        void SelectFirstPageInGroup(string group)
        {
            IBentoPage page = GetPagesForGroup(group).FirstOrDefault();
            if (page == null)
            {
                _groupTab = Array.IndexOf(_groupNames, group);
                return;
            }

            _groupTab = Array.IndexOf(_groupNames, group);
            SelectPage(_pages.IndexOf(page));
            if (string.Equals(group, FavoritesGroup, StringComparison.Ordinal))
                _groupTab = Array.IndexOf(_groupNames, FavoritesGroup);
        }

        void DrawFavoriteButton(IBentoPage page, params GUILayoutOption[] options)
        {
            if (page == null) return;
            bool favorite = BentoFavorites.Contains(page.Id);
            if (!GUILayout.Button(
                    new GUIContent(favorite ? "★" : "☆", favorite ? "从常用中移除" : "加入常用"),
                    options))
                return;

            BentoFavorites.Toggle(page.Id);
            RefreshFavorites();
            if (_groupNames[_groupTab] == FavoritesGroup && _favoritePages.Count == 0)
                _groupTab = Array.IndexOf(_groupNames, page.Group);
            Repaint();
        }

        void RefreshFavorites()
        {
            _favoritePages = BentoFavorites.Resolve(_pages);
        }

        static void NotifyLifecycle(IBentoPage page, Action<IBentoPageLifecycle> action, string phase)
        {
            if (!(page is IBentoPageLifecycle lifecycle)) return;
            try
            {
                action(lifecycle);
            }
            catch (ExitGUIException)
            {
                throw;
            }
            catch (Exception exception)
            {
                Debug.LogError($"[Bento] 页面“{page.Name}”生命周期阶段“{phase}”失败。\n{exception}");
            }
        }
    }
}

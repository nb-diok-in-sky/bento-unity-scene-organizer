using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Bento
{
    sealed class ProjectFolderFavoritesSidebar
    {
        const float Width = 210f;
        const string VisibleKeyPrefix = "Bento.FolderFavoritesSidebar.Visible.";
        readonly string _visibleKey =
            BentoUtility.GetProjectEditorPrefsKey(VisibleKeyPrefix);
        ProjectFolderFavoriteList _data;
        Vector2 _scroll;
        Vector2 _mouseDownPosition;
        int _mouseDownFavoriteIndex = -1;
        int _lastClickedFavoriteIndex = -1;
        double _lastClickTime;
        string _hierarchyMessage = string.Empty;

        public bool Visible
        {
            get => EditorPrefs.GetBool(_visibleKey, true);
            set => EditorPrefs.SetBool(_visibleKey, value);
        }

        public void Draw()
        {
            EnsureLoaded();
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.Width(Width)))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("文件夹收藏", EditorStyles.boldLabel);
                    if (GUILayout.Button("×", GUILayout.Width(24)))
                    {
                        Visible = false;
                        GUIUtility.ExitGUI();
                    }
                }

                EditorGUILayout.LabelField(
                    "单击定位 · 双击进入 · 可拖动",
                    EditorStyles.miniLabel);
                if (GUILayout.Button(
                        new GUIContent(
                            "折叠当前 Hierarchy",
                            "递归折叠当前 Prefab 模式，或所有已加载场景的父物体；不会改变当前选中对象。"),
                        GUILayout.Height(28)))
                    CollapseHierarchy();
                if (!string.IsNullOrEmpty(_hierarchyMessage))
                    EditorGUILayout.LabelField(
                        _hierarchyMessage,
                        EditorStyles.wordWrappedMiniLabel);
                _scroll = EditorGUILayout.BeginScrollView(_scroll);
                if (_data.items.Count == 0)
                    EditorGUILayout.HelpBox(
                        "暂无收藏。\n从 Project 拖入文件夹即可添加。",
                        MessageType.None);
                for (int index = 0; index < _data.items.Count; index++)
                    DrawFavorite(index);
                GUILayout.FlexibleSpace();
                Rect dropRect = GUILayoutUtility.GetRect(0, 42, GUILayout.ExpandWidth(true));
                GUI.Box(dropRect, "拖入 Project 文件夹\n添加收藏", EditorStyles.helpBox);
                HandleAddFolderDrop(dropRect);
                EditorGUILayout.EndScrollView();
            }
        }

        void DrawFavorite(int index)
        {
            ProjectFolderFavorite favorite = _data.items[index];
            if (favorite == null) return;
            DefaultAsset folder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(favorite.path);
            string label = string.IsNullOrWhiteSpace(favorite.alias)
                ? Path.GetFileName(favorite.path)
                : favorite.alias;
            Rect rect = GUILayoutUtility.GetRect(
                new GUIContent(label),
                EditorStyles.miniButton,
                GUILayout.Height(28),
                GUILayout.ExpandWidth(true));
            GUIStyle style = folder == null
                ? EditorStyles.miniButtonMid
                : EditorStyles.miniButton;
            Color previousColor = GUI.color;
            if (folder == null) GUI.color = new Color(1f, 1f, 1f, 0.45f);
            GUI.Box(rect, new GUIContent(label, favorite.path), style);
            GUI.color = previousColor;
            HandleFavoriteEvent(rect, index, favorite, folder);
        }

        void HandleFavoriteEvent(
            Rect rect,
            int index,
            ProjectFolderFavorite favorite,
            DefaultAsset folder)
        {
            Event current = Event.current;
            if (!rect.Contains(current.mousePosition)) return;

            int controlId = GUIUtility.GetControlID(
                "BentoFolderFavorite".GetHashCode() + index,
                FocusType.Passive,
                rect);
            EventType eventType = current.GetTypeForControl(controlId);

            if (eventType == EventType.MouseDown && current.button == 0 && folder != null)
            {
                GUIUtility.hotControl = controlId;
                _mouseDownPosition = current.mousePosition;
                _mouseDownFavoriteIndex = index;
                current.Use();
                return;
            }

            if (eventType == EventType.MouseUp &&
                current.button == 0 &&
                GUIUtility.hotControl == controlId &&
                _mouseDownFavoriteIndex == index &&
                folder != null)
            {
                GUIUtility.hotControl = 0;
                double now = EditorApplication.timeSinceStartup;
                bool isDoubleClick =
                    _lastClickedFavoriteIndex == index &&
                    now - _lastClickTime <= 0.45d;
                if (isDoubleClick)
                {
                    OpenFolder(folder);
                    _lastClickedFavoriteIndex = -1;
                    _lastClickTime = 0d;
                }
                else
                {
                    LocateFolder(folder);
                    _lastClickedFavoriteIndex = index;
                    _lastClickTime = now;
                }
                _mouseDownFavoriteIndex = -1;
                current.Use();
                return;
            }

            if (eventType == EventType.MouseDown && current.button == 1)
            {
                ShowContextMenu(index, favorite, folder);
                current.Use();
                return;
            }

            if (eventType == EventType.MouseDrag &&
                current.button == 0 &&
                GUIUtility.hotControl == controlId &&
                _mouseDownFavoriteIndex == index &&
                folder != null &&
                Vector2.Distance(_mouseDownPosition, current.mousePosition) >= 4f)
            {
                GUIUtility.hotControl = 0;
                DragAndDrop.PrepareStartDrag();
                DragAndDrop.objectReferences = new UnityEngine.Object[] { folder };
                DragAndDrop.paths = new[] { favorite.path };
                DragAndDrop.StartDrag(favorite.alias ?? folder.name);
                _mouseDownFavoriteIndex = -1;
                current.Use();
                return;
            }

            if (eventType == EventType.DragUpdated || eventType == EventType.DragPerform)
                HandleMoveAssetsDrop(rect, favorite.path, current);
        }

        void HandleMoveAssetsDrop(Rect rect, string targetFolder, Event current)
        {
            UnityEngine.Object[] assets = DragAndDrop.objectReferences
                .Where(asset =>
                {
                    string path = AssetDatabase.GetAssetPath(asset);
                    return !string.IsNullOrEmpty(path) &&
                           !AssetDatabase.IsValidFolder(path);
                })
                .Distinct()
                .ToArray();
            if (assets.Length == 0) return;

            DragAndDrop.visualMode = DragAndDropVisualMode.Move;
            if (current.type != EventType.DragPerform) return;
            DragAndDrop.AcceptDrag();
            current.Use();

            List<AssetMoveEntry> plan =
                SafeAssetMoveService.BuildFlatExtractionPlan(assets, targetFolder);
            int validCount = plan.Count(entry => string.IsNullOrEmpty(entry.warning));
            if (validCount != plan.Count)
            {
                string warnings = string.Join(
                    "\n",
                    plan.Where(entry => !string.IsNullOrEmpty(entry.warning))
                        .Select(entry => $"{entry.sourcePath}：{entry.warning}"));
                EditorUtility.DisplayDialog("无法移动到收藏文件夹", warnings, "确定");
                return;
            }

            if (!BentoCommand.Confirm(
                    "移动到收藏文件夹",
                    $"将把 {plan.Count} 个 Project 资产移动到：\n{targetFolder}\n\nGUID 与引用保持不变。",
                    "开始移动"))
                return;

            BentoCommand.Run("移动到收藏文件夹失败", () =>
            {
                var history = new AssetMoveHistory();
                SafeAssetMoveService.Execute(plan, history);
                AssetOperationHistory.TryRecord(
                    AssetMoveOperationHandler.CreateRecord(history),
                    out _);
            }, () => $"已移动 {plan.Count} 个资产到 {targetFolder}。");
        }

        void HandleAddFolderDrop(Rect rect)
        {
            Event current = Event.current;
            if (!rect.Contains(current.mousePosition) ||
                current.type != EventType.DragUpdated &&
                current.type != EventType.DragPerform)
                return;

            string[] folders = DragAndDrop.objectReferences
                .Select(AssetDatabase.GetAssetPath)
                .Where(AssetDatabase.IsValidFolder)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (folders.Length == 0) return;
            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            if (current.type != EventType.DragPerform) return;

            DragAndDrop.AcceptDrag();
            foreach (string folder in folders)
                ProjectFolderFavorites.Add(_data, folder);
            current.Use();
        }

        void ShowContextMenu(
            int index,
            ProjectFolderFavorite favorite,
            DefaultAsset folder)
        {
            var menu = new GenericMenu();
            if (folder == null)
            {
                menu.AddDisabledItem(new GUIContent("定位"));
                menu.AddDisabledItem(new GUIContent("进入文件夹"));
            }
            else
            {
                menu.AddItem(new GUIContent("定位"), false, () => LocateFolder(folder));
                menu.AddItem(new GUIContent("进入文件夹"), false, () => OpenFolder(folder));
            }
            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent("恢复文件夹名称"), false, () =>
            {
                favorite.alias = Path.GetFileName(favorite.path);
                ProjectFolderFavorites.Save(_data);
            });
            menu.AddItem(new GUIContent("取消收藏"), false, () =>
            {
                if (index < 0 || index >= _data.items.Count) return;
                _data.items.RemoveAt(index);
                ProjectFolderFavorites.Save(_data);
            });
            menu.ShowAsContext();
        }

        void EnsureLoaded()
        {
            if (_data == null)
            {
                _data = ProjectFolderFavorites.Load();
                ProjectFolderFavorites.Changed += Reload;
            }
        }

        public void Dispose()
        {
            ProjectFolderFavorites.Changed -= Reload;
        }

        void Reload() => _data = ProjectFolderFavorites.Load();

        void CollapseHierarchy()
        {
            try
            {
                HierarchyCollapseResult result =
                    HierarchyCollapseUtility.CollapseAllLoadedScenes();
                _hierarchyMessage = result.ParentObjectCount == 0
                    ? "当前已加载场景没有可折叠的父物体。"
                    : $"已折叠 {result.ParentObjectCount} 个父物体；当前选择保持不变。";
            }
            catch (Exception exception)
            {
                _hierarchyMessage = string.Empty;
                Debug.LogException(exception);
                EditorUtility.DisplayDialog(
                    "折叠 Hierarchy 失败",
                    exception.GetBaseException().Message,
                    "确定");
            }
        }

        static void LocateFolder(DefaultAsset folder)
        {
            EditorApplication.ExecuteMenuItem("Window/General/Project");
            BentoGUI.Locate(folder);
        }

        static void OpenFolder(DefaultAsset folder)
        {
            EditorApplication.ExecuteMenuItem("Window/General/Project");
            Selection.activeObject = folder;
            bool opened = false;
            Type projectBrowserType = typeof(Editor).Assembly.GetType("UnityEditor.ProjectBrowser");
            UnityEngine.Object[] browsers = projectBrowserType == null
                ? Array.Empty<UnityEngine.Object>()
                : Resources.FindObjectsOfTypeAll(projectBrowserType);
            foreach (UnityEngine.Object browser in browsers)
            {
                foreach (MethodInfo method in projectBrowserType.GetMethods(
                             BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
                {
                    if (!string.Equals(method.Name, "ShowFolderContents", StringComparison.Ordinal))
                        continue;
                    ParameterInfo[] parameters = method.GetParameters();
                    try
                    {
                        if (parameters.Length == 2 &&
                            parameters[0].ParameterType == typeof(int) &&
                            parameters[1].ParameterType == typeof(bool))
                        {
                            method.Invoke(browser, new object[] { folder.GetInstanceID(), true });
                            opened = true;
                            break;
                        }
                        if (parameters.Length == 1 && parameters[0].ParameterType == typeof(int))
                        {
                            method.Invoke(browser, new object[] { folder.GetInstanceID() });
                            opened = true;
                            break;
                        }
                    }
                    catch (Exception exception)
                    {
                        Debug.LogWarning(
                            $"[Bento] Project 文件夹进入接口调用失败，将退回定位：{exception.Message}");
                    }
                }
                if (opened) break;
            }
            if (!opened)
                Debug.LogWarning("[Bento] 当前 Unity 版本未找到 Project 文件夹进入接口，已退回定位。");
            EditorGUIUtility.PingObject(folder);
        }
    }
}

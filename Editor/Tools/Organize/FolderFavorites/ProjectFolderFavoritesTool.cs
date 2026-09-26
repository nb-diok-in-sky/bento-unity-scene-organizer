using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Bento
{
    [Serializable]
    class ProjectFolderFavorite
    {
        public string path;
        public string alias;
    }

    [Serializable]
    class ProjectFolderFavoriteList
    {
        public List<ProjectFolderFavorite> items = new List<ProjectFolderFavorite>();
    }

    [BentoPage("folder_favorites", BentoGroups.Organize, "文件夹收藏", Order = 10)]
    class ProjectFolderFavoritesTool : IBentoTool
    {
        readonly ProjectFolderFavoriteList _data;
        readonly ProjectFolderFavoriteStore _store = new ProjectFolderFavoriteStore();
        Vector2 _scrollPosition;
        DefaultAsset _folderToAdd;

        public ProjectFolderFavoritesTool()
        {
            _data = _store.Load();
        }

        public void Draw()
        {
            DrawHeader();
            DrawAddSection();
            DrawFavoritesHeader();
            DrawFavoritesList();
        }

        void DrawHeader()
        {
            EditorGUILayout.LabelField("Project 文件夹收藏", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "收藏常用的 Project 文件夹，一键在 Project 窗口定位。收藏只保存在本机 EditorPrefs，不会写入项目或上传 Git。",
                MessageType.Info);
        }

        void DrawAddSection()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                _folderToAdd = (DefaultAsset)EditorGUILayout.ObjectField(
                    new GUIContent("添加文件夹", "从 Project 窗口拖入一个文件夹。"),
                    _folderToAdd,
                    typeof(DefaultAsset),
                    false);

                string path = _folderToAdd == null ? string.Empty : AssetDatabase.GetAssetPath(_folderToAdd);
                bool isFolder = AssetDatabase.IsValidFolder(path);
                using (new EditorGUI.DisabledScope(!isFolder))
                {
                    if (GUILayout.Button("加入收藏", GUILayout.Height(30)))
                    {
                        AddFolder(path);
                        _folderToAdd = null;
                        GUI.FocusControl(null);
                    }
                }

                if (_folderToAdd != null && !isFolder)
                    EditorGUILayout.HelpBox("只能收藏 Project 窗口中的文件夹。", MessageType.Warning);
            }
        }

        void DrawFavoritesHeader()
        {
            EditorGUILayout.Space(8);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField($"已收藏：{_data.items.Count} 个", EditorStyles.boldLabel);
                if (GUILayout.Button("清理失效项", GUILayout.Width(90)))
                    RemoveInvalidEntries(save: true);
            }
        }

        void DrawFavoritesList()
        {
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
            if (_data.items.Count == 0)
            {
                EditorGUILayout.HelpBox("还没有收藏。把 Project 中的文件夹拖到上方即可。", MessageType.None);
            }
            else
            {
                for (int index = 0; index < _data.items.Count; index++)
                {
                    if (!DrawFavorite(index))
                        continue;
                    Save();
                    break;
                }
            }
            EditorGUILayout.EndScrollView();
        }

        bool DrawFavorite(int index)
        {
            var favorite = _data.items[index];
            var folder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(favorite.path);
            string displayName = string.IsNullOrWhiteSpace(favorite.alias)
                ? System.IO.Path.GetFileName(favorite.path)
                : favorite.alias;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (DrawFavoriteToolbar(index, favorite, folder, displayName))
                    return true;

                string newAlias = EditorGUILayout.TextField("显示名称", favorite.alias ?? string.Empty);
                if (newAlias != favorite.alias)
                {
                    favorite.alias = newAlias;
                    Save();
                }
                EditorGUILayout.SelectableLabel(favorite.path, EditorStyles.miniLabel, GUILayout.Height(18));

                if (folder == null)
                    EditorGUILayout.HelpBox("文件夹已不存在，可以删除或点击“清理失效项”。", MessageType.Warning);
            }
            return false;
        }

        bool DrawFavoriteToolbar(int index, ProjectFolderFavorite favorite, DefaultAsset folder, string displayName)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(folder == null))
                {
                    if (GUILayout.Button(new GUIContent(displayName, favorite.path), GUILayout.Height(27)))
                        LocateFolder(folder);
                }

                if (DrawMoveButton("▲", index, index - 1, index == 0))
                    return true;
                if (DrawMoveButton("▼", index, index + 1, index >= _data.items.Count - 1))
                    return true;

                if (GUILayout.Button("删除", GUILayout.Width(48)) && EditorUtility.DisplayDialog(
                    "删除收藏",
                    $"确定从收藏中移除“{displayName}”吗？\n不会删除真实文件夹。",
                    "删除收藏",
                    "取消"))
                {
                    _data.items.RemoveAt(index);
                    return true;
                }
            }
            return false;
        }

        bool DrawMoveButton(string label, int sourceIndex, int targetIndex, bool disabled)
        {
            using (new EditorGUI.DisabledScope(disabled))
            {
                if (!GUILayout.Button(label, GUILayout.Width(28)))
                    return false;
                Swap(sourceIndex, targetIndex);
                return true;
            }
        }

        void AddFolder(string path)
        {
            if (!AssetDatabase.IsValidFolder(path))
                return;

            var existing = _data.items.FirstOrDefault(item =>
                string.Equals(item.path, path, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                LocateFolder(AssetDatabase.LoadAssetAtPath<DefaultAsset>(path));
                EditorUtility.DisplayDialog("已经收藏", $"该文件夹已经在收藏中：\n{path}", "确定");
                return;
            }

            _data.items.Add(new ProjectFolderFavorite
            {
                path = path,
                alias = System.IO.Path.GetFileName(path)
            });
            Save();
        }

        static void LocateFolder(DefaultAsset folder)
        {
            if (folder == null)
                return;
            Selection.activeObject = folder;
            EditorGUIUtility.PingObject(folder);
        }

        void Swap(int first, int second)
        {
            var value = _data.items[first];
            _data.items[first] = _data.items[second];
            _data.items[second] = value;
        }

        void RemoveInvalidEntries(bool save)
        {
            int removedCount = _data.items.RemoveAll(item =>
                item == null || string.IsNullOrEmpty(item.path) || !AssetDatabase.IsValidFolder(item.path));
            if (removedCount > 0 || save)
                Save();
        }

        void Save() => ProjectFolderFavorites.Save(_data);
    }

    sealed class ProjectFolderFavoriteStore
    {
        const string PrefKeyPrefix = "Bento.ProjectFolderFavorites.";
        readonly string _key = BentoUtility.GetProjectEditorPrefsKey(PrefKeyPrefix);

        public ProjectFolderFavoriteList Load()
        {
            try
            {
                string json = EditorPrefs.GetString(_key, string.Empty);
                var data = string.IsNullOrEmpty(json)
                    ? new ProjectFolderFavoriteList()
                    : JsonUtility.FromJson<ProjectFolderFavoriteList>(json) ?? new ProjectFolderFavoriteList();
                data.items = data.items ?? new List<ProjectFolderFavorite>();
                return data;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[Bento] 文件夹收藏数据损坏，已重置：{exception.Message}");
                EditorPrefs.DeleteKey(_key);
                return new ProjectFolderFavoriteList();
            }
        }

        public void Save(ProjectFolderFavoriteList data) => EditorPrefs.SetString(_key, JsonUtility.ToJson(data));
    }

    static class ProjectFolderFavorites
    {
        public static event Action Changed;

        public static ProjectFolderFavoriteList Load() => new ProjectFolderFavoriteStore().Load();

        public static void Save(ProjectFolderFavoriteList data)
        {
            new ProjectFolderFavoriteStore().Save(data);
            Changed?.Invoke();
        }

        public static bool Add(ProjectFolderFavoriteList data, string path)
        {
            if (data == null || !AssetDatabase.IsValidFolder(path))
                return false;
            if (data.items.Any(item =>
                    item != null &&
                    string.Equals(item.path, path, StringComparison.OrdinalIgnoreCase)))
                return false;

            data.items.Add(new ProjectFolderFavorite
            {
                path = path,
                alias = System.IO.Path.GetFileName(path)
            });
            Save(data);
            return true;
        }
    }
}

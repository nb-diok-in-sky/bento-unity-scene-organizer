using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Bento
{
    sealed class AssetSelectionList
    {
        readonly List<UnityEngine.Object> _assets = new List<UnityEngine.Object>();
        readonly Func<UnityEngine.Object, bool> _accept;
        UnityEngine.Object _pending;

        public AssetSelectionList(Func<UnityEngine.Object, bool> accept) => _accept = accept;
        public int Count => _assets.Count;
        public IReadOnlyList<UnityEngine.Object> Items => _assets;

        public bool Draw(string emptyHint)
        {
            bool changed = false;
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.HelpBox(emptyHint, MessageType.None);
                using (new EditorGUILayout.HorizontalScope())
                {
                    _pending = EditorGUILayout.ObjectField("添加源资产", _pending, typeof(UnityEngine.Object), false);
                    using (new EditorGUI.DisabledScope(!_accept(_pending)))
                    if (GUILayout.Button("添加", GUILayout.Width(54)))
                    {
                        changed |= Add(_pending);
                        _pending = null;
                    }
                }

                changed |= DrawDropArea();
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("使用 Project 当前选择"))
                        foreach (var asset in ProjectSelection.CurrentAssets) changed |= Add(asset);
                    using (new EditorGUI.DisabledScope(_assets.Count == 0))
                    if (GUILayout.Button("清空来源")) { _assets.Clear(); changed = true; }
                }

                for (int index = 0; index < _assets.Count; index++)
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.ObjectField(_assets[index], typeof(UnityEngine.Object), false);
                    if (GUILayout.Button("移除", GUILayout.Width(48)))
                    {
                        _assets.RemoveAt(index);
                        changed = true;
                        break;
                    }
                }
            }
            return changed;
        }

        public IEnumerable<UnityEngine.Object> ResolveOrSelection()
        {
            return _assets.Count > 0 ? _assets.Where(_accept) : ProjectSelection.SupportedAssets(_accept);
        }

        bool Add(UnityEngine.Object asset)
        {
            if (!_accept(asset) || _assets.Contains(asset)) return false;
            _assets.Add(asset);
            return true;
        }

        bool DrawDropArea()
        {
            Rect area = GUILayoutUtility.GetRect(0, 56, GUILayout.ExpandWidth(true));
            GUI.Box(area, "把一个或多个 Project 资产拖到这里", EditorStyles.helpBox);
            var current = Event.current;
            if (!area.Contains(current.mousePosition) ||
                current.type != EventType.DragUpdated && current.type != EventType.DragPerform)
                return false;

            bool supported = DragAndDrop.objectReferences.Any(_accept);
            DragAndDrop.visualMode = supported ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;
            bool changed = false;
            if (current.type == EventType.DragPerform && supported)
            {
                DragAndDrop.AcceptDrag();
                foreach (var asset in DragAndDrop.objectReferences) changed |= Add(asset);
            }
            current.Use();
            return changed;
        }
    }

    static class AssetPlanGUI
    {
        public static Vector2 DrawMovePlan(IReadOnlyList<AssetMoveEntry> entries, Vector2 scroll, Action<string> ping)
        {
            scroll = EditorGUILayout.BeginScrollView(scroll, EditorStyles.helpBox, GUILayout.MinHeight(200));
            if (entries.Count == 0)
                EditorGUILayout.LabelField("在 Project 窗口选择资产，然后点击扫描。", EditorStyles.miniLabel);
            foreach (var entry in entries)
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                entry.include = EditorGUILayout.ToggleLeft($"{entry.kind}: {entry.sourcePath}", entry.include);
                EditorGUILayout.LabelField($"→ {entry.targetPath}", EditorStyles.miniLabel);
                if (!string.IsNullOrEmpty(entry.warning)) EditorGUILayout.HelpBox(entry.warning, MessageType.Warning);
                if (GUILayout.Button("定位源资源", GUILayout.Width(90))) ping(entry.sourcePath);
            }
            EditorGUILayout.EndScrollView();
            return scroll;
        }
    }
}

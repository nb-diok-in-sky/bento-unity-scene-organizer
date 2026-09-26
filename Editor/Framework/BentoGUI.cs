using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Bento
{
    public static class BentoGUI
    {
        public static DefaultAsset DrawProjectFolder(string label, DefaultAsset current, out string path)
        {
            DefaultAsset next;
            using (new EditorGUILayout.HorizontalScope())
            {
                next = (DefaultAsset)EditorGUILayout.ObjectField(label, current, typeof(DefaultAsset), false);
                if (GUILayout.Button("使用当前", GUILayout.Width(72)))
                {
                    if (ProjectSelection.TryGetActiveProjectFolder(out DefaultAsset selectedFolder)) next = selectedFolder;
                    else EditorUtility.DisplayDialog("无法获取 Project 文件夹", "请在 Project 面板选中一个文件夹，或选中该文件夹内的任意资源。", "确定");
                }
            }
            path = next == null ? string.Empty : AssetDatabase.GetAssetPath(next);
            if (next == null || AssetDatabase.IsValidFolder(path)) return next;
            EditorGUILayout.HelpBox($"{label} 只能选择 Project 文件夹。", MessageType.Warning);
            path = current == null ? string.Empty : AssetDatabase.GetAssetPath(current);
            return current;
        }

        public static void DrawNamingFields(AssetNamingOptions options, string baseNameLabel)
        {
            options.prefix = EditorGUILayout.TextField("前缀", options.prefix);
            options.baseName = EditorGUILayout.TextField(baseNameLabel, options.baseName);
            DrawNamingAffixesAndNumbering(options, drawPrefix: false);
        }

        public static void DrawNamingAffixesAndNumbering(
            AssetNamingOptions options,
            bool drawPrefix = true)
        {
            if (drawPrefix)
                options.prefix = EditorGUILayout.TextField("前缀", options.prefix);
            options.suffix = EditorGUILayout.TextField("后缀", options.suffix);
            options.appendNumber = EditorGUILayout.Toggle("自动编号", options.appendNumber);
            if (!options.appendNumber) return;
            options.startNumber = EditorGUILayout.IntField("起始编号", options.startNumber);
            options.numberDigits = EditorGUILayout.IntSlider("编号位数", options.numberDigits, 1, 6);
        }

        /// <summary>Selects <paramref name="target"/> and highlights it in the Project or Hierarchy window.</summary>
        public static void Locate(Object target)
        {
            if (target == null) return;
            Selection.activeObject = target;
            EditorGUIUtility.PingObject(target);
        }

        public static void LocateAsset(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return;
            Locate(AssetDatabase.LoadMainAssetAtPath(assetPath));
        }

        /// <summary>Selects a scene object by instance id, highlights it and frames it in the Scene view.</summary>
        public static void FocusSceneObject(int instanceId)
        {
            Object target = EditorUtility.InstanceIDToObject(instanceId);
            if (target == null) return;
            Locate(target);
            SceneView.lastActiveSceneView?.FrameSelected();
        }

        public static void LocateButton(Object target, float width = 44f)
        {
            if (GUILayout.Button("定位", GUILayout.Width(width))) Locate(target);
        }

        /// <summary>
        /// Draws "select all / select none / invert" buttons for a checklist. Entries rejected by
        /// <paramref name="selectable"/> are never switched on.
        /// </summary>
        public static void DrawIncludeButtons<T>(
            IEnumerable<T> entries,
            Func<T, bool> isIncluded,
            Action<T, bool> setIncluded,
            Func<T, bool> selectable = null,
            string selectAllLabel = "全选",
            float selectAllWidth = 60f)
        {
            if (entries == null) return;
            selectable = selectable ?? (_ => true);
            if (GUILayout.Button(selectAllLabel, GUILayout.Width(selectAllWidth)))
                foreach (T entry in entries) setIncluded(entry, selectable(entry));
            if (GUILayout.Button("全不选", GUILayout.Width(70)))
                foreach (T entry in entries) setIncluded(entry, false);
            if (GUILayout.Button("反选", GUILayout.Width(60)))
                foreach (T entry in entries) setIncluded(entry, !isIncluded(entry) && selectable(entry));
        }
    }
}

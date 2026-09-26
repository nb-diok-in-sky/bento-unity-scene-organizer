using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Bento
{
    class HierarchyRenameTool
    {
        readonly AssetNamingOptions _naming = new AssetNamingOptions
        {
            baseName = "Object",
            appendNumber = true
        };
        bool _addPrefixSuffixOnly;
        Vector2 _scrollPosition;

        public void Draw()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorGUILayout.HelpBox("Play Mode 中禁止批量重命名，请先退出运行模式。", MessageType.Warning);
                return;
            }

            var selectedObjects = BentoUtility.GetTopLevelSelectedSceneObjects(sortByHierarchy: true);
            DrawSettings(selectedObjects);
            DrawPreview(selectedObjects);
            DrawExecuteButton(selectedObjects);
        }

        void DrawSettings(List<GameObject> selectedObjects)
        {
            EditorGUILayout.LabelField("Hierarchy 批量重命名", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "只修改 Hierarchy 中选中的场景实例名称，不修改 Project 里的 FBX 或 Prefab 资产。支持 Ctrl+Z 撤销。",
                MessageType.Info);

            _naming.prefix = EditorGUILayout.TextField(new GUIContent("前缀", "添加到名称最前面，可留空。"), _naming.prefix);
            _addPrefixSuffixOnly = EditorGUILayout.ToggleLeft(
                new GUIContent("单独增加前后缀", "保留每个对象当前名称，只统一添加前缀、后缀和可选编号。"),
                _addPrefixSuffixOnly);
            using (new EditorGUI.DisabledScope(_addPrefixSuffixOnly))
                _naming.baseName = EditorGUILayout.TextField(
                    new GUIContent("基础名称", "所有选中对象使用的主体名称。"),
                    _naming.baseName);
            _naming.suffix = EditorGUILayout.TextField(new GUIContent("后缀", "添加到编号后面，可留空。"), _naming.suffix);
            _naming.appendNumber = EditorGUILayout.Toggle("自动编号", _naming.appendNumber);
            if (_naming.appendNumber)
            {
                _naming.startNumber = EditorGUILayout.IntField("起始编号", _naming.startNumber);
                _naming.numberDigits = EditorGUILayout.IntSlider("编号位数", _naming.numberDigits, 1, 6);
            }

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField($"当前选中场景对象：{selectedObjects.Count} 个", EditorStyles.boldLabel);
        }

        void DrawPreview(List<GameObject> selectedObjects)
        {
            EditorGUILayout.LabelField("重命名预览：");
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition, EditorStyles.helpBox, GUILayout.MinHeight(100));
            if (selectedObjects.Count == 0)
            {
                EditorGUILayout.LabelField("请在 Hierarchy 中选择对象。", EditorStyles.miniLabel);
            }
            else
            {
                int previewCount = Mathf.Min(selectedObjects.Count, 30);
                for (int index = 0; index < previewCount; index++)
                    EditorGUILayout.LabelField(
                        $"{selectedObjects[index].name}  →  {BuildName(selectedObjects[index], index)}");
                if (selectedObjects.Count > previewCount)
                    EditorGUILayout.LabelField($"……其余 {selectedObjects.Count - previewCount} 个对象");
            }
            EditorGUILayout.EndScrollView();
        }

        void DrawExecuteButton(List<GameObject> selectedObjects)
        {
            bool missingBaseName = !_addPrefixSuffixOnly && string.IsNullOrWhiteSpace(_naming.baseName);
            using (new EditorGUI.DisabledScope(selectedObjects.Count == 0 || missingBaseName))
            {
                if (GUILayout.Button($"重命名选中的 {selectedObjects.Count} 个对象", GUILayout.Height(36)) &&
                    ConfirmDuplicateNamesIfNeeded(selectedObjects.Count))
                {
                    RenameSelected(selectedObjects);
                }
            }

            if (missingBaseName)
                EditorGUILayout.HelpBox("基础名称不能为空。", MessageType.Warning);
        }

        bool ConfirmDuplicateNamesIfNeeded(int selectedCount)
        {
            if (_addPrefixSuffixOnly || _naming.appendNumber || selectedCount <= 1)
                return true;

            return EditorUtility.DisplayDialog(
                "确认使用相同名称",
                $"自动编号已关闭，{selectedCount} 个对象都会被重命名为同一个名称：\n{BuildName(null, 0)}\n\n仍要继续吗？",
                "继续重命名",
                "取消");
        }

        string BuildName(GameObject gameObject, int index)
        {
            if (!_addPrefixSuffixOnly)
                return AssetNaming.BuildName(_naming.baseName, index, _naming);

            var options = new AssetNamingOptions
            {
                prefix = _naming.prefix,
                suffix = _naming.suffix,
                appendNumber = _naming.appendNumber,
                startNumber = _naming.startNumber,
                numberDigits = _naming.numberDigits
            };
            return AssetNaming.BuildName(gameObject?.name ?? string.Empty, index, options);
        }

        void RenameSelected(List<GameObject> selectedObjects)
        {
            using (var undo = new BentoUndoScope("批量重命名场景对象"))
            {
                for (int index = 0; index < selectedObjects.Count; index++)
                {
                    var gameObject = selectedObjects[index];
                    Undo.RecordObject(gameObject, "重命名场景对象");
                    gameObject.name = BuildName(gameObject, index);
                    EditorSceneManager.MarkSceneDirty(gameObject.scene);
                }
                undo.Complete();
            }
            Debug.Log($"[Bento] 已重命名 {selectedObjects.Count} 个场景对象，可按 Ctrl+Z 撤销");
        }
    }
}

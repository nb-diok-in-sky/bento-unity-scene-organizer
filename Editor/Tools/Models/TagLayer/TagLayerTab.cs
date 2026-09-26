using System.Linq;
using UnityEditor;
using System;
using UnityEditor.SceneManagement;
using UnityEditorInternal;
using UnityEngine;

namespace Bento
{
    [BentoPage("tag_layer", BentoGroups.Models, "Tag/Layer", Order = 50)]
    class TagLayerTab : IBentoTool
    {
        Vector2 _scroll;
        int _selectedTag;
        int _selectedLayer;
        bool _includeChildren = true;
        bool _showPreview = true;
        string[] _cachedLayerNames;
        int[] _cachedLayerIndices;

        public void Draw()
        {
            var selected = Selection.gameObjects;

            if (selected.Length == 0)
            {
                EditorGUILayout.HelpBox("在 Hierarchy（场景）中选中一个或多个 GameObject", MessageType.Info);
                return;
            }

            EditorGUILayout.HelpBox($"已选中 {selected.Length} 个场景物体", MessageType.None);
            EditorGUILayout.Space(4);

            _includeChildren = EditorGUILayout.Toggle("包含子物体", _includeChildren);

            var targets = ResolveTargets(selected, _includeChildren);
            int totalCount = targets.Length;

            if (_includeChildren && totalCount != selected.Length)
                EditorGUILayout.LabelField($"  含子物体共 {totalCount} 个", EditorStyles.miniLabel);

            EditorGUILayout.Space(8);

            // ── Tag ──
            EditorGUILayout.LabelField("批量修改 Tag", EditorStyles.boldLabel);

            var tags = InternalEditorUtility.tags;
            _selectedTag = Mathf.Clamp(_selectedTag, 0, tags.Length - 1);

            EditorGUILayout.BeginHorizontal();
            _selectedTag = EditorGUILayout.Popup("目标 Tag", _selectedTag, tags);

            EditorGUI.BeginDisabledGroup(selected.Length == 0);
            if (GUILayout.Button($"应用 Tag", GUILayout.Width(100), GUILayout.Height(22)))
            {
                string tag = tags[_selectedTag];
                int count = Apply(targets, "批量修改 Tag", go => go.tag != tag, go => go.tag = tag);
                Debug.Log($"[Bento] 已将 {count} 个物体的 Tag 改为 \"{tag}\"");
            }
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(8);

            // ── Layer ──
            EditorGUILayout.LabelField("批量修改 Layer", EditorStyles.boldLabel);

            if (_cachedLayerNames == null)
            {
                _cachedLayerNames = Enumerable.Range(0, 32)
                    .Select(i => LayerMask.LayerToName(i))
                    .Where(n => !string.IsNullOrEmpty(n))
                    .ToArray();
                _cachedLayerIndices = Enumerable.Range(0, 32)
                    .Where(i => !string.IsNullOrEmpty(LayerMask.LayerToName(i)))
                    .ToArray();
            }

            _selectedLayer = Mathf.Clamp(_selectedLayer, 0, _cachedLayerNames.Length - 1);

            EditorGUILayout.BeginHorizontal();
            _selectedLayer = EditorGUILayout.Popup("目标 Layer", _selectedLayer, _cachedLayerNames);

            EditorGUI.BeginDisabledGroup(selected.Length == 0);
            if (GUILayout.Button($"应用 Layer", GUILayout.Width(100), GUILayout.Height(22)))
            {
                int layer = _cachedLayerIndices[_selectedLayer];
                int count = Apply(targets, "批量修改 Layer", go => go.layer != layer, go => go.layer = layer);
                Debug.Log($"[Bento] 已将 {count} 个物体的 Layer 改为 \"{LayerMask.LayerToName(layer)}\"");
            }
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(12);

            // ── Preview ──
            _showPreview = EditorGUILayout.Foldout(_showPreview, $"当前选中物体预览 ({selected.Length} 个)");
            if (_showPreview)
            {
                _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.MaxHeight(300));

                EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
                EditorGUILayout.LabelField("物体", EditorStyles.boldLabel, GUILayout.Width(200));
                EditorGUILayout.LabelField("Tag", EditorStyles.boldLabel, GUILayout.Width(100));
                EditorGUILayout.LabelField("Layer", EditorStyles.boldLabel);
                EditorGUILayout.EndHorizontal();

                foreach (var go in selected)
                    DrawObjectRow(go, 0);

                EditorGUILayout.EndScrollView();
            }
        }

        void DrawObjectRow(GameObject go, int depth)
        {
            EditorGUILayout.BeginHorizontal();

            string indent = depth > 0 ? new string(' ', depth * 2) + "└ " : "";
            EditorGUILayout.LabelField(indent + go.name, GUILayout.Width(200));
            EditorGUILayout.LabelField(go.tag, GUILayout.Width(100));
            EditorGUILayout.LabelField(LayerMask.LayerToName(go.layer));

            EditorGUILayout.EndHorizontal();

            if (_includeChildren)
            {
                foreach (Transform child in go.transform)
                    DrawObjectRow(child.gameObject, depth + 1);
            }
        }

        static GameObject[] ResolveTargets(GameObject[] selected, bool includeChildren)
        {
            return includeChildren
                ? selected.SelectMany(go => go.GetComponentsInChildren<Transform>(true))
                    .Select(transform => transform.gameObject).Distinct().ToArray()
                : selected;
        }

        static int Apply(GameObject[] targets, string undoName, Func<GameObject, bool> needsChange, Action<GameObject> apply)
        {
            var changed = targets.Where(needsChange).ToArray();
            if (changed.Length == 0) return 0;
            using (var undo = new BentoUndoScope(undoName))
            {
                Undo.RecordObjects(changed, undoName);
                foreach (var gameObject in changed)
                {
                    apply(gameObject);
                    EditorUtility.SetDirty(gameObject);
                    if (gameObject.scene.IsValid()) EditorSceneManager.MarkSceneDirty(gameObject.scene);
                }
                undo.Complete();
            }
            return changed.Length;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Bento
{
    static class HierarchyGroupCreationService
    {
        public static GameObject CreateParentForSelection(string groupName)
        {
            List<GameObject> selected = BentoUtility.GetTopLevelSelectedSceneObjects(true);
            if (selected.Count == 0)
                throw new InvalidOperationException("没有选中可整理的场景物体。");
            if (selected.Select(gameObject => gameObject.scene).Distinct().Count() != 1)
                throw new InvalidOperationException("不能为跨场景选择创建同一个父物体。");

            Transform commonParent = FindCommonParent(selected.Select(gameObject => gameObject.transform).ToArray());
            Vector3 center = selected.Aggregate(Vector3.zero, (sum, gameObject) => sum + gameObject.transform.position) /
                             selected.Count;
            string safeName = string.IsNullOrWhiteSpace(groupName) ? "新建分组" : groupName.Trim();

            using (var undo = new BentoUndoScope("Bento 创建场景分组"))
            {
                var group = new GameObject(safeName);
                Undo.RegisterCreatedObjectUndo(group, "Bento 创建场景分组");
                if (commonParent != null)
                    Undo.SetTransformParent(group.transform, commonParent, "Bento 设置分组父节点");
                group.transform.position = center;
                group.transform.rotation = Quaternion.identity;
                group.transform.localScale = Vector3.one;

                foreach (GameObject gameObject in selected)
                    Undo.SetTransformParent(gameObject.transform, group.transform, "Bento 归入场景分组");

                EditorSceneManager.MarkSceneDirty(selected[0].scene);
                undo.Complete();
                return group;
            }
        }

        static Transform FindCommonParent(IReadOnlyList<Transform> transforms)
        {
            if (transforms == null || transforms.Count == 0) return null;

            for (Transform candidate = transforms[0].parent; candidate != null; candidate = candidate.parent)
                if (transforms.All(transform => transform == candidate || transform.IsChildOf(candidate)))
                    return candidate;

            return null;
        }
    }
}

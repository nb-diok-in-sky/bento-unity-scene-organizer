using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Bento
{
    public static class HierarchyOrganizationService
    {
        const float MatrixTolerance = 0.0001f;

        public static List<HierarchyOrganizationEntry> BuildFromSelection(Transform parent)
        {
            return BentoUtility.GetTopLevelSelectedSceneObjects(true).Select(gameObject =>
                Validate(new HierarchyOrganizationEntry
                {
                    objectInstanceId = gameObject.GetInstanceID(),
                    objectPath = GetPath(gameObject.transform),
                    parentInstanceId = parent == null ? 0 : parent.gameObject.GetInstanceID(),
                    targetParentPath = parent == null ? string.Empty : GetPath(parent)
                })).ToList();
        }

        public static HierarchyOrganizationEntry Validate(HierarchyOrganizationEntry entry)
        {
            entry.warning = string.Empty;
            var gameObject = EditorUtility.InstanceIDToObject(entry.objectInstanceId) as GameObject;
            var parentObject = EditorUtility.InstanceIDToObject(entry.parentInstanceId) as GameObject;
            if (gameObject == null || !gameObject.scene.IsValid()) entry.warning = "找不到场景对象";
            else if (parentObject == null || !parentObject.scene.IsValid()) entry.warning = "找不到目标父节点";
            else if (gameObject.scene != parentObject.scene) entry.warning = "禁止跨场景调整父子关系";
            else if (gameObject == parentObject || parentObject.transform.IsChildOf(gameObject.transform)) entry.warning = "目标父节点位于对象自身层级内";
            entry.objectPath = gameObject == null ? entry.objectPath : GetPath(gameObject.transform);
            entry.targetParentPath = parentObject == null ? entry.targetParentPath : GetPath(parentObject.transform);
            return entry;
        }

        public static int Execute(IReadOnlyList<HierarchyOrganizationEntry> entries)
        {
            var included = entries.Where(entry => entry.include).Select(Validate).ToList();
            var invalid = included.FirstOrDefault(entry => !string.IsNullOrEmpty(entry.warning));
            if (invalid != null) throw new InvalidOperationException($"整理计划无效：{invalid.objectPath}\n{invalid.warning}");
            if (included.Count == 0) throw new InvalidOperationException("没有勾选任何整理项。");

            using (var undo = new BentoUndoScope("Bento 整理 Hierarchy"))
            {
                foreach (var entry in included)
                {
                    var gameObject = (GameObject)EditorUtility.InstanceIDToObject(entry.objectInstanceId);
                    var parentObject = (GameObject)EditorUtility.InstanceIDToObject(entry.parentInstanceId);
                    Undo.SetTransformParent(gameObject.transform, parentObject.transform, "Bento 整理 Hierarchy");
                    EditorSceneManager.MarkSceneDirty(gameObject.scene);
                }
                undo.Complete();
            }
            return included.Count;
        }

        public static bool HasReparentDeformationRisk(Transform transform, Transform targetParent)
        {
            if (transform == null || targetParent == null) return false;
            Matrix4x4 targetLocalMatrix = targetParent.worldToLocalMatrix * transform.localToWorldMatrix;
            Matrix4x4 reconstructed = Matrix4x4.TRS(
                targetLocalMatrix.GetColumn(3),
                targetLocalMatrix.rotation,
                targetLocalMatrix.lossyScale);
            for (int row = 0; row < 4; row++)
            for (int column = 0; column < 4; column++)
                if (Mathf.Abs(targetLocalMatrix[row, column] - reconstructed[row, column]) > MatrixTolerance)
                    return true;
            return false;
        }

        public static GameObject CreateParentForSelection(string groupName)
        {
            List<GameObject> selected = BentoUtility.GetTopLevelSelectedSceneObjects(true).ToList();
            if (selected.Count == 0) throw new InvalidOperationException("没有选中可整理的场景物体。");
            if (selected.Select(gameObject => gameObject.scene).Distinct().Count() != 1)
                throw new InvalidOperationException("不能为跨场景选择创建同一个父物体。");

            Transform commonParent = FindCommonParent(selected.Select(gameObject => gameObject.transform).ToArray());
            Vector3 center = selected.Aggregate(Vector3.zero, (sum, gameObject) => sum + gameObject.transform.position) / selected.Count;
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
            var candidates = new List<Transform>();
            for (Transform current = transforms[0].parent; current != null; current = current.parent)
                candidates.Add(current);
            return candidates.FirstOrDefault(candidate =>
                transforms.All(transform => transform == candidate || transform.IsChildOf(candidate)));
        }

        static string GetPath(Transform transform)
        {
            string path = transform.name;
            for (Transform parent = transform.parent; parent != null; parent = parent.parent)
                path = parent.name + "/" + path;
            return path;
        }
    }
}

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

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Bento 整理 Hierarchy");
            foreach (var entry in included)
            {
                var gameObject = (GameObject)EditorUtility.InstanceIDToObject(entry.objectInstanceId);
                var parentObject = (GameObject)EditorUtility.InstanceIDToObject(entry.parentInstanceId);
                Undo.SetTransformParent(gameObject.transform, parentObject.transform, "Bento 整理 Hierarchy");
                EditorSceneManager.MarkSceneDirty(gameObject.scene);
            }
            Undo.CollapseUndoOperations(undoGroup);
            return included.Count;
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

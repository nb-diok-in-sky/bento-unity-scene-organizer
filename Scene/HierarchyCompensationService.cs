using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Bento
{
    public static class HierarchyCompensationService
    {
        const string NodePrefix = "__Bento_TransformCompensation__";

        public static HierarchyCompensationPreview Preview(
            IReadOnlyList<HierarchyOrganizationEntry> risks,
            Transform targetParent,
            HierarchyCompensationMode mode)
        {
            var preview = new HierarchyCompensationPreview { mode = mode };
            if (targetParent == null)
            {
                preview.reason = "目标父物体已失效。";
                return preview;
            }

            preview.entries.AddRange(risks.Where(entry => entry != null));
            if (preview.entries.Count == 0)
            {
                preview.reason = "没有需要补偿的风险对象。";
                return preview;
            }

            if (mode == HierarchyCompensationMode.Shared && !CanShareParent(preview.entries, out string reason))
            {
                preview.reason = reason;
                return preview;
            }

            preview.canExecute = true;
            preview.compensationNodeCount =
                mode == HierarchyCompensationMode.Shared ? 1 : preview.entries.Count;
            return preview;
        }

        public static int Execute(HierarchyCompensationPreview preview, Transform targetParent)
        {
            if (preview == null || !preview.canExecute)
                throw new InvalidOperationException(preview?.reason ?? "补偿计划无效。");
            if (targetParent == null)
                throw new InvalidOperationException("目标父物体已失效。");

            List<GameObject> objects = preview.entries
                .Select(entry => EditorUtility.InstanceIDToObject(entry.objectInstanceId) as GameObject)
                .Where(gameObject => gameObject != null)
                .ToList();
            if (objects.Count != preview.entries.Count)
                throw new InvalidOperationException("部分风险对象已失效，请重新扫描。");

            using (var undo = new BentoUndoScope("Bento 安全归入并补偿"))
            {
                if (preview.mode == HierarchyCompensationMode.Shared)
                    CreateSharedCompensation(objects, targetParent);
                else
                    foreach (GameObject gameObject in objects)
                        CreateIndividualCompensation(gameObject, targetParent);

                foreach (GameObject gameObject in objects)
                    EditorSceneManager.MarkSceneDirty(gameObject.scene);
                undo.Complete();
            }
            return preview.compensationNodeCount;
        }

        static void CreateIndividualCompensation(GameObject gameObject, Transform targetParent)
        {
            Vector3 worldPosition = gameObject.transform.position;
            Quaternion worldRotation = gameObject.transform.rotation;
            Vector3 worldScale = gameObject.transform.lossyScale;
            var compensation = new GameObject(NodePrefix + gameObject.name);
            Undo.RegisterCreatedObjectUndo(compensation, "Bento 创建独立补偿节点");
            Undo.SetTransformParent(compensation.transform, targetParent, "Bento 设置补偿节点父级");
            compensation.transform.SetPositionAndRotation(worldPosition, worldRotation);
            SetWorldScale(compensation.transform, worldScale);
            Undo.SetTransformParent(gameObject.transform, compensation.transform, "Bento 归入独立补偿节点");
            Undo.RecordObject(gameObject.transform, "Bento 重置补偿对象变换");
            gameObject.transform.localPosition = Vector3.zero;
            gameObject.transform.localRotation = Quaternion.identity;
            gameObject.transform.localScale = Vector3.one;
        }

        static void CreateSharedCompensation(IReadOnlyList<GameObject> objects, Transform targetParent)
        {
            Transform sourceParent = objects[0].transform.parent;
            var compensation = new GameObject(NodePrefix + "Shared");
            Undo.RegisterCreatedObjectUndo(compensation, "Bento 创建共享补偿节点");
            Undo.SetTransformParent(compensation.transform, targetParent, "Bento 设置共享补偿节点父级");
            CopyWorldTransform(sourceParent, compensation.transform);

            foreach (GameObject gameObject in objects)
                Undo.SetTransformParent(gameObject.transform, compensation.transform, "Bento 归入共享补偿节点");
        }

        static bool CanShareParent(
            IEnumerable<HierarchyOrganizationEntry> entries,
            out string reason)
        {
            Transform commonParent = null;
            foreach (HierarchyOrganizationEntry entry in entries)
            {
                var gameObject = EditorUtility.InstanceIDToObject(entry.objectInstanceId) as GameObject;
                if (gameObject == null)
                {
                    reason = "部分风险对象已失效。";
                    return false;
                }

                if (commonParent == null)
                    commonParent = gameObject.transform.parent;
                else if (gameObject.transform.parent != commonParent)
                {
                    reason = "共享补偿仅适用于原本属于同一父节点的风险对象。";
                    return false;
                }
            }

            if (commonParent == null)
            {
                reason = "根节点对象没有可复用的共同父级变换。";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        static void CopyWorldTransform(Transform source, Transform target)
        {
            target.SetPositionAndRotation(source.position, source.rotation);
            SetWorldScale(target, source.lossyScale);
        }

        static void SetWorldScale(Transform transform, Vector3 worldScale)
        {
            Vector3 parentScale = transform.parent == null ? Vector3.one : transform.parent.lossyScale;
            transform.localScale = new Vector3(
                SafeDivide(worldScale.x, parentScale.x),
                SafeDivide(worldScale.y, parentScale.y),
                SafeDivide(worldScale.z, parentScale.z));
        }

        static float SafeDivide(float value, float divisor) =>
            Mathf.Abs(divisor) < 0.000001f ? value : value / divisor;
    }
}

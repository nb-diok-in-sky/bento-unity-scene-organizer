using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Bento
{
    /// <summary>
    /// Reparenting deforms objects when the target parent is rotated and non-uniformly scaled, because a
    /// child's local position/rotation/scale cannot express the resulting shear. An empty parent can simply be
    /// made "clean" (world rotation zero, world scale one, same position) without moving anything else.
    /// </summary>
    static class HierarchyParentCleanupService
    {
        const float AngleTolerance = 0.01f;
        const float ScaleTolerance = 0.0001f;

        /// <summary>Returns why the parent cannot be cleaned, or null when cleaning is safe and useful.</summary>
        public static string GetBlocker(Transform parent)
        {
            if (parent == null) return "目标父物体已失效。";
            if (parent.childCount > 0) return "目标父物体下已有子物体，整理它会带动这些子物体一起移动，请使用下方的补偿方案。";
            if (IsClean(parent)) return "目标父物体本身已经是干净的（无旋转、缩放为 1），形变来自更上层的父级，请使用下方的补偿方案。";
            return null;
        }

        public static bool IsClean(Transform transform) =>
            Quaternion.Angle(transform.rotation, Quaternion.identity) < AngleTolerance &&
            (transform.lossyScale - Vector3.one).sqrMagnitude < ScaleTolerance;

        /// <summary>Keeps the world position, zeroes the world rotation and makes the world scale one.</summary>
        public static void Clean(Transform parent)
        {
            string blocker = GetBlocker(parent);
            if (blocker != null) throw new System.InvalidOperationException(blocker);

            Undo.RecordObject(parent, "Bento 整理父物体");
            Vector3 position = parent.position;
            parent.rotation = Quaternion.identity;
            Vector3 inherited = parent.parent != null ? parent.parent.lossyScale : Vector3.one;
            parent.localScale = new Vector3(Inverse(inherited.x), Inverse(inherited.y), Inverse(inherited.z));
            parent.position = position;

            if (PrefabUtility.IsPartOfPrefabInstance(parent))
                PrefabUtility.RecordPrefabInstancePropertyModifications(parent);
            EditorUtility.SetDirty(parent);
            if (parent.gameObject.scene.IsValid()) EditorSceneManager.MarkSceneDirty(parent.gameObject.scene);
        }

        static float Inverse(float value) => Mathf.Abs(value) < 1e-6f ? 1f : 1f / value;
    }
}

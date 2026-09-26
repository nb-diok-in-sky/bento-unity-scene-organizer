using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Bento
{
    static class HierarchyTransformRiskAnalyzer
    {
        const float MatrixTolerance = 0.0001f;

        public static List<HierarchyOrganizationEntry> FindRisks(
            IEnumerable<HierarchyOrganizationEntry> entries,
            Transform targetParent)
        {
            if (targetParent == null)
                return new List<HierarchyOrganizationEntry>();

            return entries
                .Where(entry => string.IsNullOrEmpty(entry.warning))
                .Where(entry =>
                {
                    var gameObject = EditorUtility.InstanceIDToObject(entry.objectInstanceId) as GameObject;
                    return gameObject != null && HasReparentDeformationRisk(gameObject.transform, targetParent);
                })
                .ToList();
        }

        public static bool HasReparentDeformationRisk(Transform transform, Transform targetParent)
        {
            if (transform == null || targetParent == null) return false;

            Matrix4x4 targetLocalMatrix = targetParent.worldToLocalMatrix * transform.localToWorldMatrix;
            Vector4 positionColumn = targetLocalMatrix.GetColumn(3);
            Matrix4x4 reconstructed = Matrix4x4.TRS(
                new Vector3(positionColumn.x, positionColumn.y, positionColumn.z),
                targetLocalMatrix.rotation,
                targetLocalMatrix.lossyScale);

            for (int row = 0; row < 4; row++)
            for (int column = 0; column < 4; column++)
                if (Mathf.Abs(targetLocalMatrix[row, column] - reconstructed[row, column]) > MatrixTolerance)
                    return true;

            return false;
        }
    }
}

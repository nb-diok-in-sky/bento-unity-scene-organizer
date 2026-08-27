using System.Collections.Generic;
using UnityEngine;

namespace Bento
{
    readonly struct SceneTransformedBounds
    {
        public SceneTransformedBounds(
            Vector3 center,
            Vector3 halfEdgeX,
            Vector3 halfEdgeY,
            Vector3 halfEdgeZ)
        {
            Center = center;
            HalfEdgeX = halfEdgeX;
            HalfEdgeY = halfEdgeY;
            HalfEdgeZ = halfEdgeZ;
        }

        public Vector3 Center { get; }
        public Vector3 HalfEdgeX { get; }
        public Vector3 HalfEdgeY { get; }
        public Vector3 HalfEdgeZ { get; }

        public Vector3 HalfEdge(int index)
        {
            switch (index)
            {
                case 0: return HalfEdgeX;
                case 1: return HalfEdgeY;
                default: return HalfEdgeZ;
            }
        }
    }

    static class SceneTransformedBoundsUtility
    {
        const float AxisEpsilon = 0.0000001f;

        public static bool TryBuild(Renderer renderer, out SceneTransformedBounds bounds)
        {
            bounds = default;
            if (renderer == null) return false;

            Bounds localBounds = renderer.localBounds;
            if (localBounds.size.sqrMagnitude <= AxisEpsilon) return false;

            Matrix4x4 matrix = renderer.localToWorldMatrix;
            bounds = new SceneTransformedBounds(
                matrix.MultiplyPoint3x4(localBounds.center),
                matrix.MultiplyVector(Vector3.right * localBounds.extents.x),
                matrix.MultiplyVector(Vector3.up * localBounds.extents.y),
                matrix.MultiplyVector(Vector3.forward * localBounds.extents.z));
            return true;
        }

        public static bool Intersects(
            SceneTransformedBounds first,
            SceneTransformedBounds second,
            out float minimumOverlap)
        {
            minimumOverlap = float.PositiveInfinity;
            Vector3 centerDelta = second.Center - first.Center;
            foreach (Vector3 axis in SeparationAxes(first, second))
            {
                float sqrMagnitude = axis.sqrMagnitude;
                if (sqrMagnitude <= AxisEpsilon) continue;
                Vector3 normalized = axis / Mathf.Sqrt(sqrMagnitude);
                float firstRadius = ProjectionRadius(first, normalized);
                float secondRadius = ProjectionRadius(second, normalized);
                float overlap = firstRadius + secondRadius - Mathf.Abs(Vector3.Dot(centerDelta, normalized));
                if (overlap <= 0f)
                {
                    minimumOverlap = 0f;
                    return false;
                }
                minimumOverlap = Mathf.Min(minimumOverlap, overlap);
            }

            if (!float.IsPositiveInfinity(minimumOverlap)) return true;
            minimumOverlap = 0f;
            return false;
        }

        static IEnumerable<Vector3> SeparationAxes(
            SceneTransformedBounds first,
            SceneTransformedBounds second)
        {
            yield return Vector3.Cross(first.HalfEdgeY, first.HalfEdgeZ);
            yield return Vector3.Cross(first.HalfEdgeZ, first.HalfEdgeX);
            yield return Vector3.Cross(first.HalfEdgeX, first.HalfEdgeY);
            yield return Vector3.Cross(second.HalfEdgeY, second.HalfEdgeZ);
            yield return Vector3.Cross(second.HalfEdgeZ, second.HalfEdgeX);
            yield return Vector3.Cross(second.HalfEdgeX, second.HalfEdgeY);

            for (int firstIndex = 0; firstIndex < 3; firstIndex++)
            for (int secondIndex = 0; secondIndex < 3; secondIndex++)
                yield return Vector3.Cross(
                    first.HalfEdge(firstIndex),
                    second.HalfEdge(secondIndex));
        }

        static float ProjectionRadius(SceneTransformedBounds bounds, Vector3 axis)
        {
            return Mathf.Abs(Vector3.Dot(bounds.HalfEdgeX, axis)) +
                   Mathf.Abs(Vector3.Dot(bounds.HalfEdgeY, axis)) +
                   Mathf.Abs(Vector3.Dot(bounds.HalfEdgeZ, axis));
        }
    }
}

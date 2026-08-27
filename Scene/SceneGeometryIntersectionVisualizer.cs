using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Bento
{
    [InitializeOnLoad]
    static class SceneGeometryIntersectionVisualizer
    {
        static bool _enabled;

        static SceneGeometryIntersectionVisualizer()
        {
            SceneView.duringSceneGui += DrawSceneMarkers;
        }

        public static bool Enabled
        {
            get => _enabled;
            set
            {
                if (_enabled == value) return;
                _enabled = value;
                SceneView.RepaintAll();
            }
        }

        public static void Repaint() => SceneView.RepaintAll();

        static void DrawSceneMarkers(SceneView sceneView)
        {
            SceneGeometryScanResult result = SceneGeometryIntersectionSession.Current;
            if (!_enabled || result == null || result.intersections.Count == 0) return;

            CompareFunction previousZTest = Handles.zTest;
            Handles.zTest = CompareFunction.LessEqual;
            var drawnFirst = new HashSet<int>();
            var drawnSecond = new HashSet<int>();

            foreach (SceneGeometryIntersectionRecord record in result.intersections.Where(item => item.include))
            {
                if (!DrawMarker(record.firstInstanceId, new Color(0.1f, 0.85f, 1f, 0.95f), drawnFirst, out Vector3 firstCenter) ||
                    !DrawMarker(record.secondInstanceId, new Color(1f, 0.25f, 0.15f, 0.95f), drawnSecond, out Vector3 secondCenter))
                    continue;

                Handles.color = record.kind == SceneGeometryIntersectionKind.ColliderPenetration
                    ? new Color(1f, 0.1f, 0.1f, 0.9f)
                    : new Color(1f, 0.75f, 0.1f, 0.75f);
                Handles.DrawDottedLine(firstCenter, secondCenter, 4f);
            }

            Handles.zTest = previousZTest;
        }

        static bool DrawMarker(
            int instanceId,
            Color color,
            ISet<int> drawn,
            out Vector3 center)
        {
            center = Vector3.zero;
            var target = EditorUtility.InstanceIDToObject(instanceId) as GameObject;
            if (target == null) return false;
            Renderer[] renderers = target.GetComponents<Renderer>()
                .Where(renderer => renderer != null && renderer.enabled)
                .ToArray();
            if (renderers.Length == 0) return false;
            center = renderers[0].bounds.center;
            for (int index = 1; index < renderers.Length; index++)
                center += renderers[index].bounds.center;
            center /= renderers.Length;

            if (!drawn.Add(instanceId)) return true;
            Handles.color = color;
            foreach (Renderer renderer in renderers)
            {
                Matrix4x4 previousMatrix = Handles.matrix;
                Handles.matrix = renderer.localToWorldMatrix;
                Bounds localBounds = renderer.localBounds;
                Handles.DrawWireCube(localBounds.center, localBounds.size);
                Handles.matrix = previousMatrix;
            }
            return true;
        }
    }
}

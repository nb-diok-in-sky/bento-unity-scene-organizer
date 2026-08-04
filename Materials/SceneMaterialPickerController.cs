using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Bento
{
    sealed class SceneMaterialPickerController : IDisposable
    {
        static readonly MethodInfo IntersectRayMeshMethod = typeof(HandleUtility).GetMethod(
            "IntersectRayMesh",
            BindingFlags.Static | BindingFlags.NonPublic,
            null,
            new[] { typeof(Ray), typeof(Mesh), typeof(Matrix4x4), typeof(RaycastHit).MakeByRefType() },
            null);

        bool _active;
        string _status = "未启用";

        public bool IsActive => _active;
        public string Status => _status;

        public void Start()
        {
            if (_active) return;
            _active = true;
            _status = "请在 Scene 视图中点击模型表面；按 Esc 取消。";
            SceneView.duringSceneGui += OnSceneGUI;
            SceneView.RepaintAll();
        }

        public void Stop(string status = "已停止")
        {
            if (_active) SceneView.duringSceneGui -= OnSceneGUI;
            _active = false;
            _status = status;
            SceneView.RepaintAll();
        }

        public void Dispose() => Stop();

        void OnSceneGUI(SceneView sceneView)
        {
            Event current = Event.current;
            if (!_active || current == null) return;

            Handles.BeginGUI();
            GUI.Label(new Rect(12, 12, 500, 26), "Bento 材质吸管：点击哪块表面，就定位哪颗材质；Esc 退出", EditorStyles.helpBox);
            Handles.EndGUI();

            HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
            if (current.type == EventType.KeyDown && current.keyCode == KeyCode.Escape)
            {
                current.Use();
                Stop("已取消材质吸取");
                return;
            }

            if (current.type != EventType.MouseDown || current.button != 0 || current.alt) return;

            Vector2 mousePosition = current.mousePosition;
            GameObject pickedObject = HandleUtility.PickGameObject(mousePosition, false);
            Ray worldRay = HandleUtility.GUIPointToWorldRay(mousePosition);
            current.Use();

            Renderer renderer = ResolveRenderer(pickedObject);
            if (renderer == null)
            {
                _status = "没有点到带 Renderer 的模型，请重试。";
                sceneView.Repaint();
                return;
            }

            if (!TryPickMaterial(renderer, worldRay, out Material material, out string failure))
            {
                _status = failure;
                sceneView.Repaint();
                return;
            }

            Selection.activeObject = material;
            EditorGUIUtility.PingObject(material);
            Stop($"已从“{renderer.name}”定位材质：{material.name}");
        }

        static Renderer ResolveRenderer(GameObject pickedObject)
        {
            if (pickedObject == null) return null;
            Renderer renderer = pickedObject.GetComponent<Renderer>();
            if (renderer != null) return renderer;

            renderer = pickedObject.GetComponentInChildren<Renderer>();
            return renderer != null ? renderer : pickedObject.GetComponentInParent<Renderer>();
        }

        static bool TryPickMaterial(Renderer renderer, Ray worldRay, out Material material, out string failure)
        {
            material = null;
            failure = null;
            Mesh temporaryMesh = null;
            try
            {
                Mesh mesh;
                Matrix4x4 localToWorld;
                if (renderer is SkinnedMeshRenderer skinned)
                {
                    if (skinned.sharedMesh == null)
                    {
                        failure = "点击对象没有可用的 SkinnedMesh。";
                        return false;
                    }

                    temporaryMesh = new Mesh { name = "Bento_MaterialPicker_TemporaryMesh" };
                    skinned.BakeMesh(temporaryMesh);
                    mesh = temporaryMesh;
                    localToWorld = skinned.transform.localToWorldMatrix;
                }
                else
                {
                    MeshFilter meshFilter = renderer.GetComponent<MeshFilter>();
                    mesh = meshFilter != null ? meshFilter.sharedMesh : null;
                    localToWorld = renderer.transform.localToWorldMatrix;
                }

                if (mesh == null)
                {
                    failure = $"“{renderer.name}”没有可拾取的 Mesh。";
                    return false;
                }

                if (!TryIntersectEditorMesh(worldRay, mesh, localToWorld, out int triangleIndex))
                {
                    failure = $"已点到“{renderer.name}”，但没有命中它的三角面。请点击可见表面中央。";
                    return false;
                }

                int subMeshIndex = FindSubMeshIndex(mesh, triangleIndex);
                Material[] materials = renderer.sharedMaterials;
                if (subMeshIndex < 0 || subMeshIndex >= materials.Length)
                {
                    failure = $"已命中“{renderer.name}”，但子网格 {subMeshIndex} 没有对应材质槽。";
                    return false;
                }

                material = materials[subMeshIndex];
                if (material != null) return true;
                failure = $"“{renderer.name}”的材质槽 {subMeshIndex} 为空。";
                return false;
            }
            finally
            {
                if (temporaryMesh != null) UnityEngine.Object.DestroyImmediate(temporaryMesh);
            }
        }

        static bool TryIntersectEditorMesh(Ray ray, Mesh mesh, Matrix4x4 matrix, out int triangleIndex)
        {
            triangleIndex = -1;
            if (IntersectRayMeshMethod != null)
            {
                RaycastHit hit = default;
                object[] parameters = { ray, mesh, matrix, hit };
                try
                {
                    bool intersected = (bool)IntersectRayMeshMethod.Invoke(null, parameters);
                    if (intersected) triangleIndex = ((RaycastHit)parameters[3]).triangleIndex;
                    return intersected;
                }
                catch (TargetInvocationException exception)
                {
                    Debug.LogException(exception.InnerException ?? exception);
                }
            }

            return mesh.isReadable && TryIntersectReadableMesh(ray, mesh, matrix, out triangleIndex);
        }

        static bool TryIntersectReadableMesh(Ray ray, Mesh mesh, Matrix4x4 matrix, out int triangleIndex)
        {
            float bestDistance = float.PositiveInfinity;
            triangleIndex = -1;
            int accumulatedTriangleCount = 0;
            Vector3[] vertices = mesh.vertices;

            for (int subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
            {
                if (mesh.GetTopology(subMesh) != MeshTopology.Triangles) continue;
                int[] indices = mesh.GetIndices(subMesh);
                for (int index = 0; index + 2 < indices.Length; index += 3)
                {
                    Vector3 a = matrix.MultiplyPoint3x4(vertices[indices[index]]);
                    Vector3 b = matrix.MultiplyPoint3x4(vertices[indices[index + 1]]);
                    Vector3 c = matrix.MultiplyPoint3x4(vertices[indices[index + 2]]);
                    if (!RayIntersectsTriangle(ray, a, b, c, out float distance) || distance >= bestDistance) continue;
                    bestDistance = distance;
                    triangleIndex = accumulatedTriangleCount + index / 3;
                }
                accumulatedTriangleCount += indices.Length / 3;
            }

            return triangleIndex >= 0;
        }

        static int FindSubMeshIndex(Mesh mesh, int triangleIndex)
        {
            int triangleOffset = 0;
            for (int subMeshIndex = 0; subMeshIndex < mesh.subMeshCount; subMeshIndex++)
            {
                if (mesh.GetTopology(subMeshIndex) != MeshTopology.Triangles) continue;
                int triangleCount = (int)mesh.GetIndexCount(subMeshIndex) / 3;
                if (triangleIndex >= triangleOffset && triangleIndex < triangleOffset + triangleCount) return subMeshIndex;
                triangleOffset += triangleCount;
            }
            return -1;
        }

        static bool RayIntersectsTriangle(Ray ray, Vector3 a, Vector3 b, Vector3 c, out float distance)
        {
            const float epsilon = 0.000001f;
            Vector3 edge1 = b - a;
            Vector3 edge2 = c - a;
            Vector3 p = Vector3.Cross(ray.direction, edge2);
            float determinant = Vector3.Dot(edge1, p);
            if (Mathf.Abs(determinant) < epsilon) { distance = 0f; return false; }

            float inverse = 1f / determinant;
            Vector3 t = ray.origin - a;
            float u = Vector3.Dot(t, p) * inverse;
            if (u < 0f || u > 1f) { distance = 0f; return false; }

            Vector3 q = Vector3.Cross(t, edge1);
            float v = Vector3.Dot(ray.direction, q) * inverse;
            if (v < 0f || u + v > 1f) { distance = 0f; return false; }

            distance = Vector3.Dot(edge2, q) * inverse;
            return distance > epsilon;
        }
    }
}

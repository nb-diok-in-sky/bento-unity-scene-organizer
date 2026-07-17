using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Bento
{
    public static class AssetInfoCollector
    {
        public static AssetInfoExportDocument Collect(IEnumerable<UnityEngine.Object> selection)
        {
            var document = new AssetInfoExportDocument { exportedAtUtc = DateTime.UtcNow.ToString("O") };
            foreach (var asset in ExpandSelection(selection))
            {
                string path = AssetDatabase.GetAssetPath(asset);
                if (!(asset is GameObject root) || string.IsNullOrEmpty(path)) continue;
                document.assets.Add(CollectAsset(root, path));
            }
            return document;
        }

        static AssetInfoRecord CollectAsset(GameObject root, string path)
        {
            var record = new AssetInfoRecord
            {
                originalName = Path.GetFileName(path),
                format = Path.GetExtension(path).TrimStart('.').ToLowerInvariant(),
                path = Path.GetFullPath(path),
                sourceAsset = ResolveSourceAsset(root, path)
            };
            var materials = new HashSet<Material>();
            var textures = new HashSet<Texture>();
            Bounds? combinedBounds = null;

            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            {
                var node = CollectNode(root.transform, transform, materials, textures, ref combinedBounds);
                record.nodes.Add(node);
                record.rendererCount += string.IsNullOrEmpty(node.rendererType) ? 0 : 1;
                record.colliderCount += node.colliders.Count;
            }

            record.nodeCount = record.nodes.Count;
            record.materialCount = materials.Count;
            record.textureCount = textures.Count;
            Vector3 size = combinedBounds?.size ?? Vector3.zero;
            record.boundsSize = new AssetInfoBounds { x = size.x, y = size.y, z = size.z };
            return record;
        }

        static AssetInfoNode CollectNode(
            Transform assetRoot,
            Transform transform,
            HashSet<Material> materialSet,
            HashSet<Texture> textureSet,
            ref Bounds? combinedBounds)
        {
            var gameObject = transform.gameObject;
            var node = new AssetInfoNode
            {
                name = gameObject.name,
                hierarchyPath = BuildHierarchyPath(assetRoot, transform),
                activeSelf = gameObject.activeSelf,
                localPosition = transform.localPosition,
                localEulerAngles = transform.localEulerAngles,
                localScale = transform.localScale,
                components = gameObject.GetComponents<Component>()
                    .Select(component => component == null ? "Missing Script" : component.GetType().FullName)
                    .ToList()
            };

            var renderer = gameObject.GetComponent<Renderer>();
            if (renderer != null)
            {
                node.rendererType = renderer.GetType().Name;
                combinedBounds = combinedBounds.HasValue ? Encapsulate(combinedBounds.Value, renderer.bounds) : renderer.bounds;
                foreach (var material in renderer.sharedMaterials.Where(material => material != null))
                    node.materials.Add(CollectMaterial(material, materialSet, textureSet));
            }

            var meshFilter = gameObject.GetComponent<MeshFilter>();
            var skinnedRenderer = gameObject.GetComponent<SkinnedMeshRenderer>();
            var mesh = meshFilter != null ? meshFilter.sharedMesh : skinnedRenderer != null ? skinnedRenderer.sharedMesh : null;
            if (mesh != null)
            {
                node.mesh = mesh.name;
                node.meshAssetPath = AssetDatabase.GetAssetPath(mesh);
            }

            foreach (var collider in gameObject.GetComponents<Collider>())
                node.colliders.Add(new AssetInfoCollider { type = collider.GetType().Name, enabled = collider.enabled, isTrigger = collider.isTrigger });
            return node;
        }

        static AssetInfoMaterial CollectMaterial(Material material, HashSet<Material> materials, HashSet<Texture> textures)
        {
            materials.Add(material);
            var info = new AssetInfoMaterial
            {
                name = material.name,
                assetPath = AssetDatabase.GetAssetPath(material),
                shader = material.shader != null ? material.shader.name : string.Empty
            };
            if (material.shader == null) return info;
            for (int index = 0; index < ShaderUtil.GetPropertyCount(material.shader); index++)
            {
                if (ShaderUtil.GetPropertyType(material.shader, index) != ShaderUtil.ShaderPropertyType.TexEnv) continue;
                string property = ShaderUtil.GetPropertyName(material.shader, index);
                var texture = material.GetTexture(property);
                if (texture == null) continue;
                textures.Add(texture);
                info.textures.Add(new AssetInfoTexture
                {
                    property = property,
                    name = texture.name,
                    assetPath = AssetDatabase.GetAssetPath(texture)
                });
            }
            return info;
        }

        static IEnumerable<UnityEngine.Object> ExpandSelection(IEnumerable<UnityEngine.Object> selection)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var selected in selection.Where(item => item != null))
            {
                string path = AssetDatabase.GetAssetPath(selected);
                if (AssetDatabase.IsValidFolder(path))
                {
                    foreach (string guid in AssetDatabase.FindAssets("t:GameObject", new[] { path }))
                    {
                        string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                        if (seen.Add(assetPath)) yield return AssetDatabase.LoadMainAssetAtPath(assetPath);
                    }
                }
                else if (seen.Add(path)) yield return selected;
            }
        }

        static string ResolveSourceAsset(GameObject root, string path)
        {
            if (!path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)) return string.Empty;
            var mesh = root.GetComponentsInChildren<MeshFilter>(true).Select(item => item.sharedMesh).FirstOrDefault(item => item != null);
            string meshPath = AssetDatabase.GetAssetPath(mesh);
            return string.Equals(meshPath, path, StringComparison.OrdinalIgnoreCase) ? string.Empty : ToFullPath(meshPath);
        }

        static string ToFullPath(string path) => string.IsNullOrEmpty(path) ? string.Empty : Path.GetFullPath(path);
        static Bounds Encapsulate(Bounds current, Bounds next) { current.Encapsulate(next); return current; }
        static string BuildHierarchyPath(Transform root, Transform current)
        {
            string path = current.name;
            while (current != root && current.parent != null) { current = current.parent; path = current.name + "/" + path; }
            return path;
        }
    }

    public static class AssetInfoJsonExporter
    {
        public static void Export(AssetInfoExportDocument document, string absolutePath)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            string directory = Path.GetDirectoryName(absolutePath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(absolutePath, JsonUtility.ToJson(document, true), new System.Text.UTF8Encoding(false));
            AssetDatabase.Refresh();
        }
    }
}

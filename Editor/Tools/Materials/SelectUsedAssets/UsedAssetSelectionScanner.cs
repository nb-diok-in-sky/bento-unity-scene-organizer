using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Bento
{
    static class UsedAssetSelectionScanner
    {
        public static List<GameObject> ResolveCurrentRoots()
        {
            return ResolveRoots(Selection.objects);
        }

        public static List<GameObject> ResolveRoots(IEnumerable<UnityEngine.Object> selection)
        {
            return selection
                .Select(ResolveGameObject)
                .Where(root => root != null)
                .Distinct()
                .ToList();
        }

        public static UsedAssetSelectionResult Scan(IEnumerable<GameObject> roots)
        {
            var result = new UsedAssetSelectionResult();
            result.roots.AddRange(roots.Where(root => root != null).Distinct());

            Renderer[] renderers = result.roots
                .SelectMany(root => root.GetComponentsInChildren<Renderer>(true))
                .Where(UsedAssetSelectionFilter.IsSupportedRenderer)
                .Distinct()
                .ToArray();

            foreach (Renderer renderer in renderers)
            {
                AddModel(result.models, ResolveMesh(renderer));
                foreach (Material material in renderer.sharedMaterials)
                {
                    if (UsedAssetSelectionFilter.IsEditableMaterial(material) &&
                        !result.materials.Contains(material))
                        result.materials.Add(material);
                }
            }

            foreach (Renderer renderer in renderers)
            foreach (Material material in renderer.sharedMaterials)
                CollectTextures(result, renderer, material);

            Sort(result.models);
            Sort(result.materials);
            Sort(result.textures);
            return result;
        }

        static GameObject ResolveGameObject(UnityEngine.Object selected)
        {
            if (selected is Component component)
                return IsSupportedRoot(component.gameObject) ? component.gameObject : null;
            if (selected is GameObject gameObject)
                return IsSupportedRoot(gameObject) ? gameObject : null;

            string path = selected != null ? AssetDatabase.GetAssetPath(selected) : string.Empty;
            if (string.IsNullOrEmpty(path) || selected is Material || selected is Texture)
                return null;

            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            return IsSupportedRoot(asset) ? asset : null;
        }

        static bool IsSupportedRoot(GameObject gameObject)
        {
            return gameObject != null && gameObject.GetComponent<Terrain>() == null;
        }

        static Mesh ResolveMesh(Renderer renderer)
        {
            if (renderer is SkinnedMeshRenderer skinned)
                return skinned.sharedMesh;
            return renderer.GetComponent<MeshFilter>()?.sharedMesh;
        }

        static void AddModel(ICollection<UnityEngine.Object> models, Mesh mesh)
        {
            if (mesh == null) return;
            string path = AssetDatabase.GetAssetPath(mesh);
            if (string.IsNullOrEmpty(path)) return;

            UnityEngine.Object mainAsset = AssetDatabase.LoadMainAssetAtPath(path) ?? mesh;
            if (!models.Contains(mainAsset))
                models.Add(mainAsset);
        }

        static void CollectTextures(UsedAssetSelectionResult result, Renderer renderer, Material material)
        {
            if (!UsedAssetSelectionFilter.IsEditableMaterial(material) || material.shader == null)
                return;

            foreach (MaterialTextureReference reference in MaterialTextureReferenceReader.Read(material))
            {
                Texture texture = reference.Texture;
                string path = AssetDatabase.GetAssetPath(texture);
                bool included = UsedAssetSelectionFilter.IsProjectTexture(texture);
                result.textureTraces.Add(new TextureScanTrace
                {
                    renderer = renderer != null ? renderer.name : "<null>",
                    material = material.name,
                    property = reference.PropertyName,
                    texture = texture.name,
                    path = path,
                    type = texture.GetType().FullName,
                    isSubAsset = AssetDatabase.IsSubAsset(texture),
                    included = included
                });
                if (included && !result.textures.Contains(texture))
                    result.textures.Add(texture);
            }
        }

        static void Sort<T>(List<T> assets) where T : UnityEngine.Object
        {
            assets.Sort((left, right) => string.Compare(
                AssetDatabase.GetAssetPath(left),
                AssetDatabase.GetAssetPath(right),
                StringComparison.OrdinalIgnoreCase));
        }
    }
}

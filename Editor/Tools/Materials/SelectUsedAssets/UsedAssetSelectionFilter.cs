using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Bento
{
    static class UsedAssetSelectionFilter
    {
        static readonly string[] SupportedTextureExtensions =
        {
            ".png", ".jpg", ".jpeg", ".tga", ".tif", ".tiff",
            ".psd", ".exr", ".hdr", ".bmp", ".gif"
        };

        public static bool IsSupportedRenderer(Renderer renderer)
        {
            if (renderer == null) return false;
            return renderer.GetComponentInParent<Terrain>() == null;
        }

        public static bool IsEditableMaterial(Material material)
        {
            if (material == null) return false;
            string path = AssetDatabase.GetAssetPath(material);
            if (string.IsNullOrEmpty(path)) return false;
            return string.Equals(Path.GetExtension(path), ".mat", StringComparison.OrdinalIgnoreCase) &&
                   AssetDatabase.IsMainAsset(material);
        }

        public static bool IsProjectTexture(Texture texture)
        {
            if (texture == null) return false;
            string path = AssetDatabase.GetAssetPath(texture);
            if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                return false;

            string extension = Path.GetExtension(path);
            if (!Array.Exists(SupportedTextureExtensions,
                    candidate => string.Equals(candidate, extension, StringComparison.OrdinalIgnoreCase)))
                return false;
            if (AssetDatabase.IsSubAsset(texture))
                return false;

            Type mainType = AssetDatabase.GetMainAssetTypeAtPath(path);
            return mainType != typeof(TerrainData) &&
                   mainType != typeof(TerrainLayer) &&
                   typeof(Texture).IsAssignableFrom(mainType);
        }

        public static bool IsModelTextureProperty(string propertyName)
        {
            if (string.IsNullOrEmpty(propertyName)) return false;
            switch (propertyName)
            {
                    return false;
                default:
                    return true;
            }
        }
    }
}

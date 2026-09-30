using System;
using UnityEditor;
using UnityEngine;

namespace Bento
{
    static class ResourceVariantTextureClassifier
    {
        /// <summary>Textures without an asset path (render textures, runtime-created) are never copied.</summary>
        public static VariantTextureDisposition GetDisposition(Texture texture)
        {
            return texture == null || string.IsNullOrEmpty(AssetDatabase.GetAssetPath(texture))
                ? VariantTextureDisposition.RuntimeInternal
                : VariantTextureDisposition.CopyCandidate;
        }

        public static VariantTextureKind Classify(
            Shader shader,
            int propertyIndex,
            string propertyName,
            string propertyDescription,
            Texture texture)
        {
            string semantic = $"{propertyName} {propertyDescription}".ToLowerInvariant();
            if (ContainsAny(semantic, "normal", "bump", "nrm")) return VariantTextureKind.Normal;
            if (ContainsAny(semantic, "base", "albedo", "diffuse", "maintex", "color map")) return VariantTextureKind.BaseColor;
            if (ContainsAny(semantic, "metal", "specular", "smooth", "rough", "gloss")) return VariantTextureKind.Metallic;
            if (ContainsAny(semantic, "occlusion", "ambient occlusion", " ao", "ao ")) return VariantTextureKind.Occlusion;
            if (ContainsAny(semantic, "emission", "emissive", "emit")) return VariantTextureKind.Emission;
            if (ContainsAny(semantic, "height", "parallax", "displace")) return VariantTextureKind.Height;
            if (ContainsAny(semantic, "mask", "detail", "control", "splat")) return VariantTextureKind.Mask;

            string texturePath = AssetDatabase.GetAssetPath(texture);
            if (AssetImporter.GetAtPath(texturePath) is TextureImporter importer && importer.textureType == TextureImporterType.NormalMap)
                return VariantTextureKind.Normal;

            string textureName = texture == null ? string.Empty : texture.name.ToLowerInvariant();
            if (ContainsAny(textureName, "normal", "_nrm", "_nor", "_n")) return VariantTextureKind.Normal;
            if (ContainsAny(textureName, "basecolor", "albedo", "diffuse", "_d", "_a")) return VariantTextureKind.BaseColor;
            if (ContainsAny(textureName, "metal", "rough", "smooth", "spec", "gloss")) return VariantTextureKind.Metallic;
            if (ContainsAny(textureName, "occlusion", "_ao", "ambient")) return VariantTextureKind.Occlusion;
            if (ContainsAny(textureName, "emission", "emissive")) return VariantTextureKind.Emission;
            if (ContainsAny(textureName, "height", "disp", "parallax")) return VariantTextureKind.Height;
            if (ContainsAny(textureName, "mask", "control", "splat")) return VariantTextureKind.Mask;
            return VariantTextureKind.Other;
        }

        public static string GetDisplayName(VariantTextureKind kind)
        {
            switch (kind)
            {
                case VariantTextureKind.BaseColor: return "基础色";
                case VariantTextureKind.Normal: return "法线";
                case VariantTextureKind.Metallic: return "金属/粗糙度";
                case VariantTextureKind.Occlusion: return "AO/遮蔽";
                case VariantTextureKind.Emission: return "自发光";
                case VariantTextureKind.Height: return "高度/置换";
                case VariantTextureKind.Mask: return "遮罩/控制";
                default: return "其他";
            }
        }

        static bool ContainsAny(string value, params string[] terms)
        {
            foreach (string term in terms)
            {
                if (value.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }
    }
}

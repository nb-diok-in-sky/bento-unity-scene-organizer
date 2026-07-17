using System.Collections.Generic;
using UnityEngine;

namespace Bento
{
    static partial class TexRules
    {

        enum RenderMode { Opaque, Cutout, Transparent }

        static RenderMode DetectRenderMode(UnityEngine.Material mat)
        {
            if (mat.HasProperty("_Mode"))
            {
                int mode = (int)mat.GetFloat("_Mode");
                if (mode == 1) return RenderMode.Cutout;
                if (mode >= 2) return RenderMode.Transparent;
                return RenderMode.Opaque;
            }

            if (mat.HasProperty("_AlphaClip") && (int)mat.GetFloat("_AlphaClip") == 1)
                return RenderMode.Cutout;
            if (mat.HasProperty("_Surface") && (int)mat.GetFloat("_Surface") == 1)
                return RenderMode.Transparent;

            if (mat.IsKeywordEnabled("_ALPHATEST_ON"))
                return RenderMode.Cutout;
            if (mat.IsKeywordEnabled("_ALPHABLEND_ON") ||
                mat.IsKeywordEnabled("_ALPHAPREMULTIPLY_ON") ||
                mat.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT"))
                return RenderMode.Transparent;

            string renderType = mat.GetTag("RenderType", false);
            if (renderType == "TransparentCutout") return RenderMode.Cutout;
            if (renderType == "Transparent") return RenderMode.Transparent;

            int q = mat.renderQueue;
            if (q > 2000 && q <= 2500) return RenderMode.Cutout;
            if (q >= 3000) return RenderMode.Transparent;

            return RenderMode.Opaque;
        }

        static float DetectCutoff(UnityEngine.Material mat)
        {
            string[] cutoffNames = { "_Cutoff", "_AlphaCutoff", "_Alpha_Cutoff", "_AlphaClipThreshold" };
            foreach (var name in cutoffNames)
                if (mat.HasProperty(name))
                    return mat.GetFloat(name);
            return 0.5f;
        }

        public static void MigrateShader(UnityEngine.Material mat, UnityEngine.Shader newShader)
        {
            var detectedMode = DetectRenderMode(mat);
            float savedCutoff = DetectCutoff(mat);

            var savedTex = new Dictionary<string, UnityEngine.Texture>();
            foreach (var (src, _) in TexMigrations)
            {
                if (!mat.HasProperty(src)) continue;
                var t = mat.GetTexture(src);
                if (t != null) savedTex.TryAdd(src, t);
            }

            var savedCol = new Dictionary<string, UnityEngine.Color>();
            foreach (var (src, _) in ColorMigrations)
            {
                if (!mat.HasProperty(src)) continue;
                var c = mat.GetColor(src);
                if (detectedMode == RenderMode.Opaque && c.a < 0.01f)
                    c.a = 1f;
                savedCol.TryAdd(src, c);
            }

            var savedFloat = new Dictionary<string, float>();
            foreach (var (src, _) in FloatMigrations)
            {
                if (!mat.HasProperty(src)) continue;
                savedFloat.TryAdd(src, mat.GetFloat(src));
            }

            mat.shader = newShader;

            foreach (var (src, dst) in TexMigrations)
            {
                if (!mat.HasProperty(dst)) continue;
                if (mat.GetTexture(dst) != null) continue;
                if (savedTex.TryGetValue(src, out var t)) mat.SetTexture(dst, t);
            }

            foreach (var (src, dst) in ColorMigrations)
            {
                if (!mat.HasProperty(dst)) continue;
                if (savedCol.TryGetValue(src, out var c)) mat.SetColor(dst, c);
            }

            foreach (var (src, dst) in FloatMigrations)
            {
                if (!mat.HasProperty(dst)) continue;
                if (savedFloat.TryGetValue(src, out var f)) mat.SetFloat(dst, f);
            }

            ApplyRenderMode(mat, detectedMode, savedCutoff);
            EnableKeywordsForTextures(mat);
        }

        static void ApplyRenderMode(UnityEngine.Material mat, RenderMode mode, float cutoff)
        {
            bool toURP = mat.shader != null && mat.shader.name.Contains("Universal");
            bool toStd = mat.shader != null && mat.shader.name.Contains("Standard");

            if (toURP)
            {
                switch (mode)
                {
                    case RenderMode.Opaque:      SetURPMode(mat, 0, false, cutoff); break;
                    case RenderMode.Cutout:       SetURPMode(mat, 0, true, cutoff); break;
                    case RenderMode.Transparent:  SetURPMode(mat, 1, false, cutoff); break;
                }
            }
            else if (toStd)
            {
                switch (mode)
                {
                    case RenderMode.Opaque:      SetStandardMode(mat, 0, cutoff); break;
                    case RenderMode.Cutout:       SetStandardMode(mat, 1, cutoff); break;
                    case RenderMode.Transparent:  SetStandardMode(mat, 3, cutoff); break;
                }
            }
            else
            {
                // Generic fallback for unknown target shaders
                if (mat.HasProperty("_Cutoff")) mat.SetFloat("_Cutoff", cutoff);
                if (mat.HasProperty("_AlphaCutoff")) mat.SetFloat("_AlphaCutoff", cutoff);
                if (mat.HasProperty("_AlphaClip"))
                    mat.SetFloat("_AlphaClip", mode == RenderMode.Cutout ? 1 : 0);

                switch (mode)
                {
                    case RenderMode.Cutout:
                        mat.EnableKeyword("_ALPHATEST_ON");
                        mat.SetOverrideTag("RenderType", "TransparentCutout");
                        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
                        break;
                    case RenderMode.Transparent:
                        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                        mat.SetOverrideTag("RenderType", "Transparent");
                        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                        break;
                }
            }
        }

        static void SetURPMode(UnityEngine.Material mat, int surface, bool alphaClip, float cutoff)
        {
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", surface);
            if (mat.HasProperty("_AlphaClip")) mat.SetFloat("_AlphaClip", alphaClip ? 1 : 0);
            if (mat.HasProperty("_Cutoff")) mat.SetFloat("_Cutoff", cutoff);

            if (alphaClip) mat.EnableKeyword("_ALPHATEST_ON");
            else mat.DisableKeyword("_ALPHATEST_ON");

            if (surface == 1)
            {
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.SetOverrideTag("RenderType", "Transparent");
                mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                mat.SetFloat("_ZWrite", 0);
                mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            }
            else
            {
                mat.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
                if (alphaClip)
                {
                    mat.SetOverrideTag("RenderType", "TransparentCutout");
                    mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
                }
                else
                {
                    mat.SetOverrideTag("RenderType", "Opaque");
                    mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;
                }
                mat.SetFloat("_ZWrite", 1);
                mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
                mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.Zero);
            }
        }

        static void SetStandardMode(UnityEngine.Material mat, int mode, float cutoff)
        {
            mat.SetFloat("_Mode", mode);
            if (mat.HasProperty("_Cutoff")) mat.SetFloat("_Cutoff", cutoff);
            switch (mode)
            {
                case 0:
                    mat.SetOverrideTag("RenderType", "");
                    mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
                    mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.Zero);
                    mat.SetFloat("_ZWrite", 1);
                    mat.DisableKeyword("_ALPHATEST_ON");
                    mat.DisableKeyword("_ALPHABLEND_ON");
                    mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                    mat.renderQueue = -1;
                    break;
                case 1:
                    mat.SetOverrideTag("RenderType", "TransparentCutout");
                    mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
                    mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.Zero);
                    mat.SetFloat("_ZWrite", 1);
                    mat.EnableKeyword("_ALPHATEST_ON");
                    mat.DisableKeyword("_ALPHABLEND_ON");
                    mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                    mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
                    break;
                case 3:
                    mat.SetOverrideTag("RenderType", "Transparent");
                    mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    mat.SetFloat("_ZWrite", 0);
                    mat.DisableKeyword("_ALPHATEST_ON");
                    mat.DisableKeyword("_ALPHABLEND_ON");
                    mat.EnableKeyword("_ALPHAPREMULTIPLY_ON");
                    mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                    break;
            }
        }

        public static void EnableKeywordsForTextures(UnityEngine.Material mat)
        {
            if (mat.HasProperty("_BumpMap") && mat.GetTexture("_BumpMap") != null)
                mat.EnableKeyword("_NORMALMAP");
            if (mat.HasProperty("_EmissionMap") && mat.GetTexture("_EmissionMap") != null)
            {
                mat.EnableKeyword("_EMISSION");
                mat.globalIlluminationFlags = UnityEngine.MaterialGlobalIlluminationFlags.BakedEmissive;
            }
            if (mat.HasProperty("_MetallicGlossMap") && mat.GetTexture("_MetallicGlossMap") != null)
                mat.EnableKeyword("_METALLICSPECGLOSSMAP");
            if (mat.HasProperty("_OcclusionMap") && mat.GetTexture("_OcclusionMap") != null)
                mat.EnableKeyword("_OCCLUSIONMAP");
            if (mat.HasProperty("_ParallaxMap") && mat.GetTexture("_ParallaxMap") != null)
                mat.EnableKeyword("_PARALLAXMAP");
        }

    }
}

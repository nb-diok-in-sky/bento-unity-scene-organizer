using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Bento
{
    enum TexType { BaseColor, Normal, Metallic, AO, Height, Emission, Roughness, Transparency, Unknown }

    [Serializable] class PresetData
    {
        public string presetName = "Default";
        public List<SuffixRuleEntry> suffixRules = new();
        public List<OutputNameEntry> outputNames = new();
    }
    [Serializable] class SuffixRuleEntry { public string suffix = ""; public int type; }
    [Serializable] class OutputNameEntry { public int type; public string outputSuffix = ""; }

    class TexInfo
    {
        public string assetPath;
        public string nameNoExt;
        public string baseName;
        public string suffix;
        public TexType type;
        public bool include = true;
        public bool guessedByContent;
    }

    class MatFillInfo
    {
        public Material material;
        public string matchedGroupName;
        public Dictionary<TexType, Texture2D> matched = new();
    }

    class SharedContext
    {
        public List<TexInfo> textures = new();
        public Dictionary<string, List<TexInfo>> groups = new();
        public Dictionary<string, TexType> suffixMap = new();
        public Dictionary<TexType, string> outputSuffixes = new();
        public bool scanDone;
    }

    static class TexRules
    {
        static readonly (string suffix, TexType type)[] BuiltInByLength;

        static TexRules()
        {
            BuiltInByLength = BuiltIn.OrderByDescending(r => r.suffix.Length).ToArray();
        }

        public static readonly (string suffix, TexType type)[] BuiltIn =
        {
            ("base_color", TexType.BaseColor), ("basecolor", TexType.BaseColor),
            ("albedo", TexType.BaseColor), ("diffuse", TexType.BaseColor), ("diff", TexType.BaseColor),
            ("color", TexType.BaseColor), ("col", TexType.BaseColor),
            ("a", TexType.BaseColor), ("d", TexType.BaseColor),

            ("normal", TexType.Normal), ("norm", TexType.Normal), ("nrm", TexType.Normal),
            ("bump", TexType.Normal), ("n", TexType.Normal),

            ("metallic", TexType.Metallic), ("metalness", TexType.Metallic),
            ("met", TexType.Metallic), ("m", TexType.Metallic),

            ("occlusion", TexType.AO), ("occ", TexType.AO), ("ao", TexType.AO),

            ("height", TexType.Height), ("displacement", TexType.Height),
            ("disp", TexType.Height), ("h", TexType.Height),

            ("emission", TexType.Emission), ("emissive", TexType.Emission),
            ("emit", TexType.Emission), ("e", TexType.Emission),

            ("roughness", TexType.Roughness), ("smoothness", TexType.Roughness),
            ("smooth", TexType.Roughness), ("rough", TexType.Roughness), ("gloss", TexType.Roughness),

            ("transparency", TexType.Transparency), ("opacity", TexType.Transparency),
            ("trans", TexType.Transparency), ("alpha", TexType.Transparency),
        };

        public static readonly Dictionary<TexType, string> DefaultOutput = new()
        {
            {TexType.BaseColor,"BaseColor"}, {TexType.Normal,"Normal"}, {TexType.Metallic,"Metallic"},
            {TexType.AO,"AO"}, {TexType.Height,"Height"}, {TexType.Emission,"Emission"},
            {TexType.Roughness,"Roughness"}, {TexType.Transparency,"Transparency"},
        };

        static readonly string[] KnownPrefixes =
        {
            "T_", "M_", "SM_", "SK_", "TX_", "Mat_", "MI_", "Tex_",
        };

        public static string StripPrefix(string name)
        {
            foreach (var p in KnownPrefixes)
                if (name.Length > p.Length && name.StartsWith(p, StringComparison.OrdinalIgnoreCase))
                    return name.Substring(p.Length);
            return name;
        }

        public static readonly Dictionary<TexType, string> URPLitProps = new()
        {
            {TexType.BaseColor,"_BaseMap"}, {TexType.Normal,"_BumpMap"},
            {TexType.Metallic,"_MetallicGlossMap"}, {TexType.AO,"_OcclusionMap"},
            {TexType.Height,"_ParallaxMap"}, {TexType.Emission,"_EmissionMap"},
        };

        public static readonly Dictionary<TexType, string> StandardProps = new()
        {
            {TexType.BaseColor,"_MainTex"}, {TexType.Normal,"_BumpMap"},
            {TexType.Metallic,"_MetallicGlossMap"}, {TexType.AO,"_OcclusionMap"},
            {TexType.Height,"_ParallaxMap"}, {TexType.Emission,"_EmissionMap"},
        };

        public static readonly (string src, string dst)[] TexMigrations =
        {
            ("_MainTex",          "_BaseMap"),
            ("_BaseMap",          "_MainTex"),
            ("_Albedo",           "_BaseMap"),
            ("_Albedo",           "_MainTex"),
            ("_BumpMap",          "_BumpMap"),
            ("_NormalMap",        "_BumpMap"),
            ("_EmissionMap",      "_EmissionMap"),
            ("_MetallicGlossMap", "_MetallicGlossMap"),
            ("_OcclusionMap",     "_OcclusionMap"),
            ("_ParallaxMap",      "_ParallaxMap"),
        };

        public static readonly (string src, string dst)[] ColorMigrations =
        {
            ("_Color",            "_BaseColor"),
            ("_BaseColor",        "_Color"),
            ("_EmissionColor",    "_EmissionColor"),
        };

        public static readonly (string src, string dst)[] FloatMigrations =
        {
            ("_BumpScale",         "_BumpScale"),
            ("_NormalScale",       "_BumpScale"),
            ("_Metallic",          "_Metallic"),
            ("_Glossiness",        "_Smoothness"),
            ("_GlossMapScale",     "_Smoothness"),
            ("_Smoothness",        "_Glossiness"),
            ("_OcclusionStrength", "_OcclusionStrength"),
            ("_Parallax",          "_Parallax"),
        };

        public static readonly (string src, string dst)[] PropertyMigrations =
        {
            ("_MainTex",           "_BaseMap"),
            ("_Color",             "_BaseColor"),
            ("_BumpMap",           "_BumpMap"),
            ("_BumpScale",         "_BumpScale"),
            ("_EmissionMap",       "_EmissionMap"),
            ("_EmissionColor",     "_EmissionColor"),
            ("_MetallicGlossMap",  "_MetallicGlossMap"),
            ("_Metallic",          "_Metallic"),
            ("_Glossiness",        "_Smoothness"),
            ("_GlossMapScale",     "_Smoothness"),
            ("_OcclusionMap",      "_OcclusionMap"),
            ("_OcclusionStrength", "_OcclusionStrength"),
            ("_ParallaxMap",       "_ParallaxMap"),
            ("_Parallax",          "_Parallax"),
            ("_BaseMap",           "_MainTex"),
            ("_BaseColor",         "_Color"),
            ("_Smoothness",        "_Glossiness"),
            ("_Albedo",            "_BaseMap"),
            ("_Albedo",            "_MainTex"),
            ("_NormalMap",         "_BumpMap"),
            ("_NormalScale",       "_BumpScale"),
        };

        public static (string baseName, string suffix) ExtractSuffix(
            string name, HashSet<string> allNames, bool allowSingleCharSuffix = true)
        {
            if (allNames.Any(n => n != name && n.StartsWith(name + "_", StringComparison.OrdinalIgnoreCase)))
                return (name, "");

            foreach (var (known, _) in BuiltInByLength)
            {
                if (!allowSingleCharSuffix && known.Length == 1) continue;
                string ending = "_" + known;
                if (name.EndsWith(ending, StringComparison.OrdinalIgnoreCase) && name.Length > ending.Length)
                    return (name.Substring(0, name.Length - ending.Length),
                            name.Substring(name.Length - known.Length));
            }

            int idx = name.LastIndexOf('_');
            if (idx > 0)
            {
                string potentialSuffix = name.Substring(idx + 1);
                if (potentialSuffix.Length > 0 && !potentialSuffix.All(char.IsDigit) &&
                    (allowSingleCharSuffix || potentialSuffix.Length > 1))
                    return (name.Substring(0, idx), potentialSuffix);
            }

            return (name, "");
        }

        public static TexType GuessType(string suffix)
        {
            if (string.IsNullOrEmpty(suffix)) return TexType.BaseColor;
            foreach (var (known, type) in BuiltIn)
                if (suffix.Equals(known, StringComparison.OrdinalIgnoreCase))
                    return type;
            return TexType.Unknown;
        }

        public static TexType GuessTypeFromContent(string assetPath)
        {
            try
            {
                var importer = UnityEditor.AssetImporter.GetAtPath(assetPath) as UnityEditor.TextureImporter;
                if (importer != null && importer.textureType == UnityEditor.TextureImporterType.NormalMap)
                    return TexType.Normal;

                var tex = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
                if (tex == null) return TexType.Unknown;

                const int size = 16;
                var rt = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGB32);
                Graphics.Blit(tex, rt);
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tmp = new Texture2D(size, size, TextureFormat.RGBA32, false);
                tmp.ReadPixels(new Rect(0, 0, size, size), 0, 0);
                tmp.Apply();
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
                var pixels = tmp.GetPixels();
                UnityEngine.Object.DestroyImmediate(tmp);

                return ClassifyPixels(pixels);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[Bento] 像素分析失败 {assetPath}: {e.Message}");
                return TexType.Unknown;
            }
        }

        static TexType ClassifyPixels(Color[] pixels)
        {
            float avgR = 0, avgG = 0, avgB = 0;
            float channelDiff = 0;
            int blackCount = 0;
            int brightCount = 0;
            int n = pixels.Length;

            foreach (var p in pixels)
            {
                avgR += p.r; avgG += p.g; avgB += p.b;
                channelDiff += Mathf.Abs(p.r - p.g) + Mathf.Abs(p.r - p.b) + Mathf.Abs(p.g - p.b);
                if (p.r + p.g + p.b < 0.15f) blackCount++;
                if (p.r > 0.5f || p.g > 0.5f || p.b > 0.5f) brightCount++;
            }
            avgR /= n; avgG /= n; avgB /= n;
            channelDiff /= n * 3;

            // Normal: tangent-space normals are blue-dominant
            if (avgB > 0.45f && avgB > avgR * 1.2f && avgB > avgG * 1.2f)
                return TexType.Normal;

            // Emission: mostly black but must have some bright spots
            if (blackCount > n * 0.7f && brightCount > 0 && brightCount < n * 0.3f)
                return TexType.Emission;

            // Grayscale (R ≈ G ≈ B) → can't distinguish Roughness/Metallic/AO/Height
            if (channelDiff < 0.03f)
                return TexType.Unknown;

            return TexType.BaseColor;
        }

        public static Dictionary<TexType, string> GetPropertyMap(Material mat)
        {
            if (mat.shader != null && mat.shader.name.Contains("Universal"))
                return URPLitProps;
            return StandardProps;
        }

        // Merges sub-groups whose baseName has an extra descriptive segment (e.g. "bush_1_ambient",
        // "bush_1_filtered") back into the shortest matching parent group (e.g. "bush_1"), so that
        // textures like "bush_1_ambient_AO.png" still land in the same group as "bush_1_BaseColor.png".
        public static void ConsolidateTexGroups(Dictionary<string, Dictionary<string, string>> texGroups)
        {
            foreach (var shortKey in texGroups.Keys.OrderBy(k => k.Length).ToList())
            {
                if (!texGroups.TryGetValue(shortKey, out var parent)) continue;

                foreach (var longKey in texGroups.Keys
                             .Where(k => k != shortKey && k.StartsWith(shortKey + "_", StringComparison.OrdinalIgnoreCase))
                             .ToList())
                {
                    foreach (var kv in texGroups[longKey])
                        parent.TryAdd(kv.Key, kv.Value);
                    texGroups.Remove(longKey);
                }
            }
        }

        public static (string groupName, Dictionary<string, string> group) FindTexGroup(
            string matName,
            Dictionary<string, Dictionary<string, string>> texGroups,
            HashSet<string> allTexNames)
        {
            if (texGroups.TryGetValue(matName, out var exact))
                return (matName, exact);

            var (matBase, _) = ExtractSuffix(matName, allTexNames, allowSingleCharSuffix: false);
            if (matBase != matName && texGroups.TryGetValue(matBase, out var stripped))
                return (matBase, stripped);

            string candidate = matName;
            while (true)
            {
                int idx = candidate.LastIndexOf('_');
                if (idx <= 0) break;
                candidate = candidate.Substring(0, idx);
                if (texGroups.TryGetValue(candidate, out var partial))
                    return (candidate, partial);
            }

            foreach (var kv in texGroups)
            {
                if (matName.StartsWith(kv.Key + "_", StringComparison.OrdinalIgnoreCase) ||
                    kv.Key.StartsWith(matName + "_", StringComparison.OrdinalIgnoreCase))
                    return (kv.Key, kv.Value);
            }

            // Strip known prefixes (T_, M_, SM_ etc.) and retry
            string matCore = StripPrefix(matName);
            string matCoreBase = ExtractSuffix(matCore, allTexNames, allowSingleCharSuffix: false).baseName;
            foreach (var kv in texGroups)
            {
                string texCore = StripPrefix(kv.Key);
                if (matCore.Equals(texCore, StringComparison.OrdinalIgnoreCase) ||
                    matCoreBase.Equals(texCore, StringComparison.OrdinalIgnoreCase))
                    return (kv.Key, kv.Value);

                string c = matCore;
                while (true)
                {
                    int ci = c.LastIndexOf('_');
                    if (ci <= 0) break;
                    c = c.Substring(0, ci);
                    if (c.Equals(texCore, StringComparison.OrdinalIgnoreCase))
                        return (kv.Key, kv.Value);
                }
            }

            // Fallback: if only one texture group exists, assume it matches
            if (texGroups.Count == 1)
            {
                var single = texGroups.First();
                return (single.Key, single.Value);
            }

            // Fuzzy fallback: pick the closest group by edit-distance similarity, but only
            // if there's a clear winner (otherwise a wrong guess is worse than no guess).
            {
                string bestKey = null;
                Dictionary<string, string> bestGroup = null;
                float bestScore = 0f, secondScore = 0f;
                foreach (var kv in texGroups)
                {
                    string texCore = StripPrefix(kv.Key);
                    float score = StringSimilarity(matCore, texCore);
                    if (score > bestScore)
                    {
                        secondScore = bestScore;
                        bestScore = score;
                        bestKey = kv.Key;
                        bestGroup = kv.Value;
                    }
                    else if (score > secondScore)
                    {
                        secondScore = score;
                    }
                }
                if (bestKey != null && bestScore >= 0.7f && bestScore - secondScore >= 0.15f)
                    return (bestKey, bestGroup);
            }

            return (null, null);
        }

        static float StringSimilarity(string a, string b)
        {
            int maxLen = Math.Max(a.Length, b.Length);
            if (maxLen == 0) return 1f;
            return 1f - (float)LevenshteinDistance(a, b) / maxLen;
        }

        static int LevenshteinDistance(string a, string b)
        {
            a = a.ToLowerInvariant();
            b = b.ToLowerInvariant();
            var dp = new int[a.Length + 1, b.Length + 1];
            for (int i = 0; i <= a.Length; i++) dp[i, 0] = i;
            for (int j = 0; j <= b.Length; j++) dp[0, j] = j;
            for (int i = 1; i <= a.Length; i++)
                for (int j = 1; j <= b.Length; j++)
                    dp[i, j] = a[i - 1] == b[j - 1]
                        ? dp[i - 1, j - 1]
                        : 1 + Math.Min(dp[i - 1, j - 1], Math.Min(dp[i - 1, j], dp[i, j - 1]));
            return dp[a.Length, b.Length];
        }

        public static Dictionary<TexType, Texture2D> MatchTexSlots(
            Material mat,
            Dictionary<string, string> texGroup,
            Dictionary<string, TexType> suffixOverrides)
        {
            var result = new Dictionary<TexType, Texture2D>();
            var propMap = GetPropertyMap(mat);
            foreach (var kv in propMap)
            {
                foreach (var texEntry in texGroup)
                {
                    var suffType = GuessType(texEntry.Key);
                    if (suffixOverrides != null && suffixOverrides.TryGetValue(texEntry.Key, out var ov))
                        suffType = ov;
                    if (suffType == kv.Key)
                    {
                        var tex = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>(texEntry.Value);
                        if (tex != null) result[kv.Key] = tex;
                        break;
                    }
                }
            }
            return result;
        }

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

        public static void ShowShaderDropdown(System.Action<UnityEngine.Shader> onSelect, UnityEngine.Shader current)
        {
            var menu = new UnityEditor.GenericMenu();
            var shaderNames = UnityEditor.ShaderUtil.GetAllShaderInfo()
                .Select(info => info.name)
                .Where(n => !n.StartsWith("Hidden/"))
                .OrderBy(n => n)
                .ToArray();
            foreach (var name in shaderNames)
            {
                var n = name;
                bool on = current != null && current.name == n;
                menu.AddItem(new UnityEngine.GUIContent(n), on, () => onSelect(UnityEngine.Shader.Find(n)));
            }
            menu.ShowAsContext();
        }

        public static int ReplaceShaders(UnityEngine.Material[] mats, UnityEngine.Shader targetShader)
        {
            UnityEditor.Undo.RecordObjects(mats, "Batch Replace Shader");
            int count = 0;
            foreach (var m in mats)
            {
                if (m.shader != targetShader)
                {
                    MigrateShader(m, targetShader);
                    UnityEditor.EditorUtility.SetDirty(m);
                    count++;
                }
            }
            UnityEditor.AssetDatabase.SaveAssets();
            return count;
        }

        public static void FillMaterialSlots(UnityEngine.Material mat, Dictionary<TexType, UnityEngine.Texture2D> slots)
        {
            var propMap = GetPropertyMap(mat);
            foreach (var kv in slots)
            {
                if (kv.Value == null) continue;
                if (!propMap.TryGetValue(kv.Key, out var propName)) continue;
                if (!mat.HasProperty(propName)) continue;
                mat.SetTexture(propName, kv.Value);
            }
            EnableKeywordsForTextures(mat);
        }
    }
}

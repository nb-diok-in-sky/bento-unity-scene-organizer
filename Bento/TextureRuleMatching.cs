using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Bento
{
    static partial class TexRules
    {
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
    }
}

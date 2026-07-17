using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Bento
{
    enum MaterialRepairIssue
    {
        TransparentMissingKeyword,
        TransparentWrongBlend,
        CutoutMissingKeyword,
        CutoutWrongBlend,
        OpaqueHasTransparentState,
        FadeShouldBeCutout,
        MissingNormalMapKeyword,
        MissingEmissionKeyword,
        MissingMetallicKeyword
    }

    sealed class MaterialRepairEntry
    {
        public Material material;
        public string path;
        public List<MaterialRepairIssue> issues = new List<MaterialRepairIssue>();
        public bool selected = true;
    }

    static class MaterialRepairAnalyzer
    {
        public static List<MaterialRepairEntry> ScanFolders(params string[] folders)
        {
            return ScanPaths(AssetDatabase.FindAssets("t:Material", folders)
                .Select(AssetDatabase.GUIDToAssetPath));
        }

        public static List<MaterialRepairEntry> ScanSelection(IEnumerable<UnityEngine.Object> selection)
        {
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var asset in selection)
            {
                if (asset is Material material) paths.Add(AssetDatabase.GetAssetPath(material));
                string path = AssetDatabase.GetAssetPath(asset);
                if (AssetDatabase.IsValidFolder(path))
                    foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { path }))
                        paths.Add(AssetDatabase.GUIDToAssetPath(guid));
            }
            return ScanPaths(paths);
        }

        public static MaterialRepairEntry Analyze(Material material, string path)
        {
            if (!IsUrp(material)) return null;
            var entry = new MaterialRepairEntry { material = material, path = path };
            bool transparent = material.HasProperty("_Surface") && (int)material.GetFloat("_Surface") == 1;
            bool alphaClip = material.HasProperty("_AlphaClip") && (int)material.GetFloat("_AlphaClip") == 1;

            if (transparent)
            {
                if (!material.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT")) entry.issues.Add(MaterialRepairIssue.TransparentMissingKeyword);
                if (!HasValidTransparentBlend(material)) entry.issues.Add(MaterialRepairIssue.TransparentWrongBlend);
                if (material.HasProperty("_Mode") && (int)material.GetFloat("_Mode") == 2 && !alphaClip &&
                    material.HasProperty("_BaseMap") && material.GetTexture("_BaseMap") != null)
                    entry.issues.Add(MaterialRepairIssue.FadeShouldBeCutout);
            }
            else if (alphaClip)
            {
                if (!material.IsKeywordEnabled("_ALPHATEST_ON")) entry.issues.Add(MaterialRepairIssue.CutoutMissingKeyword);
                if (material.HasProperty("_ZWrite") && material.GetFloat("_ZWrite") < 0.5f) entry.issues.Add(MaterialRepairIssue.CutoutWrongBlend);
            }
            else if (material.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT") || !HasOpaqueBlend(material))
                entry.issues.Add(MaterialRepairIssue.OpaqueHasTransparentState);

            AddMissingTextureKeywords(material, entry.issues);
            return entry.issues.Count > 0 ? entry : null;
        }

        static List<MaterialRepairEntry> ScanPaths(IEnumerable<string> paths)
        {
            var result = new List<MaterialRepairEntry>();
            foreach (string path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var entry = Analyze(AssetDatabase.LoadAssetAtPath<Material>(path), path);
                if (entry != null) result.Add(entry);
            }
            return result;
        }

        static bool IsUrp(Material material) => material != null && material.shader != null &&
            (material.shader.name.Contains("Universal") || material.shader.name.Contains("URP"));
        static bool HasValidTransparentBlend(Material material) => material.HasProperty("_ZWrite") && material.GetFloat("_ZWrite") < 0.5f &&
            material.HasProperty("_DstBlend") && (int)material.GetFloat("_DstBlend") != 0;
        static bool HasOpaqueBlend(Material material) => !material.HasProperty("_ZWrite") || material.GetFloat("_ZWrite") >= 0.5f ||
            !material.HasProperty("_DstBlend") || (int)material.GetFloat("_DstBlend") == 0;

        static void AddMissingTextureKeywords(Material material, ICollection<MaterialRepairIssue> issues)
        {
            if (HasTextureWithoutKeyword(material, "_BumpMap", "_NORMALMAP")) issues.Add(MaterialRepairIssue.MissingNormalMapKeyword);
            if (HasTextureWithoutKeyword(material, "_EmissionMap", "_EMISSION")) issues.Add(MaterialRepairIssue.MissingEmissionKeyword);
            if (HasTextureWithoutKeyword(material, "_MetallicGlossMap", "_METALLICSPECGLOSSMAP")) issues.Add(MaterialRepairIssue.MissingMetallicKeyword);
        }

        static bool HasTextureWithoutKeyword(Material material, string property, string keyword) =>
            material.HasProperty(property) && material.GetTexture(property) != null && !material.IsKeywordEnabled(keyword);
    }

    static class MaterialRepairService
    {
        public static int Repair(IReadOnlyList<MaterialRepairEntry> entries)
        {
            var targets = entries.Where(entry => entry.selected).ToList();
            if (targets.Count == 0) return 0;
            Undo.RecordObjects(targets.Select(entry => (UnityEngine.Object)entry.material).ToArray(), "Repair URP Materials");
            int issueCount = 0;
            foreach (var entry in targets)
            {
                foreach (var issue in entry.issues) { Apply(entry.material, issue); issueCount++; }
                EditorUtility.SetDirty(entry.material);
            }
            AssetDatabase.SaveAssets();
            return issueCount;
        }

        public static int RepairFolder(string folder)
        {
            var entries = MaterialRepairAnalyzer.ScanFolders(folder);
            Repair(entries);
            return entries.Count;
        }

        static void Apply(Material material, MaterialRepairIssue issue)
        {
            switch (issue)
            {
                case MaterialRepairIssue.TransparentMissingKeyword: material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); break;
                case MaterialRepairIssue.TransparentWrongBlend: FixTransparentBlend(material); break;
                case MaterialRepairIssue.CutoutMissingKeyword: material.EnableKeyword("_ALPHATEST_ON"); break;
                case MaterialRepairIssue.CutoutWrongBlend: FixCutoutBlend(material); break;
                case MaterialRepairIssue.OpaqueHasTransparentState: FixOpaqueState(material); break;
                case MaterialRepairIssue.FadeShouldBeCutout: FixFadeToCutout(material); break;
                case MaterialRepairIssue.MissingNormalMapKeyword: material.EnableKeyword("_NORMALMAP"); break;
                case MaterialRepairIssue.MissingEmissionKeyword:
                    material.EnableKeyword("_EMISSION");
                    material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
                    break;
                case MaterialRepairIssue.MissingMetallicKeyword: material.EnableKeyword("_METALLICSPECGLOSSMAP"); break;
            }
        }

        static void FixTransparentBlend(Material material)
        {
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = (int)RenderQueue.Transparent;
            SetFloat(material, "_ZWrite", 0);
            SetFloat(material, "_SrcBlend", material.IsKeywordEnabled("_ALPHAPREMULTIPLY_ON") ? (float)BlendMode.One : (float)BlendMode.SrcAlpha);
            SetFloat(material, "_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        }

        static void FixCutoutBlend(Material material)
        {
            material.SetOverrideTag("RenderType", "TransparentCutout");
            material.renderQueue = (int)RenderQueue.AlphaTest;
            SetFloat(material, "_ZWrite", 1); SetFloat(material, "_SrcBlend", (float)BlendMode.One); SetFloat(material, "_DstBlend", (float)BlendMode.Zero);
        }

        static void FixFadeToCutout(Material material)
        {
            material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT"); material.DisableKeyword("_ALPHAPREMULTIPLY_ON"); material.DisableKeyword("_ALPHABLEND_ON");
            material.EnableKeyword("_ALPHATEST_ON"); material.SetOverrideTag("RenderType", "TransparentCutout"); material.renderQueue = (int)RenderQueue.AlphaTest;
            SetFloat(material, "_Surface", 0); SetFloat(material, "_AlphaClip", 1); SetFloat(material, "_ZWrite", 1);
            SetFloat(material, "_SrcBlend", (float)BlendMode.One); SetFloat(material, "_DstBlend", (float)BlendMode.Zero);
            material.SetShaderPassEnabled("DepthOnly", true); material.SetShaderPassEnabled("ShadowCaster", true);
        }

        static void FixOpaqueState(Material material)
        {
            material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT"); material.DisableKeyword("_ALPHAPREMULTIPLY_ON"); material.DisableKeyword("_ALPHABLEND_ON");
            material.SetOverrideTag("RenderType", "Opaque"); material.renderQueue = (int)RenderQueue.Geometry;
            SetFloat(material, "_ZWrite", 1); SetFloat(material, "_SrcBlend", (float)BlendMode.One); SetFloat(material, "_DstBlend", (float)BlendMode.Zero); SetFloat(material, "_Surface", 0);
        }

        static void SetFloat(Material material, string property, float value) { if (material.HasProperty(property)) material.SetFloat(property, value); }
    }
}

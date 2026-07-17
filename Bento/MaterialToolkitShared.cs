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

    static partial class TexRules
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

        public static void ShowShaderDropdown(System.Action<UnityEngine.Shader> onSelect, UnityEngine.Shader current)
            => MaterialShaderOperations.ShowShaderDropdown(onSelect, current);

        public static int ReplaceShaders(UnityEngine.Material[] mats, UnityEngine.Shader targetShader)
            => MaterialShaderOperations.ReplaceShaders(mats, targetShader);

        public static void FillMaterialSlots(UnityEngine.Material mat, Dictionary<TexType, UnityEngine.Texture2D> slots)
            => MaterialShaderOperations.FillMaterialSlots(mat, slots);
    }
}

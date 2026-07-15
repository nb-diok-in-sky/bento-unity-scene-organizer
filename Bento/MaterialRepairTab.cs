using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Bento
{
    class MaterialRepairTab
    {
        Vector2 _scroll;
        DefaultAsset _folderAsset;
        string _folder = "";
        List<RepairEntry> _entries = new();
        bool _scanned;

        enum Issue
        {
            TransparentMissingKeyword,
            TransparentWrongBlend,
            CutoutMissingKeyword,
            CutoutWrongBlend,
            OpaqueHasTransparentState,
            FadeShouldBeCutout,
            MissingNormalMapKeyword,
            MissingEmissionKeyword,
            MissingMetallicKeyword,
        }

        static readonly Dictionary<Issue, string> IssueLabels = new()
        {
            { Issue.TransparentMissingKeyword, "透明材质缺 _SURFACE_TYPE_TRANSPARENT keyword → 重影/实心渲染" },
            { Issue.TransparentWrongBlend,     "透明材质 Blend/ZWrite 状态错误" },
            { Issue.CutoutMissingKeyword,      "Cutout 材质缺 _ALPHATEST_ON keyword → 叶子/边缘不透明" },
            { Issue.CutoutWrongBlend,          "Cutout 材质 Blend/ZWrite 状态错误" },
            { Issue.OpaqueHasTransparentState,  "不透明材质残留透明状态" },
            { Issue.FadeShouldBeCutout,        "Standard Fade→URP 透明 导致重影，应改为 AlphaClip" },
            { Issue.MissingNormalMapKeyword,    "有法线贴图但缺 _NORMALMAP keyword" },
            { Issue.MissingEmissionKeyword,     "有自发光贴图但缺 _EMISSION keyword" },
            { Issue.MissingMetallicKeyword,     "有金属度贴图但缺 _METALLICSPECGLOSSMAP keyword" },
        };

        class RepairEntry
        {
            public Material material;
            public string path;
            public List<Issue> issues = new();
            public bool selected = true;
        }

        public void Draw()
        {
            EditorGUILayout.LabelField("URP 材质修复", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("扫描 Standard→URP 转换后遗留的状态不一致问题（重影、透明失效等）", EditorStyles.miniLabel);
            EditorGUILayout.Space(4);

            EditorGUILayout.BeginHorizontal();
            var newFolder = EditorGUILayout.ObjectField("扫描文件夹", _folderAsset, typeof(DefaultAsset), false) as DefaultAsset;
            if (newFolder != _folderAsset)
            {
                _folderAsset = newFolder;
                _folder = newFolder != null ? AssetDatabase.GetAssetPath(newFolder) : "";
                _scanned = false;
                _entries.Clear();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("扫描全部材质", GUILayout.Height(26)))
                ScanAll();
            EditorGUI.BeginDisabledGroup(string.IsNullOrEmpty(_folder));
            if (GUILayout.Button("扫描文件夹", GUILayout.Height(26)))
                ScanFolder();
            EditorGUI.EndDisabledGroup();
            if (GUILayout.Button("扫描选中", GUILayout.Height(26)))
                ScanSelected();
            EditorGUILayout.EndHorizontal();

            if (!_scanned) return;

            // Summary
            int totalIssues = _entries.Sum(e => e.issues.Count);
            if (totalIssues == 0)
            {
                EditorGUILayout.Space(8);
                EditorGUILayout.HelpBox("未发现问题，所有材质状态一致。", MessageType.Info);
                return;
            }

            var issueGroups = _entries.SelectMany(e => e.issues).GroupBy(i => i)
                .OrderByDescending(g => g.Count()).ToList();

            EditorGUILayout.Space(4);
            EditorGUILayout.HelpBox(
                $"发现 {_entries.Count} 个材质有问题，共 {totalIssues} 个异常",
                MessageType.Warning);

            foreach (var g in issueGroups)
            {
                EditorGUILayout.BeginHorizontal();
                GUI.contentColor = new Color(1f, 0.7f, 0.2f);
                EditorGUILayout.LabelField($"  × {g.Count()}", GUILayout.Width(40));
                GUI.contentColor = Color.white;
                EditorGUILayout.LabelField(IssueLabels[g.Key], EditorStyles.miniLabel);
                EditorGUILayout.EndHorizontal();
            }

            // Detail list
            EditorGUILayout.Space(6);
            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.MaxHeight(350));

            foreach (var entry in _entries)
            {
                EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
                entry.selected = EditorGUILayout.Toggle(entry.selected, GUILayout.Width(18));
                EditorGUILayout.ObjectField(entry.material, typeof(Material), false, GUILayout.Width(180));

                GUI.contentColor = new Color(1f, 0.6f, 0.2f);
                string issueSummary = string.Join(" | ", entry.issues.Select(i =>
                {
                    switch (i)
                    {
                        case Issue.TransparentMissingKeyword: return "重影";
                        case Issue.TransparentWrongBlend: return "Blend错";
                        case Issue.CutoutMissingKeyword: return "Cutout失效";
                        case Issue.CutoutWrongBlend: return "Cutout Blend错";
                        case Issue.OpaqueHasTransparentState: return "残留透明";
                        case Issue.FadeShouldBeCutout: return "Fade→重影";
                        case Issue.MissingNormalMapKeyword: return "法线keyword缺";
                        case Issue.MissingEmissionKeyword: return "自发光keyword缺";
                        case Issue.MissingMetallicKeyword: return "金属度keyword缺";
                        default: return i.ToString();
                    }
                }));
                EditorGUILayout.LabelField(issueSummary, EditorStyles.miniLabel);
                GUI.contentColor = Color.white;
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();

            // Actions
            EditorGUILayout.Space(6);
            int selectedCount = _entries.Count(e => e.selected);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("全选", GUILayout.Width(50)))
                _entries.ForEach(e => e.selected = true);
            if (GUILayout.Button("全不选", GUILayout.Width(60)))
                _entries.ForEach(e => e.selected = false);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);
            EditorGUI.BeginDisabledGroup(selectedCount == 0);
            if (GUILayout.Button($"修复选中的 {selectedCount} 个材质", GUILayout.Height(32)))
                RepairSelected();
            EditorGUI.EndDisabledGroup();
        }

        void ScanAll()
        {
            _entries.Clear();
            var guids = AssetDatabase.FindAssets("t:Material", new[] { "Assets" });
            for (int i = 0; i < guids.Length; i++)
            {
                if (i % 50 == 0 && EditorUtility.DisplayCancelableProgressBar(
                    "扫描全部材质", $"{i}/{guids.Length}", (float)i / guids.Length))
                    break;
                var path = AssetDatabase.GUIDToAssetPath(guid: guids[i]);
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat != null) CheckMaterial(mat, path);
            }
            EditorUtility.ClearProgressBar();
            _scanned = true;
            Debug.Log($"[MaterialRepair] 全局扫描完成: {_entries.Count} 个材质有问题（共 {guids.Length} 个）");
        }

        void ScanFolder()
        {
            _entries.Clear();
            var guids = AssetDatabase.FindAssets("t:Material", new[] { _folder });
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null) continue;
                CheckMaterial(mat, path);
            }
            _scanned = true;
            Debug.Log($"[MaterialRepair] 扫描完成: {_entries.Count} 个材质有问题（共扫描 {guids.Length} 个）");
        }

        void ScanSelected()
        {
            _entries.Clear();
            foreach (var obj in Selection.objects)
            {
                if (obj is Material mat)
                {
                    CheckMaterial(mat, AssetDatabase.GetAssetPath(mat));
                    continue;
                }
                var path = AssetDatabase.GetAssetPath(obj);
                if (AssetDatabase.IsValidFolder(path))
                {
                    foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { path }))
                    {
                        var matPath = AssetDatabase.GUIDToAssetPath(guid);
                        var m = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                        if (m != null) CheckMaterial(m, matPath);
                    }
                }
            }
            _scanned = true;
            Debug.Log($"[MaterialRepair] 扫描完成: {_entries.Count} 个材质有问题");
        }

        void CheckMaterial(Material mat, string path)
        {
            if (mat.shader == null) return;
            if (!mat.shader.name.Contains("Universal") && !mat.shader.name.Contains("URP"))
                return;

            var entry = new RepairEntry { material = mat, path = path };

            bool isTransparent = mat.HasProperty("_Surface") && (int)mat.GetFloat("_Surface") == 1;
            bool isAlphaClip = mat.HasProperty("_AlphaClip") && (int)mat.GetFloat("_AlphaClip") == 1;

            if (isTransparent)
            {
                if (!mat.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT"))
                    entry.issues.Add(Issue.TransparentMissingKeyword);

                bool blendOk = mat.HasProperty("_ZWrite") && mat.GetFloat("_ZWrite") < 0.5f
                    && mat.HasProperty("_DstBlend") && (int)mat.GetFloat("_DstBlend") != 0;
                if (!blendOk)
                    entry.issues.Add(Issue.TransparentWrongBlend);

                bool isFadeMode = mat.HasProperty("_Mode") && (int)mat.GetFloat("_Mode") == 2;
                bool hasAlphaTexture = mat.HasProperty("_BaseMap") && mat.GetTexture("_BaseMap") != null;
                if (isFadeMode && !isAlphaClip && hasAlphaTexture)
                    entry.issues.Add(Issue.FadeShouldBeCutout);
            }
            else if (isAlphaClip)
            {
                if (!mat.IsKeywordEnabled("_ALPHATEST_ON"))
                    entry.issues.Add(Issue.CutoutMissingKeyword);

                if (mat.HasProperty("_ZWrite") && mat.GetFloat("_ZWrite") < 0.5f)
                    entry.issues.Add(Issue.CutoutWrongBlend);
            }
            else
            {
                if (mat.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT") ||
                    (mat.HasProperty("_ZWrite") && mat.GetFloat("_ZWrite") < 0.5f &&
                     mat.HasProperty("_DstBlend") && (int)mat.GetFloat("_DstBlend") != 0))
                    entry.issues.Add(Issue.OpaqueHasTransparentState);
            }

            if (mat.HasProperty("_BumpMap") && mat.GetTexture("_BumpMap") != null
                && !mat.IsKeywordEnabled("_NORMALMAP"))
                entry.issues.Add(Issue.MissingNormalMapKeyword);

            if (mat.HasProperty("_EmissionMap") && mat.GetTexture("_EmissionMap") != null
                && !mat.IsKeywordEnabled("_EMISSION"))
                entry.issues.Add(Issue.MissingEmissionKeyword);

            if (mat.HasProperty("_MetallicGlossMap") && mat.GetTexture("_MetallicGlossMap") != null
                && !mat.IsKeywordEnabled("_METALLICSPECGLOSSMAP"))
                entry.issues.Add(Issue.MissingMetallicKeyword);

            if (entry.issues.Count > 0)
                _entries.Add(entry);
        }

        void RepairSelected()
        {
            var targets = _entries.Where(e => e.selected).ToList();
            if (targets.Count == 0) return;

            Undo.RecordObjects(targets.Select(e => (Object)e.material).ToArray(), "Repair URP Materials");
            int fixCount = 0;

            foreach (var entry in targets)
            {
                var mat = entry.material;
                foreach (var issue in entry.issues)
                {
                    switch (issue)
                    {
                        case Issue.TransparentMissingKeyword:
                            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                            fixCount++;
                            break;

                        case Issue.TransparentWrongBlend:
                            FixTransparentBlend(mat);
                            fixCount++;
                            break;

                        case Issue.CutoutMissingKeyword:
                            mat.EnableKeyword("_ALPHATEST_ON");
                            fixCount++;
                            break;

                        case Issue.CutoutWrongBlend:
                            FixCutoutBlend(mat);
                            fixCount++;
                            break;

                        case Issue.OpaqueHasTransparentState:
                            FixOpaqueState(mat);
                            fixCount++;
                            break;

                        case Issue.FadeShouldBeCutout:
                            FixFadeToCutout(mat);
                            fixCount++;
                            break;

                        case Issue.MissingNormalMapKeyword:
                            mat.EnableKeyword("_NORMALMAP");
                            fixCount++;
                            break;

                        case Issue.MissingEmissionKeyword:
                            mat.EnableKeyword("_EMISSION");
                            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
                            fixCount++;
                            break;

                        case Issue.MissingMetallicKeyword:
                            mat.EnableKeyword("_METALLICSPECGLOSSMAP");
                            fixCount++;
                            break;
                    }
                }
                EditorUtility.SetDirty(mat);
            }

            AssetDatabase.SaveAssets();

            _entries.RemoveAll(e => e.selected);
            Debug.Log($"[MaterialRepair] 修复完成: {fixCount} 个问题已修复");
            EditorUtility.DisplayDialog("修复完成",
                $"已修复 {targets.Count} 个材质，共 {fixCount} 个问题。\n\n可通过 Ctrl+Z 撤销。",
                "确定");
        }

        static void FixTransparentBlend(Material mat)
        {
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.renderQueue = (int)RenderQueue.Transparent;
            if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0);

            bool premultiply = mat.IsKeywordEnabled("_ALPHAPREMULTIPLY_ON");
            if (mat.HasProperty("_SrcBlend"))
                mat.SetFloat("_SrcBlend", premultiply
                    ? (float)BlendMode.One
                    : (float)BlendMode.SrcAlpha);
            if (mat.HasProperty("_DstBlend"))
                mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        }

        static void FixCutoutBlend(Material mat)
        {
            mat.SetOverrideTag("RenderType", "TransparentCutout");
            mat.renderQueue = (int)RenderQueue.AlphaTest;
            if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 1);
            if (mat.HasProperty("_SrcBlend")) mat.SetFloat("_SrcBlend", (float)BlendMode.One);
            if (mat.HasProperty("_DstBlend")) mat.SetFloat("_DstBlend", (float)BlendMode.Zero);
        }

        static void FixFadeToCutout(Material mat)
        {
            mat.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.DisableKeyword("_ALPHABLEND_ON");
            mat.EnableKeyword("_ALPHATEST_ON");
            mat.SetOverrideTag("RenderType", "TransparentCutout");
            mat.renderQueue = (int)RenderQueue.AlphaTest;
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 0);
            if (mat.HasProperty("_AlphaClip")) mat.SetFloat("_AlphaClip", 1);
            if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 1);
            if (mat.HasProperty("_SrcBlend")) mat.SetFloat("_SrcBlend", (float)BlendMode.One);
            if (mat.HasProperty("_DstBlend")) mat.SetFloat("_DstBlend", (float)BlendMode.Zero);
            mat.SetShaderPassEnabled("DepthOnly", true);
            mat.SetShaderPassEnabled("ShadowCaster", true);
        }

        static void FixOpaqueState(Material mat)
        {
            mat.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.DisableKeyword("_ALPHABLEND_ON");
            mat.SetOverrideTag("RenderType", "Opaque");
            mat.renderQueue = (int)RenderQueue.Geometry;
            if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 1);
            if (mat.HasProperty("_SrcBlend")) mat.SetFloat("_SrcBlend", (float)BlendMode.One);
            if (mat.HasProperty("_DstBlend")) mat.SetFloat("_DstBlend", (float)BlendMode.Zero);
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 0);
        }

        public static int BatchRepairAll(string folder = "Assets")
        {
            var guids = AssetDatabase.FindAssets("t:Material", new[] { folder });
            int fixCount = 0;
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null || mat.shader == null) continue;
                if (!mat.shader.name.Contains("Universal") && !mat.shader.name.Contains("URP"))
                    continue;

                bool isTransparent = mat.HasProperty("_Surface") && (int)mat.GetFloat("_Surface") == 1;
                bool isAlphaClip = mat.HasProperty("_AlphaClip") && (int)mat.GetFloat("_AlphaClip") == 1;
                bool touched = false;

                if (isTransparent)
                {
                    bool isFadeMode = mat.HasProperty("_Mode") && (int)mat.GetFloat("_Mode") == 2;
                    bool hasAlphaTex = mat.HasProperty("_BaseMap") && mat.GetTexture("_BaseMap") != null;
                    if (isFadeMode && !isAlphaClip && hasAlphaTex)
                    { FixFadeToCutout(mat); touched = true; }
                    else
                    {
                        if (!mat.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT"))
                        { mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); touched = true; }

                        bool blendOk = mat.HasProperty("_ZWrite") && mat.GetFloat("_ZWrite") < 0.5f
                            && mat.HasProperty("_DstBlend") && (int)mat.GetFloat("_DstBlend") != 0;
                        if (!blendOk) { FixTransparentBlend(mat); touched = true; }
                    }
                }
                else if (isAlphaClip)
                {
                    if (!mat.IsKeywordEnabled("_ALPHATEST_ON"))
                    { mat.EnableKeyword("_ALPHATEST_ON"); touched = true; }
                    if (mat.HasProperty("_ZWrite") && mat.GetFloat("_ZWrite") < 0.5f)
                    { FixCutoutBlend(mat); touched = true; }
                }

                if (mat.HasProperty("_BumpMap") && mat.GetTexture("_BumpMap") != null
                    && !mat.IsKeywordEnabled("_NORMALMAP"))
                { mat.EnableKeyword("_NORMALMAP"); touched = true; }

                if (mat.HasProperty("_EmissionMap") && mat.GetTexture("_EmissionMap") != null
                    && !mat.IsKeywordEnabled("_EMISSION"))
                {
                    mat.EnableKeyword("_EMISSION");
                    mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
                    touched = true;
                }

                if (mat.HasProperty("_MetallicGlossMap") && mat.GetTexture("_MetallicGlossMap") != null
                    && !mat.IsKeywordEnabled("_METALLICSPECGLOSSMAP"))
                { mat.EnableKeyword("_METALLICSPECGLOSSMAP"); touched = true; }

                if (touched)
                {
                    EditorUtility.SetDirty(mat);
                    fixCount++;
                }
            }
            if (fixCount > 0) AssetDatabase.SaveAssets();
            return fixCount;
        }
    }
}

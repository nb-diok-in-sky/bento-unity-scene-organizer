using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Bento
{
    class AutoProcessTab
    {
        readonly SharedContext _ctx;
        Vector2 _scroll;

        DefaultAsset _rootFolderAsset;
        string _rootFolder = "";

        bool _doRename = true;
        bool _doFill = true;
        bool _doShaderReplace;
        bool _doRepair = true;
        Shader _targetShader;

        List<GroupInfo> _groups = new();
        bool _scanned;

        class GroupInfo
        {
            public string category;
            public string folderPath;
            public List<TexInfo> textures = new();
            public List<Material> materials = new();
            public Dictionary<string, Dictionary<string, string>> texGroupMap = new();
            public HashSet<string> allTexNames = new();
            public List<(Material mat, string groupName, Dictionary<TexType, Texture2D> slots)> matches = new();
        }

        public AutoProcessTab(SharedContext ctx) => _ctx = ctx;

        public void Draw()
        {
            EditorGUILayout.LabelField("一键批量处理", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("选中资产根文件夹，自动发现贴图与材质球并批量处理", EditorStyles.miniLabel);
            EditorGUILayout.Space(4);

            var newFolder = EditorGUILayout.ObjectField("资产根文件夹", _rootFolderAsset,
                typeof(DefaultAsset), false) as DefaultAsset;
            if (newFolder != _rootFolderAsset)
            {
                _rootFolderAsset = newFolder;
                _rootFolder = newFolder != null ? AssetDatabase.GetAssetPath(newFolder) : "";
                _scanned = false;
                _groups.Clear();
            }

            if (string.IsNullOrEmpty(_rootFolder))
            {
                EditorGUILayout.HelpBox("将资产根文件夹拖入上方（如 Coniferous forest）", MessageType.Info);
                return;
            }

            EditorGUILayout.Space(4);
            if (GUILayout.Button("扫描文件夹结构", GUILayout.Height(26)))
                ScanFolder();
            if (!_scanned) return;

            // Summary
            int totalTex = _groups.Sum(g => g.textures.Count);
            int totalMat = _groups.Sum(g => g.materials.Count);
            int totalSlots = _groups.Sum(g => g.matches.Sum(m => m.slots.Count(kv => kv.Value != null)));
            EditorGUILayout.Space(4);
            EditorGUILayout.HelpBox(
                $"发现 {_groups.Count} 个子分组 | {totalTex} 张贴图 | {totalMat} 个材质球 | {totalSlots} 个可填充槽位",
                MessageType.None);

            // Preview
            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.MaxHeight(350));
            foreach (var g in _groups)
            {
                int matched = g.matches.Sum(m => m.slots.Count(kv => kv.Value != null));
                EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
                EditorGUILayout.LabelField($"■ {g.category}", EditorStyles.boldLabel, GUILayout.Width(160));
                EditorGUILayout.LabelField($"{g.textures.Count} 贴图", GUILayout.Width(60));
                EditorGUILayout.LabelField($"{g.materials.Count} 材质", GUILayout.Width(60));
                EditorGUILayout.LabelField($"{matched} 匹配", GUILayout.Width(60));
                EditorGUILayout.EndHorizontal();

                foreach (var (mat, groupName, slots) in g.matches)
                {
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Space(16);
                    EditorGUILayout.LabelField(mat.name, GUILayout.Width(180));
                    int sc = slots.Count(kv => kv.Value != null);
                    if (sc > 0)
                    {
                        GUI.contentColor = new Color(0.5f, 1f, 0.5f);
                        EditorGUILayout.LabelField($"← {string.Join(", ", slots.Where(kv => kv.Value != null).Select(kv => kv.Key))}");
                    }
                    else
                    {
                        GUI.contentColor = new Color(1f, 0.7f, 0.2f);
                        EditorGUILayout.LabelField(string.IsNullOrEmpty(groupName) ? "(未找到匹配贴图组)" : "(贴图组无可用类型)");
                    }
                    GUI.contentColor = Color.white;
                    EditorGUILayout.EndHorizontal();
                }

                var unknowns = g.textures.Where(t => t.type == TexType.Unknown).ToList();
                if (unknowns.Count > 0)
                {
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Space(16);
                    GUI.contentColor = new Color(1f, 0.6f, 0.2f);
                    EditorGUILayout.LabelField(
                        $"⚠ {unknowns.Count} 张后缀无法识别: {string.Join(", ", unknowns.Take(5).Select(t => "_" + t.suffix))}{(unknowns.Count > 5 ? "..." : "")}",
                        EditorStyles.miniLabel);
                    GUI.contentColor = Color.white;
                    EditorGUILayout.EndHorizontal();
                }
            }
            EditorGUILayout.EndScrollView();

            // Options
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("执行选项", EditorStyles.boldLabel);
            _doRename = EditorGUILayout.Toggle("贴图改名（使用当前预设）", _doRename);
            _doFill = EditorGUILayout.Toggle("材质球填充贴图", _doFill);
            _doRepair = EditorGUILayout.Toggle("URP 材质修复（重影/keyword缺失）", _doRepair);
            _doShaderReplace = EditorGUILayout.Toggle("Shader 替换", _doShaderReplace);
            if (_doShaderReplace)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PrefixLabel("目标 Shader");
                if (EditorGUILayout.DropdownButton(
                        new GUIContent(_targetShader != null ? _targetShader.name : "(选择 Shader)"),
                        FocusType.Keyboard))
                    TexRules.ShowShaderDropdown(s => _targetShader = s, _targetShader);
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.Space(6);
            bool canExecute = _doRename || _doFill || _doRepair || (_doShaderReplace && _targetShader != null);
            EditorGUI.BeginDisabledGroup(!canExecute);
            if (GUILayout.Button("执行批量处理", GUILayout.Height(32)))
                Execute();
            EditorGUI.EndDisabledGroup();
        }

        void ScanFolder()
        {
            _groups.Clear();
            _scanned = false;

            var subDirs = AssetDatabase.GetSubFolders(_rootFolder).ToList();
            if (subDirs.Count == 0) subDirs.Add(_rootFolder);

            for (int i = 0; i < subDirs.Count; i++)
            {
                string sub = subDirs[i];
                string category = sub == _rootFolder ? Path.GetFileName(_rootFolder) : sub.Substring(_rootFolder.Length + 1);

                if (EditorUtility.DisplayCancelableProgressBar("扫描文件夹", category, (float)i / subDirs.Count))
                    break;

                var texGuids = AssetDatabase.FindAssets("t:Texture2D", new[] { sub });
                var matGuids = AssetDatabase.FindAssets("t:Material", new[] { sub });
                if (texGuids.Length == 0 && matGuids.Length == 0) continue;

                var g = new GroupInfo { category = category, folderPath = sub };

                g.allTexNames = new HashSet<string>(
                    texGuids.Select(guid => Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(guid))));

                foreach (var guid in texGuids)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    var nameNoExt = Path.GetFileNameWithoutExtension(path);
                    var (baseName, suffix) = TexRules.ExtractSuffix(nameNoExt, g.allTexNames);
                    var type = TexRules.GuessType(suffix);
                    if (_ctx.suffixMap.TryGetValue(suffix, out var ov)) type = ov;

                    g.textures.Add(new TexInfo { assetPath = path, nameNoExt = nameNoExt, baseName = baseName, suffix = suffix, type = type });

                    if (!g.texGroupMap.ContainsKey(baseName))
                        g.texGroupMap[baseName] = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
                    g.texGroupMap[baseName][suffix] = path;
                }

                TexRules.ConsolidateTexGroups(g.texGroupMap);

                // Pixel analysis for unknown types
                var unknownInGroup = g.textures
                    .Where(t => t.type == TexType.Unknown)
                    .Select(t => t.suffix).Distinct().ToList();
                foreach (var s in unknownInGroup)
                {
                    var sample = g.textures.FirstOrDefault(t => t.suffix == s);
                    if (sample == null) continue;
                    var guessed = TexRules.GuessTypeFromContent(sample.assetPath);
                    if (guessed != TexType.Unknown)
                    {
                        foreach (var t in g.textures.Where(t => t.suffix == s))
                        {
                            t.type = guessed;
                            t.guessedByContent = true;
                        }
                    }
                }

                foreach (var guid in matGuids)
                {
                    var mat = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                    if (mat != null) g.materials.Add(mat);
                }

                foreach (var mat in g.materials)
                {
                    var (groupName, texGroup) = TexRules.FindTexGroup(mat.name, g.texGroupMap, g.allTexNames);
                    var slots = texGroup != null
                        ? TexRules.MatchTexSlots(mat, texGroup, _ctx.suffixMap)
                        : new Dictionary<TexType, Texture2D>();
                    g.matches.Add((mat, groupName, slots));
                }

                _groups.Add(g);
            }

            EditorUtility.ClearProgressBar();
            _scanned = true;
            Debug.Log($"[AutoProcess] 扫描完成: {_groups.Count} 组, {_groups.Sum(g => g.textures.Count)} 贴图, {_groups.Sum(g => g.materials.Count)} 材质球");
        }

        void Execute()
        {
            var allMats = _groups.SelectMany(g => g.materials).ToArray();
            var allTextures = _groups.SelectMany(g => g.textures).ToList();
            var allMatches = _groups.SelectMany(g => g.matches).ToList();

            int totalSteps = (_doRename ? allTextures.Count : 0)
                           + (_doShaderReplace ? allMats.Length : 0)
                           + (_doFill ? allMatches.Count : 0);
            if (totalSteps == 0) return;

            int step = 0, renameCount = 0, fillCount = 0, shaderCount = 0, repairCount = 0;
            bool cancelled = false;
            var log = new List<string>
            {
                "═══════════════════════════════════════════",
                "[AutoProcess] 开始批量处理",
                $"  贴图改名: {(_doRename ? "是" : "否")}",
                $"  材质填充: {(_doFill ? "是" : "否")}",
                $"  URP材质修复: {(_doRepair ? "是" : "否")}",
                $"  Shader替换: {(_doShaderReplace && _targetShader ? _targetShader.name : "否")}",
                "═══════════════════════════════════════════"
            };

            // 1. Rename
            if (_doRename && !cancelled)
            {
                log.Add("\n── 贴图改名 ──");
                var renameTextures = new List<TexInfo>();
                var renamePlan = new List<AssetRenameEntry>();
                foreach (var tex in allTextures)
                {
                    if (tex.type == TexType.Unknown) continue;
                    if (!_ctx.outputSuffixes.TryGetValue(tex.type, out var outSuffix)) continue;
                    string newName = string.IsNullOrEmpty(outSuffix) ? tex.baseName : tex.baseName + "_" + outSuffix;
                    if (newName == tex.nameNoExt) continue;
                    renameTextures.Add(tex);
                    renamePlan.Add(SafeAssetRenameService.BuildEntry(tex.assetPath, newName));
                }

                try
                {
                    if (renamePlan.Count > 0)
                    {
                        SafeAssetRenameService.Execute(renamePlan, new AssetRenameHistory());
                        for (int index = 0; index < renameTextures.Count; index++)
                        {
                            string oldName = renameTextures[index].nameNoExt;
                            renameTextures[index].assetPath = renamePlan[index].targetPath;
                            renameTextures[index].nameNoExt = Path.GetFileNameWithoutExtension(renamePlan[index].targetPath);
                            log.Add($"  {oldName} → {renameTextures[index].nameNoExt}");
                        }
                        renameCount = renamePlan.Count;
                    }
                }
                catch (Exception exception)
                {
                    cancelled = true;
                    log.Add($"  重命名中断：{exception.Message}");
                    Debug.LogException(exception);
                }
                step += allTextures.Count;
            }

            // 2. Shader replace
            if (_doShaderReplace && _targetShader != null && !cancelled)
            {
                log.Add("\n── Shader 替换 ──");
                EditorUtility.DisplayProgressBar("批量处理 - Shader 替换", _targetShader.name, (float)step / totalSteps);
                shaderCount = TexRules.ReplaceShaders(allMats, _targetShader);
                step += allMats.Length;
                log.Add($"  {shaderCount} 个材质球 → {_targetShader.name}");
            }

            // 3. URP Material Repair
            if (_doRepair && !cancelled)
            {
                log.Add("\n── URP 材质修复 ──");
                EditorUtility.DisplayProgressBar("批量处理 - URP 材质修复", "扫描全部材质...", (float)step / totalSteps);
                repairCount = MaterialRepairTab.BatchRepairAll(_rootFolder);
                log.Add($"  修复了 {repairCount} 个材质");
            }

            // 4. Fill
            if (_doFill && !cancelled)
            {
                log.Add("\n── 材质球填充 ──");
                if (_doRename && renameCount > 0) RefreshMatches();

                Undo.RecordObjects(allMats, "AutoProcess Fill Textures");
                foreach (var g in _groups)
                {
                    foreach (var (mat, _, slots) in g.matches)
                    {
                        if (cancelled) break;
                        cancelled = EditorUtility.DisplayCancelableProgressBar("批量处理 - 填充材质球", mat.name, (float)step++ / totalSteps);
                        int before = slots.Count(kv => kv.Value != null);
                        if (before == 0) continue;
                        TexRules.FillMaterialSlots(mat, slots);
                        EditorUtility.SetDirty(mat);
                        fillCount += before;
                        foreach (var kv in slots.Where(kv => kv.Value != null))
                            log.Add($"  {mat.name}.{kv.Key} ← {kv.Value.name}");
                    }
                }
                AssetDatabase.SaveAssets();
            }

            EditorUtility.ClearProgressBar();

            log.Add("\n═══════════════════════════════════════════");
            log.Add($"[AutoProcess] 完成: 改名 {renameCount} | Shader {shaderCount} | 材质修复 {repairCount} | 填充 {fillCount}");
            if (cancelled) log.Add("  ⚠ 用户取消，部分未执行");
            log.Add("═══════════════════════════════════════════");
            Debug.Log(string.Join("\n", log));

            EditorUtility.DisplayDialog("批量处理完成",
                $"改名: {renameCount}\nShader替换: {shaderCount}\nURP材质修复: {repairCount}\n贴图填充: {fillCount}" + (cancelled ? "\n\n⚠ 已取消" : ""),
                "确定");
        }

        void RefreshMatches()
        {
            foreach (var g in _groups)
            {
                g.textures.Clear();
                g.texGroupMap.Clear();
                g.matches.Clear();

                var texGuids = AssetDatabase.FindAssets("t:Texture2D", new[] { g.folderPath });
                g.allTexNames = new HashSet<string>(
                    texGuids.Select(guid => Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(guid))));

                foreach (var guid in texGuids)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    var nameNoExt = Path.GetFileNameWithoutExtension(path);
                    var (baseName, suffix) = TexRules.ExtractSuffix(nameNoExt, g.allTexNames);
                    var type = TexRules.GuessType(suffix);
                    if (_ctx.suffixMap.TryGetValue(suffix, out var ov)) type = ov;
                    g.textures.Add(new TexInfo { assetPath = path, nameNoExt = nameNoExt, baseName = baseName, suffix = suffix, type = type });
                    if (!g.texGroupMap.ContainsKey(baseName))
                        g.texGroupMap[baseName] = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
                    g.texGroupMap[baseName][suffix] = path;
                }

                TexRules.ConsolidateTexGroups(g.texGroupMap);

                foreach (var mat in g.materials)
                {
                    var (groupName, texGroup) = TexRules.FindTexGroup(mat.name, g.texGroupMap, g.allTexNames);
                    var slots = texGroup != null ? TexRules.MatchTexSlots(mat, texGroup, _ctx.suffixMap) : new Dictionary<TexType, Texture2D>();
                    g.matches.Add((mat, groupName, slots));
                }
            }
        }
    }
}

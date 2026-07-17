using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Bento
{
    class ResourceVariantTool
    {
        readonly ResourceVariantSession _session = new ResourceVariantSession();
        int _selectedPreset;
        Vector2 _scroll;
        Vector2 _planScroll;
        DefaultAsset _prefabFolder;
        DefaultAsset _materialFolder;
        DefaultAsset _textureFolder;
        DefaultAsset _excludedFolder;
        bool _showPlan = true;
        bool _showHealth;

        public ResourceVariantTool()
        {
            RefreshFolderAssets();
        }

        ResourceVariantPreset Settings => _session.Settings;
        ResourceVariantPresetList Presets => _session.Presets;
        ResourceVariantPlan Plan => _session.Plan;
        ResourceVariantGenerationResult LastResult => _session.LastResult;

        public void Draw()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorGUILayout.HelpBox("Play Mode 中禁止生成资源变体，请先退出运行模式。", MessageType.Warning);
                return;
            }

            var selectedObjects = BentoUtility.GetTopLevelSelectedSceneObjects();
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            DrawHeader();
            DrawPresets();
            DrawCopyOptions();
            DrawTextureKinds();
            DrawExcludedFolders();
            DrawFolders();
            DrawNaming();
            DrawPlan(selectedObjects);
            DrawActions(selectedObjects);
            DrawLastResult();
            EditorGUILayout.EndScrollView();
        }

        void DrawHeader()
        {
            EditorGUILayout.LabelField("场景资源变体", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("把 Hierarchy 选中的 Prefab、FBX 或普通对象保存为独立 Prefab。可选复制材质和贴图并自动重连。", MessageType.Info);
        }

        void DrawPresets()
        {
            EditorGUILayout.LabelField("配置预设", EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                string[] names = Presets.items.Select(item => item.name).ToArray();
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (names.Length > 0) _selectedPreset = EditorGUILayout.Popup("已有预设", Mathf.Clamp(_selectedPreset, 0, names.Length - 1), names);
                    else EditorGUILayout.LabelField("已有预设", "暂无");
                    using (new EditorGUI.DisabledScope(names.Length == 0))
                    {
                        if (GUILayout.Button("载入", GUILayout.Width(50))) LoadPreset();
                        if (GUILayout.Button("删除", GUILayout.Width(50))) DeletePreset();
                    }
                }
                Settings.name = EditorGUILayout.TextField("当前预设名称", Settings.name);
                using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(Settings.name)))
                    if (GUILayout.Button("保存/更新当前预设")) SavePreset();
            }
        }

        void DrawCopyOptions()
        {
            EditorGUILayout.LabelField("复制内容", EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.ToggleLeft("生成独立 Prefab（必选）", true);
                Settings.copyPrefab = true;
                EditorGUILayout.ToggleLeft("复制并重连材质球（必选）", true);
                Settings.copyMaterials = true;
                Settings.copyTextures = EditorGUILayout.ToggleLeft("复制并重连材质引用的贴图", Settings.copyTextures);
                Settings.replaceSceneObjects = EditorGUILayout.ToggleLeft("生成后替换当前场景选中对象（支持 Ctrl+Z）", Settings.replaceSceneObjects);
            }
        }

        void DrawTextureKinds()
        {
            using (new EditorGUI.DisabledScope(!Settings.copyTextures))
            {
                EditorGUILayout.LabelField("贴图语义筛选", EditorStyles.boldLabel);
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.HelpBox("优先按 Shader 属性语义识别；TextureImporter 和文件名仅作兜底。", MessageType.None);
                    Settings.copyBaseColor = EditorGUILayout.ToggleLeft("基础色 / Albedo", Settings.copyBaseColor);
                    Settings.copyNormal = EditorGUILayout.ToggleLeft("法线 / Normal", Settings.copyNormal);
                    Settings.copyMetallic = EditorGUILayout.ToggleLeft("金属、粗糙度、光滑度、Specular", Settings.copyMetallic);
                    Settings.copyOcclusion = EditorGUILayout.ToggleLeft("AO / Occlusion", Settings.copyOcclusion);
                    Settings.copyEmission = EditorGUILayout.ToggleLeft("自发光 / Emission", Settings.copyEmission);
                    Settings.copyHeight = EditorGUILayout.ToggleLeft("高度、置换、Parallax", Settings.copyHeight);
                    Settings.copyMask = EditorGUILayout.ToggleLeft("Mask、Detail、Control、Splat", Settings.copyMask);
                    Settings.copyOtherTextures = EditorGUILayout.ToggleLeft("其他无法识别的贴图", Settings.copyOtherTextures);
                }
            }
        }

        void DrawExcludedFolders()
        {
            EditorGUILayout.LabelField("公共资源排除目录", EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                _excludedFolder = (DefaultAsset)EditorGUILayout.ObjectField("添加排除目录", _excludedFolder, typeof(DefaultAsset), false);
                string path = _excludedFolder == null ? string.Empty : AssetDatabase.GetAssetPath(_excludedFolder);
                using (new EditorGUI.DisabledScope(!AssetDatabase.IsValidFolder(path)))
                    if (GUILayout.Button("加入排除目录")) { if (!Settings.excludedFolders.Contains(path)) Settings.excludedFolders.Add(path); _excludedFolder = null; _session.InvalidatePlan(); }
                for (int index = 0; index < Settings.excludedFolders.Count; index++)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.SelectableLabel(Settings.excludedFolders[index], EditorStyles.miniLabel, GUILayout.Height(18));
                        if (GUILayout.Button("移除", GUILayout.Width(48))) { Settings.excludedFolders.RemoveAt(index); _session.InvalidatePlan(); break; }
                    }
                }
            }
        }

        void DrawFolders()
        {
            EditorGUILayout.LabelField("输出文件夹", EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawFolder("Prefab 文件夹", ref _prefabFolder, ref Settings.prefabFolder);
                DrawFolder("Material 文件夹", ref _materialFolder, ref Settings.materialFolder);
                using (new EditorGUI.DisabledScope(!Settings.copyTextures)) DrawFolder("Texture 文件夹", ref _textureFolder, ref Settings.textureFolder);
            }
        }

        void DrawNaming()
        {
            EditorGUILayout.LabelField("命名规则", EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                var naming = GetNamingOptions();
                AssetNaming.DrawFields(naming, "统一基础名称（留空使用原对象名）");
                ApplyNaming(naming);
            }
        }

        void DrawPlan(List<GameObject> selectedObjects)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                _showPlan = EditorGUILayout.Foldout(_showPlan, "生成计划 / 干运行", true);
                if (GUILayout.Button("分析选中对象", GUILayout.Width(100))) _session.Analyze(selectedObjects);
            }
            if (!_showPlan) return;
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (Plan == null) { EditorGUILayout.LabelField("点击分析，不会创建文件。", EditorStyles.miniLabel); return; }
                EditorGUILayout.LabelField($"Prefab：{Plan.objects.Count}　材质：{Plan.MaterialCopyCount}　贴图：{Plan.TextureCopyCount}", EditorStyles.boldLabel);
                foreach (string warning in Plan.warnings.Distinct()) EditorGUILayout.HelpBox(warning, MessageType.Warning);
                _planScroll = EditorGUILayout.BeginScrollView(_planScroll, GUILayout.MinHeight(180), GUILayout.MaxHeight(360));
                foreach (var objectPlan in Plan.objects) DrawObjectPlan(objectPlan);
                EditorGUILayout.EndScrollView();
            }
        }

        static void DrawObjectPlan(ResourceVariantObjectPlan objectPlan)
        {
            EditorGUILayout.LabelField($"● {objectPlan.sourceObject.name} → {objectPlan.prefabOutputName}.prefab", EditorStyles.boldLabel);
            foreach (var material in objectPlan.materials)
            {
                string matName = ResourceVariantGenerator.BuildDependentName(objectPlan.prefabOutputName, material.source.name, "Material");
                EditorGUILayout.LabelField($"　材质：{material.source.name} → {matName}.mat [{State(material.willCopy, material.reason)}]");
                foreach (var texture in material.textures)
                {
                    if (texture.disposition != VariantTextureDisposition.CopyCandidate)
                    {
                        EditorGUILayout.LabelField($"　　{texture.propertyName}: {texture.source.name} [{texture.reason}]", EditorStyles.miniLabel);
                        continue;
                    }
                    string texName = ResourceVariantGenerator.BuildDependentName(objectPlan.prefabOutputName, texture.source.name, "Texture");
                    EditorGUILayout.LabelField($"　　{texture.propertyName}: {texture.source.name} → {texName}{Path.GetExtension(texture.sourcePath)} [{State(texture.willCopy, texture.reason)}]", EditorStyles.miniLabel);
                }
            }
        }

        void DrawActions(List<GameObject> selectedObjects)
        {
            bool valid = _session.Validate(selectedObjects, out string message);
            using (new EditorGUILayout.HorizontalScope())
            using (new EditorGUI.DisabledScope(!valid))
            {
                if (GUILayout.Button("仅干运行", GUILayout.Height(38))) _session.Analyze(selectedObjects);
                if (GUILayout.Button($"生成 {selectedObjects.Count} 个资源变体", GUILayout.Height(38)))
                {
                    _session.Analyze(selectedObjects);
                    if (EditorUtility.DisplayDialog("确认生成资源变体", $"Prefab {Plan.objects.Count} / 材质 {Plan.MaterialCopyCount} / 贴图 {Plan.TextureCopyCount}", "开始生成", "取消")) Generate();
                }
            }
            if (!valid) EditorGUILayout.HelpBox(message, MessageType.Warning);
        }

        void DrawLastResult()
        {
            if (LastResult == null) return;
            _showHealth = EditorGUILayout.Foldout(_showHealth, "上次生成结果与健康检查", true);
            if (!_showHealth) return;
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField($"Prefab {LastResult.createdPrefabs.Count} / 材质 {LastResult.createdMaterials.Count} / 贴图 {LastResult.createdTextures.Count}");
                if (LastResult.healthWarnings.Count == 0) EditorGUILayout.HelpBox("健康检查通过。", MessageType.Info);
                else foreach (string warning in LastResult.healthWarnings) EditorGUILayout.HelpBox(warning, MessageType.Warning);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("定位新 Prefab")) PingFirst(LastResult.createdPrefabs);
                    if (GUILayout.Button("定位材质")) PingFirst(LastResult.createdMaterials);
                    if (GUILayout.Button("定位贴图")) PingFirst(LastResult.createdTextures);
                }
                if (GUILayout.Button("一键完整撤销本次生成")) UndoLastGeneration();
            }
        }

        void Generate()
        {
            try
            {
                var result = _session.Generate();
                if (result.replacements.Count > 0) Selection.objects = result.replacements.Cast<UnityEngine.Object>().ToArray();
            }
            catch (ResourceVariantGenerationException exception)
            {
                Debug.LogException(exception.InnerException ?? exception);
                EditorUtility.DisplayDialog("生成中断", $"{exception.Message}\n\n已创建 {exception.result.createdAssets.Count} 个资源，可在下方撤销清理。", "确定");
            }
            catch (Exception exception) { Debug.LogException(exception); EditorUtility.DisplayDialog("生成中断", exception.Message, "确定"); }
        }

        void UndoLastGeneration()
        {
            if (!_session.TryUndoLastGeneration(out string referencedPrefab))
            {
                if (string.IsNullOrEmpty(referencedPrefab)) return;
                EditorUtility.DisplayDialog(
                    "完整撤销已停止",
                    $"当前已加载场景仍在使用：\n{referencedPrefab}\n\n刚才最顶层的 Undo 不是本次资源变体生成，或场景中还有其他实例引用它。为了避免误删引用，工具没有删除任何 Project 资源。请先撤销后续编辑，再重新点击。",
                    "确定");
                return;
            }

            if (!EditorUtility.DisplayDialog(
                    "确认完整撤销",
                    $"场景中的生成实例已经撤销。接下来将删除 {LastResult.createdAssets.Count} 个本次生成的 Project 资源。",
                    "删除资源",
                    "取消")) return;

            _session.DeleteGeneratedAssets();
        }

        void SavePreset()
        {
            _session.SavePreset();
            _selectedPreset = Mathf.Max(0, Presets.items.FindIndex(item => string.Equals(item.name, Settings.name, StringComparison.OrdinalIgnoreCase)));
        }

        void LoadPreset()
        {
            _session.LoadPreset(_selectedPreset);
            RefreshFolderAssets();
        }

        void DeletePreset()
        {
            if (Presets.items.Count == 0) return;
            var preset = Presets.items[Mathf.Clamp(_selectedPreset, 0, Presets.items.Count - 1)];
            if (!EditorUtility.DisplayDialog("删除预设", $"确定删除“{preset.name}”？", "删除", "取消")) return;
            _session.DeletePreset(_selectedPreset);
            _selectedPreset = Mathf.Max(0, _selectedPreset - 1);
        }

        void ApplyNaming(AssetNamingOptions naming)
        {
            if (naming == null) return;
            Settings.prefix = naming.prefix;
            Settings.baseName = naming.baseName;
            Settings.suffix = naming.suffix;
            Settings.appendNumber = naming.appendNumber;
            Settings.startNumber = naming.startNumber;
            Settings.numberDigits = naming.numberDigits;
        }

        AssetNamingOptions GetNamingOptions() => new AssetNamingOptions
        {
            prefix = Settings.prefix,
            baseName = Settings.baseName,
            suffix = Settings.suffix,
            appendNumber = Settings.appendNumber,
            startNumber = Settings.startNumber,
            numberDigits = Settings.numberDigits
        };

        void RefreshFolderAssets()
        {
            _prefabFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(Settings.prefabFolder);
            _materialFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(Settings.materialFolder);
            _textureFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(Settings.textureFolder);
        }

        void DrawFolder(string label, ref DefaultAsset asset, ref string path)
        {
            var next = (DefaultAsset)EditorGUILayout.ObjectField(label, asset, typeof(DefaultAsset), false);
            if (next != asset)
            {
                string nextPath = next == null ? string.Empty : AssetDatabase.GetAssetPath(next);
                if (next == null || AssetDatabase.IsValidFolder(nextPath)) { asset = next; path = nextPath; _session.InvalidatePlan(); }
            }
            EditorGUILayout.SelectableLabel(path ?? string.Empty, EditorStyles.miniLabel, GUILayout.Height(18));
        }

        static string State(bool enabled, string reason) => enabled ? "复制" : $"保留（{reason}）";
        static void PingFirst(List<string> paths) { if (paths.Count == 0) return; var asset = AssetDatabase.LoadMainAssetAtPath(paths[0]); Selection.activeObject = asset; EditorGUIUtility.PingObject(asset); }
    }

    static class ResourceVariantMcpSummary
    {
        public static object Build(ResourceVariantPlan plan) => new
        {
            selected_object_count = plan.objects.Count,
            material_copy_count = plan.MaterialCopyCount,
            texture_copy_count = plan.TextureCopyCount,
            warnings = plan.warnings.Distinct().ToArray(),
            objects = plan.objects.Select(item => new { source_name = item.sourceObject.name, source_asset_path = item.sourceAssetPath, prefab_output_name = item.prefabOutputName }).ToArray()
        };
    }
}

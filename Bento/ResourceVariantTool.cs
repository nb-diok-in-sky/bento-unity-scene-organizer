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
        readonly ResourceVariantPresetStore _store = new ResourceVariantPresetStore();
        ResourceVariantPreset _settings = new ResourceVariantPreset();
        ResourceVariantPresetList _presets;
        ResourceVariantPlan _plan;
        ResourceVariantGenerationResult _lastResult;
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
            _presets = _store.LoadPresets();
            NormalizeSettings(false);
            RefreshFolderAssets();
        }

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

        public object AnalyzeSelectionForMcp(AssetNamingOptions naming = null)
        {
            ApplyNaming(naming);
            var selectedObjects = BentoUtility.GetTopLevelSelectedSceneObjects();
            _plan = BuildPlan(selectedObjects);
            return ResourceVariantMcpSummary.Build(_plan);
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
                string[] names = _presets.items.Select(item => item.name).ToArray();
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
                _settings.name = EditorGUILayout.TextField("当前预设名称", _settings.name);
                using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_settings.name)))
                    if (GUILayout.Button("保存/更新当前预设")) SavePreset();
            }
        }

        void DrawCopyOptions()
        {
            EditorGUILayout.LabelField("复制内容", EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.ToggleLeft("生成独立 Prefab（必选）", true);
                _settings.copyPrefab = true;
                _settings.copyMaterials = EditorGUILayout.ToggleLeft("复制并重连材质球", _settings.copyMaterials);
                using (new EditorGUI.DisabledScope(!_settings.copyMaterials))
                    _settings.copyTextures = EditorGUILayout.ToggleLeft("复制并重连材质引用的贴图", _settings.copyTextures);
                _settings.replaceSceneObjects = EditorGUILayout.ToggleLeft("生成后替换当前场景选中对象（支持 Ctrl+Z）", _settings.replaceSceneObjects);
            }
        }

        void DrawTextureKinds()
        {
            using (new EditorGUI.DisabledScope(!_settings.copyTextures))
            {
                EditorGUILayout.LabelField("贴图语义筛选", EditorStyles.boldLabel);
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.HelpBox("优先按 Shader 属性语义识别；TextureImporter 和文件名仅作兜底。", MessageType.None);
                    _settings.copyBaseColor = EditorGUILayout.ToggleLeft("基础色 / Albedo", _settings.copyBaseColor);
                    _settings.copyNormal = EditorGUILayout.ToggleLeft("法线 / Normal", _settings.copyNormal);
                    _settings.copyMetallic = EditorGUILayout.ToggleLeft("金属、粗糙度、光滑度、Specular", _settings.copyMetallic);
                    _settings.copyOcclusion = EditorGUILayout.ToggleLeft("AO / Occlusion", _settings.copyOcclusion);
                    _settings.copyEmission = EditorGUILayout.ToggleLeft("自发光 / Emission", _settings.copyEmission);
                    _settings.copyHeight = EditorGUILayout.ToggleLeft("高度、置换、Parallax", _settings.copyHeight);
                    _settings.copyMask = EditorGUILayout.ToggleLeft("Mask、Detail、Control、Splat", _settings.copyMask);
                    _settings.copyOtherTextures = EditorGUILayout.ToggleLeft("其他无法识别的贴图", _settings.copyOtherTextures);
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
                    if (GUILayout.Button("加入排除目录")) { if (!_settings.excludedFolders.Contains(path)) _settings.excludedFolders.Add(path); _excludedFolder = null; InvalidatePlan(); }
                for (int index = 0; index < _settings.excludedFolders.Count; index++)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.SelectableLabel(_settings.excludedFolders[index], EditorStyles.miniLabel, GUILayout.Height(18));
                        if (GUILayout.Button("移除", GUILayout.Width(48))) { _settings.excludedFolders.RemoveAt(index); InvalidatePlan(); break; }
                    }
                }
            }
        }

        void DrawFolders()
        {
            EditorGUILayout.LabelField("输出文件夹", EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawFolder("Prefab 文件夹", ref _prefabFolder, ref _settings.prefabFolder);
                using (new EditorGUI.DisabledScope(!_settings.copyMaterials)) DrawFolder("Material 文件夹", ref _materialFolder, ref _settings.materialFolder);
                using (new EditorGUI.DisabledScope(!_settings.copyTextures)) DrawFolder("Texture 文件夹", ref _textureFolder, ref _settings.textureFolder);
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
                if (GUILayout.Button("分析选中对象", GUILayout.Width(100))) _plan = BuildPlan(selectedObjects);
            }
            if (!_showPlan) return;
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (_plan == null) { EditorGUILayout.LabelField("点击分析，不会创建文件。", EditorStyles.miniLabel); return; }
                EditorGUILayout.LabelField($"Prefab：{_plan.objects.Count}　材质：{_plan.MaterialCopyCount}　贴图：{_plan.TextureCopyCount}", EditorStyles.boldLabel);
                foreach (string warning in _plan.warnings.Distinct()) EditorGUILayout.HelpBox(warning, MessageType.Warning);
                _planScroll = EditorGUILayout.BeginScrollView(_planScroll, GUILayout.MinHeight(180), GUILayout.MaxHeight(360));
                foreach (var objectPlan in _plan.objects) DrawObjectPlan(objectPlan);
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
                    if (texture.isRuntimeTexture)
                    {
                        EditorGUILayout.LabelField($"　　{texture.propertyName}: {texture.source.name} [运行时内部纹理，保留引用]", EditorStyles.miniLabel);
                        continue;
                    }
                    string texName = ResourceVariantGenerator.BuildDependentName(objectPlan.prefabOutputName, texture.source.name, "Texture");
                    EditorGUILayout.LabelField($"　　{texture.propertyName}: {texture.source.name} → {texName}{Path.GetExtension(texture.sourcePath)} [{State(texture.willCopy, texture.reason)}]", EditorStyles.miniLabel);
                }
            }
        }

        void DrawActions(List<GameObject> selectedObjects)
        {
            bool valid = Validate(selectedObjects, out string message);
            using (new EditorGUILayout.HorizontalScope())
            using (new EditorGUI.DisabledScope(!valid))
            {
                if (GUILayout.Button("仅干运行", GUILayout.Height(38))) _plan = BuildPlan(selectedObjects);
                if (GUILayout.Button($"生成 {selectedObjects.Count} 个资源变体", GUILayout.Height(38)))
                {
                    _plan = BuildPlan(selectedObjects);
                    if (EditorUtility.DisplayDialog("确认生成资源变体", $"Prefab {_plan.objects.Count} / 材质 {_plan.MaterialCopyCount} / 贴图 {_plan.TextureCopyCount}", "开始生成", "取消")) Generate();
                }
            }
            if (!valid) EditorGUILayout.HelpBox(message, MessageType.Warning);
        }

        void DrawLastResult()
        {
            if (_lastResult == null) return;
            _showHealth = EditorGUILayout.Foldout(_showHealth, "上次生成结果与健康检查", true);
            if (!_showHealth) return;
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField($"Prefab {_lastResult.createdPrefabs.Count} / 材质 {_lastResult.createdMaterials.Count} / 贴图 {_lastResult.createdTextures.Count}");
                if (_lastResult.healthWarnings.Count == 0) EditorGUILayout.HelpBox("健康检查通过。", MessageType.Info);
                else foreach (string warning in _lastResult.healthWarnings) EditorGUILayout.HelpBox(warning, MessageType.Warning);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("定位新 Prefab")) PingFirst(_lastResult.createdPrefabs);
                    if (GUILayout.Button("定位材质")) PingFirst(_lastResult.createdMaterials);
                    if (GUILayout.Button("定位贴图")) PingFirst(_lastResult.createdTextures);
                }
                if (GUILayout.Button("撤销本次生成的 Project 资源")) UndoGeneratedAssets();
            }
        }

        ResourceVariantPlan BuildPlan(IReadOnlyList<GameObject> selectedObjects) => new ResourceVariantPlanner(_settings).Build(selectedObjects);

        void Generate()
        {
            try
            {
                _lastResult = new ResourceVariantGenerator(_settings).Generate(_plan);
                foreach (var record in _lastResult.sourceRecords) record.presetName = _settings.name;
                _store.AddSourceRecords(_lastResult.sourceRecords);
                if (_lastResult.replacements.Count > 0) Selection.objects = _lastResult.replacements.Cast<UnityEngine.Object>().ToArray();
            }
            catch (ResourceVariantGenerationException exception)
            {
                _lastResult = exception.result;
                Debug.LogException(exception.InnerException ?? exception);
                EditorUtility.DisplayDialog("生成中断", $"{exception.Message}\n\n已创建 {_lastResult.createdAssets.Count} 个资源，可在下方撤销清理。", "确定");
            }
            catch (Exception exception) { Debug.LogException(exception); EditorUtility.DisplayDialog("生成中断", exception.Message, "确定"); }
        }

        void UndoGeneratedAssets()
        {
            if (_lastResult == null || _lastResult.createdAssets.Count == 0) return;
            string referencedPrefab = FindLoadedSceneReference(_lastResult.createdPrefabs);
            if (!string.IsNullOrEmpty(referencedPrefab))
            {
                EditorUtility.DisplayDialog(
                    "无法删除生成资源",
                    $"当前已加载场景仍在使用：\n{referencedPrefab}\n\n请先按 Ctrl+Z 撤销场景替换，再删除生成资源。",
                    "确定");
                return;
            }
            if (!EditorUtility.DisplayDialog("撤销本次生成", $"将删除 {_lastResult.createdAssets.Count} 个生成资源。场景替换请先 Ctrl+Z。", "删除", "取消")) return;
            foreach (string path in _lastResult.createdPrefabs.Concat(_lastResult.createdMaterials).Concat(_lastResult.createdTextures).Distinct())
                if (!string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(path))) AssetDatabase.DeleteAsset(path);
            _store.RemoveSourceRecords(new HashSet<string>(_lastResult.createdPrefabs, StringComparer.OrdinalIgnoreCase));
            _lastResult.createdAssets.Clear();
        }

        static string FindLoadedSceneReference(IEnumerable<string> prefabPaths)
        {
            var pathSet = new HashSet<string>(prefabPaths, StringComparer.OrdinalIgnoreCase);
            foreach (var gameObject in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (gameObject == null || !gameObject.scene.IsValid() || !gameObject.scene.isLoaded) continue;
                string prefabPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(gameObject);
                if (pathSet.Contains(prefabPath)) return prefabPath;
            }
            return string.Empty;
        }

        bool Validate(IReadOnlyList<GameObject> selected, out string message)
        {
            if (selected.Count == 0) { message = "请在 Hierarchy 选择对象。"; return false; }
            if (!BentoUtility.IsProjectFolder(_settings.prefabFolder)) { message = "请选择 Prefab 输出文件夹。"; return false; }
            if (_settings.copyMaterials && !BentoUtility.IsProjectFolder(_settings.materialFolder)) { message = "请选择 Material 输出文件夹。"; return false; }
            if (_settings.copyTextures && !BentoUtility.IsProjectFolder(_settings.textureFolder)) { message = "请选择 Texture 输出文件夹。"; return false; }
            message = string.Empty; return true;
        }

        void SavePreset()
        {
            var copy = Clone(_settings);
            int index = _presets.items.FindIndex(item => string.Equals(item.name, copy.name, StringComparison.OrdinalIgnoreCase));
            if (index >= 0) _presets.items[index] = copy; else { _presets.items.Add(copy); index = _presets.items.Count - 1; }
            _selectedPreset = index;
            _store.SavePresets(_presets);
        }

        void LoadPreset()
        {
            if (_presets.items.Count == 0) return;
            _settings = Clone(_presets.items[Mathf.Clamp(_selectedPreset, 0, _presets.items.Count - 1)]);
            NormalizeSettings(true); RefreshFolderAssets(); InvalidatePlan();
        }

        void DeletePreset()
        {
            if (_presets.items.Count == 0) return;
            var preset = _presets.items[Mathf.Clamp(_selectedPreset, 0, _presets.items.Count - 1)];
            if (!EditorUtility.DisplayDialog("删除预设", $"确定删除“{preset.name}”？", "删除", "取消")) return;
            _presets.items.Remove(preset); _selectedPreset = Mathf.Max(0, _selectedPreset - 1); _store.SavePresets(_presets);
        }

        void ApplyNaming(AssetNamingOptions naming)
        {
            if (naming == null) return;
            _settings.prefix = naming.prefix;
            _settings.baseName = naming.baseName;
            _settings.suffix = naming.suffix;
            _settings.appendNumber = naming.appendNumber;
            _settings.startNumber = naming.startNumber;
            _settings.numberDigits = naming.numberDigits;
        }

        AssetNamingOptions GetNamingOptions() => new AssetNamingOptions
        {
            prefix = _settings.prefix,
            baseName = _settings.baseName,
            suffix = _settings.suffix,
            appendNumber = _settings.appendNumber,
            startNumber = _settings.startNumber,
            numberDigits = _settings.numberDigits
        };

        void NormalizeSettings(bool migrateLegacy)
        {
            _settings.excludedFolders = _settings.excludedFolders ?? new List<string>();
            if (!migrateLegacy) return;
            bool any = _settings.copyBaseColor || _settings.copyNormal || _settings.copyMetallic || _settings.copyOcclusion || _settings.copyEmission || _settings.copyHeight || _settings.copyMask || _settings.copyOtherTextures;
            if (!any) _settings.copyBaseColor = _settings.copyNormal = _settings.copyMetallic = _settings.copyOcclusion = _settings.copyEmission = _settings.copyHeight = _settings.copyMask = _settings.copyOtherTextures = true;
        }

        void RefreshFolderAssets()
        {
            _prefabFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(_settings.prefabFolder);
            _materialFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(_settings.materialFolder);
            _textureFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(_settings.textureFolder);
        }

        void DrawFolder(string label, ref DefaultAsset asset, ref string path)
        {
            var next = (DefaultAsset)EditorGUILayout.ObjectField(label, asset, typeof(DefaultAsset), false);
            if (next != asset)
            {
                string nextPath = next == null ? string.Empty : AssetDatabase.GetAssetPath(next);
                if (next == null || AssetDatabase.IsValidFolder(nextPath)) { asset = next; path = nextPath; InvalidatePlan(); }
            }
            EditorGUILayout.SelectableLabel(path ?? string.Empty, EditorStyles.miniLabel, GUILayout.Height(18));
        }

        static string State(bool enabled, string reason) => enabled ? "复制" : $"保留（{reason}）";
        static ResourceVariantPreset Clone(ResourceVariantPreset value) => JsonUtility.FromJson<ResourceVariantPreset>(JsonUtility.ToJson(value));
        static void PingFirst(List<string> paths) { if (paths.Count == 0) return; var asset = AssetDatabase.LoadMainAssetAtPath(paths[0]); Selection.activeObject = asset; EditorGUIUtility.PingObject(asset); }
        void InvalidatePlan() => _plan = null;
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

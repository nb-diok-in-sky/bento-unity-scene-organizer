using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Bento
{
    sealed class ResourceVariantSettingsGUI
    {
        readonly ResourceVariantSession _session;
        int _selectedPreset;
        DefaultAsset _prefabFolder;
        DefaultAsset _materialFolder;
        DefaultAsset _textureFolder;
        DefaultAsset _excludedFolder;

        public ResourceVariantSettingsGUI(ResourceVariantSession session)
        {
            _session = session;
            RefreshFolderAssets();
        }

        ResourceVariantPreset Settings => _session.Settings;
        ResourceVariantPresetList Presets => _session.Presets;

        public void Draw()
        {
            DrawPresets();
            EditorGUI.BeginChangeCheck();
            DrawCopyOptions();
            DrawTextureKinds();
            DrawExcludedFolders();
            DrawFolders();
            DrawNaming();
            if (EditorGUI.EndChangeCheck()) _session.InvalidatePlan();
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
                if (GUILayout.Button("保存/更新当前预设"))
                {
                    _session.SavePreset();
                    _selectedPreset = Mathf.Max(0, Presets.items.FindIndex(item =>
                        string.Equals(item.name, Settings.name, StringComparison.OrdinalIgnoreCase)));
                }
            }
        }

        void DrawCopyOptions()
        {
            EditorGUILayout.LabelField("复制内容", EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.ToggleLeft("生成独立 Prefab（必选）", true);
                EditorGUILayout.ToggleLeft("复制并重连材质球（必选）", true);
                Settings.copyPrefab = Settings.copyMaterials = true;
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
                if (GUILayout.Button("加入排除目录"))
                {
                    if (!Settings.excludedFolders.Contains(path)) Settings.excludedFolders.Add(path);
                    _excludedFolder = null;
                }
                for (int index = 0; index < Settings.excludedFolders.Count; index++)
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.SelectableLabel(Settings.excludedFolders[index], EditorStyles.miniLabel, GUILayout.Height(18));
                    if (GUILayout.Button("移除", GUILayout.Width(48)))
                    {
                        Settings.excludedFolders.RemoveAt(index);
                        break;
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
                using (new EditorGUI.DisabledScope(!Settings.copyTextures))
                    DrawFolder("Texture 文件夹", ref _textureFolder, ref Settings.textureFolder);
            }
        }

        void DrawNaming()
        {
            EditorGUILayout.LabelField("命名规则", EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                var naming = Settings.GetNamingOptions();
                BentoGUI.DrawNamingFields(naming, "统一基础名称（留空使用原对象名）");
                Settings.ApplyNaming(naming);
            }
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
                if (next == null || AssetDatabase.IsValidFolder(nextPath))
                {
                    asset = next;
                    path = nextPath;
                }
            }
            EditorGUILayout.SelectableLabel(path ?? string.Empty, EditorStyles.miniLabel, GUILayout.Height(18));
        }
    }
}

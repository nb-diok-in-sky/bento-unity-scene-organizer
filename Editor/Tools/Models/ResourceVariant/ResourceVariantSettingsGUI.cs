using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Bento
{
    sealed class ResourceVariantSettingsGUI
    {
        readonly ResourceVariantSession _session;
        int _selectedPreset;
        DefaultAsset _prefabFolder;
        DefaultAsset _modelFolder;
        DefaultAsset _materialFolder;
        DefaultAsset _textureFolder;
        DefaultAsset _excludedFolder;
        string _lastObservedProjectFolder = string.Empty;
        string _folderDetectionMessage = string.Empty;
        MessageType _folderDetectionMessageType = MessageType.None;

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
                Settings.copyModels = EditorGUILayout.ToggleLeft(
                    "复制并重连模型主资产（FBX / OBJ / Mesh .asset）",
                    Settings.copyModels);
                Settings.copyTextures = EditorGUILayout.ToggleLeft(
                    "按原材质属性槽复制并重连全部贴图",
                    Settings.copyTextures);
                Settings.replaceSceneObjects = EditorGUILayout.ToggleLeft("生成后替换当前场景选中对象（支持 Ctrl+Z）", Settings.replaceSceneObjects);
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
            ObserveProjectFolder();
            EditorGUILayout.LabelField("输出文件夹", EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                string currentFolder = ResourceVariantFolderResolver.GetCurrentProjectFolder();
                EditorGUILayout.LabelField(
                    $"Project 当前目录：{currentFolder}",
                    EditorStyles.miniLabel);
                if (GUILayout.Button("从 Project 当前目录自动识别 Model / Prefab / Material / Texture"))
                    DetectFolders(currentFolder, true);
                if (!string.IsNullOrEmpty(_folderDetectionMessage))
                    EditorGUILayout.HelpBox(
                        _folderDetectionMessage,
                        _folderDetectionMessageType);

                DrawFolder("Prefab 文件夹", ref _prefabFolder, ref Settings.prefabFolder);
                using (new EditorGUI.DisabledScope(!Settings.copyModels))
                    DrawFolder("Model 文件夹", ref _modelFolder, ref Settings.modelFolder);
                DrawFolder("Material 文件夹", ref _materialFolder, ref Settings.materialFolder);
                using (new EditorGUI.DisabledScope(!Settings.copyTextures))
                    DrawFolder("Texture 文件夹", ref _textureFolder, ref Settings.textureFolder);
            }
        }

        void ObserveProjectFolder()
        {
            string currentFolder = ResourceVariantFolderResolver.GetCurrentProjectFolder();
            if (string.IsNullOrEmpty(currentFolder) ||
                string.Equals(
                    currentFolder,
                    _lastObservedProjectFolder,
                    StringComparison.OrdinalIgnoreCase))
                return;

            _lastObservedProjectFolder = currentFolder;
            DetectFolders(currentFolder, false);
        }

        void DetectFolders(string currentFolder, bool showFailure)
        {
            ResourceVariantFolderDetection detection =
                ResourceVariantFolderResolver.Detect(currentFolder);
            if (detection == null || detection.FoundCount == 0)
            {
                if (showFailure)
                {
                    _folderDetectionMessage =
                        $"没有在“{currentFolder}”对应的资源根目录找到必需的 Prefab(s) 和 Material(s) 兄弟文件夹。";
                    _folderDetectionMessageType = MessageType.Warning;
                }
                return;
            }

            if (!string.IsNullOrEmpty(detection.prefabFolder))
                Settings.prefabFolder = detection.prefabFolder;
            if (!string.IsNullOrEmpty(detection.modelFolder))
                Settings.modelFolder = detection.modelFolder;
            if (!string.IsNullOrEmpty(detection.materialFolder))
                Settings.materialFolder = detection.materialFolder;
            if (!string.IsNullOrEmpty(detection.textureFolder))
                Settings.textureFolder = detection.textureFolder;

            RefreshFolderAssets();
            _session.InvalidatePlan();
            string missing = detection.GetMissingDisplayNames();
            _folderDetectionMessage = string.IsNullOrEmpty(missing)
                ? $"已从“{detection.rootFolder}”识别全部 4 个输出文件夹。"
                : $"已从“{detection.rootFolder}”识别 {detection.FoundCount}/4 个文件夹；未找到：{missing}。未找到的字段保持原值。";
            _folderDetectionMessageType = string.IsNullOrEmpty(missing)
                ? MessageType.Info
                : MessageType.Warning;
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
            _modelFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(Settings.modelFolder);
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

    sealed class ResourceVariantFolderDetection
    {
        public string rootFolder;
        public string prefabFolder;
        public string modelFolder;
        public string materialFolder;
        public string textureFolder;

        public int FoundCount => new[]
        {
            prefabFolder,
            modelFolder,
            materialFolder,
            textureFolder
        }.Count(path => !string.IsNullOrEmpty(path));

        public string GetMissingDisplayNames()
        {
            var missing = new List<string>();
            if (string.IsNullOrEmpty(modelFolder)) missing.Add("Model(s)");
            if (string.IsNullOrEmpty(prefabFolder)) missing.Add("Prefab(s)");
            if (string.IsNullOrEmpty(materialFolder)) missing.Add("Material(s)");
            if (string.IsNullOrEmpty(textureFolder)) missing.Add("Texture(s)");
            return string.Join("、", missing);
        }
    }

    static class ResourceVariantFolderResolver
    {
        static readonly MethodInfo GetActiveFolderPathMethod =
            typeof(Editor).Assembly
                .GetType("UnityEditor.ProjectBrowser")?
                .GetMethod(
                    "GetActiveFolderPath",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        static readonly string[] ModelNames = { "model", "models" };
        static readonly string[] PrefabNames = { "prefab", "prefabs" };
        static readonly string[] MaterialNames = { "material", "materials" };
        static readonly string[] TextureNames = { "texture", "textures" };

        public static string GetCurrentProjectFolder()
        {
            string path = string.Empty;
            try
            {
                path = GetActiveFolderPathMethod?.Invoke(null, null) as string;
            }
            catch (Exception)
            {
                // Unity 小版本若调整 ProjectBrowser 内部 API，继续使用 Selection 回退。
            }
            if (AssetDatabase.IsValidFolder(path)) return path;

            path = AssetDatabase.GetAssetPath(Selection.activeObject);
            if (AssetDatabase.IsValidFolder(path)) return path;
            return string.IsNullOrEmpty(path)
                ? "Assets"
                : Normalize(Path.GetDirectoryName(path));
        }

        public static ResourceVariantFolderDetection Detect(string currentFolder)
        {
            if (!AssetDatabase.IsValidFolder(currentFolder)) return null;

            string candidate = ResolveLayoutRoot(Normalize(currentFolder));
            if (!AssetDatabase.IsValidFolder(candidate)) return null;

            ResourceVariantFolderDetection detection = DetectDirectChildren(candidate);
            return !string.IsNullOrEmpty(detection.prefabFolder) &&
                   !string.IsNullOrEmpty(detection.materialFolder)
                ? detection
                : null;
        }

        static string ResolveLayoutRoot(string currentFolder)
        {
            string candidate = currentFolder;
            for (int depth = 0; depth < 5 && AssetDatabase.IsValidFolder(candidate); depth++)
            {
                if (IsCategoryFolderName(Path.GetFileName(candidate)))
                    return Normalize(Path.GetDirectoryName(candidate));

                string parent = Normalize(Path.GetDirectoryName(candidate));
                if (string.IsNullOrEmpty(parent) ||
                    string.Equals(parent, candidate, StringComparison.OrdinalIgnoreCase))
                    break;
                candidate = parent;
            }

            // 没处在明确分类目录内时，只把当前目录本身当作资源根，禁止任意向上猜测。
            return currentFolder;
        }

        static bool IsCategoryFolderName(string folderName)
        {
            return ModelNames.Contains(folderName, StringComparer.OrdinalIgnoreCase) ||
                   PrefabNames.Contains(folderName, StringComparer.OrdinalIgnoreCase) ||
                   MaterialNames.Contains(folderName, StringComparer.OrdinalIgnoreCase) ||
                   TextureNames.Contains(folderName, StringComparer.OrdinalIgnoreCase);
        }

        static ResourceVariantFolderDetection DetectDirectChildren(string root)
        {
            string[] children = AssetDatabase.GetSubFolders(root);
            return new ResourceVariantFolderDetection
            {
                rootFolder = root,
                modelFolder = Find(children, ModelNames),
                prefabFolder = Find(children, PrefabNames),
                materialFolder = Find(children, MaterialNames),
                textureFolder = Find(children, TextureNames)
            };
        }

        static string Find(IEnumerable<string> folders, IEnumerable<string> acceptedNames)
        {
            var names = new HashSet<string>(
                acceptedNames,
                StringComparer.OrdinalIgnoreCase);
            return folders.FirstOrDefault(folder =>
                names.Contains(Path.GetFileName(folder)));
        }

        static string Normalize(string path) =>
            string.IsNullOrEmpty(path) ? string.Empty : path.Replace('\\', '/');
    }
}

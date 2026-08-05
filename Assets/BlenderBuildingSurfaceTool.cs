using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Bento
{
    sealed class BlenderBuildingSurfaceTool : IBentoPageLifecycle
    {
        const string BlenderPathKeyPrefix = "Bento.BlenderExecutable.";
        string _blenderExecutable;
        readonly string _addonPath;
        readonly string _launchScriptPath;
        readonly string _integrationError;
        GameObject _selectedFbx;

        string BlenderPathKey => BentoUtility.GetProjectEditorPrefsKey(BlenderPathKeyPrefix);

        public BlenderBuildingSurfaceTool()
        {
            _blenderExecutable = EditorPrefs.GetString(BlenderPathKey, string.Empty);
            if (!File.Exists(_blenderExecutable))
                _blenderExecutable = FindBlenderExecutable();
            try
            {
                _addonPath = BlenderBuildingSurfaceLauncher.ResolveAddonPath();
                _launchScriptPath = BlenderBuildingSurfaceLauncher.ResolveLaunchScriptPath(_addonPath);
            }
            catch (Exception exception)
            {
                _integrationError = exception.Message;
            }
            LockSelectedFbx();
        }

        public void Draw()
        {
            EditorGUILayout.LabelField("Blender 建筑内外墙拆分", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "选中 Project 中的 FBX 后，一键启动 Blender、导入模型并加载仓库内的 Bento 建筑拆分插件。" +
                "Unity 不处理网格；Blender 输出新 FBX，原文件不会被覆盖。",
                MessageType.Info);
            using (new EditorGUILayout.HorizontalScope())
            {
                _selectedFbx = (GameObject)EditorGUILayout.ObjectField("目标 FBX", _selectedFbx, typeof(GameObject), false);
                if (GUILayout.Button("使用当前选择", GUILayout.Width(100)))
                    LockSelectedFbx();
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel("Blender.exe");
                EditorGUILayout.SelectableLabel(_blenderExecutable ?? string.Empty, EditorStyles.textField, GUILayout.Height(18));
                if (GUILayout.Button("自动查找", GUILayout.Width(76)))
                    SetBlenderPath(FindBlenderExecutable());
                if (GUILayout.Button("浏览", GUILayout.Width(56)))
                    BrowseBlender();
            }

            string fbxAssetPath = _selectedFbx == null ? string.Empty : AssetDatabase.GetAssetPath(_selectedFbx);
            string fbxAbsolutePath = ResolveAssetAbsolutePath(fbxAssetPath);
            if (!IsFbx(_selectedFbx))
                EditorGUILayout.HelpBox("请在 Project 面板选择一个 .fbx 模型资产。", MessageType.Warning);
            if (string.IsNullOrEmpty(_blenderExecutable) || !File.Exists(_blenderExecutable))
                EditorGUILayout.HelpBox("尚未找到 Blender。首次使用时请指定 blender.exe；路径只保存在本机。", MessageType.Warning);
            if (!string.IsNullOrEmpty(_integrationError))
                EditorGUILayout.HelpBox(_integrationError, MessageType.Error);
            else if (!Directory.Exists(_addonPath) || !File.Exists(_launchScriptPath))
                EditorGUILayout.HelpBox("仓库中的 Blender 插件文件不完整，请确认 BlenderTools/BuildingSurfaceSplitter 已同步。", MessageType.Error);

            bool canLaunch = IsFbx(_selectedFbx)
                             && File.Exists(fbxAbsolutePath)
                             && File.Exists(_blenderExecutable)
                             && Directory.Exists(_addonPath)
                             && File.Exists(_launchScriptPath);
            using (new EditorGUI.DisabledScope(!canLaunch))
            {
                if (GUILayout.Button("在 Blender 中打开选中 FBX", GUILayout.Height(40)))
                    BentoCommand.Run("启动 Blender 失败", () =>
                        BlenderBuildingSurfaceLauncher.Launch(
                            _blenderExecutable,
                            fbxAbsolutePath,
                            _launchScriptPath,
                            _addonPath));
            }

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("共享说明", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Blender 插件源码随 Bento 仓库保存，但不会影响未安装 Blender 的使用者。" +
                "使用者只使用 Unity 工具时无需配置；需要建筑拆分时，首次指定各自电脑上的 blender.exe 即可。",
                MessageType.None);
        }

        public void OnActivated() => OnSelectionChanged();
        public void OnDeactivated() { }
        public void OnSelectionChanged()
        {
            GameObject selected = Selection.objects.OfType<GameObject>().FirstOrDefault(IsFbx);
            if (selected != null)
                _selectedFbx = selected;
        }

        void LockSelectedFbx()
        {
            _selectedFbx = Selection.objects.OfType<GameObject>().FirstOrDefault(IsFbx) ?? _selectedFbx;
        }

        void BrowseBlender()
        {
            string initialDirectory = File.Exists(_blenderExecutable)
                ? Path.GetDirectoryName(_blenderExecutable)
                : Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string selected = EditorUtility.OpenFilePanel("选择 Blender 可执行文件", initialDirectory, "exe");
            if (!string.IsNullOrEmpty(selected))
                SetBlenderPath(selected);
        }

        void SetBlenderPath(string path)
        {
            _blenderExecutable = path ?? string.Empty;
            if (File.Exists(_blenderExecutable))
                EditorPrefs.SetString(BlenderPathKey, _blenderExecutable);
        }

        static bool IsFbx(GameObject asset)
        {
            return asset != null && AssetDatabase.GetAssetPath(asset)
                .EndsWith(".fbx", StringComparison.OrdinalIgnoreCase);
        }

        static string ResolveAssetAbsolutePath(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                return string.Empty;
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return Path.GetFullPath(Path.Combine(projectRoot, assetPath));
        }

        static string FindBlenderExecutable()
        {
            foreach (string candidate in new[]
                     {
                         Environment.GetEnvironmentVariable("BLENDER_PATH"),
                         Environment.GetEnvironmentVariable("BLENDER_EXE")
                     })
            {
                if (File.Exists(candidate))
                    return candidate;
            }
            foreach (string root in new[]
                     {
                         Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Blender Foundation"),
                         Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Blender Foundation"),
                         Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Blender Foundation"),
                         Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WinGet", "Packages")
                     })
            {
                if (!Directory.Exists(root))
                    continue;
                try
                {
                    string found = Directory.EnumerateFiles(root, "blender.exe", SearchOption.AllDirectories)
                        .OrderByDescending(File.GetLastWriteTimeUtc)
                        .FirstOrDefault();
                    if (!string.IsNullOrEmpty(found))
                        return found;
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"[Bento] 查找 Blender 时跳过目录 {root}：{exception.Message}");
                }
            }
            return string.Empty;
        }
    }

    static class BlenderBuildingSurfaceLauncher
    {
        public static string ResolveAddonPath()
        {
            return Path.GetFullPath(Path.Combine(ResolveToolboxRoot(), "BlenderTools", "BuildingSurfaceSplitter"));
        }

        public static string ResolveLaunchScriptPath(string addonPath) =>
            Path.Combine(addonPath, "launch_from_unity.py");

        public static void Launch(string blenderExecutable, string fbxPath, string launchScriptPath, string addonPath)
        {
            ValidateFile(blenderExecutable, "Blender 可执行文件");
            ValidateFile(fbxPath, "FBX 文件");
            ValidateFile(launchScriptPath, "Blender 启动脚本");
            if (!Directory.Exists(addonPath))
                throw new DirectoryNotFoundException("Blender 插件目录不存在：" + addonPath);

            var startInfo = new ProcessStartInfo
            {
                FileName = blenderExecutable,
                Arguments = string.Join(" ", new[]
                {
                    "--python",
                    QuoteArgument(launchScriptPath),
                    "--",
                    QuoteArgument(fbxPath),
                    QuoteArgument(addonPath)
                }),
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(fbxPath),
                CreateNoWindow = false
            };
            if (Process.Start(startInfo) == null)
                throw new InvalidOperationException("系统未能启动 Blender。");
        }

        static string ResolveToolboxRoot()
        {
            string[] guids = AssetDatabase.FindAssets("BlenderBuildingSurfaceTool t:MonoScript");
            string scriptPath = guids.Select(AssetDatabase.GUIDToAssetPath)
                .FirstOrDefault(path => path.EndsWith("/BlenderBuildingSurfaceTool.cs", StringComparison.Ordinal));
            if (string.IsNullOrEmpty(scriptPath))
                throw new InvalidOperationException("无法定位 Bento 目录。");
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string scriptAbsolute = Path.GetFullPath(Path.Combine(projectRoot, scriptPath));
            return Directory.GetParent(Path.GetDirectoryName(scriptAbsolute))?.FullName
                   ?? throw new InvalidOperationException("无法解析 Bento 根目录。");
        }

        static void ValidateFile(string path, string label)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                throw new FileNotFoundException(label + "不存在：" + path);
        }

        static string QuoteArgument(string value)
        {
            if (value == null)
                return "\"\"";
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }
    }
}

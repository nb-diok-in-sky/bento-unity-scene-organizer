using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Bento
{
    [BentoPage("model_import_workflow", BentoGroups.Models, "模型导入", Order = 10)]
    sealed class ModelImportWorkflowTool : IBentoTool
    {
        readonly List<ModelImportWorkflowSource> _sources = new() { new ModelImportWorkflowSource() };
        readonly PackedMaskLayout _maskLayout = new();
        readonly List<ModelImportWorkflowEntry> _plan = new();
        DefaultAsset _prefabOutputFolder;
        bool _createMaterials = true;
        bool _createPrefabs = true;
        bool _createSourceNamedFolder = true;
        FbxPrefabFolderMode _folderMode = FbxPrefabFolderMode.PreserveFolders;
        FbxPrefabConflictMode _conflictMode = FbxPrefabConflictMode.Skip;
        Vector2 _scroll;
        string _message;

        public void Draw()
        {
            EditorGUILayout.LabelField("模型导入工作流", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("多个来源文件夹共用一条流程：递归扫描 FBX/贴图 → 创建并绑定 URP/Lit 材质 → 生成 Prefab。原来的细分工具仍可独立使用。", MessageType.Info);
            DrawSources();
            DrawOptions();
            DrawCommands();
            DrawPlan();
        }

        void DrawOptions()
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("流程步骤", EditorStyles.boldLabel);
            _createMaterials = EditorGUILayout.ToggleLeft("① 创建并绑定 URP/Lit 材质（A / N / MSA）", _createMaterials);
            _createPrefabs = EditorGUILayout.ToggleLeft("② 生成 Prefab", _createPrefabs);
            using (new EditorGUI.DisabledScope(!_createMaterials))
            {
                EditorGUI.indentLevel++;
                _maskLayout.metallic = (PackedMaskChannel)EditorGUILayout.EnumPopup("MSA Metallic", _maskLayout.metallic);
                _maskLayout.smoothness = (PackedMaskChannel)EditorGUILayout.EnumPopup("MSA Smoothness", _maskLayout.smoothness);
                _maskLayout.invertSmoothness = EditorGUILayout.Toggle("反相 Smoothness", _maskLayout.invertSmoothness);
                _maskLayout.occlusion = (PackedMaskChannel)EditorGUILayout.EnumPopup("MSA AO", _maskLayout.occlusion);
                EditorGUI.indentLevel--;
            }
            using (new EditorGUI.DisabledScope(!_createPrefabs))
            {
                EditorGUI.indentLevel++;
                _prefabOutputFolder = BentoGUI.DrawProjectFolder("Prefab 输出根目录", _prefabOutputFolder, out _);
                _createSourceNamedFolder = EditorGUILayout.Toggle("按来源创建同名文件夹", _createSourceNamedFolder);
                _folderMode = (FbxPrefabFolderMode)EditorGUILayout.EnumPopup("目录模式", _folderMode);
                _conflictMode = (FbxPrefabConflictMode)EditorGUILayout.EnumPopup("同名处理", _conflictMode);
                EditorGUI.indentLevel--;
            }
        }

        void DrawCommands()
        {
            bool canScan = GetSourcePaths().Count > 0 && (!_createPrefabs || BentoUtility.IsProjectFolder(GetPrefabOutputPath()));
            int selected = _plan.Count(entry => entry.include);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(!canScan || (!_createMaterials && !_createPrefabs)))
                    if (GUILayout.Button("① 扫描并预览整条流程", GUILayout.Height(36))) Scan();
                using (new EditorGUI.DisabledScope(selected == 0))
                    if (GUILayout.Button(selected > 0 ? $"② 执行 {selected} 个 FBX" : "② 执行（请先扫描）", GUILayout.Height(36))) Execute(selected);
            }
            if (!string.IsNullOrEmpty(_message)) EditorGUILayout.HelpBox(_message, _plan.Count > 0 ? MessageType.Info : MessageType.Warning);
        }

        void DrawPlan()
        {
            if (_plan.Count == 0) return;
            using (new EditorGUILayout.HorizontalScope())
            {
                BentoGUI.DrawIncludeButtons(_plan, entry => entry.include, (entry, value) => entry.include = value,
                    entry => ModelImportWorkflowPlanner.CanExecute(entry, BuildOptions()), "全选可执行", 90);
            }
            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.MinHeight(240), GUILayout.MaxHeight(620));
            foreach (var entry in _plan)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        entry.include = EditorGUILayout.Toggle(entry.include, GUILayout.Width(18));
                        EditorGUILayout.ObjectField(entry.material.fbx, typeof(GameObject), false);
                        BentoGUI.LocateButton(entry.material.fbx);
                    }
                    EditorGUILayout.LabelField("来源：" + entry.rootFolder, EditorStyles.miniLabel);
                    if (_createMaterials) EditorGUILayout.LabelField("材质：" + entry.material.note, EditorStyles.miniLabel);
                    if (_createPrefabs) EditorGUILayout.LabelField("Prefab：" + entry.prefab.prefabPath + "（" + entry.prefab.note + "）", EditorStyles.miniLabel);
                }
            }
            EditorGUILayout.EndScrollView();
        }

        void DrawSources()
        {
            EditorGUILayout.LabelField("来源文件夹（可添加多个）", EditorStyles.boldLabel);
            for (int i = 0; i < _sources.Count; i++)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    _sources[i].include = EditorGUILayout.Toggle(_sources[i].include, GUILayout.Width(18));
                    _sources[i].folder = (DefaultAsset)EditorGUILayout.ObjectField(_sources[i].folder, typeof(DefaultAsset), false);
                    if (GUILayout.Button("使用当前", GUILayout.Width(72)))
                    {
                        if (ProjectSelection.TryGetActiveProjectFolder(out DefaultAsset selectedFolder)) _sources[i].folder = selectedFolder;
                        else EditorUtility.DisplayDialog("无法获取 Project 文件夹", "请在 Project 面板选中一个文件夹，或选中该文件夹内的任意资源。", "确定");
                    }
                    using (new EditorGUI.DisabledScope(_sources.Count == 1))
                        if (GUILayout.Button("-", GUILayout.Width(28))) { _sources.RemoveAt(i); i--; }
                }
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("+ 添加文件夹", GUILayout.Width(110))) _sources.Add(new ModelImportWorkflowSource());
                if (GUILayout.Button("载入 Project 当前选中的文件夹", GUILayout.Width(210))) LoadSelectedFolders();
            }
        }

        void LoadSelectedFolders()
        {
            var folders = Selection.objects.OfType<DefaultAsset>().Where(asset => AssetDatabase.IsValidFolder(AssetDatabase.GetAssetPath(asset))).Distinct().ToList();
            if (folders.Count == 0) return;
            _sources.Clear();
            _sources.AddRange(folders.Select(folder => new ModelImportWorkflowSource { folder = folder }));
        }

        void Scan()
        {
            _plan.Clear();
            _plan.AddRange(ModelImportWorkflowPlanner.Build(GetSourcePaths(), BuildOptions()));
            _message = _plan.Count == 0 ? "没有找到 FBX。扫描会递归所有子文件夹。" : $"扫描完成：找到 {_plan.Count} 个 FBX，可执行 {_plan.Count(entry => entry.include)} 个。";
        }

        void Execute(int selected)
        {
            if (!BentoCommand.Confirm("执行模型导入工作流", $"将按顺序处理 {selected} 个 FBX。\n材质绑定：{(_createMaterials ? "开启" : "关闭")}\nPrefab：{(_createPrefabs ? "开启" : "关闭")}", "开始执行")) return;
            ModelImportWorkflowResult result = null;
            BentoCommand.Run("模型导入工作流失败", () => { result = ModelImportWorkflowService.Execute(_plan, BuildOptions()); Scan(); }, () => BuildResultMessage(result));
            if (result?.materials != null) foreach (string failure in result.materials.failures) Debug.LogError("[Bento] " + failure);
            if (result?.prefabs != null) foreach (string failure in result.prefabs.failures) Debug.LogError("[Bento] " + failure);
            if (result != null) foreach (string failure in result.failures) Debug.LogError("[Bento] " + failure);
        }

        ModelImportWorkflowOptions BuildOptions() => new ModelImportWorkflowOptions
        {
            createMaterials = _createMaterials,
            createPrefabs = _createPrefabs,
            prefabOutputFolder = GetPrefabOutputPath(),
            createSourceNamedFolder = _createSourceNamedFolder,
            folderMode = _folderMode,
            conflictMode = _conflictMode,
            maskLayout = _maskLayout
        };

        List<string> GetSourcePaths() => _sources.Where(source => source.include && source.folder != null).Select(source => AssetDatabase.GetAssetPath(source.folder)).Where(BentoUtility.IsProjectFolder).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        string GetPrefabOutputPath() => _prefabOutputFolder == null ? string.Empty : AssetDatabase.GetAssetPath(_prefabOutputFolder);

        static string BuildResultMessage(ModelImportWorkflowResult result)
        {
            var parts = new List<string>();
            if (result?.materials != null) parts.Add($"材质成功 {result.materials.created}，失败 {result.materials.failures.Count}");
            if (result?.prefabs != null) parts.Add($"Prefab 创建 {result.prefabs.created}，覆盖 {result.prefabs.overwritten}，跳过 {result.prefabs.skipped}，失败 {result.prefabs.failures.Count}");
            if (result != null && result.failures.Count > 0) parts.Add($"工作流额外失败 {result.failures.Count}");
            return string.Join("\n", parts);
        }
    }
}

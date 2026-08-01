using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Bento
{
    sealed class FbxQuickSetupTool
    {
        GameObject _fbx;
        List<FbxTextureBinding> _textures = new();
        DefaultAsset _outputFolder;
        Vector2 _textureScroll;
        readonly PackedMaskLayout _maskLayout = new();

        DefaultAsset _batchMaterialFolder;
        List<FbxBatchMaterialEntry> _batchMaterialPlan = new();
        Vector2 _batchMaterialScroll;

        DefaultAsset _batchSourceFolder;
        DefaultAsset _batchOutputFolder;
        FbxPrefabFolderMode _batchFolderMode = FbxPrefabFolderMode.PreserveFolders;
        FbxPrefabConflictMode _batchConflictMode = FbxPrefabConflictMode.Skip;
        bool _createSourceNamedFolder = true;
        List<FbxPrefabBatchEntry> _batchPlan = new();
        Vector2 _batchScroll;
        bool _batchScanCompleted;
        string _batchScanMessage;

        public void Draw()
        {
            EditorGUILayout.LabelField("FBX 快速导入", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("细分操作入口。需要连续处理多个文件夹时，优先使用“工作流 → 模型导入”。", MessageType.Info);
            DrawSingleSetup();
            DrawBatchMaterials();
            DrawBatchPrefabs();
        }

        void DrawSingleSetup()
        {
            EditorGUILayout.LabelField("单个 FBX", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                _fbx = (GameObject)EditorGUILayout.ObjectField("FBX", _fbx, typeof(GameObject), false);
                if (GUILayout.Button("锁定选中 FBX", GUILayout.Width(105))) LockSelectedFbx();
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel("贴图");
                if (GUILayout.Button("载入选中贴图")) _textures = FbxUrpMaterialBindingService.BuildBindings(Selection.objects.OfType<Texture2D>());
                if (GUILayout.Button("清空", GUILayout.Width(50))) _textures.Clear();
            }
            DrawTextureBindings();
            DrawMaskLayout();

            _outputFolder = BentoGUI.DrawProjectFolder("输出目录（留空为 FBX 同目录）", _outputFolder, out string folder);
            bool canBind = FbxUrpMaterialBindingService.IsFbx(_fbx) && _textures.Any(item => item.role == FbxTextureRole.BaseColor);
            using (new EditorGUI.DisabledScope(!canBind))
                if (GUILayout.Button("生成 URP 材质并绑定 FBX", GUILayout.Height(34)) &&
                    BentoCommand.Confirm("绑定 FBX 材质", "将创建一个 URP/Lit 材质，并覆盖该 FBX 所有材质槽的外部映射。", "生成并绑定"))
                    BentoCommand.Run("FBX 材质绑定失败", () =>
                    {
                        var material = FbxUrpMaterialBindingService.CreateAndBind(_fbx, _textures, _maskLayout, folder);
                        Selection.activeObject = material;
                        EditorGUIUtility.PingObject(material);
                    });

            using (new EditorGUI.DisabledScope(!FbxUrpMaterialBindingService.IsFbx(_fbx)))
                if (GUILayout.Button("直接生成 Prefab", GUILayout.Height(32)))
                    BentoCommand.Run("Prefab 生成失败", () =>
                    {
                        var prefab = FbxPrefabCreationService.CreatePrefab(_fbx, folder);
                        Selection.activeObject = prefab;
                        EditorGUIUtility.PingObject(prefab);
                    });
        }

        void DrawTextureBindings()
        {
            if (_textures.Count == 0) return;
            _textureScroll = EditorGUILayout.BeginScrollView(_textureScroll, GUILayout.MinHeight(120), GUILayout.MaxHeight(300));
            foreach (var binding in _textures)
            {
                using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.ObjectField(binding.texture, typeof(Texture2D), false, GUILayout.Width(210));
                    binding.role = (FbxTextureRole)EditorGUILayout.EnumPopup(binding.role);
                }
            }
            EditorGUILayout.EndScrollView();
        }

        void DrawMaskLayout()
        {
            if (!_textures.Any(item => item.role == FbxTextureRole.PackedMask) && _batchMaterialPlan.Count == 0) return;
            EditorGUILayout.LabelField("打包贴图通道", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("默认 MSA：R=Metallic，G=Smoothness，B=AO。若 G 是 Roughness，请勾选反相。", MessageType.None);
            _maskLayout.metallic = (PackedMaskChannel)EditorGUILayout.EnumPopup("Metallic 来源", _maskLayout.metallic);
            _maskLayout.smoothness = (PackedMaskChannel)EditorGUILayout.EnumPopup("Smoothness 来源", _maskLayout.smoothness);
            _maskLayout.invertSmoothness = EditorGUILayout.Toggle("反相 Smoothness", _maskLayout.invertSmoothness);
            _maskLayout.occlusion = (PackedMaskChannel)EditorGUILayout.EnumPopup("AO 来源", _maskLayout.occlusion);
        }

        void DrawBatchMaterials()
        {
            EditorGUILayout.Space(14);
            EditorGUILayout.LabelField("批量创建并绑定 URP 材质", EditorStyles.boldLabel);
            _batchMaterialFolder = BentoGUI.DrawProjectFolder("FBX 与贴图来源文件夹", _batchMaterialFolder, out string folder);
            int selected = _batchMaterialPlan.Count(entry => entry.include);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(!BentoUtility.IsProjectFolder(folder)))
                    if (GUILayout.Button("① 扫描材质组", GUILayout.Height(34))) _batchMaterialPlan = FbxUrpMaterialBindingService.BuildBatchMaterialPlan(folder);
                using (new EditorGUI.DisabledScope(selected == 0))
                    if (GUILayout.Button(selected > 0 ? $"② 创建并绑定 {selected} 个材质" : "② 创建材质（请先扫描）", GUILayout.Height(34))) ExecuteBatchMaterials(folder, selected);
            }
            if (_batchMaterialPlan.Count == 0) return;
            DrawMaskLayout();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("全选完整组", GUILayout.Width(90))) foreach (var entry in _batchMaterialPlan) entry.include = entry.baseColor != null && entry.normal != null && entry.packedMask != null;
                if (GUILayout.Button("全不选", GUILayout.Width(70))) foreach (var entry in _batchMaterialPlan) entry.include = false;
                if (GUILayout.Button("反选", GUILayout.Width(60))) foreach (var entry in _batchMaterialPlan) entry.include = !entry.include;
            }
            EditorGUILayout.LabelField($"找到 {_batchMaterialPlan.Count} 个 FBX，可处理 {selected} 个", EditorStyles.boldLabel);
            _batchMaterialScroll = EditorGUILayout.BeginScrollView(_batchMaterialScroll, GUILayout.MinHeight(170), GUILayout.MaxHeight(380));
            foreach (var entry in _batchMaterialPlan)
            {
                using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
                {
                    entry.include = EditorGUILayout.Toggle(entry.include, GUILayout.Width(18));
                    EditorGUILayout.ObjectField(entry.fbx, typeof(GameObject), false, GUILayout.Width(130));
                    EditorGUILayout.ObjectField(entry.baseColor, typeof(Texture2D), false, GUILayout.Width(90));
                    EditorGUILayout.ObjectField(entry.normal, typeof(Texture2D), false, GUILayout.Width(90));
                    EditorGUILayout.ObjectField(entry.packedMask, typeof(Texture2D), false, GUILayout.Width(90));
                    EditorGUILayout.LabelField(entry.note, EditorStyles.miniLabel);
                }
            }
            EditorGUILayout.EndScrollView();
        }

        void ExecuteBatchMaterials(string folder, int selected)
        {
            if (!BentoCommand.Confirm("批量创建 URP 材质", $"将为 {selected} 个 FBX 创建材质、拆分 MSA，并覆盖全部材质槽映射。", "开始处理")) return;
            FbxBatchMaterialResult result = null;
            BentoCommand.Run("批量材质绑定失败", () =>
            {
                result = FbxUrpMaterialBindingService.ExecuteBatchMaterials(_batchMaterialPlan, _maskLayout);
                _batchMaterialPlan = FbxUrpMaterialBindingService.BuildBatchMaterialPlan(folder);
            }, () => $"成功 {result.created}，失败 {result.failures.Count}。" + (result.failures.Count > 0 ? "\n失败详情已输出到 Console。" : string.Empty));
            if (result != null) foreach (string failure in result.failures) Debug.LogError("[Bento] " + failure);
        }

        void DrawBatchPrefabs()
        {
            EditorGUILayout.Space(14);
            EditorGUILayout.LabelField("批量 FBX 转 Prefab", EditorStyles.boldLabel);
            _batchSourceFolder = BentoGUI.DrawProjectFolder("① FBX 来源文件夹（只读）", _batchSourceFolder, out string source);
            _batchOutputFolder = BentoGUI.DrawProjectFolder("② Prefab 输出根目录", _batchOutputFolder, out string output);
            _createSourceNamedFolder = EditorGUILayout.Toggle("创建来源同名文件夹", _createSourceNamedFolder);
            _batchFolderMode = (FbxPrefabFolderMode)EditorGUILayout.EnumPopup("目录模式", _batchFolderMode);
            _batchConflictMode = (FbxPrefabConflictMode)EditorGUILayout.EnumPopup("同名处理", _batchConflictMode);
            bool valid = BentoUtility.IsProjectFolder(source) && BentoUtility.IsProjectFolder(output);
            int selected = _batchPlan.Count(entry => entry.include);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(!valid))
                    if (GUILayout.Button("① 扫描并预览 FBX", GUILayout.Height(34))) ScanBatchPrefabs(source, output);
                DrawBatchCreateButton(selected, source, output);
            }
            if (_batchScanCompleted) EditorGUILayout.HelpBox(_batchScanMessage, _batchPlan.Count > 0 ? MessageType.Info : MessageType.Warning);
            if (_batchPlan.Count == 0) return;
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("全选", GUILayout.Width(60))) foreach (var entry in _batchPlan) entry.include = true;
                if (GUILayout.Button("全不选", GUILayout.Width(70))) foreach (var entry in _batchPlan) entry.include = false;
                if (GUILayout.Button("反选", GUILayout.Width(60))) foreach (var entry in _batchPlan) entry.include = !entry.include;
            }
            EditorGUILayout.LabelField($"扫描到 {_batchPlan.Count} 个 FBX，准备转换 {selected} 个", EditorStyles.boldLabel);
            _batchScroll = EditorGUILayout.BeginScrollView(_batchScroll, GUILayout.MinHeight(180), GUILayout.MaxHeight(420));
            foreach (var entry in _batchPlan)
            {
                using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
                {
                    entry.include = EditorGUILayout.Toggle(entry.include, GUILayout.Width(18));
                    EditorGUILayout.ObjectField(entry.fbx, typeof(GameObject), false, GUILayout.Width(170));
                    EditorGUILayout.LabelField(entry.prefabPath, EditorStyles.miniLabel);
                    EditorGUILayout.LabelField(entry.note, EditorStyles.miniLabel, GUILayout.Width(120));
                    if (GUILayout.Button("定位", GUILayout.Width(44))) { Selection.activeObject = entry.fbx; EditorGUIUtility.PingObject(entry.fbx); }
                }
            }
            EditorGUILayout.EndScrollView();
        }

        void ScanBatchPrefabs(string source, string output)
        {
            BentoCommand.Run("FBX 扫描失败", () =>
            {
                _batchPlan = FbxPrefabCreationService.BuildBatchPlan(source, output, _createSourceNamedFolder, _batchFolderMode, _batchConflictMode);
                _batchScanCompleted = true;
                _batchScanMessage = _batchPlan.Count > 0 ? $"扫描完成：找到 {_batchPlan.Count} 个 FBX。" : "扫描完成：没有找到 .fbx 文件。";
            });
        }

        void DrawBatchCreateButton(int selected, string source, string output)
        {
            using (new EditorGUI.DisabledScope(selected == 0))
                if (GUILayout.Button(selected > 0 ? $"② 生成 {selected} 个 Prefab" : "② 生成 Prefab（请先扫描）", GUILayout.Height(34)) &&
                    BentoCommand.Confirm("输出 Prefab", $"来源：{source}\n输出：{FbxPrefabCreationService.ResolveBatchOutputFolder(source, output, _createSourceNamedFolder)}\n数量：{selected}\n同名策略：{_batchConflictMode}", "生成 Prefab"))
                {
                    FbxPrefabBatchResult result = null;
                    BentoCommand.Run("批量生成 Prefab 失败", () =>
                    {
                        result = FbxPrefabCreationService.ExecuteBatch(_batchPlan, _batchConflictMode);
                        _batchPlan = FbxPrefabCreationService.BuildBatchPlan(source, output, _createSourceNamedFolder, _batchFolderMode, _batchConflictMode);
                    }, () => $"创建 {result.created}，覆盖 {result.overwritten}，跳过 {result.skipped}，失败 {result.failures.Count}。" +
                              (result.failures.Count > 0 ? "\n失败详情已输出到 Console。" : string.Empty));
                    if (result != null) foreach (string failure in result.failures) Debug.LogError("[Bento] " + failure);
                }
        }

        void LockSelectedFbx()
        {
            _fbx = Selection.objects.OfType<GameObject>().FirstOrDefault(FbxUrpMaterialBindingService.IsFbx);
            if (_fbx == null) EditorUtility.DisplayDialog("没有选中 FBX", "请在 Project 面板选中一个 .fbx 文件。", "确定");
        }
    }
}

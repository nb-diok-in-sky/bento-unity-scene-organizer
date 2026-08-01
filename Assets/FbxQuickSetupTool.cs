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
        Vector2 _scroll;
        readonly PackedMaskLayout _maskLayout = new();
        DefaultAsset _batchSourceFolder;
        DefaultAsset _batchOutputFolder;
        FbxPrefabFolderMode _batchFolderMode = FbxPrefabFolderMode.PreserveFolders;
        FbxPrefabConflictMode _batchConflictMode = FbxPrefabConflictMode.Skip;
        bool _createSourceNamedFolder = true;
        List<FbxPrefabBatchEntry> _batchPlan = new();
        Vector2 _batchScroll;

        public void Draw()
        {
            EditorGUILayout.LabelField("FBX 快速导入", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("锁定一个 Project FBX，再载入贴图并确认语义。当前版本使用一个 URP/Lit 材质映射 FBX 的全部材质槽。", MessageType.Info);

            using (new EditorGUILayout.HorizontalScope())
            {
                _fbx = (GameObject)EditorGUILayout.ObjectField("FBX", _fbx, typeof(GameObject), false);
                if (GUILayout.Button("锁定选中 FBX", GUILayout.Width(105))) LockSelectedFbx();
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel("贴图");
                if (GUILayout.Button("载入选中贴图"))
                    _textures = FbxUrpMaterialBindingService.BuildBindings(Selection.objects.OfType<Texture2D>());
                if (GUILayout.Button("清空", GUILayout.Width(50))) _textures.Clear();
            }

            if (_textures.Count > 0)
            {
                _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.MinHeight(120), GUILayout.MaxHeight(300));
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

            if (_textures.Any(item => item.role == FbxTextureRole.PackedMask))
            {
                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("打包贴图通道", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox("默认 MSA：R=Metallic，G=Smoothness，B=AO。若 G 实际是 Roughness，请勾选反相。", MessageType.None);
                _maskLayout.metallic = (PackedMaskChannel)EditorGUILayout.EnumPopup("Metallic 来源", _maskLayout.metallic);
                _maskLayout.smoothness = (PackedMaskChannel)EditorGUILayout.EnumPopup("Smoothness 来源", _maskLayout.smoothness);
                _maskLayout.invertSmoothness = EditorGUILayout.Toggle("反相 Smoothness", _maskLayout.invertSmoothness);
                _maskLayout.occlusion = (PackedMaskChannel)EditorGUILayout.EnumPopup("AO 来源", _maskLayout.occlusion);
            }

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

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("FBX 转 Prefab", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("直接创建 Prefab 资产，不向当前场景添加对象。", MessageType.None);
            using (new EditorGUI.DisabledScope(!FbxUrpMaterialBindingService.IsFbx(_fbx)))
                if (GUILayout.Button("直接生成 Prefab", GUILayout.Height(32)))
                    BentoCommand.Run("Prefab 生成失败", () =>
                    {
                        var prefab = FbxPrefabCreationService.CreatePrefab(_fbx, folder);
                        Selection.activeObject = prefab;
                        EditorGUIUtility.PingObject(prefab);
                    });

            EditorGUILayout.Space(14);
            EditorGUILayout.LabelField("批量 FBX 转 Prefab", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("递归扫描来源文件夹中的 FBX，批量创建 Prefab。此功能不会自动生成或修改材质。", MessageType.Info);
            _batchSourceFolder = BentoGUI.DrawProjectFolder("来源文件夹", _batchSourceFolder, out string batchSource);
            _batchOutputFolder = BentoGUI.DrawProjectFolder("Prefab 输出文件夹", _batchOutputFolder, out string batchOutput);
            _createSourceNamedFolder = EditorGUILayout.Toggle("创建来源同名文件夹", _createSourceNamedFolder);
            _batchFolderMode = (FbxPrefabFolderMode)EditorGUILayout.EnumPopup("目录模式", _batchFolderMode);
            _batchConflictMode = (FbxPrefabConflictMode)EditorGUILayout.EnumPopup("同名处理", _batchConflictMode);

            if (BentoUtility.IsProjectFolder(batchSource) && BentoUtility.IsProjectFolder(batchOutput))
                EditorGUILayout.HelpBox("实际输出：" + FbxPrefabCreationService.ResolveBatchOutputFolder(
                    batchSource, batchOutput, _createSourceNamedFolder), MessageType.None);

            bool validBatchFolders = BentoUtility.IsProjectFolder(batchSource) && BentoUtility.IsProjectFolder(batchOutput);
            using (new EditorGUI.DisabledScope(!validBatchFolders))
                if (GUILayout.Button("扫描并预览 FBX", GUILayout.Height(28)))
                    BentoCommand.Run("FBX 扫描失败", () =>
                        _batchPlan = FbxPrefabCreationService.BuildBatchPlan(batchSource, batchOutput, _createSourceNamedFolder, _batchFolderMode, _batchConflictMode));

            if (_batchPlan.Count == 0) return;
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("全选", GUILayout.Width(60))) foreach (var entry in _batchPlan) entry.include = true;
                if (GUILayout.Button("全不选", GUILayout.Width(70))) foreach (var entry in _batchPlan) entry.include = false;
                if (GUILayout.Button("反选", GUILayout.Width(60))) foreach (var entry in _batchPlan) entry.include = !entry.include;
            }
            int batchSelected = _batchPlan.Count(entry => entry.include);
            EditorGUILayout.LabelField($"扫描到 {_batchPlan.Count} 个 FBX，准备转换 {batchSelected} 个", EditorStyles.boldLabel);
            _batchScroll = EditorGUILayout.BeginScrollView(_batchScroll, GUILayout.MinHeight(180), GUILayout.MaxHeight(420));
            foreach (var entry in _batchPlan)
            {
                using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
                {
                    entry.include = EditorGUILayout.Toggle(entry.include, GUILayout.Width(18));
                    EditorGUILayout.ObjectField(entry.fbx, typeof(GameObject), false, GUILayout.Width(170));
                    EditorGUILayout.LabelField(entry.prefabPath, EditorStyles.miniLabel);
                    EditorGUILayout.LabelField(entry.note, EditorStyles.miniLabel, GUILayout.Width(120));
                    if (GUILayout.Button("定位", GUILayout.Width(44)))
                    {
                        Selection.activeObject = entry.fbx;
                        EditorGUIUtility.PingObject(entry.fbx);
                    }
                }
            }
            EditorGUILayout.EndScrollView();

            using (new EditorGUI.DisabledScope(batchSelected == 0))
                if (GUILayout.Button("批量生成 Prefab", GUILayout.Height(36)) &&
                    BentoCommand.Confirm("批量生成 Prefab", $"将处理 {batchSelected} 个 FBX。同名策略：{_batchConflictMode}。", "开始生成"))
                {
                    FbxPrefabBatchResult result = null;
                    BentoCommand.Run("批量生成 Prefab 失败", () =>
                    {
                        result = FbxPrefabCreationService.ExecuteBatch(_batchPlan, _batchConflictMode);
                        _batchPlan = FbxPrefabCreationService.BuildBatchPlan(batchSource, batchOutput, _createSourceNamedFolder, _batchFolderMode, _batchConflictMode);
                    }, () => $"创建 {result.created}，覆盖 {result.overwritten}，跳过 {result.skipped}，失败 {result.failures.Count}。" +
                              (result.failures.Count > 0 ? "\n失败详情已输出到 Console。" : string.Empty));
                    if (result != null)
                        foreach (string failure in result.failures) Debug.LogError("[Bento] " + failure);
                }
        }

        void LockSelectedFbx()
        {
            _fbx = Selection.objects.OfType<GameObject>().FirstOrDefault(FbxUrpMaterialBindingService.IsFbx);
            if (_fbx == null) EditorUtility.DisplayDialog("没有选中 FBX", "请在 Project 面板选中一个 .fbx 文件。", "确定");
        }
    }
}

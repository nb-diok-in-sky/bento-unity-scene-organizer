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
        }

        void LockSelectedFbx()
        {
            _fbx = Selection.objects.OfType<GameObject>().FirstOrDefault(FbxUrpMaterialBindingService.IsFbx);
            if (_fbx == null) EditorUtility.DisplayDialog("没有选中 FBX", "请在 Project 面板选中一个 .fbx 文件。", "确定");
        }
    }
}

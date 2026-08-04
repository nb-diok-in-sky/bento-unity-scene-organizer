using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Bento
{
    sealed class SelectedAssetClassificationTool
    {
        readonly AssetNamingOptions _modelNaming = NewNaming();
        readonly AssetNamingOptions _materialNaming = NewNaming();
        readonly AssetNamingOptions _textureNaming = NewNaming();
        readonly List<AssetMoveEntry> _plan = new List<AssetMoveEntry>();
        DefaultAsset _modelFolder;
        DefaultAsset _materialFolder;
        DefaultAsset _textureFolder;
        string _modelFolderPath = string.Empty;
        string _materialFolderPath = string.Empty;
        string _textureFolderPath = string.Empty;
        Vector2 _scroll;
        AssetMoveHistory _lastHistory;

        public void Draw(IReadOnlyList<UnityEngine.Object> models, IReadOnlyList<Material> materials, IReadOnlyList<Texture> textures)
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("分类移动与独立改名", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "直接使用上方扫描结果，通过 AssetDatabase.MoveAsset 分类移动并验证 GUID。模型、材质、贴图分别独立编号。",
                MessageType.Info);

            EditorGUI.BeginChangeCheck();
            _modelFolder = BentoGUI.DrawProjectFolder("模型移动至", _modelFolder, out _modelFolderPath);
            _materialFolder = BentoGUI.DrawProjectFolder("材质移动至", _materialFolder, out _materialFolderPath);
            _textureFolder = BentoGUI.DrawProjectFolder("贴图移动至", _textureFolder, out _textureFolderPath);
            DrawNaming("模型命名", _modelNaming);
            DrawNaming("材质命名", _materialNaming);
            DrawNaming("贴图命名", _textureNaming);
            if (EditorGUI.EndChangeCheck() && _plan.Count > 0) BuildPlan(models, materials, textures);

            using (new EditorGUILayout.HorizontalScope())
            {
                bool hasAssets = models.Count + materials.Count + textures.Count > 0;
                using (new EditorGUI.DisabledScope(!hasAssets))
                    if (GUILayout.Button($"生成分类移动计划（{models.Count + materials.Count + textures.Count}）", GUILayout.Height(34)))
                        BuildPlan(models, materials, textures);
                using (new EditorGUI.DisabledScope(_plan.Count == 0))
                    if (GUILayout.Button("清空计划", GUILayout.Height(34))) _plan.Clear();
            }

            DrawPrimaryAction();
            DrawPlan();
            using (new EditorGUI.DisabledScope(_lastHistory == null || _lastHistory.moves.Count == 0))
                if (GUILayout.Button("恢复上次分类移动")) Restore();
        }

        void DrawNaming(string title, AssetNamingOptions naming)
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            BentoGUI.DrawNamingFields(naming, "统一基础名称（留空保留原名）");
        }

        void BuildPlan(IReadOnlyList<UnityEngine.Object> models, IReadOnlyList<Material> materials, IReadOnlyList<Texture> textures)
        {
            _plan.Clear();
            var selected = models.Concat(materials.Cast<UnityEngine.Object>()).Concat(textures.Cast<UnityEngine.Object>())
                .Where(IsSupported).Distinct().ToArray();
            _plan.AddRange(SafeAssetMoveService.BuildPlan(
                selected, _modelFolderPath, _materialFolderPath, _textureFolderPath, NamingForKind));
        }

        AssetNamingOptions NamingForKind(AssetMoveKind kind)
        {
            if (kind == AssetMoveKind.Material) return _materialNaming;
            if (kind == AssetMoveKind.Texture) return _textureNaming;
            return _modelNaming;
        }

        void DrawPlan()
        {
            if (_plan.Count == 0) return;
            EditorGUILayout.LabelField(
                $"计划：模型 {_plan.Count(entry => entry.kind == AssetMoveKind.PrefabOrModel)} / 材质 {_plan.Count(entry => entry.kind == AssetMoveKind.Material)} / 贴图 {_plan.Count(entry => entry.kind == AssetMoveKind.Texture)}",
                EditorStyles.boldLabel);
            _scroll = EditorGUILayout.BeginScrollView(_scroll, EditorStyles.helpBox, GUILayout.MinHeight(240), GUILayout.MaxHeight(620));
            foreach (var group in _plan.GroupBy(entry => entry.kind))
            {
                EditorGUILayout.LabelField(KindName(group.Key), EditorStyles.boldLabel);
                foreach (var entry in group)
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    entry.include = EditorGUILayout.ToggleLeft(entry.sourcePath, entry.include);
                    EditorGUILayout.LabelField("→ " + entry.targetPath, EditorStyles.miniLabel);
                    if (!string.IsNullOrEmpty(entry.warning)) EditorGUILayout.HelpBox(entry.warning, MessageType.Warning);
                }
            }
            EditorGUILayout.EndScrollView();
        }

        void DrawPrimaryAction()
        {
            int count = _plan.Count(entry => entry.include);
            bool valid = count > 0 && _plan.Where(entry => entry.include).All(entry => string.IsNullOrEmpty(entry.warning));
            if (_plan.Count == 0)
            {
                EditorGUILayout.HelpBox("先在上方锁定物体并扫描，再指定三个目标文件夹。", MessageType.Info);
                return;
            }
            if (!valid) EditorGUILayout.HelpBox("计划存在警告，处理完成后才可执行。", MessageType.Warning);
            using (new EditorGUI.DisabledScope(!valid))
                if (GUILayout.Button($"确认分类移动 {count} 个资产", GUILayout.Height(44)) &&
                    BentoCommand.Confirm("确认分类移动", $"将分类移动并独立重命名 {count} 个资产，移动后验证 GUID 不变。", "开始移动"))
                    Execute();
        }

        void Execute()
        {
            BentoCommand.Run("分类移动失败", () =>
            {
                _lastHistory = new AssetMoveHistory();
                SafeAssetMoveService.Execute(_plan, _lastHistory);
                AssetOperationHistory.TryRecord(AssetMoveOperationHandler.CreateRecord(_lastHistory), out _);
                _plan.Clear();
                EditorUtility.DisplayDialog("分类完成", $"已安全移动 {_lastHistory.moves.Count} 个资产，GUID 验证通过。", "确定");
            });
        }

        void Restore()
        {
            BentoCommand.Run("恢复分类移动失败", () =>
            {
                SafeAssetMoveService.Restore(_lastHistory);
                _lastHistory = null;
                EditorUtility.DisplayDialog("恢复完成", "上次分类移动已按 GUID 恢复。", "确定");
            });
        }

        static bool IsSupported(UnityEngine.Object asset)
        {
            if (asset == null) return false;
            string path = AssetDatabase.GetAssetPath(asset);
            if (string.IsNullOrEmpty(path) || AssetDatabase.IsValidFolder(path)) return false;
            if (asset is Material || asset is Texture) return true;
            return asset is GameObject && new[] { ".prefab", ".fbx", ".obj" }
                .Any(extension => path.EndsWith(extension, StringComparison.OrdinalIgnoreCase));
        }

        static AssetNamingOptions NewNaming() => new AssetNamingOptions
        {
            appendNumber = false,
            startNumber = 1,
            numberDigits = 2
        };

        static string KindName(AssetMoveKind kind) =>
            kind == AssetMoveKind.Material ? "材质" : kind == AssetMoveKind.Texture ? "贴图" : "模型";
    }
}

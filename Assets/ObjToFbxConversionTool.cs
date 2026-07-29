using UnityEditor;
using UnityEngine;

namespace Bento
{
    sealed class ObjToFbxConversionTool
    {
        GameObject _obj;
        DefaultAsset _mtl;
        DefaultAsset _outputFolder;
        DefaultAsset _materialFolder;
        GameObject _generatedFbx;
        string _outputName = "模型";
        ObjSourceCleanupPlan _cleanupPlan;

        public void Draw()
        {
            EditorGUILayout.LabelField("OBJ 转项目 FBX", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                MessageType.Info);
            _obj = (GameObject)EditorGUILayout.ObjectField("OBJ 模型", _obj, typeof(GameObject), false);
            _mtl = (DefaultAsset)EditorGUILayout.ObjectField("MTL 文件", _mtl, typeof(DefaultAsset), false);
            _outputFolder = BentoGUI.DrawProjectFolder("FBX 输出文件夹", _outputFolder, out string outputFolder);
            _materialFolder = BentoGUI.DrawProjectFolder("材质输出文件夹", _materialFolder, out string materialFolder);
            _outputName = EditorGUILayout.TextField("FBX 名称", _outputName);

            bool valid = IsExtension(_obj, ".obj") && IsExtension(_mtl, ".mtl") &&
                         !string.IsNullOrWhiteSpace(_outputName) && !string.IsNullOrEmpty(outputFolder) &&
                         !string.IsNullOrEmpty(materialFolder);
            using (new EditorGUI.DisabledScope(!valid))
            if (GUILayout.Button("转换并创建项目材质", GUILayout.Height(38)) && BentoCommand.Confirm(
                    "OBJ 转 FBX",
                    "将使用 Unity 官方 FBX Exporter 生成 FBX并创建独立项目材质。原 OBJ、MTL和贴图不会删除。",
                    "开始转换"))
            {
                BentoCommand.Run("OBJ 转 FBX失败", () =>
                {
                    ObjToFbxConversionResult result = ObjToFbxConversionService.Convert(new ObjToFbxConversionRequest
                    {
                        objAssetPath = AssetDatabase.GetAssetPath(_obj),
                        mtlAssetPath = AssetDatabase.GetAssetPath(_mtl),
                        outputFolder = outputFolder,
                        materialFolder = materialFolder,
                        outputName = _outputName
                    });
                    var asset = AssetDatabase.LoadMainAssetAtPath(result.fbxAssetPath);
                    _generatedFbx = asset as GameObject;
                    _cleanupPlan = ObjSourceCleanupService.BuildPlan(
                        AssetDatabase.GetAssetPath(_obj),
                        AssetDatabase.GetAssetPath(_mtl),
                        result.fbxAssetPath);
                    Selection.activeObject = asset;
                    EditorGUIUtility.PingObject(asset);
                    EditorUtility.DisplayDialog("转换完成", result.fbxAssetPath, "确定");
                });
            }

            DrawSourceCleanup();
        }

        void DrawSourceCleanup()
        {
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("转换源文件清理", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "只删除 OBJ 与 MTL，保留 FBX、Prefab、所有材质和贴图。执行前会重新检查 FBX 是否仍依赖源文件。Unity 删除资产不保证支持 Ctrl+Z，请先确认转换结果正常。",
                MessageType.Warning);
            _generatedFbx = (GameObject)EditorGUILayout.ObjectField("已生成 FBX", _generatedFbx, typeof(GameObject), false);

            bool canBuildPlan = IsExtension(_obj, ".obj") && IsExtension(_mtl, ".mtl") && IsExtension(_generatedFbx, ".fbx");
            using (new EditorGUI.DisabledScope(!canBuildPlan))
            if (GUILayout.Button("扫描可安全删除的源文件"))
            {
                BentoCommand.Run("源文件清理审计失败", () =>
                {
                    _cleanupPlan = ObjSourceCleanupService.BuildPlan(
                        AssetDatabase.GetAssetPath(_obj),
                        AssetDatabase.GetAssetPath(_mtl),
                        AssetDatabase.GetAssetPath(_generatedFbx));
                });
            }

            if (_cleanupPlan == null) return;
            EditorGUILayout.LabelField("确认删除清单", EditorStyles.miniBoldLabel);
            foreach (string path in _cleanupPlan.deletePaths)
                EditorGUILayout.SelectableLabel("• " + path, EditorStyles.textField, GUILayout.Height(18));

            if (GUILayout.Button("删除以上 OBJ / MTL", GUILayout.Height(34)) && BentoCommand.Confirm(
                    "删除转换源文件",
                    "将通过 Unity AssetDatabase 删除以下文件及其 .meta：\n\n" +
                    string.Join("\n", _cleanupPlan.deletePaths) +
                    "\n\nFBX、材质和贴图不会删除。此操作不保证支持 Ctrl+Z。",
                    "确认删除"))
            {
                ObjSourceCleanupResult result = null;
                if (BentoCommand.Run("删除源文件失败", () => result = ObjSourceCleanupService.Execute(_cleanupPlan),
                        () => $"已将 {result.deletedPaths.Count} 个 OBJ/MTL 源文件移入系统回收站；FBX、材质和贴图均已保留。"))
                {
                    _obj = null;
                    _mtl = null;
                    _cleanupPlan = null;
                }
            }
        }

        static bool IsExtension(UnityEngine.Object asset, string extension)
        {
            return asset != null && AssetDatabase.GetAssetPath(asset)
                .EndsWith(extension, System.StringComparison.OrdinalIgnoreCase);
        }
    }
}

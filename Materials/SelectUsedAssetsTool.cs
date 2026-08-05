using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Bento
{
    sealed class SelectUsedAssetsTool : IBentoPageLifecycle
    {
        readonly List<GameObject> _lockedRoots = new List<GameObject>();
        readonly List<UnityEngine.Object> _models = new List<UnityEngine.Object>();
        readonly List<Material> _materials = new List<Material>();
        readonly List<Texture> _textures = new List<Texture>();
        readonly SelectedAssetClassificationTool _classificationTool = new SelectedAssetClassificationTool();
        Vector2 _scroll;

        public void Draw()
        {
            EditorGUILayout.LabelField("进阶：选中模型、材质和贴图", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "锁定场景网格物体或 Project 中的 Prefab/FBX，收集模型资产、材质球和材质贴图。Terrain 自动忽略，请使用 Terrain 专用工具。",
                MessageType.Info);
            List<GameObject> current = UsedAssetSelectionScanner.ResolveCurrentRoots();
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(current.Count == 0))
                    if (GUILayout.Button($"锁定当前物体并扫描（{current.Count}）", GUILayout.Height(32)))
                        Scan(current);
                using (new EditorGUI.DisabledScope(_lockedRoots.Count == 0))
                    if (GUILayout.Button("重新扫描已锁定物体", GUILayout.Height(32)))
                        Scan(_lockedRoots);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                DrawSelectionButton($"只选模型（{_models.Count}）", _models);
                DrawSelectionButton($"只选材质（{_materials.Count}）", _materials);
                DrawSelectionButton($"只选贴图（{_textures.Count}）", _textures);
            }

            if (_lockedRoots.Count == 0) return;

            UnityEngine.Object[] allAssets = AllAssets();
            EditorGUILayout.LabelField(
                $"模型 {_models.Count}　材质 {_materials.Count}　贴图 {_textures.Count}　合计 {allAssets.Length}",
                EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(allAssets.Length == 0))
                    if (GUILayout.Button("重新全选全部资产", GUILayout.Height(30)))
                        Selection.objects = allAssets;
                if (GUILayout.Button("清空锁定", GUILayout.Height(30)))
                    Clear();
            }

            _scroll = EditorGUILayout.BeginScrollView(
                _scroll, EditorStyles.helpBox, GUILayout.MinHeight(260), GUILayout.MaxHeight(600));
            DrawGroup("模型 / FBX", _models);
            DrawGroup("材质球", _materials);
            DrawGroup("贴图", _textures);
            EditorGUILayout.EndScrollView();
            _classificationTool.Draw(_models, _materials, _textures);
        }

        void Scan(IEnumerable<GameObject> roots)
        {
            UsedAssetSelectionResult result = UsedAssetSelectionScanner.Scan(roots);
            Replace(_lockedRoots, result.roots);
            Replace(_models, result.models);
            Replace(_materials, result.materials);
            Replace(_textures, result.textures);
            Selection.objects = AllAssets();
        }

        static void DrawSelectionButton<T>(string label, IReadOnlyCollection<T> assets)
            where T : UnityEngine.Object
        {
            using (new EditorGUI.DisabledScope(assets.Count == 0))
                if (GUILayout.Button(label))
                    Selection.objects = assets
                        .Where(asset => asset != null)
                        .Cast<UnityEngine.Object>()
                        .ToArray();
        }

        void DrawGroup<T>(string title, IEnumerable<T> assets) where T : UnityEngine.Object
        {
            T[] items = assets.Where(asset => asset != null).ToArray();
            EditorGUILayout.LabelField($"{title}（{items.Length}）", EditorStyles.boldLabel);
            foreach (T asset in items)
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.ObjectField(asset, asset.GetType(), false);
                EditorGUILayout.LabelField(
                    AssetDatabase.GetAssetPath(asset), EditorStyles.miniLabel, GUILayout.MinWidth(220));
                if (GUILayout.Button("定位", GUILayout.Width(60)))
                {
                    Selection.objects = AllAssets();
                    EditorGUIUtility.PingObject(asset);
                }
            }

            EditorGUILayout.Space(6);
        }

        UnityEngine.Object[] AllAssets()
        {
            return _models
                .Concat(_materials.Cast<UnityEngine.Object>())
                .Concat(_textures.Cast<UnityEngine.Object>())
                .Where(asset => asset != null)
                .Distinct()
                .ToArray();
        }

        void Clear()
        {
            _lockedRoots.Clear();
            _models.Clear();
            _materials.Clear();
            _textures.Clear();
        }

        static void Replace<T>(ICollection<T> target, IEnumerable<T> values)
        {
            target.Clear();
            foreach (T value in values)
                target.Add(value);
        }

        public void OnActivated() { }
        public void OnDeactivated() { }
        public void OnSelectionChanged() { }

        [MenuItem("GameObject/Bento/选中物体的模型、材质和贴图", false, -8990)]
        [MenuItem("Assets/Bento/选中物体的模型、材质和贴图", false, -8990)]
        internal static void SelectAdvancedFromCurrentSelection()
        {
            UsedAssetSelectionResult result =
                UsedAssetSelectionScanner.Scan(UsedAssetSelectionScanner.ResolveCurrentRoots());
            Selection.objects = result.AllAssets().Distinct().ToArray();
        }

        [MenuItem("GameObject/Bento/选中物体的模型、材质和贴图", true)]
        [MenuItem("Assets/Bento/选中物体的模型、材质和贴图", true)]
        internal static bool ValidateSelection() =>
            UsedAssetSelectionScanner.ResolveCurrentRoots().Count > 0;

        internal static string BuildTextureScanDiagnosticFromCurrentSelection() =>
            UsedAssetSelectionDiagnosticService.BuildFromCurrentSelection();

        internal static string BuildTextureScanDiagnosticByName(string objectName) =>
            UsedAssetSelectionDiagnosticService.BuildByObjectName(objectName);
    }
}

using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Bento
{
    sealed class SelectUsedAssetsTool
    {
        readonly List<GameObject> _lockedRoots = new List<GameObject>();
        readonly List<UnityEngine.Object> _models = new List<UnityEngine.Object>();
        readonly List<Material> _materials = new List<Material>();
        readonly List<Texture> _textures = new List<Texture>();
        Vector2 _scroll;

        public void Draw()
        {
            EditorGUILayout.LabelField("进阶：选中模型、材质和贴图", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "锁定场景物体或 Project 中的 Prefab/FBX，收集其全部子 Renderer 使用的模型资产、材质球和材质贴图。",
                MessageType.Info);

            List<GameObject> current = ResolveSelectedRoots();
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(current.Count == 0))
                    if (GUILayout.Button($"锁定当前物体并扫描（{current.Count}）", GUILayout.Height(32))) Scan(current);
                using (new EditorGUI.DisabledScope(_lockedRoots.Count == 0))
                    if (GUILayout.Button("重新扫描已锁定物体", GUILayout.Height(32)))
                        Scan(_lockedRoots.Where(root => root != null).ToList());
            }

            if (_lockedRoots.Count == 0) return;
            EditorGUILayout.LabelField(
                $"模型 {_models.Count}　材质 {_materials.Count}　贴图 {_textures.Count}　合计 {AllAssets().Count}",
                EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(AllAssets().Count == 0))
                    if (GUILayout.Button("重新全选全部资产", GUILayout.Height(30))) SelectAll();
                if (GUILayout.Button("清空锁定", GUILayout.Height(30))) Clear();
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll, EditorStyles.helpBox, GUILayout.MinHeight(260), GUILayout.MaxHeight(600));
            DrawGroup("模型 / FBX", _models);
            DrawGroup("材质球", _materials.Cast<UnityEngine.Object>());
            DrawGroup("贴图", _textures.Cast<UnityEngine.Object>());
            EditorGUILayout.EndScrollView();
        }

        [MenuItem("GameObject/Bento/选中物体的模型、材质和贴图", false, -8990)]
        [MenuItem("Assets/Bento/选中物体的模型、材质和贴图", false, -8990)]
        static void SelectAdvancedFromCurrentSelection()
        {
            var tool = new SelectUsedAssetsTool();
            tool.Scan(ResolveSelectedRoots());
        }

        [MenuItem("GameObject/Bento/选中物体的模型、材质和贴图", true)]
        [MenuItem("Assets/Bento/选中物体的模型、材质和贴图", true)]
        static bool ValidateSelection() => ResolveSelectedRoots().Count > 0;

        void Scan(IReadOnlyList<GameObject> roots)
        {
            _lockedRoots.Clear();
            _lockedRoots.AddRange(roots.Where(root => root != null).Distinct());
            _models.Clear();
            _materials.Clear();
            _textures.Clear();

            Renderer[] renderers = _lockedRoots.SelectMany(root => root.GetComponentsInChildren<Renderer>(true)).Distinct().ToArray();
            foreach (Renderer renderer in renderers)
            {
                Mesh mesh = renderer is SkinnedMeshRenderer skinned
                    ? skinned.sharedMesh
                    : renderer.GetComponent<MeshFilter>()?.sharedMesh;
                AddModel(mesh);
                foreach (Material material in renderer.sharedMaterials)
                    if (material != null && !_materials.Contains(material)) _materials.Add(material);
            }
            foreach (Material material in _materials) CollectTextures(material);
            SortAssets(_models);
            SortAssets(_materials);
            SortAssets(_textures);
            SelectAll();
        }

        void AddModel(Mesh mesh)
        {
            if (mesh == null) return;
            string path = AssetDatabase.GetAssetPath(mesh);
            if (string.IsNullOrEmpty(path)) return;
            UnityEngine.Object mainAsset = AssetDatabase.LoadMainAssetAtPath(path) ?? mesh;
            if (!_models.Contains(mainAsset)) _models.Add(mainAsset);
        }

        void CollectTextures(Material material)
        {
            if (material == null || material.shader == null) return;
            for (int index = 0; index < ShaderUtil.GetPropertyCount(material.shader); index++)
            {
                if (ShaderUtil.GetPropertyType(material.shader, index) != ShaderUtil.ShaderPropertyType.TexEnv) continue;
                Texture texture = material.GetTexture(ShaderUtil.GetPropertyName(material.shader, index));
                if (texture != null && !_textures.Contains(texture)) _textures.Add(texture);
            }
        }

        void DrawGroup(string title, IEnumerable<UnityEngine.Object> assets)
        {
            UnityEngine.Object[] items = assets.Where(asset => asset != null).ToArray();
            EditorGUILayout.LabelField($"{title}（{items.Length}）", EditorStyles.boldLabel);
            foreach (UnityEngine.Object asset in items)
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.ObjectField(asset, asset.GetType(), false);
                EditorGUILayout.LabelField(AssetDatabase.GetAssetPath(asset), EditorStyles.miniLabel, GUILayout.MinWidth(220));
                if (GUILayout.Button("定位", GUILayout.Width(60))) FocusKeepingSelection(asset);
            }
            EditorGUILayout.Space(6);
        }

        void SelectAll()
        {
            UnityEngine.Object[] assets = AllAssets().ToArray();
            if (assets.Length > 0) Selection.objects = assets;
        }

        void FocusKeepingSelection(UnityEngine.Object asset)
        {
            UnityEngine.Object[] all = AllAssets().ToArray();
            if (asset == null || all.Length == 0) return;
            Selection.objects = all;
            EditorGUIUtility.PingObject(asset);
        }

        List<UnityEngine.Object> AllAssets() => _models
            .Concat(_materials.Cast<UnityEngine.Object>())
            .Concat(_textures.Cast<UnityEngine.Object>())
            .Where(asset => asset != null)
            .Distinct()
            .ToList();

        void Clear()
        {
            _lockedRoots.Clear();
            _models.Clear();
            _materials.Clear();
            _textures.Clear();
        }

        static void SortAssets<T>(List<T> assets) where T : UnityEngine.Object => assets.Sort((left, right) =>
            string.Compare(AssetDatabase.GetAssetPath(left), AssetDatabase.GetAssetPath(right), System.StringComparison.OrdinalIgnoreCase));

        static List<GameObject> ResolveSelectedRoots()
        {
            var roots = new List<GameObject>();
            foreach (UnityEngine.Object selected in Selection.objects)
            {
                GameObject root = ResolveGameObject(selected);
                if (root != null && !roots.Contains(root)) roots.Add(root);
            }
            return roots;
        }

        static GameObject ResolveGameObject(UnityEngine.Object selected)
        {
            if (selected is Component component) return component.gameObject;
            if (selected is GameObject gameObject) return gameObject;
            string path = selected != null ? AssetDatabase.GetAssetPath(selected) : string.Empty;
            if (string.IsNullOrEmpty(path) || selected is Material || selected is Texture) return null;
            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }
    }
}

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
        readonly List<TextureScanTrace> _textureTraces = new List<TextureScanTrace>();
        readonly SelectedAssetClassificationTool _classificationTool = new SelectedAssetClassificationTool();
        readonly SceneMaterialPickerController _materialPicker = new SceneMaterialPickerController();
        Vector2 _scroll;

        public void Draw()
        {
            EditorGUILayout.LabelField("进阶：选中模型、材质和贴图", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "锁定场景网格物体或 Project 中的 Prefab/FBX，收集模型资产、材质球和材质贴图。Terrain 自动忽略，请使用 Terrain 专用工具。",
                MessageType.Info);

            DrawMaterialPicker();

            List<GameObject> current = UsedAssetSelectionScanner.ResolveCurrentRoots();
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(current.Count == 0))
                    if (GUILayout.Button($"锁定当前物体并扫描（{current.Count}）", GUILayout.Height(32))) Scan(current);
                using (new EditorGUI.DisabledScope(_lockedRoots.Count == 0))
                    if (GUILayout.Button("重新扫描已锁定物体", GUILayout.Height(32)))
                        Scan(_lockedRoots.Where(root => root != null).ToList());
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(_models.Count == 0))
                    if (GUILayout.Button($"只选模型（{_models.Count}）")) SelectAssets(_models);
                using (new EditorGUI.DisabledScope(_materials.Count == 0))
                    if (GUILayout.Button($"只选材质（{_materials.Count}）")) SelectAssets(_materials);
                using (new EditorGUI.DisabledScope(_textures.Count == 0))
                    if (GUILayout.Button($"只选贴图（{_textures.Count}）")) SelectAssets(_textures);
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
            _classificationTool.Draw(_models, _materials, _textures);
        }

        void DrawMaterialPicker()
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("场景材质吸管", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "启用后在 Scene 视图点击模型表面，即可在 Project 中选中对应材质。MeshCollider 与渲染网格一致时可精确到子网格；否则会列出该 Renderer 的材质槽供选择。",
                MessageType.Info);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (!_materialPicker.IsActive)
                {
                    if (GUILayout.Button("开始吸取材质", GUILayout.Height(30))) _materialPicker.Start();
                }
                else
                {
                    if (GUILayout.Button("停止吸取", GUILayout.Height(30))) _materialPicker.Stop();
                }
            }
            EditorGUILayout.HelpBox(_materialPicker.Status, _materialPicker.IsActive ? MessageType.Warning : MessageType.None);
            EditorGUILayout.Space(6);
        }

        public void OnActivated() { }
        public void OnDeactivated() => _materialPicker.Stop();
        public void OnSelectionChanged() { }

        [MenuItem("GameObject/Bento/选中物体的模型、材质和贴图", false, -8990)]
        [MenuItem("Assets/Bento/选中物体的模型、材质和贴图", false, -8990)]
        internal static void SelectAdvancedFromCurrentSelection()
        {
            var tool = new SelectUsedAssetsTool();
            tool.Scan(UsedAssetSelectionScanner.ResolveCurrentRoots());
        }

        [MenuItem("GameObject/Bento/选中物体的模型、材质和贴图", true)]
        [MenuItem("Assets/Bento/选中物体的模型、材质和贴图", true)]
        internal static bool ValidateSelection() => UsedAssetSelectionScanner.ResolveCurrentRoots().Count > 0;

        internal static string BuildTextureScanDiagnosticFromCurrentSelection()
        {
            return BuildTextureScanDiagnostic(ResolveSelectedRoots());
        }

        internal static string BuildTextureScanDiagnosticByName(string objectName)
        {
            var roots = UnityEngine.Object.FindObjectsOfType<GameObject>(true)
                .Where(gameObject => gameObject.scene.IsValid() &&
                                     string.Equals(gameObject.name, objectName, System.StringComparison.Ordinal))
                .ToList();
            return BuildTextureScanDiagnostic(roots);
        }

        static string BuildTextureScanDiagnostic(List<GameObject> roots)
        {
            var lines = new List<string>
            {
                $"[Bento] 贴图扫描诊断：根物体 {roots.Count}"
            };
            Renderer[] renderers = roots
                .Where(root => root != null)
                .SelectMany(root => root.GetComponentsInChildren<Renderer>(true))
                .Where(UsedAssetSelectionFilter.IsSupportedRenderer)
                .Distinct()
                .ToArray();
            foreach (Renderer renderer in renderers)
            foreach (Material material in renderer.sharedMaterials.Where(UsedAssetSelectionFilter.IsEditableMaterial))
            {
                if (material.shader == null) continue;
                foreach (MaterialTextureReference reference in GetSerializedTextureReferences(material))
                {
                    string propertyName = reference.propertyName;
                    Texture texture = reference.texture;
                    string path = AssetDatabase.GetAssetPath(texture);
                    bool included = UsedAssetSelectionFilter.IsModelTextureProperty(propertyName) &&
                                    UsedAssetSelectionFilter.IsProjectTexture(texture);
                    lines.Add(
                        $"{(included ? "保留" : "排除")} | " +
                        $"Renderer={renderer.name} | Material={material.name} | Property={propertyName} | " +
                        $"Texture={texture.name} | Type={texture.GetType().FullName} | " +
                        $"SubAsset={AssetDatabase.IsSubAsset(texture)} | Path={path}");
                }
            }
            return string.Join("\n", lines);
        }

        void Scan(IReadOnlyList<GameObject> roots)
        {
            _lockedRoots.Clear();
            _lockedRoots.AddRange(roots.Where(root => root != null).Distinct());
            _models.Clear();
            _materials.Clear();
            _textures.Clear();
            _textureTraces.Clear();

            Renderer[] renderers = _lockedRoots
                .SelectMany(root => root.GetComponentsInChildren<Renderer>(true))
                .Where(UsedAssetSelectionFilter.IsSupportedRenderer)
                .Distinct()
                .ToArray();
            foreach (Renderer renderer in renderers)
            {
                Mesh mesh = renderer is SkinnedMeshRenderer skinned
                    ? skinned.sharedMesh
                    : renderer.GetComponent<MeshFilter>()?.sharedMesh;
                AddModel(mesh);
                foreach (Material material in renderer.sharedMaterials)
                    if (UsedAssetSelectionFilter.IsEditableMaterial(material) && !_materials.Contains(material))
                        _materials.Add(material);
            }
            foreach (Renderer renderer in renderers)
                foreach (Material material in renderer.sharedMaterials)
                    CollectTextures(renderer, material);
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

        void CollectTextures(Renderer renderer, Material material)
        {
            if (!UsedAssetSelectionFilter.IsEditableMaterial(material) || material.shader == null) return;
            foreach (MaterialTextureReference reference in GetSerializedTextureReferences(material))
            {
                string propertyName = reference.propertyName;
                Texture texture = reference.texture;
                string path = AssetDatabase.GetAssetPath(texture);
                bool included = UsedAssetSelectionFilter.IsModelTextureProperty(propertyName) &&
                                UsedAssetSelectionFilter.IsProjectTexture(texture);
                _textureTraces.Add(new TextureScanTrace
                {
                    renderer = renderer != null ? renderer.name : "<null>",
                    material = material.name,
                    property = propertyName,
                    texture = texture.name,
                    path = path,
                    type = texture.GetType().FullName,
                    isSubAsset = AssetDatabase.IsSubAsset(texture),
                    included = included
                });
                if (included && !_textures.Contains(texture))
                    _textures.Add(texture);
            }
        }

        static IEnumerable<MaterialTextureReference> GetSerializedTextureReferences(Material material)
        {
            if (material == null) yield break;
            var serialized = new SerializedObject(material);
            SerializedProperty textures = serialized.FindProperty("m_SavedProperties.m_TexEnvs");
            if (textures == null || !textures.isArray) yield break;
            for (int index = 0; index < textures.arraySize; index++)
            {
                SerializedProperty entry = textures.GetArrayElementAtIndex(index);
                string propertyName = entry.FindPropertyRelative("first")?.stringValue;
                Texture texture = entry.FindPropertyRelative("second.m_Texture")?.objectReferenceValue as Texture;
                if (texture != null) yield return new MaterialTextureReference(propertyName, texture);
            }
        }

        readonly struct MaterialTextureReference
        {
            public MaterialTextureReference(string propertyName, Texture texture)
            {
                this.propertyName = propertyName;
                this.texture = texture;
            }

            public readonly string propertyName;
            public readonly Texture texture;
        }

        [System.Serializable]
        sealed class TextureScanTrace
        {
            public string renderer;
            public string material;
            public string property;
            public string texture;
            public string path;
            public string type;
            public bool isSubAsset;
            public bool included;
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

        static void SelectAssets<T>(IEnumerable<T> assets) where T : UnityEngine.Object
        {
            UnityEngine.Object[] items = assets.Where(asset => asset != null).Cast<UnityEngine.Object>().ToArray();
            if (items.Length > 0) Selection.objects = items;
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
            if (selected is GameObject gameObject)
                return gameObject.GetComponent<Terrain>() != null ? null : gameObject;
            string path = selected != null ? AssetDatabase.GetAssetPath(selected) : string.Empty;
            if (string.IsNullOrEmpty(path) || selected is Material || selected is Texture) return null;
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            return asset != null && asset.GetComponent<Terrain>() == null ? asset : null;
        }
    }
}

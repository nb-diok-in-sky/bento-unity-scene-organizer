using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Bento
{
    sealed class ModelMaterialInheritanceEntry
    {
        public string targetPath;
        public Renderer sourceRenderer;
        public Renderer targetRenderer;
        public Material[] materials;
        public string reason;
        public int confidence;
        public bool include;
    }

    [BentoPage("fbx_material_inherit", BentoGroups.Models, "FBX 材质继承", Order = 30)]
    sealed class ModelMaterialInheritanceTool : IBentoPageLifecycle, IBentoTool
    {
        GameObject _source;
        GameObject _target;
        Vector2 _scroll;
        List<ModelMaterialInheritanceEntry> _entries = new List<ModelMaterialInheritanceEntry>();

        public void Draw()
        {
            EditorGUILayout.LabelField("模型材质复制", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "按子物体路径或名称，把参考模型每个 Renderer 的现有材质引用复制到待修复模型。不会创建或复制材质球。",
                MessageType.Info);

            DrawModelField(
                "参考模型（材质正确）",
                "选择材质配置正确的模型根或任意子物体。",
                ref _source,
                "当前选中物体作为参考模型");

            EditorGUILayout.Space(6);

            DrawModelField(
                "待修复模型（材质灰色）",
                "选择需要接收材质的场景模型根或任意子物体。",
                ref _target,
                "当前选中物体作为待修复模型");

            if (_target != null && !_target.scene.IsValid())
                EditorGUILayout.HelpBox(
                    "请把待修复 FBX 拖到场景后，再把场景实例放到这里。FBX 全局 Remap 无法表达不同子物体使用同名槽但不同材质的情况。",
                    MessageType.Warning);

            using (new EditorGUI.DisabledScope(_source == null || _target == null || !_target.scene.IsValid()))
                if (GUILayout.Button("对比同名子物体并匹配材质", GUILayout.Height(30)))
                    _entries = BuildPlan(_source, _target).ToList();

            DrawPlan();
        }

        void DrawModelField(string label, string tooltip, ref GameObject value, string selectionButton)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                UnityEngine.Object next = EditorGUILayout.ObjectField(
                    new GUIContent(label, tooltip), value, typeof(UnityEngine.Object), true);
                GameObject resolved = ResolveModelRoot(next);
                if (resolved != value)
                {
                    value = resolved;
                    _entries.Clear();
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    GameObject selected = Selection.activeGameObject;
                    using (new EditorGUI.DisabledScope(selected == null))
                        if (GUILayout.Button(selectionButton, GUILayout.Width(230)))
                        {
                            value = ResolveModelRoot(selected);
                            _entries.Clear();
                        }
                    GUILayout.FlexibleSpace();
                    using (new EditorGUI.DisabledScope(value == null))
                        if (GUILayout.Button("定位", GUILayout.Width(60))) BentoGUI.Locate(value);
                }
            }
        }

        void DrawPlan()
        {
            if (_entries.Count == 0) return;
            int matched = _entries.Count(entry => entry.sourceRenderer != null);
            EditorGUILayout.LabelField($"匹配结果：{matched}/{_entries.Count} 个 Renderer", EditorStyles.boldLabel);
            _scroll = EditorGUILayout.BeginScrollView(_scroll, EditorStyles.helpBox, GUILayout.MinHeight(220), GUILayout.MaxHeight(600));
            foreach (var entry in _entries)
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                entry.include = EditorGUILayout.ToggleLeft(entry.targetPath, entry.include);
                EditorGUILayout.LabelField($"匹配依据：{entry.reason}", EditorStyles.miniLabel);
                EditorGUILayout.LabelField($"材质槽：{entry.materials.Length}", EditorStyles.miniLabel);
                for (int index = 0; index < entry.materials.Length; index++)
                    entry.materials[index] = (Material)EditorGUILayout.ObjectField(
                        $"槽位 {index}", entry.materials[index], typeof(Material), false);
                if (entry.sourceRenderer == null)
                    EditorGUILayout.HelpBox("没有找到同路径或唯一同名的参考子物体。", MessageType.Warning);
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(entry.sourceRenderer == null))
                        if (GUILayout.Button("定位参考子物体")) BentoGUI.Locate(entry.sourceRenderer != null ? entry.sourceRenderer.gameObject : null);
                    if (GUILayout.Button("定位待修复子物体")) BentoGUI.Locate(entry.targetRenderer.gameObject);
                }
            }
            EditorGUILayout.EndScrollView();

            var selected = _entries.Where(entry => entry.include && entry.sourceRenderer != null).ToList();
            using (new EditorGUI.DisabledScope(selected.Count == 0))
                if (GUILayout.Button($"将材质复制到待修复模型（{selected.Count} 个子物体）", GUILayout.Height(36)) &&
                    BentoCommand.Confirm(
                        "复制模型材质",
                        $"将按子物体逐一复制 {selected.Count} 组现有材质引用。不会创建材质球，并支持 Ctrl+Z。确定继续吗？"))
                    Apply(selected);
        }

        static IEnumerable<ModelMaterialInheritanceEntry> BuildPlan(GameObject source, GameObject target)
        {
            var sources = CollectRenderers(source);
            foreach (RendererInfo targetInfo in CollectRenderers(target))
            {
                RendererInfo sourceInfo = FindMatch(targetInfo, sources, out int confidence, out string reason);
                yield return new ModelMaterialInheritanceEntry
                {
                    targetPath = targetInfo.path,
                    sourceRenderer = sourceInfo?.renderer,
                    targetRenderer = targetInfo.renderer,
                    materials = sourceInfo != null ? sourceInfo.renderer.sharedMaterials.ToArray() : Array.Empty<Material>(),
                    reason = reason,
                    confidence = confidence,
                    include = sourceInfo != null && confidence >= 80
                };
            }
        }

        static RendererInfo FindMatch(RendererInfo target, IReadOnlyList<RendererInfo> sources,
            out int confidence, out string reason)
        {
            RendererInfo match = sources.FirstOrDefault(source =>
                string.Equals(source.path, target.path, StringComparison.Ordinal));
            if (match != null)
            {
                confidence = 100;
                reason = "子物体路径一致";
                return match;
            }

            var sameName = sources.Where(source =>
                string.Equals(source.name, target.name, StringComparison.OrdinalIgnoreCase)).ToList();
            if (sameName.Count == 1)
            {
                confidence = 90;
                reason = "子物体名称唯一一致";
                return sameName[0];
            }

            var sameMesh = sources.Where(source =>
                !string.IsNullOrEmpty(target.meshName) &&
                string.Equals(source.meshName, target.meshName, StringComparison.OrdinalIgnoreCase)).ToList();
            if (sameMesh.Count == 1)
            {
                confidence = 80;
                reason = "Mesh 名称唯一一致";
                return sameMesh[0];
            }

            confidence = 0;
            reason = "未匹配";
            return null;
        }

        static List<RendererInfo> CollectRenderers(GameObject root)
        {
            return root.GetComponentsInChildren<Renderer>(true).Select(renderer =>
            {
                Mesh mesh = renderer is SkinnedMeshRenderer skinned
                    ? skinned.sharedMesh
                    : renderer.GetComponent<MeshFilter>()?.sharedMesh;
                return new RendererInfo
                {
                    renderer = renderer,
                    name = renderer.name,
                    path = GetPath(root.transform, renderer.transform),
                    meshName = mesh != null ? mesh.name : string.Empty
                };
            }).ToList();
        }

        static void Apply(IReadOnlyList<ModelMaterialInheritanceEntry> entries)
        {
            using (var undo = new BentoUndoScope("复制模型材质"))
            {
                foreach (var entry in entries)
                {
                    Undo.RecordObject(entry.targetRenderer, "复制模型材质");
                    entry.targetRenderer.sharedMaterials = entry.materials.ToArray();
                    EditorUtility.SetDirty(entry.targetRenderer);
                }
                undo.Complete();
            }
            EditorUtility.DisplayDialog(
                "复制完成",
                $"已更新 {entries.Count} 个子物体的材质引用，没有创建新材质球。可使用 Ctrl+Z 撤销。",
                "确定");
        }

        static GameObject ResolveModelRoot(UnityEngine.Object asset)
        {
            GameObject gameObject = asset as GameObject;
            if (asset is Component component) gameObject = component.gameObject;
            if (gameObject == null) return null;
            if (gameObject.scene.IsValid())
                return PrefabUtility.GetNearestPrefabInstanceRoot(gameObject) ?? gameObject.transform.root.gameObject;
            string path = AssetDatabase.GetAssetPath(gameObject);
            return string.IsNullOrEmpty(path) ? gameObject : AssetDatabase.LoadAssetAtPath<GameObject>(path) ?? gameObject;
        }

        static string GetPath(Transform root, Transform current)
        {
            var names = new Stack<string>();
            while (current != null && current != root)
            {
                names.Push(current.name);
                current = current.parent;
            }
            return names.Count == 0 ? root.name : string.Join("/", names);
        }

        public void OnActivated() { }
        public void OnDeactivated() { }
        public void OnSelectionChanged() { }

        sealed class RendererInfo
        {
            public Renderer renderer;
            public string name;
            public string path;
            public string meshName;
        }
    }
}

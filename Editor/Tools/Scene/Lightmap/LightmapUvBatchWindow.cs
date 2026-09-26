using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Bento
{
    sealed class LightmapUvModelEntry
    {
        public string assetPath;
        public bool generateLightmapUvs;
        public int meshFilterCount;
        public readonly List<string> hierarchyPaths = new List<string>();
    }

    sealed class LightmapUvImportHistoryEntry
    {
        public string assetPath;
        public bool previousValue;
    }

    sealed class PooledLightState
    {
        public Light light;
        public bool enabled;
    }

    [InitializeOnLoad]
    static class LightmapBakePoolController
    {
        static readonly List<PooledLightState> OriginalStates = new List<PooledLightState>();
        static double _requestedAt;
        static bool _bakeStarted;

        public static event Action StateChanged;

        public static bool IsManagedBake { get; private set; }
        public static string Status { get; private set; } = string.Empty;

        static LightmapBakePoolController()
        {
            Lightmapping.bakeStarted -= OnBakeStarted;
            Lightmapping.bakeStarted += OnBakeStarted;
            Lightmapping.bakeCompleted -= OnBakeCompleted;
            Lightmapping.bakeCompleted += OnBakeCompleted;
            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
            EditorApplication.quitting -= OnEditorQuitting;
            EditorApplication.quitting += OnEditorQuitting;
            EditorApplication.update -= MonitorBake;
            EditorApplication.update += MonitorBake;
        }

        public static bool StartBake(
            IReadOnlyCollection<Light> dynamicLights,
            IReadOnlyCollection<Light> staticLights,
            out string error)
        {
            error = string.Empty;
            if (IsManagedBake || Lightmapping.isRunning)
            {
                error = "Unity 当前正在烘焙光照。";
                return false;
            }

            Light[] dynamicTargets = ValidDistinct(dynamicLights);
            Light[] staticTargets = ValidDistinct(staticLights);
            Light overlap = dynamicTargets.FirstOrDefault(staticTargets.Contains);
            if (overlap != null)
            {
                error = $"灯光“{BentoUtility.GetHierarchyPath(overlap.transform)}”同时存在于动态光池和静态光池，请先移除重复项。";
                return false;
            }

            OriginalStates.Clear();
            foreach (Light light in dynamicTargets.Concat(staticTargets).Distinct())
                OriginalStates.Add(new PooledLightState { light = light, enabled = light.enabled });

            try
            {
                SetLights(dynamicTargets, false, recordUndo: false, markScenesDirty: false);
                SetLights(staticTargets, true, recordUndo: false, markScenesDirty: false);
                IsManagedBake = true;
                _bakeStarted = false;
                _requestedAt = EditorApplication.timeSinceStartup;
                Status = $"准备烘焙：动态光关闭 {dynamicTargets.Length} 个，静态光开启 {staticTargets.Length} 个。";
                NotifyStateChanged();

                if (!Lightmapping.BakeAsync())
                {
                    RestoreOriginalStates("Unity 未能启动光照烘焙，灯光状态已恢复。");
                    error = "Unity 未能启动光照烘焙，请检查 Lighting Settings 和 Console。";
                    return false;
                }

                Status = "正在烘焙光照；完成后会自动恢复动态光和静态光的原状态。";
                NotifyStateChanged();
                return true;
            }
            catch (Exception exception)
            {
                RestoreOriginalStates("启动烘焙失败，灯光状态已恢复。");
                error = exception.Message;
                return false;
            }
        }

        public static void CancelBake()
        {
            if (!IsManagedBake) return;
            Lightmapping.Cancel();
            RestoreOriginalStates("已取消烘焙，动态光和静态光已恢复到烘焙前状态。");
        }

        public static int SetLightsForDebug(IEnumerable<Light> lights, bool enabled, string undoName)
        {
            Light[] targets = ValidDistinct(lights)
                .Where(light => light.enabled != enabled)
                .ToArray();
            SetLights(
                targets,
                enabled,
                recordUndo: true,
                markScenesDirty: true,
                undoName: undoName);
            return targets.Length;
        }

        static void OnBakeStarted()
        {
            if (!IsManagedBake) return;
            _bakeStarted = true;
            Status = "正在烘焙光照；完成后会自动恢复灯光。";
            NotifyStateChanged();
        }

        static void OnBakeCompleted()
        {
            if (!IsManagedBake) return;
            RestoreOriginalStates("光照烘焙完成，动态光和静态光已恢复到烘焙前状态。");
        }

        static void OnBeforeAssemblyReload()
        {
            if (!IsManagedBake) return;
            Lightmapping.Cancel();
            RestoreOriginalStates("脚本即将重载，已取消烘焙并恢复灯光状态。");
        }

        static void OnEditorQuitting()
        {
            if (!IsManagedBake) return;
            Lightmapping.Cancel();
            RestoreOriginalStates("Unity 正在退出，已取消烘焙并恢复灯光状态。");
        }

        static void MonitorBake()
        {
            if (!IsManagedBake) return;
            double elapsed = EditorApplication.timeSinceStartup - _requestedAt;
            if (!_bakeStarted && elapsed > 30d && !Lightmapping.isRunning)
            {
                RestoreOriginalStates("等待烘焙启动超时，灯光状态已恢复。请检查 Lighting Settings 和 Console。");
                return;
            }

            if (_bakeStarted && !Lightmapping.isRunning && elapsed > 1d)
                EditorApplication.delayCall += RestoreIfStillPending;
        }

        static void RestoreIfStillPending()
        {
            if (IsManagedBake && !Lightmapping.isRunning)
                RestoreOriginalStates("光照烘焙已停止，动态光和静态光已恢复到烘焙前状态。");
        }

        static void RestoreOriginalStates(string status)
        {
            foreach (PooledLightState state in OriginalStates)
                if (state.light != null) state.light.enabled = state.enabled;
            OriginalStates.Clear();
            IsManagedBake = false;
            _bakeStarted = false;
            Status = status;
            SceneView.RepaintAll();
            NotifyStateChanged();
        }

        static void SetLights(
            IEnumerable<Light> lights,
            bool enabled,
            bool recordUndo,
            bool markScenesDirty,
            string undoName = "Bento 灯光池调试")
        {
            Light[] targets = ValidDistinct(lights);
            if (recordUndo && targets.Length > 0)
                Undo.RecordObjects(targets.Cast<UnityEngine.Object>().ToArray(), undoName);

            var affectedScenes = new HashSet<Scene>();
            foreach (Light light in targets)
            {
                light.enabled = enabled;
                if (markScenesDirty && light.gameObject.scene.IsValid())
                    affectedScenes.Add(light.gameObject.scene);
            }

            if (markScenesDirty)
                foreach (Scene scene in affectedScenes)
                    EditorSceneManager.MarkSceneDirty(scene);
            SceneView.RepaintAll();
        }

        static Light[] ValidDistinct(IEnumerable<Light> lights)
        {
            return (lights ?? Enumerable.Empty<Light>())
                .Where(light => light != null && light.gameObject.scene.IsValid() && light.gameObject.scene.isLoaded)
                .Distinct()
                .ToArray();
        }

        static void NotifyStateChanged()
        {
            StateChanged?.Invoke();
        }
    }

    public sealed class LightmapUvBatchWindow : EditorWindow
    {
        [SerializeField] List<GameObject> _dynamicLightRoots = new List<GameObject>();
        [SerializeField] List<GameObject> _staticLightRoots = new List<GameObject>();
        readonly List<LightmapUvModelEntry> _entries = new List<LightmapUvModelEntry>();
        readonly List<LightmapUvImportHistoryEntry> _lastHistory = new List<LightmapUvImportHistoryEntry>();
        Vector2 _windowScroll;
        Vector2 _scroll;
        bool _includeInactive = true;
        int _selectedRootCount;
        int _meshFilterCount;
        int _unsupportedMeshCount;
        string _message = string.Empty;

        [MenuItem("Tools/Bento/烘培光照", priority = -9990)]
        public static void Open()
        {
            var window = GetWindow<LightmapUvBatchWindow>("烘培光照");
            window.minSize = new Vector2(620f, 480f);
            window.RefreshFromSelection();
            window.Show();
            window.Focus();
        }

        void OnEnable()
        {
            LightmapBakePoolController.StateChanged -= OnBakeStateChanged;
            LightmapBakePoolController.StateChanged += OnBakeStateChanged;
            RemoveInvalidLightRoots(_dynamicLightRoots);
            RemoveInvalidLightRoots(_staticLightRoots);
            RefreshFromSelection();
        }

        void OnDisable()
        {
            LightmapBakePoolController.StateChanged -= OnBakeStateChanged;
        }

        void OnSelectionChange()
        {
            _message = string.Empty;
            RefreshFromSelection();
            Repaint();
        }

        void OnGUI()
        {
            _windowScroll = EditorGUILayout.BeginScrollView(_windowScroll);
            EditorGUILayout.LabelField("烘培光照", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "读取 Hierarchy 当前选中物体及其子物体的 MeshFilter.sharedMesh，按 FBX/OBJ 模型资产去重，一键开启或关闭 Model Import Settings 中的 Generate Lightmap UVs。不会扫描整个 Project。",
                MessageType.Info);

            DrawSelectionOptions();
            DrawSummary();
            DrawActions();
            DrawEntries();
            EditorGUILayout.Space(12f);
            DrawLightPoolsAndBake();
            EditorGUILayout.EndScrollView();
        }

        void DrawSelectionOptions()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUI.BeginChangeCheck();
                _includeInactive = EditorGUILayout.ToggleLeft("包含未激活子物体", _includeInactive);
                if (EditorGUI.EndChangeCheck())
                {
                    _message = string.Empty;
                    RefreshFromSelection();
                }

                if (GUILayout.Button("重新读取 Hierarchy 当前选择", GUILayout.Height(30)))
                {
                    RefreshFromSelection();
                    _message = "已重新读取 Hierarchy 当前选择。";
                }
            }
        }

        void DrawSummary()
        {
            int enabledCount = _entries.Count(entry => entry.generateLightmapUvs);
            int disabledCount = _entries.Count - enabledCount;
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(
                    $"选中父物体 {_selectedRootCount} 个　MeshFilter {_meshFilterCount} 个　模型资产 {_entries.Count} 个",
                    EditorStyles.wordWrappedLabel);
                EditorGUILayout.LabelField(
                    $"Generate Lightmap UVs：已开启 {enabledCount} 个，已关闭 {disabledCount} 个",
                    EditorStyles.wordWrappedMiniLabel);
                if (_unsupportedMeshCount > 0)
                    EditorGUILayout.HelpBox(
                        $"跳过 {_unsupportedMeshCount} 个没有 ModelImporter 的 Mesh（例如运行时网格或独立 .asset Mesh）。",
                        MessageType.None);
                if (!string.IsNullOrEmpty(_message))
                    EditorGUILayout.HelpBox(_message, MessageType.Info);
            }
        }

        void DrawActions()
        {
            int enableCount = _entries.Count(entry => !entry.generateLightmapUvs);
            int disableCount = _entries.Count(entry => entry.generateLightmapUvs);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(enableCount == 0))
                    if (GUILayout.Button($"一键开启（{enableCount}）", GUILayout.Height(38)) &&
                        ConfirmChange(enableCount, true))
                        ApplySetting(true);

                using (new EditorGUI.DisabledScope(disableCount == 0))
                    if (GUILayout.Button($"一键关闭（{disableCount}）", GUILayout.Height(38)) &&
                        ConfirmChange(disableCount, false))
                        ApplySetting(false);
            }

            using (new EditorGUI.DisabledScope(_lastHistory.Count == 0))
                if (GUILayout.Button("恢复上次 Generate Lightmap UVs 设置") &&
                    EditorUtility.DisplayDialog(
                        "恢复上次设置",
                        $"将恢复 {_lastHistory.Count} 个模型资产在上次批处理前的 Generate Lightmap UVs 状态，并重新导入模型。\n\n确定继续吗？",
                        "恢复",
                        "取消"))
                    RestoreLastSetting();
        }

        void DrawEntries()
        {
            if (_entries.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "请在 Hierarchy 选择包含 MeshFilter 的场景物体。",
                    MessageType.Warning);
                return;
            }

            _scroll = EditorGUILayout.BeginScrollView(
                _scroll,
                GUILayout.MinHeight(250f),
                GUILayout.MaxHeight(680f));
            foreach (LightmapUvModelEntry entry in _entries)
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(
                        entry.generateLightmapUvs ? "☑ Generate Lightmap UVs" : "☐ Generate Lightmap UVs",
                        EditorStyles.boldLabel);
                    if (GUILayout.Button("定位模型", GUILayout.Width(72f)))
                    {
                        UnityEngine.Object asset = AssetDatabase.LoadMainAssetAtPath(entry.assetPath);
                        if (asset != null) EditorGUIUtility.PingObject(asset);
                    }
                }

                EditorGUILayout.LabelField(entry.assetPath, EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.LabelField(
                    $"当前选择中由 {entry.meshFilterCount} 个 MeshFilter 使用",
                    EditorStyles.miniLabel);
                if (entry.hierarchyPaths.Count > 0)
                    EditorGUILayout.LabelField(
                        "示例：" + entry.hierarchyPaths[0],
                        EditorStyles.wordWrappedMiniLabel);
            }
            EditorGUILayout.EndScrollView();
        }

        void DrawLightPoolsAndBake()
        {
            EditorGUILayout.LabelField("灯光池与自动烘焙", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "灯光池只控制 Light.enabled，不隐藏 GameObject，也不改变 Light Mode。按灯光池烘焙时会暂存两组灯当前状态，动态光全部关闭、静态光全部开启；烘焙完成、停止或取消后恢复原状态。",
                MessageType.Info);

            using (new EditorGUI.DisabledScope(LightmapBakePoolController.IsManagedBake))
            {
                DrawLightPool(
                    "动态光池",
                    _dynamicLightRoots,
                    _staticLightRoots,
                    "烘焙前关闭；调试时可一键开启/关闭",
                    "Bento 动态光池");
                DrawLightPool(
                    "静态光池",
                    _staticLightRoots,
                    _dynamicLightRoots,
                    "烘焙前开启；调试时可一键开启/关闭",
                    "Bento 静态光池");
            }

            Light[] dynamicLights = CollectLights(_dynamicLightRoots);
            Light[] staticLights = CollectLights(_staticLightRoots);
            int overlapCount = dynamicLights.Count(staticLights.Contains);
            if (overlapCount > 0)
                EditorGUILayout.HelpBox(
                    $"有 {overlapCount} 个 Light 同时落入动态光池和静态光池，开始烘焙前必须移除重复层级。",
                    MessageType.Error);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(
                    $"烘焙准备：动态光 {dynamicLights.Length} 个 → 关闭；静态光 {staticLights.Length} 个 → 开启",
                    EditorStyles.wordWrappedLabel);
                EditorGUILayout.LabelField(
                    $"Unity 烘焙状态：{(Lightmapping.isRunning ? "正在运行" : "空闲")}",
                    EditorStyles.wordWrappedMiniLabel);
                if (!string.IsNullOrEmpty(LightmapBakePoolController.Status))
                    EditorGUILayout.HelpBox(
                        LightmapBakePoolController.Status,
                        LightmapBakePoolController.IsManagedBake ? MessageType.Warning : MessageType.Info);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                bool canStart = !LightmapBakePoolController.IsManagedBake &&
                                !Lightmapping.isRunning &&
                                overlapCount == 0 &&
                                dynamicLights.Length + staticLights.Length > 0;
                using (new EditorGUI.DisabledScope(!canStart))
                    if (GUILayout.Button("按灯光池开始烘焙", GUILayout.Height(42f)) &&
                        EditorUtility.DisplayDialog(
                            "确认开始光照烘焙",
                            $"烘焙期间将临时关闭 {dynamicLights.Length} 个动态光、开启 {staticLights.Length} 个静态光。\n\n烘焙完成或取消后会恢复它们当前的启用状态。\n\n确定继续吗？",
                            "开始烘焙",
                            "取消"))
                    {
                        if (!LightmapBakePoolController.StartBake(
                                dynamicLights,
                                staticLights,
                                out string error))
                            EditorUtility.DisplayDialog("无法开始烘焙", error, "确定");
                    }

                using (new EditorGUI.DisabledScope(!LightmapBakePoolController.IsManagedBake))
                    if (GUILayout.Button("取消本次烘焙并恢复灯光", GUILayout.Height(42f)))
                        LightmapBakePoolController.CancelBake();
            }

            if (GUILayout.Button("打开 Unity Lighting 窗口"))
                EditorApplication.ExecuteMenuItem("Window/Rendering/Lighting");
        }

        void DrawLightPool(
            string title,
            List<GameObject> roots,
            List<GameObject> otherRoots,
            string description,
            string undoName)
        {
            RemoveInvalidLightRoots(roots);
            Light[] lights = CollectLights(roots);
            int enabledCount = lights.Count(light => light.enabled);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
                EditorGUILayout.LabelField(description, EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.LabelField(
                    $"父物体 {roots.Count} 个　Light {lights.Length} 个　已开启 {enabledCount}　已关闭 {lights.Length - enabledCount}",
                    EditorStyles.wordWrappedMiniLabel);

                GameObject dropped = (GameObject)EditorGUILayout.ObjectField(
                    "拖入灯光或父物体",
                    null,
                    typeof(GameObject),
                    true);
                if (dropped != null) AddLightRoot(roots, otherRoots, dropped, title);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("加入 Hierarchy 当前选择"))
                        foreach (GameObject selected in BentoUtility.GetTopLevelSelectedSceneObjects(true))
                            AddLightRoot(roots, otherRoots, selected, title);
                    using (new EditorGUI.DisabledScope(roots.Count == 0))
                        if (GUILayout.Button("清空池", GUILayout.Width(70f)))
                            roots.Clear();
                }

                for (int index = roots.Count - 1; index >= 0; index--)
                {
                    GameObject root = roots[index];
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.ObjectField(root, typeof(GameObject), true);
                        EditorGUILayout.LabelField(
                            $"{root.GetComponentsInChildren<Light>(true).Length} 灯",
                            EditorStyles.miniLabel,
                            GUILayout.Width(45f));
                        if (GUILayout.Button("移除", GUILayout.Width(52f)))
                            roots.RemoveAt(index);
                    }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    int offCount = lights.Count(light => !light.enabled);
                    using (new EditorGUI.DisabledScope(offCount == 0))
                        if (GUILayout.Button($"一键开启（{offCount}）"))
                        {
                            int changed = LightmapBakePoolController.SetLightsForDebug(
                                lights,
                                true,
                                undoName + "开启");
                            _message = $"{title}：已开启 {changed} 个 Light，可按 Ctrl+Z 撤销。";
                        }

                    using (new EditorGUI.DisabledScope(enabledCount == 0))
                        if (GUILayout.Button($"一键关闭（{enabledCount}）"))
                        {
                            int changed = LightmapBakePoolController.SetLightsForDebug(
                                lights,
                                false,
                                undoName + "关闭");
                            _message = $"{title}：已关闭 {changed} 个 Light，可按 Ctrl+Z 撤销。";
                        }
                }
            }
        }

        void AddLightRoot(
            List<GameObject> roots,
            List<GameObject> otherRoots,
            GameObject candidate,
            string poolName)
        {
            if (candidate == null || !candidate.scene.IsValid() || !candidate.scene.isLoaded)
            {
                _message = "灯光池只能加入当前已加载场景中的 Hierarchy 物体。";
                return;
            }

            if (candidate.GetComponentsInChildren<Light>(true).Length == 0)
            {
                _message = $"“{BentoUtility.GetHierarchyPath(candidate.transform)}”下面没有 Light，未加入{poolName}。";
                return;
            }

            Light[] candidateLights = candidate.GetComponentsInChildren<Light>(true);
            var otherLights = new HashSet<Light>(CollectLights(otherRoots));
            Light overlap = candidateLights.FirstOrDefault(otherLights.Contains);
            if (overlap != null)
            {
                _message = $"未加入{poolName}：灯光“{BentoUtility.GetHierarchyPath(overlap.transform)}”已被另一组灯光池包含。";
                return;
            }

            if (roots.Any(root => root != null &&
                    (candidate == root || candidate.transform.IsChildOf(root.transform))))
            {
                _message = $"“{candidate.name}”已经位于{poolName}中。";
                return;
            }

            roots.RemoveAll(root => root == null || root.transform.IsChildOf(candidate.transform));
            roots.Add(candidate);
            roots.Sort((left, right) => string.Compare(
                BentoUtility.GetHierarchyPath(left.transform),
                BentoUtility.GetHierarchyPath(right.transform),
                StringComparison.OrdinalIgnoreCase));
            _message = $"已加入{poolName}：{BentoUtility.GetHierarchyPath(candidate.transform)}。";
        }

        static Light[] CollectLights(IEnumerable<GameObject> roots)
        {
            return (roots ?? Enumerable.Empty<GameObject>())
                .Where(root => root != null && root.scene.IsValid() && root.scene.isLoaded)
                .SelectMany(root => root.GetComponentsInChildren<Light>(true))
                .Where(light => light != null)
                .Distinct()
                .ToArray();
        }

        static void RemoveInvalidLightRoots(List<GameObject> roots)
        {
            roots.RemoveAll(root => root == null || !root.scene.IsValid() || !root.scene.isLoaded);
        }

        void OnBakeStateChanged()
        {
            Repaint();
        }

        void RefreshFromSelection()
        {
            _entries.Clear();
            _selectedRootCount = 0;
            _meshFilterCount = 0;
            _unsupportedMeshCount = 0;

            List<GameObject> roots = BentoUtility
                .GetTopLevelSelectedSceneObjects(sortByHierarchy: true);
            _selectedRootCount = roots.Count;
            var byAssetPath = new Dictionary<string, LightmapUvModelEntry>(StringComparer.OrdinalIgnoreCase);

            foreach (GameObject root in roots)
            foreach (MeshFilter meshFilter in root.GetComponentsInChildren<MeshFilter>(_includeInactive))
            {
                if (meshFilter == null || meshFilter.sharedMesh == null) continue;
                _meshFilterCount++;
                string assetPath = AssetDatabase.GetAssetPath(meshFilter.sharedMesh);
                var importer = string.IsNullOrEmpty(assetPath)
                    ? null
                    : AssetImporter.GetAtPath(assetPath) as ModelImporter;
                if (importer == null)
                {
                    _unsupportedMeshCount++;
                    continue;
                }

                if (!byAssetPath.TryGetValue(assetPath, out LightmapUvModelEntry entry))
                {
                    entry = new LightmapUvModelEntry
                    {
                        assetPath = assetPath,
                        generateLightmapUvs = importer.generateSecondaryUV
                    };
                    byAssetPath.Add(assetPath, entry);
                }

                entry.meshFilterCount++;
                entry.hierarchyPaths.Add(BentoUtility.GetHierarchyPath(meshFilter.transform));
            }

            _entries.AddRange(byAssetPath.Values
                .OrderBy(entry => entry.assetPath, StringComparer.OrdinalIgnoreCase));
        }

        static bool ConfirmChange(int count, bool enabled)
        {
            string action = enabled ? "开启" : "关闭";
            return EditorUtility.DisplayDialog(
                $"确认{action} Generate Lightmap UVs",
                $"将为 {count} 个模型资产{action} Generate Lightmap UVs，并逐个重新导入。模型较大时可能需要等待。\n\n确定继续吗？",
                action,
                "取消");
        }

        void ApplySetting(bool enabled)
        {
            LightmapUvModelEntry[] targets = _entries
                .Where(entry => entry.generateLightmapUvs != enabled)
                .ToArray();
            if (targets.Length == 0) return;

            _lastHistory.Clear();
            var applied = new List<LightmapUvImportHistoryEntry>();
            try
            {
                for (int index = 0; index < targets.Length; index++)
                {
                    LightmapUvModelEntry entry = targets[index];
                    EditorUtility.DisplayProgressBar(
                        "烘培光照",
                        $"正在{(enabled ? "开启" : "关闭")} Generate Lightmap UVs\n{entry.assetPath}",
                        (float)(index + 1) / targets.Length);
                    var importer = AssetImporter.GetAtPath(entry.assetPath) as ModelImporter ??
                                   throw new InvalidOperationException($"无法读取 ModelImporter：{entry.assetPath}");
                    var history = new LightmapUvImportHistoryEntry
                    {
                        assetPath = entry.assetPath,
                        previousValue = importer.generateSecondaryUV
                    };
                    applied.Add(history);
                    importer.generateSecondaryUV = enabled;
                    importer.SaveAndReimport();
                }

                _lastHistory.AddRange(applied);
                _message = $"完成：已为 {applied.Count} 个模型资产{(enabled ? "开启" : "关闭")} Generate Lightmap UVs。";
            }
            catch (Exception exception)
            {
                RollBack(applied);
                _message = "批处理失败，已尝试恢复本次已处理模型的原设置。";
                Debug.LogException(exception);
                EditorUtility.DisplayDialog(
                    "批处理失败",
                    exception.Message + "\n\n已尝试恢复本次已处理模型的原设置。",
                    "确定");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                RefreshFromSelection();
                Repaint();
            }
        }

        void RestoreLastSetting()
        {
            LightmapUvImportHistoryEntry[] history = _lastHistory.ToArray();
            try
            {
                for (int index = 0; index < history.Length; index++)
                {
                    LightmapUvImportHistoryEntry entry = history[index];
                    EditorUtility.DisplayProgressBar(
                        "烘培光照",
                        $"正在恢复\n{entry.assetPath}",
                        (float)(index + 1) / history.Length);
                    var importer = AssetImporter.GetAtPath(entry.assetPath) as ModelImporter ??
                                   throw new InvalidOperationException($"无法读取 ModelImporter：{entry.assetPath}");
                    importer.generateSecondaryUV = entry.previousValue;
                    importer.SaveAndReimport();
                }

                _lastHistory.Clear();
                _message = $"已恢复 {history.Length} 个模型资产的 Generate Lightmap UVs 设置。";
            }
            catch (Exception exception)
            {
                _message = "恢复上次设置失败，请查看 Console。";
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("恢复失败", exception.Message, "确定");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                RefreshFromSelection();
                Repaint();
            }
        }

        static void RollBack(IEnumerable<LightmapUvImportHistoryEntry> history)
        {
            foreach (LightmapUvImportHistoryEntry entry in history.Reverse())
            {
                try
                {
                    var importer = AssetImporter.GetAtPath(entry.assetPath) as ModelImporter;
                    if (importer == null) continue;
                    importer.generateSecondaryUV = entry.previousValue;
                    importer.SaveAndReimport();
                }
                catch (Exception rollbackException)
                {
                    Debug.LogError(
                        $"[Bento] 恢复模型 Import Settings 失败：{entry.assetPath}\n{rollbackException}");
                }
            }
        }
    }
}

using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Bento
{
    [BentoPage("prefab_replace", BentoGroups.Scene, "预制体替换", Order = 40)]
    public class PrefabReplacer : IBentoTool
    {
        static readonly string[] ModeNames = { "替换选中对象", "按名称批量替换" };
        readonly PrefabReplacementOptions _options = new PrefabReplacementOptions();
        int _mode;
        Vector2 _scroll;
        GameObject _prefab;
        string _searchName = "Default";
        bool _unlockAllScenes;

        public void Draw()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { EditorGUILayout.HelpBox("Play Mode 中禁止执行预制体替换。", MessageType.Warning); return; }
            _mode = GUILayout.Toolbar(_mode, ModeNames);
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            if (_mode == 0) DrawSelectionMode(); else DrawNameMode();
            EditorGUILayout.EndScrollView();
        }

        void DrawSelectionMode()
        {
            var selected = BentoUtility.GetTopLevelSelectedSceneObjects();
            EditorGUILayout.LabelField("替换 Hierarchy 选中对象", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("只替换当前选中对象，保留父节点和排序，支持 Ctrl+Z。", MessageType.Info);
            DrawPrefabField();
            _options.preserveName = EditorGUILayout.Toggle("保留原对象名", _options.preserveName);
            _options.preserveActiveState = EditorGUILayout.Toggle("保留激活状态", _options.preserveActiveState);
            DrawTransformOptions();
            using (new EditorGUI.DisabledScope(!PrefabReplacementService.IsValidPrefabAsset(_prefab) || selected.Count == 0))
                if (GUILayout.Button($"替换选中的 {selected.Count} 个对象", GUILayout.Height(35))) ReplaceSelection(selected);
        }

        void DrawNameMode()
        {
            EditorGUILayout.LabelField("按名称批量替换", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("当前场景支持 Ctrl+Z；所有 Build Settings 场景会保存文件，无法跨场景撤销。", MessageType.Warning);
            DrawPrefabField();
            _searchName = EditorGUILayout.TextField("搜索对象名（精确匹配）", _searchName);
            DrawTransformOptions();
            _options.deepCopyChildTransforms = EditorGUILayout.Toggle("复制子物体 Transform", _options.deepCopyChildTransforms);
            _options.deepCopyComponents = EditorGUILayout.Toggle("复制同名子物体组件数值", _options.deepCopyComponents);

            bool valid = PrefabReplacementService.IsValidPrefabAsset(_prefab) && !string.IsNullOrEmpty(_searchName);
            using (new EditorGUI.DisabledScope(!valid))
                if (GUILayout.Button("只替换当前场景（支持 Ctrl+Z）", GUILayout.Height(30))) ReplaceCurrent();

            _unlockAllScenes = EditorGUILayout.ToggleLeft("我已备份/提交场景，解锁不可撤销的全场景替换", _unlockAllScenes);
            using (new EditorGUI.DisabledScope(!valid || !_unlockAllScenes))
                if (GUILayout.Button("高风险：替换所有 Build Settings 场景", GUILayout.Height(35)) && BentoCommand.Confirm(
                        "确认全场景替换",
                        $"将在所有启用场景中替换名为“{_searchName}”的对象。此前保存的场景无法自动回滚。",
                        "确认替换")) ReplaceAll();
        }

        void DrawPrefabField()
        {
            _prefab = (GameObject)EditorGUILayout.ObjectField("替换用的 Prefab/FBX", _prefab, typeof(GameObject), false);
            if (_prefab != null && !PrefabReplacementService.IsValidPrefabAsset(_prefab))
                EditorGUILayout.HelpBox("必须选择 Project 中的 Prefab 或 FBX。", MessageType.Warning);
        }

        void DrawTransformOptions()
        {
            EditorGUILayout.LabelField("保留 Transform", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                _options.preservePositionX = EditorGUILayout.ToggleLeft("X", _options.preservePositionX, GUILayout.Width(50));
                _options.preservePositionY = EditorGUILayout.ToggleLeft("Y", _options.preservePositionY, GUILayout.Width(50));
                _options.preservePositionZ = EditorGUILayout.ToggleLeft("Z", _options.preservePositionZ, GUILayout.Width(50));
            }
            _options.preserveRotation = EditorGUILayout.Toggle("保留旋转", _options.preserveRotation);
            _options.preserveScale = EditorGUILayout.Toggle("保留缩放", _options.preserveScale);
        }

        void ReplaceSelection(System.Collections.Generic.IReadOnlyList<GameObject> selected)
        {
            PrefabReplacementResult result = null;
            if (!BentoCommand.Run("替换失败", () => result = PrefabReplacementService.ReplaceSelection(selected, _prefab, _options),
                    () => $"已替换 {result.Count} 个对象，可按 Ctrl+Z 撤销。")) return;
            Selection.objects = result.replacements.Cast<Object>().ToArray();
            SceneView.RepaintAll();
        }

        void ReplaceCurrent()
        {
            int count = 0;
            BentoCommand.Run("替换失败", () => count = PrefabReplacementService.ReplaceCurrentScene(_searchName, _prefab, _options),
                () => count > 0 ? $"当前场景已替换 {count} 个对象。" : "当前场景没有找到匹配对象。");
        }

        void ReplaceAll()
        {
            PrefabBuildSceneReplacementResult result = null;
            if (BentoCommand.Run(
                    "批量替换中断",
                    () => result = PrefabReplacementService.ReplaceBuildScenes(_searchName, _prefab, _options),
                    () => $"所有场景共替换 {result.ReplacementCount} 个对象，修改 {result.ModifiedScenes.Count} 个场景。"))
                _unlockAllScenes = false;
        }
    }
}

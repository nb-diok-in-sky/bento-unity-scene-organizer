using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Bento
{
    sealed class SelectUsedMaterialsTool
    {
        readonly List<GameObject> _lockedRoots = new List<GameObject>();
        readonly List<Material> _materials = new List<Material>();
        Vector2 _scroll;

        public void Draw()
        {
            EditorGUILayout.LabelField("检查物体使用的全部材质", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "先锁定 Hierarchy 物体或 Project 中的 Prefab/FBX，再逐个定位材质。定位单个材质后，仍可点击“重新全选”恢复整组选择。",
                MessageType.Info);

            List<GameObject> currentRoots = UsedAssetSelectionScanner.ResolveCurrentRoots();
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(currentRoots.Count == 0))
                    if (GUILayout.Button($"锁定当前物体并扫描（{currentRoots.Count}）", GUILayout.Height(32)))
                        Scan(currentRoots);
                using (new EditorGUI.DisabledScope(_lockedRoots.Count == 0))
                    if (GUILayout.Button("重新扫描已锁定物体", GUILayout.Height(32)))
                        Scan(_lockedRoots.Where(root => root != null).ToList());
            }

            if (_lockedRoots.Count == 0) return;
            EditorGUILayout.LabelField($"已锁定物体：{_lockedRoots.Count} 个　材质：{ValidMaterials().Count} 个", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(ValidMaterials().Count == 0))
                    if (GUILayout.Button("重新全选全部材质", GUILayout.Height(30))) SelectAllMaterials();
                if (GUILayout.Button("清空锁定", GUILayout.Height(30)))
                {
                    _lockedRoots.Clear();
                    _materials.Clear();
                }
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll, EditorStyles.helpBox, GUILayout.MinHeight(220), GUILayout.MaxHeight(560));
            foreach (Material material in ValidMaterials())
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.ObjectField(material, typeof(Material), false);
                string path = AssetDatabase.GetAssetPath(material);
                EditorGUILayout.LabelField(string.IsNullOrEmpty(path) ? "内嵌材质" : path, EditorStyles.miniLabel, GUILayout.MinWidth(220));
                if (GUILayout.Button("定位", GUILayout.Width(60))) FocusMaterialKeepingSelection(material);
            }
            EditorGUILayout.EndScrollView();
        }

        [MenuItem("GameObject/Bento/选中物体使用的全部材质", false, -9000)]
        [MenuItem("Assets/Bento/选中物体使用的全部材质", false, -9000)]
        internal static void SelectMaterialsFromCurrentSelection()
        {
            Material[] materials = CollectMaterials(UsedAssetSelectionScanner.ResolveCurrentRoots());
            if (materials.Length == 0)
            {
                EditorUtility.DisplayDialog("没有找到材质", "当前选择及其子物体没有引用任何材质球。", "确定");
                return;
            }
            Selection.objects = materials;
            Debug.Log($"[Bento] 已在 Project 中选中 {materials.Length} 个材质球。跨文件夹材质需要在工具清单中逐个定位查看。");
        }

        [MenuItem("GameObject/Bento/选中物体使用的全部材质", true)]
        [MenuItem("Assets/Bento/选中物体使用的全部材质", true)]
        internal static bool ValidateSelection() => UsedAssetSelectionScanner.ResolveCurrentRoots().Count > 0;

        void Scan(IReadOnlyList<GameObject> roots)
        {
            _lockedRoots.Clear();
            _lockedRoots.AddRange(roots.Where(root => root != null).Distinct());
            _materials.Clear();
            _materials.AddRange(CollectMaterials(_lockedRoots));
            SelectAllMaterials();
        }

        void SelectAllMaterials()
        {
            Material[] materials = ValidMaterials().ToArray();
            if (materials.Length > 0) Selection.objects = materials;
        }

        void FocusMaterialKeepingSelection(Material material)
        {
            Material[] all = ValidMaterials().ToArray();
            if (material == null || all.Length == 0) return;
            Selection.objects = all;
            EditorGUIUtility.PingObject(material);
        }

        List<Material> ValidMaterials() => _materials.Where(material => material != null).Distinct().ToList();

        static Material[] CollectMaterials(IEnumerable<GameObject> roots)
        {
            return UsedAssetSelectionScanner.Scan(roots).materials.ToArray();
        }
    }
}

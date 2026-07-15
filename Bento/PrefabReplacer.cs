using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditorInternal;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using System.Linq;

namespace Bento
{
public class PrefabReplacer
{
    private static readonly string[] ModeNames = { "替换选中对象", "按名称批量替换" };

    private int _mode;
    private Vector2 _scrollPosition;
    private GameObject _newPrefab;
    private bool _preserveOriginalName = true;
    private bool _preserveActiveState = true;
    private string _searchName = "Default";
    private bool _preservePosX = true;
    private bool _preservePosY = true;
    private bool _preservePosZ = true;
    private bool _preserveRotation = true;
    private bool _preserveScale = true;

    private bool _deepCopyChildTransforms = true;
    private bool _deepCopyComponents = false;
    private bool _unlockAllScenesReplace;

    public void Draw()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorGUILayout.HelpBox("Play Mode 中禁止执行预制体替换，请先退出运行模式。", MessageType.Warning);
            return;
        }

        _mode = GUILayout.Toolbar(_mode, ModeNames);
        _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
        EditorGUILayout.Space(10);

        if (_mode == 0)
            DrawSelectionReplaceSection();
        else
            DrawBatchReplaceSection();

        EditorGUILayout.EndScrollView();
    }

    private void DrawBatchReplaceSection()
    {
        EditorGUILayout.LabelField("按名称批量替换", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "遍历 Build Settings 里的所有场景，\n" +
            "找到名字匹配的根对象，替换成指定的预制体实例。\n" +
            "替换后新实例是正经的预制体链接（蓝色名字），以后改预制体会自动同步。",
            MessageType.Info);

        EditorGUILayout.Space(10);

        _newPrefab = (GameObject)EditorGUILayout.ObjectField(
            "替换用的预制体", _newPrefab, typeof(GameObject), false);
        bool hasValidPrefab = IsValidPrefabAsset(_newPrefab);
        if (_newPrefab != null && !hasValidPrefab)
            EditorGUILayout.HelpBox("替换目标必须是 Project 中的 Prefab 或 FBX 模型资产，不能是 Hierarchy 场景对象。", MessageType.Warning);

        _searchName = EditorGUILayout.TextField("搜索对象名（精确匹配）", _searchName);

        DrawTransformOptions();

        EditorGUILayout.Space(5);
        EditorGUILayout.LabelField("深度复制（按名字匹配新旧子物体）：", EditorStyles.boldLabel);
        _deepCopyChildTransforms = EditorGUILayout.Toggle("复制子物体 Transform 数值", _deepCopyChildTransforms);
        _deepCopyComponents = EditorGUILayout.Toggle("复制子物体其他组件数值", _deepCopyComponents);
        if (_deepCopyComponents)
        {
            EditorGUILayout.HelpBox(
                "会用原对象子物体的组件数值，覆盖新预制体上同名子物体的同类型组件。\n" +
                "只覆盖两边都有的组件，不会新增组件。",
                MessageType.Warning);
        }

        EditorGUILayout.Space(10);

        EditorGUILayout.HelpBox(
            "高风险：“所有场景”会依次打开并保存 Build Settings 中的场景，无法用 Ctrl+Z 跨场景回滚。默认锁定，生产中优先使用“当前场景”或“替换选中对象”。",
            MessageType.Error);
        _unlockAllScenesReplace = EditorGUILayout.ToggleLeft(
            "我已备份/提交场景，解锁不可撤销的全场景替换",
            _unlockAllScenesReplace);

        using (new EditorGUI.DisabledScope(!hasValidPrefab || string.IsNullOrEmpty(_searchName) || !_unlockAllScenesReplace))
        {
            if (GUILayout.Button("高风险：开始替换所有场景", GUILayout.Height(35)))
            {
                if (EditorUtility.DisplayDialog("确认替换",
                    $"将在所有 Build Settings 场景中，\n把名为 \"{_searchName}\" 的对象替换为 [{_newPrefab.name}]。\n\n此操作不可撤销，确认继续？",
                    "确认替换", "取消"))
                {
                    ReplaceAll();
                    _unlockAllScenesReplace = false;
                }
            }
        }

        EditorGUILayout.Space(5);
        using (new EditorGUI.DisabledScope(!hasValidPrefab || string.IsNullOrEmpty(_searchName)))
            if (GUILayout.Button("只替换当前场景（支持 Ctrl+Z）", GUILayout.Height(30)))
                ReplaceCurrent();
    }

    private void DrawSelectionReplaceSection()
    {
        var selectedObjects = Bento.BentoUtility.GetTopLevelSelectedSceneObjects();

        EditorGUILayout.LabelField("替换 Hierarchy 选中对象", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "只替换当前 Hierarchy 中选中的场景对象，不会按名字搜索，也不会修改其他场景。\n" +
            "新实例会保留原对象的父节点、Hierarchy 排序，以及下方勾选的 Transform 属性。支持 Ctrl+Z 撤销。",
            MessageType.Info);

        _newPrefab = (GameObject)EditorGUILayout.ObjectField(
            new GUIContent("替换用的预制体", "请拖入 Project 窗口中的 Prefab 资产。"),
            _newPrefab, typeof(GameObject), false);
        bool hasValidPrefab = IsValidPrefabAsset(_newPrefab);

        EditorGUILayout.LabelField($"当前选中场景对象：{selectedObjects.Count} 个");
        _preserveOriginalName = EditorGUILayout.Toggle(
            new GUIContent("保留原对象名", "替换后继续使用原场景对象的名字。"),
            _preserveOriginalName);
        _preserveActiveState = EditorGUILayout.Toggle(
            new GUIContent("保留激活状态", "替换后保持原对象的 Active 状态。"),
            _preserveActiveState);

        DrawTransformOptions();

        bool canReplaceSelection = hasValidPrefab && selectedObjects.Count > 0;
        using (new EditorGUI.DisabledScope(!canReplaceSelection))
        {
            if (GUILayout.Button($"替换选中的 {selectedObjects.Count} 个对象", GUILayout.Height(35)))
                ReplaceSelection(selectedObjects);
        }

        if (_newPrefab == null)
            EditorGUILayout.HelpBox("请先拖入替换用的 Prefab。", MessageType.Warning);
        else if (!hasValidPrefab)
            EditorGUILayout.HelpBox("替换目标必须是 Project 中的 Prefab 或 FBX 模型资产，不能是 Hierarchy 场景对象。", MessageType.Warning);
        else if (selectedObjects.Count == 0)
            EditorGUILayout.HelpBox("请在 Hierarchy 中选择至少一个场景对象。", MessageType.Warning);
    }

    private static bool IsValidPrefabAsset(GameObject gameObject)
    {
        if (gameObject == null) return false;
        string assetPath = AssetDatabase.GetAssetPath(gameObject);
        return !string.IsNullOrEmpty(assetPath) &&
               PrefabUtility.GetPrefabAssetType(gameObject) != PrefabAssetType.NotAPrefab;
    }

    private void DrawTransformOptions()
    {
        EditorGUILayout.Space(5);
        EditorGUILayout.LabelField("保留原对象的哪些 Transform 属性：", EditorStyles.boldLabel);

        EditorGUILayout.LabelField("保留位置（按轴勾选，不勾的轴使用新预制体的值）：");
        EditorGUILayout.BeginHorizontal();
        _preservePosX = EditorGUILayout.ToggleLeft("X", _preservePosX, GUILayout.Width(50));
        _preservePosY = EditorGUILayout.ToggleLeft("Y", _preservePosY, GUILayout.Width(50));
        _preservePosZ = EditorGUILayout.ToggleLeft("Z", _preservePosZ, GUILayout.Width(50));
        EditorGUILayout.EndHorizontal();

        _preserveRotation = EditorGUILayout.Toggle("保留旋转", _preserveRotation);
        _preserveScale = EditorGUILayout.Toggle("保留缩放", _preserveScale);
    }

    private void ReplaceSelection(List<GameObject> selectedObjects)
    {
        if (_newPrefab == null || selectedObjects == null || selectedObjects.Count == 0)
            return;

        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("替换选中的预制体");

        var replacements = new List<GameObject>(selectedObjects.Count);
        var failedObjects = new List<string>();
        foreach (var oldObject in selectedObjects)
        {
            if (oldObject == null) continue;

            var oldTransform = oldObject.transform;
            var parent = oldTransform.parent;
            int siblingIndex = oldTransform.GetSiblingIndex();
            Vector3 localPosition = oldTransform.localPosition;
            Quaternion localRotation = oldTransform.localRotation;
            Vector3 localScale = oldTransform.localScale;
            string originalName = oldObject.name;
            bool originalActiveState = oldObject.activeSelf;

            var newObject = PrefabUtility.InstantiatePrefab(_newPrefab, oldObject.scene) as GameObject;
            if (newObject == null)
            {
                Debug.LogError("[PrefabReplacer] InstantiatePrefab 失败，请确认拖入的是 Project 里的预制体");
                failedObjects.Add(originalName);
                continue;
            }

            Undo.RegisterCreatedObjectUndo(newObject, "创建替换预制体");
            Undo.SetTransformParent(newObject.transform, parent, "设置替换预制体父节点");
            Undo.RecordObject(newObject.transform, "应用原对象 Transform");
            newObject.transform.SetSiblingIndex(siblingIndex);

            ApplyRootTransform(newObject.transform, localPosition, localRotation, localScale);
            if (_preserveOriginalName)
            {
                Undo.RecordObject(newObject, "保留原对象名");
                newObject.name = originalName;
            }
            if (_preserveActiveState)
            {
                Undo.RecordObject(newObject, "保留原对象激活状态");
                newObject.SetActive(originalActiveState);
            }

            Undo.DestroyObjectImmediate(oldObject);
            replacements.Add(newObject);
            EditorSceneManager.MarkSceneDirty(newObject.scene);
        }

        Undo.CollapseUndoOperations(undoGroup);
        Selection.objects = replacements.Cast<Object>().ToArray();
        SceneView.RepaintAll();

        if (replacements.Count > 0)
        {
            Debug.Log($"[PrefabReplacer] 已替换 {replacements.Count} 个选中对象，可按 Ctrl+Z 撤销");
            EditorUtility.DisplayDialog(
                "替换完成",
                $"已替换 {replacements.Count} 个对象。\n可以按 Ctrl+Z 撤销。" +
                (failedObjects.Count > 0 ? $"\n\n失败：{string.Join("、", failedObjects)}" : string.Empty),
                "确定");
        }
        else
        {
            EditorUtility.DisplayDialog(
                "替换失败",
                "没有对象被替换。请确认：\n1. 在 Hierarchy 中选择的是场景对象；\n2. 拖入的是 Project 中的 Prefab 资产。",
                "确定");
        }
    }

    private void ApplyRootTransform(
        Transform target,
        Vector3 originalLocalPosition,
        Quaternion originalLocalRotation,
        Vector3 originalLocalScale)
    {
        Vector3 mergedPosition = target.localPosition;
        if (_preservePosX) mergedPosition.x = originalLocalPosition.x;
        if (_preservePosY) mergedPosition.y = originalLocalPosition.y;
        if (_preservePosZ) mergedPosition.z = originalLocalPosition.z;
        target.localPosition = mergedPosition;

        if (_preserveRotation) target.localRotation = originalLocalRotation;
        if (_preserveScale) target.localScale = originalLocalScale;
    }

    private void ReplaceAll()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        var currentScene = SceneManager.GetActiveScene().path;
        var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).ToArray();
        int totalReplaced = 0;

        try
        {
            foreach (var entry in scenes)
            {
                if (!System.IO.File.Exists(entry.path))
                {
                    Debug.LogWarning($"[PrefabReplacer] 跳过不存在的场景: {entry.path}");
                    continue;
                }

                var scene = EditorSceneManager.OpenScene(entry.path, OpenSceneMode.Single);
                int count = ReplaceInScene(scene, false);
                totalReplaced += count;

                if (count > 0)
                {
                    EditorSceneManager.MarkSceneDirty(scene);
                    if (!EditorSceneManager.SaveScene(scene))
                        throw new System.InvalidOperationException($"场景保存失败：{entry.path}");
                    Debug.Log($"[PrefabReplacer] {entry.path} → 替换了 {count} 个");
                }
            }

            Debug.Log($"[PrefabReplacer] 全部完成，共替换 {totalReplaced} 个对象");
            EditorUtility.DisplayDialog("批量替换完成", $"共替换 {totalReplaced} 个对象。", "确定");
        }
        catch (System.Exception exception)
        {
            Debug.LogException(exception);
            EditorUtility.DisplayDialog(
                "批量替换中断",
                "处理过程中发生错误，已停止继续处理。此前成功保存的场景不会自动回滚，请查看 Console。",
                "确定");
        }
        finally
        {
            if (!string.IsNullOrEmpty(currentScene) && System.IO.File.Exists(currentScene))
                EditorSceneManager.OpenScene(currentScene, OpenSceneMode.Single);
        }
    }

    private void ReplaceCurrent()
    {
        var scene = SceneManager.GetActiveScene();
        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("按名称替换当前场景对象");
        int count = ReplaceInScene(scene, true);
        Undo.CollapseUndoOperations(undoGroup);
        if (count > 0)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log($"[PrefabReplacer] 当前场景替换了 {count} 个（记得 Ctrl+S 保存）");
        }
        else
        {
            Debug.Log($"[PrefabReplacer] 当前场景没找到名为 \"{_searchName}\" 的对象");
        }
    }

    private int ReplaceInScene(Scene scene, bool useUndo)
    {
        // 先递归收集所有匹配对象，避免边遍历边销毁导致漏掉嵌套对象
        var matches = new List<Transform>();
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.gameObject.name == _searchName)
                    matches.Add(t);
            }
        }

        int count = 0;
        foreach (var old in matches)
        {
            if (old == null) continue;

            // 记录原对象的层级和 Transform
            var parent = old.parent;
            int siblingIndex = old.GetSiblingIndex();
            var localPos = old.localPosition;
            var localRot = old.localRotation;
            var localScale = old.localScale;

            var newObj = (GameObject)PrefabUtility.InstantiatePrefab(_newPrefab, scene);
            if (newObj == null)
            {
                Debug.LogError("[PrefabReplacer] InstantiatePrefab 失败，请确认拖入的是 Project 里的预制体");
                return count;
            }

            // 挂到原对象同一个父物体下、同一个排序位置
            if (useUndo)
            {
                Undo.RegisterCreatedObjectUndo(newObj, "创建替换预制体");
                Undo.SetTransformParent(newObj.transform, parent, "设置替换预制体父节点");
                Undo.RecordObject(newObj.transform, "应用原对象 Transform");
            }
            else
            {
                newObj.transform.SetParent(parent, false);
            }
            newObj.transform.SetSiblingIndex(siblingIndex);

            // 在父物体本地坐标系下保留，避免父物体被缩放/旋转时错位
            // 位置按轴拼：勾上的轴用原对象的值，没勾的轴保留新预制体自带的值
            Vector3 mergedPos = newObj.transform.localPosition;
            if (_preservePosX) mergedPos.x = localPos.x;
            if (_preservePosY) mergedPos.y = localPos.y;
            if (_preservePosZ) mergedPos.z = localPos.z;
            newObj.transform.localPosition = mergedPos;

            if (_preserveRotation) newObj.transform.localRotation = localRot;
            if (_preserveScale) newObj.transform.localScale = localScale;

            // 深度复制：按名字把旧对象每个子物体的数值搬到新预制体对应子物体上
            if (_deepCopyChildTransforms || _deepCopyComponents)
                DeepCopy(old, newObj.transform);

            if (useUndo)
                Undo.DestroyObjectImmediate(old.gameObject);
            else
                Object.DestroyImmediate(old.gameObject);

            count++;
        }

        return count;
    }

    // 按相对路径（名字）匹配新旧两个层级，把旧的子物体数值复制到新的对应子物体上。
    // 跳过根节点本身（根节点由上面的「保留位置/旋转/缩放」处理）。
    private void DeepCopy(Transform src, Transform dst)
    {
        var dstMap = new Dictionary<string, Transform>();
        foreach (var t in dst.GetComponentsInChildren<Transform>(true))
        {
            string path = RelativePath(dst, t);
            if (!dstMap.ContainsKey(path)) dstMap[path] = t; // 同名同层只取第一个
        }

        foreach (var srcT in src.GetComponentsInChildren<Transform>(true))
        {
            if (srcT == src) continue; // 根节点不在这里处理

            string path = RelativePath(src, srcT);
            if (!dstMap.TryGetValue(path, out var dstT)) continue;

            if (_deepCopyChildTransforms)
            {
                // 子物体也遵守同一套按轴勾选，和根节点行为一致
                Vector3 mp = dstT.localPosition;
                if (_preservePosX) mp.x = srcT.localPosition.x;
                if (_preservePosY) mp.y = srcT.localPosition.y;
                if (_preservePosZ) mp.z = srcT.localPosition.z;
                dstT.localPosition = mp;

                if (_preserveRotation) dstT.localRotation = srcT.localRotation;
                if (_preserveScale) dstT.localScale = srcT.localScale;
            }

            if (_deepCopyComponents)
                CopyComponentValues(srcT.gameObject, dstT.gameObject);
        }
    }

    // 计算 t 相对于 root 的路径，如 "Body/LeftArm"；root 自身返回 ""
    private static string RelativePath(Transform root, Transform t)
    {
        if (t == root) return "";
        string path = t.name;
        var p = t.parent;
        while (p != null && p != root)
        {
            path = p.name + "/" + path;
            p = p.parent;
        }
        return path;
    }

    // 把 from 上每个组件的数值，粘贴到 to 上同类型的组件（只覆盖已存在的，不新增）
    private static void CopyComponentValues(GameObject from, GameObject to)
    {
        foreach (var comp in from.GetComponents<Component>())
        {
            if (comp == null) continue;
            if (comp is Transform) continue; // Transform 已在 DeepCopy 里单独处理

            var target = to.GetComponent(comp.GetType());
            if (target == null) continue;

            if (ComponentUtility.CopyComponent(comp))
                ComponentUtility.PasteComponentValues(target);
        }
    }
}
}

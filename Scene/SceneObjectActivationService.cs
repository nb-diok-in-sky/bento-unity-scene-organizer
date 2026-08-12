using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Bento
{
    static class SceneObjectActivationService
    {
        public static GameObject[] GetSelectedSceneObjects()
        {
            return Selection.gameObjects
                .Where(gameObject =>
                    gameObject != null &&
                    gameObject.scene.IsValid() &&
                    gameObject.scene.isLoaded)
                .Distinct()
                .ToArray();
        }

        public static int SetSelectedActive(bool active)
        {
            GameObject[] changed = GetSelectedSceneObjects()
                .Where(gameObject => gameObject.activeSelf != active)
                .ToArray();
            if (changed.Length == 0) return 0;

            int undoGroup = Undo.GetCurrentGroup();
            string undoName = active ? "Bento 启用选中对象" : "Bento 禁用选中对象";
            Undo.SetCurrentGroupName(undoName);
            Undo.RecordObjects(changed, undoName);
            foreach (GameObject gameObject in changed)
            {
                gameObject.SetActive(active);
                EditorUtility.SetDirty(gameObject);
            }

            Undo.CollapseUndoOperations(undoGroup);
            return changed.Length;
        }

        public static int ToggleSelectedActive()
        {
            GameObject[] selected = GetSelectedSceneObjects();
            if (selected.Length == 0) return 0;

            // 混合状态时统一启用；全部启用时统一禁用，避免多选结果更混乱。
            return SetSelectedActive(selected.Any(gameObject => !gameObject.activeSelf));
        }
    }
}

using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Bento
{
    static class SceneLodGroupBatchService
    {
        public static LODGroup[] GetSelectedLodGroups()
        {
            return SceneObjectActivationService.GetSelectedSceneObjects()
                .SelectMany(gameObject => gameObject.GetComponentsInChildren<LODGroup>(true))
                .Where(lodGroup => lodGroup != null)
                .Distinct()
                .ToArray();
        }

        public static int SetSelectedLodEnabled(bool enabled)
        {
            LODGroup[] changed = GetSelectedLodGroups()
                .Where(lodGroup => lodGroup.enabled != enabled)
                .ToArray();
            if (changed.Length == 0) return 0;

            int undoGroup = Undo.GetCurrentGroup();
            string undoName = enabled
                ? "Bento 重新启用选中对象 LOD"
                : "Bento 禁用选中对象 LOD 并固定 LOD0";
            Undo.SetCurrentGroupName(undoName);
            Undo.RecordObjects(changed, undoName);
            foreach (LODGroup lodGroup in changed)
            {
                if (!enabled)
                    lodGroup.ForceLOD(0);
                else
                    lodGroup.ForceLOD(-1);
                lodGroup.enabled = enabled;
                EditorUtility.SetDirty(lodGroup);
            }

            Undo.CollapseUndoOperations(undoGroup);
            return changed.Length;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Bento
{
    public sealed class SceneLightBatchPlan
    {
        public readonly List<GameObject> roots = new List<GameObject>();
        public readonly List<Light> lights = new List<Light>();
        public int noneCount;
        public int hardCount;
        public int softCount;
        public int directLightCount;
        public int childLightCount;
    }

    public static class SceneLightBatchService
    {
        public static SceneLightBatchPlan BuildPlan(IEnumerable<GameObject> roots)
        {
            var plan = new SceneLightBatchPlan();
            if (roots == null) return plan;
            plan.roots.AddRange(roots.Where(root => root != null && root.scene.IsValid() && root.scene.isLoaded));
            var seen = new HashSet<Light>();
            foreach (GameObject root in plan.roots)
            foreach (Light light in root.GetComponentsInChildren<Light>(true))
            {
                if (light == null || !seen.Add(light)) continue;
                plan.lights.Add(light);
                if (light.gameObject == root) plan.directLightCount++;
                else plan.childLightCount++;
                switch (light.shadows)
                {
                    case LightShadows.Hard: plan.hardCount++; break;
                    case LightShadows.Soft: plan.softCount++; break;
                    default: plan.noneCount++; break;
                }
            }
            return plan;
        }

        public static int ApplyShadows(SceneLightBatchPlan plan, LightShadows shadows)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            var changed = plan.lights.Where(light => light != null && light.shadows != shadows).ToArray();
            if (changed.Length == 0) return 0;

            Undo.SetCurrentGroupName("Bento 批量修改光源阴影");
            int undoGroup = Undo.GetCurrentGroup();
            Undo.RecordObjects(changed, "Bento 批量修改光源阴影");
            foreach (Light light in changed)
            {
                light.shadows = shadows;
                EditorUtility.SetDirty(light);
                if (light.gameObject.scene.IsValid()) EditorSceneManager.MarkSceneDirty(light.gameObject.scene);
            }
            Undo.CollapseUndoOperations(undoGroup);
            return changed.Length;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Bento
{
    public enum RendererShadowReceiveOption { NoChange, Disabled, Enabled }

    public sealed class SceneRendererShadowPlan
    {
        public readonly List<GameObject> roots = new List<GameObject>();
        public readonly List<Renderer> renderers = new List<Renderer>();
        public int directRendererCount;
        public int childRendererCount;
        public int castingOffCount;
        public int castingOnCount;
        public int receiveOffCount;
        public int receiveOnCount;
    }

    public static class SceneRendererShadowBatchService
    {
        public static SceneRendererShadowPlan BuildPlan(IEnumerable<GameObject> roots)
        {
            var plan = new SceneRendererShadowPlan();
            if (roots == null) return plan;
            plan.roots.AddRange(roots.Where(root => root != null && root.scene.IsValid() && root.scene.isLoaded));
            var seen = new HashSet<Renderer>();
            foreach (GameObject root in plan.roots)
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null || !seen.Add(renderer)) continue;
                plan.renderers.Add(renderer);
                if (renderer.gameObject == root) plan.directRendererCount++;
                else plan.childRendererCount++;
                if (renderer.shadowCastingMode == ShadowCastingMode.Off) plan.castingOffCount++;
                else plan.castingOnCount++;
                if (renderer.receiveShadows) plan.receiveOnCount++;
                else plan.receiveOffCount++;
            }
            return plan;
        }

        public static int CountChanges(SceneRendererShadowPlan plan, ShadowCastingMode? casting, RendererShadowReceiveOption receive)
        {
            if (plan == null) return 0;
            return plan.renderers.Count(renderer => renderer != null && NeedsChange(renderer, casting, receive));
        }

        public static int Apply(SceneRendererShadowPlan plan, ShadowCastingMode? casting, RendererShadowReceiveOption receive)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            ValidatePlan(plan);
            if (!casting.HasValue && receive == RendererShadowReceiveOption.NoChange)
                throw new InvalidOperationException("投射阴影和接收阴影都设置为不修改。");
            var changed = plan.renderers.Where(renderer => renderer != null && NeedsChange(renderer, casting, receive)).ToArray();
            if (changed.Length == 0) return 0;

            using (var undo = new BentoUndoScope("Bento 批量修改 Renderer 阴影"))
            {
                Undo.RecordObjects(changed, "Bento 批量修改 Renderer 阴影");
                foreach (Renderer renderer in changed)
                {
                    if (casting.HasValue) renderer.shadowCastingMode = casting.Value;
                    if (receive != RendererShadowReceiveOption.NoChange)
                        renderer.receiveShadows = receive == RendererShadowReceiveOption.Enabled;
                    EditorUtility.SetDirty(renderer);
                    if (renderer.gameObject.scene.IsValid()) EditorSceneManager.MarkSceneDirty(renderer.gameObject.scene);
                }
                undo.Complete();
            }
            return changed.Length;
        }

        static void ValidatePlan(SceneRendererShadowPlan plan)
        {
            if (plan.renderers.Any(renderer => renderer == null ||
                !plan.roots.Any(root => root != null &&
                    (renderer.transform == root.transform || renderer.transform.IsChildOf(root.transform)))))
                throw new InvalidOperationException("场景对象已变化，请重新选择或刷新计划后再执行。");
        }

        static bool NeedsChange(Renderer renderer, ShadowCastingMode? casting, RendererShadowReceiveOption receive)
        {
            if (casting.HasValue && renderer.shadowCastingMode != casting.Value) return true;
            if (receive == RendererShadowReceiveOption.Enabled && !renderer.receiveShadows) return true;
            return receive == RendererShadowReceiveOption.Disabled && renderer.receiveShadows;
        }
    }
}

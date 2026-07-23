using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Bento
{
    public enum SceneMaterialReplaceMode { AllSlots, MatchingMaterial }

    public sealed class SceneMaterialReplacementPlan
    {
        public readonly List<GameObject> roots = new List<GameObject>();
        public readonly List<Renderer> renderers = new List<Renderer>();
        public int slotCount;
    }

    public static class SceneMaterialReplacementService
    {
        public static SceneMaterialReplacementPlan BuildPlan(IEnumerable<GameObject> roots, SceneMaterialReplaceMode mode, Material sourceMaterial)
        {
            var plan = new SceneMaterialReplacementPlan();
            if (roots == null) return plan;
            plan.roots.AddRange(roots.Where(root => root != null && root.scene.IsValid() && root.scene.isLoaded));
            var seen = new HashSet<Renderer>();
            foreach (GameObject root in plan.roots)
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null || !seen.Add(renderer)) continue;
                int matches = CountSlots(renderer.sharedMaterials, mode, sourceMaterial);
                if (matches == 0) continue;
                plan.renderers.Add(renderer);
                plan.slotCount += matches;
            }
            return plan;
        }

        public static int Execute(SceneMaterialReplacementPlan plan, Material replacement, SceneMaterialReplaceMode mode, Material sourceMaterial)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            if (replacement == null || string.IsNullOrEmpty(AssetDatabase.GetAssetPath(replacement)))
                throw new InvalidOperationException("请选择 Project 中的替换材质球。");
            if (mode == SceneMaterialReplaceMode.MatchingMaterial && sourceMaterial == null)
                throw new InvalidOperationException("按旧材质替换时必须指定旧材质球。");

            Undo.SetCurrentGroupName("Bento 场景材质替换");
            int undoGroup = Undo.GetCurrentGroup();
            int changedSlots = 0;
            foreach (Renderer renderer in plan.renderers)
            {
                if (renderer == null) continue;
                Material[] materials = renderer.sharedMaterials;
                Material[] replacedMaterials = (Material[])materials.Clone();
                bool changed = false;
                for (int index = 0; index < replacedMaterials.Length; index++)
                {
                    if (mode == SceneMaterialReplaceMode.MatchingMaterial && replacedMaterials[index] != sourceMaterial) continue;
                    if (replacedMaterials[index] == replacement) continue;
                    replacedMaterials[index] = replacement;
                    changed = true;
                    changedSlots++;
                }
                if (!changed) continue;
                Undo.RecordObject(renderer, "Bento 场景材质替换");
                renderer.sharedMaterials = replacedMaterials;
                EditorUtility.SetDirty(renderer);
                if (renderer.gameObject.scene.IsValid()) EditorSceneManager.MarkSceneDirty(renderer.gameObject.scene);
            }
            Undo.CollapseUndoOperations(undoGroup);
            return changedSlots;
        }

        static int CountSlots(IReadOnlyList<Material> materials, SceneMaterialReplaceMode mode, Material sourceMaterial)
        {
            if (materials == null) return 0;
            if (mode == SceneMaterialReplaceMode.AllSlots) return materials.Count;
            return sourceMaterial == null ? 0 : materials.Count(material => material == sourceMaterial);
        }
    }
}

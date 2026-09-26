using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Bento
{
    enum SceneMaterialReplaceMode { AllSlots, MatchingMaterial }
    enum SceneMaterialReplaceScope { SelectedHierarchy, LoadedScenes }

    sealed class SceneMaterialReplacementPlan
    {
        public SceneMaterialReplaceScope scope;
        public readonly List<GameObject> roots = new List<GameObject>();
        public readonly List<Renderer> renderers = new List<Renderer>();
        public int slotCount;
    }

    static class SceneMaterialReplacementService
    {
        public static SceneMaterialReplacementPlan BuildPlan(IEnumerable<GameObject> roots, SceneMaterialReplaceMode mode, Material sourceMaterial)
        {
            var plan = new SceneMaterialReplacementPlan
            {
                scope = SceneMaterialReplaceScope.SelectedHierarchy
            };
            if (roots == null) return plan;
            plan.roots.AddRange(roots.Where(root => root != null && root.scene.IsValid() && root.scene.isLoaded));
            AddMatchingRenderers(plan, mode, sourceMaterial);
            return plan;
        }

        public static SceneMaterialReplacementPlan BuildLoadedScenesPlan(Material sourceMaterial)
        {
            var plan = new SceneMaterialReplacementPlan
            {
                scope = SceneMaterialReplaceScope.LoadedScenes
            };
            for (int index = 0; index < SceneManager.sceneCount; index++)
            {
                Scene scene = SceneManager.GetSceneAt(index);
                if (!scene.IsValid() || !scene.isLoaded) continue;
                plan.roots.AddRange(scene.GetRootGameObjects());
            }

            AddMatchingRenderers(plan, SceneMaterialReplaceMode.MatchingMaterial, sourceMaterial);
            return plan;
        }

        static void AddMatchingRenderers(
            SceneMaterialReplacementPlan plan,
            SceneMaterialReplaceMode mode,
            Material sourceMaterial)
        {
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
        }

        public static int Execute(SceneMaterialReplacementPlan plan, Material replacement, SceneMaterialReplaceMode mode, Material sourceMaterial)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            ValidatePlan(plan);
            if (replacement == null || string.IsNullOrEmpty(AssetDatabase.GetAssetPath(replacement)))
                throw new InvalidOperationException("请选择 Project 中的替换材质球。");
            if (mode == SceneMaterialReplaceMode.MatchingMaterial && sourceMaterial == null)
                throw new InvalidOperationException("按旧材质替换时必须指定旧材质球。");

            int changedSlots = 0;
            using (var undo = new BentoUndoScope("Bento 场景材质替换"))
            {
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
                undo.Complete();
            }
            return changedSlots;
        }

        static void ValidatePlan(SceneMaterialReplacementPlan plan)
        {
            if (plan.renderers.Any(renderer =>
                    renderer == null ||
                    !renderer.gameObject.scene.IsValid() ||
                    !renderer.gameObject.scene.isLoaded ||
                    !plan.roots.Any(root => root != null &&
                        (renderer.transform == root.transform ||
                         renderer.transform.IsChildOf(root.transform)))))
                throw new InvalidOperationException("场景对象已变化，请重新选择或刷新计划后再执行。");
        }

        static int CountSlots(IReadOnlyList<Material> materials, SceneMaterialReplaceMode mode, Material sourceMaterial)
        {
            if (materials == null) return 0;
            if (mode == SceneMaterialReplaceMode.AllSlots) return materials.Count;
            return sourceMaterial == null ? 0 : materials.Count(material => material == sourceMaterial);
        }
    }
}

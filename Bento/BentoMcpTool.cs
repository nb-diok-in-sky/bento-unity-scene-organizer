using System;
using System.Linq;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Tools;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Bento
{
    [McpForUnityTool(
        "bento",
        Description = "Bento Unity production tools: inspect Hierarchy selection, open the Bento toolbox, switch toolbox pages, and run a read-only resource-variant analysis.",
        AutoRegister = true,
        Group = "core")]
    public static class BentoMcpTool
    {
        public static object HandleCommand(JObject parameters)
        {
            string action = parameters?["action"]?.ToString()?.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(action))
                return new ErrorResponse("action is required");

            try
            {
                switch (action)
                {
                    case "get_selection":
                        return GetSelection();
                    case "open_toolbox":
                        return OpenToolbox(parameters?["page"]?.ToString());
                    case "analyze_resource_variant":
                        return AnalyzeResourceVariant(parameters);
                    case "get_project_selection":
                        return GetProjectSelection();
                    case "analyze_asset_move":
                        return AnalyzeAssetMove(parameters);
                    case "inspect_loaded_scenes":
                        return InspectLoadedScenes();
                    case "validate_loaded_scenes":
                        return ValidateLoadedScenes();
                    case "ping":
                        return new SuccessResponse("pong", new { tool = "bento" });
                    default:
                        return new ErrorResponse($"Unknown action: {action}. Supported: ping, get_selection, get_project_selection, open_toolbox, analyze_resource_variant, analyze_asset_move, inspect_loaded_scenes, validate_loaded_scenes");
                }
            }
            catch (Exception exception)
            {
                return new ErrorResponse(exception.Message, new { stackTrace = exception.StackTrace });
            }
        }

        static object GetSelection()
        {
            var selectedObjects = BentoUtility.GetTopLevelSelectedSceneObjects(sortByHierarchy: true);
            return new SuccessResponse("Hierarchy selection read.", new
            {
                count = selectedObjects.Count,
                objects = selectedObjects.Select(gameObject => new
                {
                    name = gameObject.name,
                    scene = gameObject.scene.path,
                    hierarchy_path = GetHierarchyPath(gameObject.transform),
                    prefab_asset_path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(gameObject),
                    renderer_count = gameObject.GetComponentsInChildren<UnityEngine.Renderer>(true).Length
                }).ToArray()
            });
        }

        static object OpenToolbox(string page)
        {
            var window = BentoWindow.OpenPage(page);
            return new SuccessResponse("Bento toolbox opened.", new
            {
                page = window.CurrentPageName
            });
        }

        static object AnalyzeResourceVariant(JObject parameters)
        {
            var window = BentoWindow.OpenPage("资源变体");
            var naming = ReadNamingOptions(parameters);
            var summary = window.AnalyzeResourceVariantSelection(naming);
            return new SuccessResponse("Resource variant dry-run completed.", summary);
        }

        static object GetProjectSelection()
        {
            var assets = Selection.objects
                .Where(asset => asset != null)
                .Select(asset => new
                {
                    name = asset.name,
                    type = asset.GetType().FullName,
                    asset_path = AssetDatabase.GetAssetPath(asset),
                    guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(asset))
                })
                .Where(asset => !string.IsNullOrEmpty(asset.asset_path))
                .ToArray();
            return new SuccessResponse("Project selection read.", new { count = assets.Length, assets });
        }

        static object AnalyzeAssetMove(JObject parameters)
        {
            var window = BentoWindow.OpenPage("资产移动");
            var naming = ReadNamingOptions(parameters);
            return new SuccessResponse(
                "Asset move plan analyzed. Execution still requires Unity UI confirmation.",
                window.AnalyzeAssetMoveSelection(
                    parameters?["prefab_folder"]?.ToString(),
                    parameters?["material_folder"]?.ToString(),
                    parameters?["texture_folder"]?.ToString(),
                    naming));
        }

        static object InspectLoadedScenes()
        {
            var scenes = Enumerable.Range(0, SceneManager.sceneCount)
                .Select(SceneManager.GetSceneAt)
                .Where(scene => scene.IsValid() && scene.isLoaded)
                .Select(scene => new
                {
                    name = scene.name,
                    path = scene.path,
                    is_dirty = scene.isDirty,
                    root_count = scene.rootCount,
                    object_count = scene.GetRootGameObjects().Sum(root => root.GetComponentsInChildren<Transform>(true).Length),
                    renderer_count = scene.GetRootGameObjects().Sum(root => root.GetComponentsInChildren<Renderer>(true).Length)
                })
                .ToArray();
            return new SuccessResponse("Loaded scenes inspected without modification.", new { count = scenes.Length, scenes });
        }

        static object ValidateLoadedScenes()
        {
            int missingScripts = 0;
            int missingMaterials = 0;
            int missingShaders = 0;
            int disconnectedPrefabInstances = 0;
            var samples = new System.Collections.Generic.List<string>();

            foreach (var gameObject in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (gameObject == null || !gameObject.scene.IsValid() || !gameObject.scene.isLoaded) continue;
                int objectMissingScripts = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(gameObject);
                if (objectMissingScripts > 0)
                {
                    missingScripts += objectMissingScripts;
                    AddSample(samples, gameObject, $"missing_scripts={objectMissingScripts}");
                }

                var renderer = gameObject.GetComponent<Renderer>();
                if (renderer != null)
                {
                    foreach (var material in renderer.sharedMaterials)
                    {
                        if (material == null)
                        {
                            missingMaterials++;
                            AddSample(samples, gameObject, "missing_material");
                        }
                        else if (material.shader == null)
                        {
                            missingShaders++;
                            AddSample(samples, gameObject, $"missing_shader={material.name}");
                        }
                    }
                }

                if (PrefabUtility.GetPrefabInstanceStatus(gameObject) == PrefabInstanceStatus.Disconnected)
                {
                    disconnectedPrefabInstances++;
                    AddSample(samples, gameObject, "disconnected_prefab");
                }
            }

            return new SuccessResponse("Loaded scenes validated without modification.", new
            {
                healthy = missingScripts == 0 && missingMaterials == 0 && missingShaders == 0 && disconnectedPrefabInstances == 0,
                missing_scripts = missingScripts,
                missing_material_slots = missingMaterials,
                missing_shaders = missingShaders,
                disconnected_prefab_objects = disconnectedPrefabInstances,
                samples = samples.ToArray()
            });
        }

        static void AddSample(System.Collections.Generic.List<string> samples, GameObject gameObject, string issue)
        {
            if (samples.Count >= 40) return;
            samples.Add($"{gameObject.scene.path} :: {GetHierarchyPath(gameObject.transform)} :: {issue}");
        }

        static AssetNamingOptions ReadNamingOptions(JObject parameters)
        {
            return new AssetNamingOptions
            {
                prefix = parameters?["prefix"]?.ToString() ?? string.Empty,
                baseName = parameters?["base_name"]?.ToString() ?? string.Empty,
                suffix = parameters?["suffix"]?.ToString() ?? string.Empty,
                appendNumber = parameters?["append_number"]?.Value<bool?>() ?? false,
                startNumber = parameters?["start_number"]?.Value<int?>() ?? 1,
                numberDigits = parameters?["number_digits"]?.Value<int?>() ?? 2
            };
        }

        static string GetHierarchyPath(UnityEngine.Transform transform)
        {
            string path = transform.name;
            for (var parent = transform.parent; parent != null; parent = parent.parent)
                path = parent.name + "/" + path;
            return path;
        }
    }
}

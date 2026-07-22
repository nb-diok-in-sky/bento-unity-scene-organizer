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
                    case "analyze_ai_asset_organizer":
                        return AnalyzeAIAssetOrganizer(parameters);
                    case "analyze_selected_asset_info":
                        return AnalyzeSelectedAssetInfo();
                    case "analyze_selected_prefab_layers":
                        return AnalyzeSelectedPrefabLayers();
                    case "analyze_project_asset_rename":
                        return new SuccessResponse("Project asset rename plan analyzed without modification.", McpAnalysisFacade.AnalyzeProjectAssetRename(ReadNamingOptions(parameters)));
                    case "inspect_loaded_scenes":
                        return InspectLoadedScenes();
                    case "get_loaded_scene_hierarchy":
                        return GetLoadedSceneHierarchy();
                    case "analyze_hierarchy_organization":
                        return AnalyzeHierarchyOrganization();
                    case "prepare_hierarchy_organization_draft":
                        return PrepareHierarchyOrganizationDraft(parameters);
                    case "validate_loaded_scenes":
                        return ValidateLoadedScenes();
                    case "ping":
                        return new SuccessResponse("pong", new { tool = "bento" });
                    default:
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
            var naming = ReadNamingOptions(parameters);
            return new SuccessResponse("Resource variant dry-run completed.", McpAnalysisFacade.AnalyzeResourceVariant(naming));
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
            var naming = ReadNamingOptions(parameters);
            return new SuccessResponse(
                "Asset move plan analyzed. Execution still requires Unity UI confirmation.",
                McpAnalysisFacade.AnalyzeAssetMove(
                    parameters?["prefab_folder"]?.ToString(),
                    parameters?["material_folder"]?.ToString(),
                    parameters?["texture_folder"]?.ToString(),
                    naming));
        }

        static object AnalyzeAIAssetOrganizer(JObject parameters)
        {
            return new SuccessResponse(
                "AI asset organizer plan analyzed. Execution requires explicit confirmation in the Unity UI.",
                McpAnalysisFacade.AnalyzeAssetOrganizer(new AIAssetOrganizerConfig
                {
                    sourceRoot = parameters?["source_root"]?.ToString(),
                    prefabFolder = parameters?["prefab_folder"]?.ToString(),
                    materialFolder = parameters?["material_folder"]?.ToString(),
                    textureFolder = parameters?["texture_folder"]?.ToString(),
                    otherFolder = parameters?["other_folder"]?.ToString(),
                    includeDependencies = parameters?["include_dependencies"]?.Value<bool?>() ?? true
                }));
        }

        static object AnalyzeSelectedAssetInfo()
        {
            return new SuccessResponse(
                "Selected Project assets analyzed without modification.",
                McpAnalysisFacade.AnalyzeSelectedAssetInfo());
        }

        static object AnalyzeSelectedPrefabLayers()
        {
            return new SuccessResponse(
                "Selected prefab layers analyzed. Applying changes requires explicit confirmation in the Unity UI.",
                McpAnalysisFacade.AnalyzeSelectedPrefabLayers());
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

        static object GetLoadedSceneHierarchy()
        {
            var snapshot = SceneHierarchySnapshotService.CaptureLoadedScenes();
            return new SuccessResponse("Loaded scene Hierarchy read without modification.", snapshot);
        }

        static object AnalyzeHierarchyOrganization()
        {
            var snapshot = SceneHierarchySnapshotService.CaptureLoadedScenes();
            return new SuccessResponse("Hierarchy organization candidates analyzed without modification.", new
            {
                scenes = snapshot.scenes.Select(scene => new
                {
                    scene.name,
                    scene.path,
                    root_candidates = scene.nodes.Where(node => node.depth == 0).Select(node => new
                    {
                        instance_id = node.instanceId,
                        node.name,
                        node.hierarchyPath,
                        node.prefabAssetPath,
                        node.layer,
                        node.childCount
                    }).ToArray(),
                    parent_candidates = scene.nodes.Where(node => node.childCount > 0 && node.depth <= 4).Select(node => new
                    {
                        instance_id = node.instanceId,
                        node.hierarchyPath,
                        node.depth,
                        node.childCount
                    }).ToArray()
                }).ToArray()
            });
        }

        static object PrepareHierarchyOrganizationDraft(JObject parameters)
        {
            var items = parameters?["entries"] as JArray;
            if (items == null || items.Count == 0) return new ErrorResponse("entries is required");
            var entries = items.Select(item => HierarchyOrganizationService.Validate(new HierarchyOrganizationEntry
            {
                objectInstanceId = item?["object_instance_id"]?.Value<int>() ?? 0,
                parentInstanceId = item?["parent_instance_id"]?.Value<int>() ?? 0,
                include = item?["include"]?.Value<bool?>() ?? true
            })).ToList();
            HierarchyOrganizationSession.SetDraft(entries);
            var window = BentoWindow.OpenPage("hierarchy_organization");
            return new SuccessResponse("Hierarchy draft prepared for human review. No scene changes were made.", new
            {
                page = window.CurrentPageName,
                count = entries.Count,
                valid_count = entries.Count(entry => string.IsNullOrEmpty(entry.warning)),
                entries = entries.Select(entry => new
                {
                    object_instance_id = entry.objectInstanceId,
                    entry.objectPath,
                    parent_instance_id = entry.parentInstanceId,
                    entry.targetParentPath,
                    entry.warning
                }).ToArray()
            });
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

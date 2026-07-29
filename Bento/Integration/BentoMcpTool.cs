using System;
using System.Linq;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Tools;
using Newtonsoft.Json.Linq;
using UnityEditor;
using System.Collections.Generic;

namespace Bento
{
    [McpForUnityTool(
        "bento",
        Description = "Bento Unity production tools: inspect Hierarchy selection, open the Bento toolbox, switch toolbox pages, and run a read-only resource-variant analysis.",
        AutoRegister = true,
        Group = "core")]
    public static class BentoMcpTool
    {
        static readonly IReadOnlyDictionary<string, Func<JObject, object>> Commands =
            new Dictionary<string, Func<JObject, object>>(StringComparer.OrdinalIgnoreCase)
            {
                ["ping"] = _ => new SuccessResponse("pong", new { tool = "bento" }),
                ["get_selection"] = _ => new SuccessResponse("Hierarchy selection read.", McpSceneAnalysisFacade.GetSelection()),
                ["open_toolbox"] = value => OpenToolbox(value?["page"]?.ToString()),
                ["analyze_resource_variant"] = AnalyzeResourceVariant,
                ["get_project_selection"] = _ => GetProjectSelection(),
                ["analyze_asset_move"] = AnalyzeAssetMove,
                ["analyze_ai_asset_organizer"] = AnalyzeAIAssetOrganizer,
                ["analyze_selected_asset_info"] = _ => AnalyzeSelectedAssetInfo(),
                ["analyze_selected_prefab_layers"] = _ => AnalyzeSelectedPrefabLayers(),
                ["analyze_project_asset_rename"] = value => new SuccessResponse("Project asset rename plan analyzed without modification.", McpAnalysisFacade.AnalyzeProjectAssetRename(ReadNamingOptions(value))),
                ["inspect_loaded_scenes"] = _ => new SuccessResponse("Loaded scenes inspected without modification.", McpSceneAnalysisFacade.InspectLoadedScenes()),
                ["get_loaded_scene_hierarchy"] = _ => new SuccessResponse("Loaded scene Hierarchy read without modification.", SceneHierarchySnapshotService.CaptureLoadedScenes()),
                ["analyze_hierarchy_organization"] = _ => new SuccessResponse("Hierarchy organization candidates analyzed without modification.", McpSceneAnalysisFacade.AnalyzeHierarchyOrganization()),
                ["prepare_hierarchy_organization_draft"] = PrepareHierarchyOrganizationDraft,
                ["validate_loaded_scenes"] = _ => new SuccessResponse("Loaded scenes validated without modification.", McpSceneAnalysisFacade.ValidateLoadedScenes()),
                ["scan_geometry_intersections"] = ScanGeometryIntersections,
                ["get_geometry_intersections"] = _ => GetGeometryIntersections(),
                ["clear_geometry_intersections"] = _ => ClearGeometryIntersections()
            };

        public static object HandleCommand(JObject parameters)
        {
            string action = parameters?["action"]?.ToString()?.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(action))
                return new ErrorResponse("action is required");

            try
            {
                return Commands.TryGetValue(action, out var command)
                    ? command(parameters)
                    : new ErrorResponse($"Unknown action: {action}. Supported: {string.Join(", ", Commands.Keys.OrderBy(value => value))}");
            }
            catch (Exception exception)
            {
                return new ErrorResponse(exception.Message, new { stackTrace = exception.StackTrace });
            }
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
                    otherFolder = parameters?["other_folder"]?.ToString()
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

        static object ScanGeometryIntersections(JObject parameters)
        {
            var options = new SceneGeometryScanOptions
            {
                scope = ReadGeometryScope(parameters?["scope"]?.ToString()),
                includeInactive = parameters?["include_inactive"]?.Value<bool?>() ?? false,
                ignoreSameHierarchy = parameters?["ignore_same_hierarchy"]?.Value<bool?>() ?? true,
                includeBoundsOnlyCandidates = parameters?["include_bounds_candidates"]?.Value<bool?>() ?? true,
                minimumBoundsOverlap = parameters?["minimum_bounds_overlap"]?.Value<float?>() ?? 0.01f,
                minimumPenetration = parameters?["minimum_penetration"]?.Value<float?>() ?? 0.001f,
                maximumResults = parameters?["maximum_results"]?.Value<int?>() ?? 500
            };
            return new SuccessResponse("Geometry intersections scanned without modifying the scene.",
                SceneGeometryIntersectionService.Scan(options));
        }

        static object GetGeometryIntersections()
        {
            var result = SceneGeometryIntersectionSession.Current;
            return result == null
                ? (object)new ErrorResponse("No geometry intersection result is cached. Run scan_geometry_intersections first.")
                : new SuccessResponse("Cached geometry intersection result read.", result);
        }

        static object ClearGeometryIntersections()
        {
            SceneGeometryIntersectionSession.Clear();
            return new SuccessResponse("Cached geometry intersection result cleared.");
        }

        static SceneGeometryScanScope ReadGeometryScope(string value)
        {
            switch ((value ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "selection":
                case "selection_only": return SceneGeometryScanScope.SelectionOnly;
                case "scene":
                case "loaded_scenes":
                case "all": return SceneGeometryScanScope.LoadedScenes;
                default: return SceneGeometryScanScope.SelectionAgainstScene;
            }
        }

    }
}

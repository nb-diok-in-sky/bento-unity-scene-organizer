using System;
using System.Collections.Generic;
using System.Linq;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;

namespace Bento
{
    static class McpSceneCommands
    {
        public static void Register(IDictionary<string, Func<JObject, object>> commands)
        {
            commands["get_selection"] = _ =>
                new SuccessResponse("Hierarchy selection read.", McpSceneAnalysisFacade.GetSelection());
            commands["open_toolbox"] = parameters => OpenToolbox(parameters?["page"]?.ToString());
            commands["inspect_loaded_scenes"] = _ =>
                new SuccessResponse("Loaded scenes inspected without modification.", McpSceneAnalysisFacade.InspectLoadedScenes());
            commands["get_loaded_scene_hierarchy"] = _ =>
                new SuccessResponse("Loaded scene Hierarchy read without modification.", SceneHierarchySnapshotService.CaptureLoadedScenes());
            commands["analyze_hierarchy_organization"] = _ =>
                new SuccessResponse("Hierarchy organization candidates analyzed without modification.", McpSceneAnalysisFacade.AnalyzeHierarchyOrganization());
            commands["prepare_hierarchy_organization_draft"] = PrepareHierarchyDraft;
            commands["validate_loaded_scenes"] = _ =>
                new SuccessResponse("Loaded scenes validated without modification.", McpSceneAnalysisFacade.ValidateLoadedScenes());
            commands["scan_geometry_intersections"] = ScanGeometryIntersections;
            commands["get_geometry_intersections"] = _ => ReadGeometryIntersections();
            commands["clear_geometry_intersections"] = _ => ClearGeometryIntersections();
            commands["scan_scene_model_instances"] = ScanSceneModelInstances;
            commands["get_scene_model_instances"] = _ => ReadSceneModelInstances();
            commands["clear_scene_model_instances"] = _ => ClearSceneModelInstances();
        }

        static object OpenToolbox(string page)
        {
            BentoWindow window = BentoWindow.OpenPage(page);
            return new SuccessResponse("Bento toolbox opened.", new { page = window.CurrentPageName });
        }

        static object PrepareHierarchyDraft(JObject parameters)
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
            BentoWindow window = BentoWindow.OpenPage("hierarchy_organization");
            return new SuccessResponse("Hierarchy draft prepared for human review. No scene changes were made.", new
            {
                page = window.CurrentPageName,
                count = entries.Count,
                valid_count = entries.Count(entry => string.IsNullOrEmpty(entry.warning)),
                entries
            });
        }

        static object ScanGeometryIntersections(JObject parameters)
        {
            var options = new SceneGeometryScanOptions
            {
                scope = ReadGeometryScope(parameters?["scope"]?.ToString()),
                firstLayer = parameters?["first_layer"]?.Value<int?>() ?? 0,
                secondLayer = parameters?["second_layer"]?.Value<int?>() ?? 1,
                includeInactive = parameters?["include_inactive"]?.Value<bool?>() ?? false,
                ignoreSameHierarchy = parameters?["ignore_same_hierarchy"]?.Value<bool?>() ?? true,
                includeBoundsOnlyCandidates = parameters?["include_bounds_candidates"]?.Value<bool?>() ?? true,
                minimumBoundsOverlap = parameters?["minimum_bounds_overlap"]?.Value<float?>() ?? 0.01f,
                minimumPenetration = parameters?["minimum_penetration"]?.Value<float?>() ?? 0.001f,
                maximumResults = parameters?["maximum_results"]?.Value<int?>() ?? 500
            };
            return new SuccessResponse(
                "Geometry intersections scanned without modifying the scene.",
                SceneGeometryIntersectionService.Scan(options));
        }

        static object ReadGeometryIntersections()
        {
            SceneGeometryScanResult result = SceneGeometryIntersectionSession.Current;
            return result == null
                ? (object)new ErrorResponse("No geometry intersection result is cached. Run scan_geometry_intersections first.")
                : new SuccessResponse("Cached geometry intersection result read.", result);
        }

        static object ClearGeometryIntersections()
        {
            SceneGeometryIntersectionSession.Clear();
            return new SuccessResponse("Cached geometry intersection result cleared.");
        }

        static object ScanSceneModelInstances(JObject parameters)
        {
            SceneModelInstanceAuditScope scope =
                string.Equals(parameters?["scope"]?.ToString(), "selection", StringComparison.OrdinalIgnoreCase)
                    ? SceneModelInstanceAuditScope.Selection
                    : SceneModelInstanceAuditScope.LoadedScenes;
            return new SuccessResponse(
                "Direct FBX/OBJ scene instances scanned without modifying the scene.",
                SceneModelInstanceAuditService.Scan(scope));
        }

        static object ReadSceneModelInstances()
        {
            SceneModelInstanceAuditResult result = SceneModelInstanceAuditSession.Current;
            return result == null
                ? (object)new ErrorResponse("No scene model instance audit is cached. Run scan_scene_model_instances first.")
                : new SuccessResponse("Cached scene model instance audit read.", result);
        }

        static object ClearSceneModelInstances()
        {
            SceneModelInstanceAuditSession.Clear();
            return new SuccessResponse("Cached scene model instance audit cleared.");
        }

        static SceneGeometryScanScope ReadGeometryScope(string value)
        {
            switch ((value ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "layers":
                case "between_layers":
                case "pair":
                    return SceneGeometryScanScope.BetweenLayers;
                case "selection":
                case "selection_only":
                    return SceneGeometryScanScope.SelectionOnly;
                case "scene":
                case "loaded_scenes":
                case "all":
                    return SceneGeometryScanScope.LoadedScenes;
                default:
                    return SceneGeometryScanScope.SelectionAgainstScene;
            }
        }
    }
}

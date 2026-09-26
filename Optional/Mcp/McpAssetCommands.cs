using System;
using System.Collections.Generic;
using System.Linq;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;

namespace Bento
{
    static class McpAssetCommands
    {
        public static void Register(IDictionary<string, Func<JObject, object>> commands)
        {
            commands["get_project_selection"] = _ => GetProjectSelection();
            commands["analyze_resource_variant"] = parameters =>
                new SuccessResponse("Resource variant dry-run completed.",
                    McpAnalysisFacade.AnalyzeResourceVariant(ReadNamingOptions(parameters)));
            commands["diagnose_selected_textures"] = _ => new SuccessResponse(
                "Selected texture scan diagnosed without modification.",
                new { report = UsedAssetSelectionDiagnosticService.BuildFromCurrentSelection() });
            commands["diagnose_object_textures"] = parameters => new SuccessResponse(
                "Named object texture scan diagnosed without modification.",
                new { report = UsedAssetSelectionDiagnosticService.BuildByObjectName(parameters?["object_name"]?.ToString()) });
            commands["audit_prefab_materials"] = AuditPrefabMaterials;
            commands["analyze_asset_move"] = AnalyzeAssetMove;
            commands["analyze_selected_asset_info"] = _ => new SuccessResponse(
                "Selected Project assets analyzed without modification.",
                McpAnalysisFacade.AnalyzeSelectedAssetInfo());
            commands["analyze_selected_prefab_layers"] = _ => new SuccessResponse(
                "Selected prefab layers analyzed. Applying changes requires explicit confirmation in the Unity UI.",
                McpAnalysisFacade.AnalyzeSelectedPrefabLayers());
            commands["analyze_project_asset_rename"] = parameters => new SuccessResponse(
                "Project asset rename plan analyzed without modification.",
                McpAnalysisFacade.AnalyzeProjectAssetRename(ReadNamingOptions(parameters)));
        }

        public static AssetNamingOptions ReadNamingOptions(JObject parameters) => new AssetNamingOptions
        {
            prefix = parameters?["prefix"]?.ToString() ?? string.Empty,
            baseName = parameters?["base_name"]?.ToString() ?? string.Empty,
            suffix = parameters?["suffix"]?.ToString() ?? string.Empty,
            appendNumber = parameters?["append_number"]?.Value<bool?>() ?? false,
            startNumber = parameters?["start_number"]?.Value<int?>() ?? 1,
            numberDigits = parameters?["number_digits"]?.Value<int?>() ?? 2
        };

        static object GetProjectSelection()
        {
            var assets = ProjectSelection.CurrentAssets.Select(asset =>
            {
                string path = AssetDatabase.GetAssetPath(asset);
                return new
                {
                    name = asset.name,
                    type = asset.GetType().FullName,
                    asset_path = path,
                    guid = AssetDatabase.AssetPathToGUID(path)
                };
            }).ToArray();
            return new SuccessResponse("Project selection read.", new { count = assets.Length, assets });
        }

        static object AnalyzeAssetMove(JObject parameters)
        {
            return new SuccessResponse(
                "Asset move plan analyzed. Execution still requires Unity UI confirmation.",
                McpAnalysisFacade.AnalyzeAssetMove(
                    parameters?["prefab_folder"]?.ToString(),
                    parameters?["material_folder"]?.ToString(),
                    parameters?["texture_folder"]?.ToString(),
                    ReadNamingOptions(parameters)));
        }

        static object AuditPrefabMaterials(JObject parameters)
        {
            string[] prefabPaths = (parameters?["prefab_paths"] as JArray)?
                .Select(item => item?.ToString())
                .Where(path => !string.IsNullOrEmpty(path))
                .ToArray() ?? Array.Empty<string>();
            return new SuccessResponse(
                "Prefab material references audited without modification.",
                PrefabMaterialAuditService.Audit(parameters?["material_folder"]?.ToString(), prefabPaths));
        }
    }
}

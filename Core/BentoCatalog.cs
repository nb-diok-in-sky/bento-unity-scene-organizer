using System;
using System.Collections.Generic;

namespace Bento
{
    sealed class BentoCatalog
    {
        public BentoCatalog()
        {
            var context = new SharedContext();
            var scanTab = new TextureScanTab(context);

            Pages = new List<IBentoPage>
            {
                Page("整理", "folder_favorites", "文件夹收藏", new ProjectFolderFavoritesTool(), tool => tool.Draw()),
                Page("整理", "batch_rename", "批量重命名", new BatchRenameTool(), tool => tool.Draw()),
                Page("整理", "asset_move", "资产移动", new SafeAssetMoveTool(), tool => tool.Draw()),
                Page("整理", "asset_operation_history", "操作历史", new AssetOperationHistoryTool(), tool => tool.Draw()),
                Page("整理", "ai_asset_organizer", "AI资产整理", new AIAssetOrganizerTool(), tool => tool.Draw()),
                Page("整理", "asset_info", "资产信息", new AssetInfoExportTool(), tool => tool.Draw()),
                Page("整理", "suffix_cleanup", "按后缀清理", new AssetSuffixCleanupTool(), tool => tool.Draw()),
                Page("整理", "recursive_extract", "递归抽取资产", new RecursiveAssetExtractionTool(), tool => tool.Draw()),
                Page("整理", "hierarchy_organization", "Hierarchy 整理", new HierarchyOrganizationTool(), tool => tool.Draw()),
                Page("整理", "scene_light_batch", "场景光源批处理", new SceneLightBatchTool(), tool => tool.Draw()),
                Page("整理", "scene_renderer_shadow", "场景物体阴影", new SceneRendererShadowBatchTool(), tool => tool.Draw()),
                Page("整理", "geometry_intersections", "几何相交审查", new SceneGeometryIntersectionTool(), tool => tool.Draw()),

                Page("资源、模型和预制体", "model_import_workflow", "模型导入", new ModelImportWorkflowTool(), tool => tool.Draw()),
                Page("资源、模型和预制体", "resource_variant", "资源变体", new ResourceVariantTool(), tool => tool.Draw()),
                Page("资源、模型和预制体", "obj_to_fbx", "OBJ 转项目 FBX", new ObjToFbxConversionTool(), tool => tool.Draw()),
                Page("资源、模型和预制体", "fbx_material_inherit", "FBX 材质继承", new ModelMaterialInheritanceTool(), tool => tool.Draw()),
                Page("资源、模型和预制体", "fbx_quick_setup", "FBX 快速导入", new FbxQuickSetupTool(), tool => tool.Draw()),
                Page("资源、模型和预制体", "prefab_layer", "Prefab Layer", new PrefabLayerTool(), tool => tool.Draw()),
                Page("资源、模型和预制体", "tag_layer", "Tag/Layer", new TagLayerTab(), tool => tool.Draw()),

                Page("材质", "shader_replace", "Shader 替换", new ShaderReplaceTab(), tool => tool.Draw()),
                Page("材质", "texture_scan", "贴图扫描", scanTab, tool => tool.Draw()),
                Page("材质", "texture_rename", "贴图改名", new TextureRenameTab(context), tool => tool.Draw()),
                Page("材质", "material_fill", "填充材质球", new MaterialFillTab(context), tool => tool.Draw()),
                Page("材质", "select_used_assets", "选中模型材质贴图", new SelectUsedAssetsTool(), tool => tool.Draw()),
                Page("材质", "select_terrain_paint_textures", "选中 Terrain 绘制贴图", new SelectTerrainPaintTexturesTool(), tool => tool.Draw()),
                Page("材质", "auto_process", "一键处理", new AutoProcessTab(context), tool => tool.Draw()),
                Page("材质", "material_repair", "材质修复", new MaterialRepairTab(), tool => tool.Draw()),

                Page("场景", "scene_material_replace", "场景材质替换", new SceneMaterialReplacementTool(), tool => tool.Draw()),
                Page("场景", "scene_material_picker", "场景材质吸管", new SceneMaterialPickerTool(), tool => tool.Draw()),
                Page("场景", "prefab_replace", "预制体替换", new PrefabReplacer(), tool => tool.Draw()),
                Page("场景", "scene_model_instance_audit", "FBX 实例检查", new SceneModelInstanceAuditTool(), tool => tool.Draw()),

                Page("AI与设置", "ai_mcp", "AI/MCP", new McpIntegrationTool(), tool => tool.Draw())
            };
        }

        public IReadOnlyList<IBentoPage> Pages { get; }

        static IBentoPage Page<T>(string group, string id, string name, T owner, Action<T> draw)
            where T : class
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            if (draw == null) throw new ArgumentNullException(nameof(draw));
            return new BentoPage(group, id, name, () => draw(owner), owner);
        }
    }
}

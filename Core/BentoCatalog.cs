using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Bento
{
    sealed class BentoCatalog
    {
        public BentoCatalog()
        {
            var context = new SharedContext();
            var scanTab = new TextureScanTab(context);
            MaterialPage = new MaterialToolPage(new[]
            {
                Page("材质", "scene_material_replace", "场景材质替换", new SceneMaterialReplacementTool(), tool => tool.Draw()),
                Page("材质", "shader_replace", "Shader 替换", new ShaderReplaceTab(), tool => tool.Draw()),
                Page("材质", "texture_scan", "贴图扫描", scanTab, tool => tool.Draw()),
                Page("材质", "texture_rename", "贴图改名", new TextureRenameTab(context), tool => tool.Draw()),
                Page("材质", "material_fill", "填充材质球", new MaterialFillTab(context), tool => tool.Draw()),
                Page("材质", "select_used_materials", "选中使用材质", new SelectUsedMaterialsTool(), tool => tool.Draw()),
                Page("材质", "select_used_assets", "选中模型材质贴图", new SelectUsedAssetsTool(), tool => tool.Draw()),
                Page("材质", "tag_layer", "Tag/Layer", new TagLayerTab(), tool => tool.Draw()),
                Page("材质", "auto_process", "一键处理", new AutoProcessTab(context), tool => tool.Draw()),
                Page("材质", "material_repair", "材质修复", new MaterialRepairTab(), tool => tool.Draw()),
            });

            Pages = new List<IBentoPage>
            {
                Page("工作流", "model_import_workflow", "模型导入", new ModelImportWorkflowTool(), tool => tool.Draw()),
                Page("资源", "folder_favorites", "文件夹收藏", new ProjectFolderFavoritesTool(), tool => tool.Draw()),
                Page("资源", "batch_rename", "批量重命名", new BatchRenameTool(), tool => tool.Draw()),
                Page("资源", "resource_variant", "资源变体", new ResourceVariantTool(), tool => tool.Draw()),
                Page("资源", "asset_move", "资产移动", new SafeAssetMoveTool(), tool => tool.Draw()),
                Page("资源", "asset_operation_history", "操作历史", new AssetOperationHistoryTool(), tool => tool.Draw()),
                Page("资源", "obj_to_fbx", "OBJ 转项目 FBX", new ObjToFbxConversionTool(), tool => tool.Draw()),
                Page("资源", "fbx_material_inherit", "FBX 材质继承", new ModelMaterialInheritanceTool(), tool => tool.Draw()),
                Page("资源", "fbx_quick_setup", "FBX 快速导入", new FbxQuickSetupTool(), tool => tool.Draw()),
                Page("资源", "ai_asset_organizer", "AI资产整理", new AIAssetOrganizerTool(), tool => tool.Draw()),
                Page("资源", "asset_info", "资产信息", new AssetInfoExportTool(), tool => tool.Draw()),
                Page("资源", "prefab_layer", "Prefab Layer", new PrefabLayerTool(), tool => tool.Draw()),
                Page("资源", "suffix_cleanup", "按后缀清理", new AssetSuffixCleanupTool(), tool => tool.Draw()),
                Page("资源", "recursive_extract", "递归抽取资产", new RecursiveAssetExtractionTool(), tool => tool.Draw()),
                Page("场景", "prefab_replace", "预制体替换", new PrefabReplacer(), tool => tool.Draw()),
                Page("场景", "hierarchy_organization", "Hierarchy 整理", new HierarchyOrganizationTool(), tool => tool.Draw()),
                Page("场景", "scene_light_batch", "场景光源批处理", new SceneLightBatchTool(), tool => tool.Draw()),
                Page("场景", "scene_renderer_shadow", "场景物体阴影", new SceneRendererShadowBatchTool(), tool => tool.Draw()),
                Page("场景", "geometry_intersections", "几何相交审查", new SceneGeometryIntersectionTool(), tool => tool.Draw()),
                Page("材质", "material_tools", "材质工具", MaterialPage, tool => tool.Draw()),
                Page("AI与设置", "ai_mcp", "AI/MCP", new McpIntegrationTool(), tool => tool.Draw())
            };
        }

        public IReadOnlyList<IBentoPage> Pages { get; }
        public MaterialToolPage MaterialPage { get; }

        static IBentoPage Page<T>(string group, string id, string name, T owner, Action<T> draw) where T : class
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            if (draw == null) throw new ArgumentNullException(nameof(draw));
            return new BentoPage(group, id, name, () => draw(owner), owner);
        }
    }

    sealed class MaterialToolPage : IBentoPageLifecycle
    {
        readonly IReadOnlyList<IBentoPage> _pages;
        readonly string[] _names;
        int _selected;
        GUIStyle _toolbarStyle;

        public MaterialToolPage(IReadOnlyList<IBentoPage> pages)
        {
            _pages = pages;
            _names = pages.Select(page => page.Name).ToArray();
        }

        public void Draw()
        {
            int next = GUILayout.Toolbar(_selected, _names, ToolbarStyle, GUILayout.Height(32));
            if (next != _selected) SwitchTo(next);
            EditorGUILayout.Space(4);
            _pages[Mathf.Clamp(_selected, 0, _pages.Count - 1)].Draw();
        }

        GUIStyle ToolbarStyle => _toolbarStyle ?? (_toolbarStyle = new GUIStyle(EditorStyles.miniButton)
        {
            fontSize = 13,
            fixedHeight = 32,
            padding = new RectOffset(8, 8, 4, 4)
        });

        public void OnActivated()
        {
            Notify(_selected, lifecycle => lifecycle.OnActivated());
        }

        public void OnDeactivated() => Notify(_selected, lifecycle => lifecycle.OnDeactivated());
        public void OnSelectionChanged() => Notify(_selected, lifecycle => lifecycle.OnSelectionChanged());

        void SwitchTo(int next)
        {
            Notify(_selected, lifecycle => lifecycle.OnDeactivated());
            _selected = Mathf.Clamp(next, 0, _pages.Count - 1);
            Notify(_selected, lifecycle => lifecycle.OnActivated());
        }

        void Notify(int index, Action<IBentoPageLifecycle> action)
        {
            if (index < 0 || index >= _pages.Count) return;
            if (_pages[index] is IBentoPageLifecycle lifecycle) action(lifecycle);
        }
    }
}

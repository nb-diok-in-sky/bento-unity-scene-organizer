using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Bento
{
    static class BentoContextMenu
    {
        [MenuItem("GameObject/Bento/智能工具", false, -10000)]
        [MenuItem("Assets/Bento/智能工具", false, -10000)]
        static void ShowSmartMenu()
        {
            var context = SelectionContext.Analyze();
            var menu = new GenericMenu();
            int count = 0;

            if (context.HasSceneObjects)
            {
                Add(menu, "选择/选中模型、材质和贴图", SelectUsedAssetsTool.SelectAdvancedFromCurrentSelection, ref count);
                AddPage(menu, "场景/预制体替换", "prefab_replace", ref count);
                AddPage(menu, "场景/Hierarchy 整理", "hierarchy_organization", ref count);
                AddPage(menu, "场景/几何相交审查", "geometry_intersections", ref count);
                AddPage(menu, "场景/物体阴影批处理", "scene_renderer_shadow", ref count);
                AddPage(menu, "材质/场景材质替换", "scene_material_replace", ref count);
            }

            if (context.HasTerrain)
                Add(menu, "Terrain/选中当前 Paint Texture 贴图", SelectTerrainPaintTexturesTool.SelectFromTerrainInspector, ref count);

            if (context.HasSceneLights)
                AddPage(menu, "场景/光源批处理", "scene_light_batch", ref count);

            if (context.HasProjectModels)
            {
                Add(menu, "选择/选中模型、材质和贴图", SelectUsedAssetsTool.SelectAdvancedFromCurrentSelection, ref count);
                AddPage(menu, "FBX/材质继承", "fbx_material_inherit", ref count);
                AddPage(menu, "FBX/快速导入", "fbx_quick_setup", ref count);
                AddPage(menu, "资源/创建资源变体", "resource_variant", ref count);
                AddPage(menu, "资源/Prefab Layer", "prefab_layer", ref count);
            }

            if (context.HasMaterials)
            {
                AddPage(menu, "材质/Shader 替换", "shader_replace", ref count);
                AddPage(menu, "材质/材质修复", "material_repair", ref count);
                AddPage(menu, "材质/填充材质球", "material_fill", ref count);
            }

            if (context.HasTextures)
            {
                AddPage(menu, "贴图/贴图扫描", "texture_scan", ref count);
                AddPage(menu, "贴图/贴图改名", "texture_rename", ref count);
                AddPage(menu, "FBX/快速导入并绑定贴图", "fbx_quick_setup", ref count);
            }

            if (context.HasFolders)
            {
                AddPage(menu, "文件夹/收藏文件夹", "folder_favorites", ref count);
                AddPage(menu, "文件夹/AI 资产整理", "ai_asset_organizer", ref count);
                AddPage(menu, "文件夹/资产移动", "asset_move", ref count);
                AddPage(menu, "文件夹/批量重命名", "batch_rename", ref count);
            }

            if (context.HasProjectAssets)
            {
                AddPage(menu, "资源/资产信息", "asset_info", ref count);
                AddPage(menu, "资源/批量重命名", "batch_rename", ref count);
                AddPage(menu, "资源/资产移动", "asset_move", ref count);
            }

            if (count == 0)
                menu.AddDisabledItem(new GUIContent("当前选择没有匹配的 Bento 功能"));
            menu.ShowAsContext();
        }

        [MenuItem("GameObject/Bento/智能工具", true)]
        [MenuItem("Assets/Bento/智能工具", true)]
        static bool ValidateSmartMenu() => Selection.objects.Any(item => item != null);

        static void Add(GenericMenu menu, string label, GenericMenu.MenuFunction action, ref int count)
        {
            if (menu == null || action == null) return;
            menu.AddItem(new GUIContent(label), false, action);
            count++;
        }

        static void AddPage(GenericMenu menu, string label, string pageId, ref int count) =>
            Add(menu, label, () => BentoWindow.OpenPage(pageId), ref count);

        sealed class SelectionContext
        {
            public bool HasSceneObjects;
            public bool HasTerrain;
            public bool HasSceneLights;
            public bool HasProjectAssets;
            public bool HasProjectModels;
            public bool HasMaterials;
            public bool HasTextures;
            public bool HasFolders;

            public static SelectionContext Analyze()
            {
                var result = new SelectionContext();
                foreach (UnityEngine.Object selected in Selection.objects.Where(item => item != null))
                {
                    if (selected is GameObject gameObject && gameObject.scene.IsValid())
                    {
                        result.HasSceneObjects = true;
                        result.HasTerrain |= gameObject.GetComponent<Terrain>() != null;
                        result.HasSceneLights |= gameObject.GetComponent<Light>() != null;
                        continue;
                    }
                    if (selected is Component component && component.gameObject.scene.IsValid())
                    {
                        result.HasSceneObjects = true;
                        result.HasTerrain |= component is Terrain || component.GetComponent<Terrain>() != null;
                        result.HasSceneLights |= component is Light || component.GetComponent<Light>() != null;
                        continue;
                    }
                    string path = AssetDatabase.GetAssetPath(selected);
                    if (string.IsNullOrEmpty(path)) continue;
                    result.HasProjectAssets = true;
                    result.HasFolders |= AssetDatabase.IsValidFolder(path);
                    result.HasMaterials |= selected is Material;
                    result.HasTextures |= selected is Texture;
                    result.HasProjectModels |= IsProjectModel(selected, path);
                }
                return result;
            }

            static bool IsProjectModel(UnityEngine.Object selected, string path)
            {
                if (!(selected is GameObject)) return false;
                string extension = System.IO.Path.GetExtension(path);
                return string.Equals(extension, ".fbx", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(extension, ".prefab", StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}

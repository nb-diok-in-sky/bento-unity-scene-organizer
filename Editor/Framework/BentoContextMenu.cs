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
                AddPage<PrefabReplacer>(menu, "场景/预制体替换", ref count);
                AddPage<HierarchyOrganizationTool>(menu, "场景/Hierarchy 整理", ref count);
                AddPage<SceneRendererShadowBatchTool>(menu, "场景/物体阴影批处理", ref count);
                AddPage<SceneMaterialReplacementTool>(menu, "材质/场景材质替换", ref count);
            }

            if (context.HasTerrain)
                Add(menu, "Terrain/选中当前 Paint Texture 贴图", SelectTerrainPaintTexturesTool.SelectFromTerrainInspector, ref count);

            if (context.HasSceneLights)
                AddPage<SceneLightBatchTool>(menu, "场景/光源批处理", ref count);

            if (context.HasProjectModels)
            {
                Add(menu, "选择/选中模型、材质和贴图", SelectUsedAssetsTool.SelectAdvancedFromCurrentSelection, ref count);
                AddPage<ModelMaterialInheritanceTool>(menu, "FBX/材质继承", ref count);
                AddPage<ResourceVariantTool>(menu, "资源/创建资源变体", ref count);
                AddPage<PrefabLayerTool>(menu, "资源/Prefab Layer", ref count);
            }

            if (context.HasMaterials)
            {
                AddPage<ShaderReplaceTab>(menu, "材质/Shader 替换", ref count);
            }

            if (context.HasFolders)
            {
                AddPage<ProjectFolderFavoritesTool>(menu, "文件夹/收藏文件夹", ref count);
                AddPage<SafeAssetMoveTool>(menu, "文件夹/资产移动", ref count);
                AddPage<BatchRenameTool>(menu, "文件夹/批量重命名", ref count);
            }

            if (context.HasProjectAssets)
            {
                AddPage<AssetInfoExportTool>(menu, "资源/资产信息", ref count);
                AddPage<BatchRenameTool>(menu, "资源/批量重命名", ref count);
                AddPage<SafeAssetMoveTool>(menu, "资源/资产移动", ref count);
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

        static void AddPage<TTool>(GenericMenu menu, string label, ref int count) where TTool : IBentoTool =>
            Add(menu, label, () => BentoWindow.OpenPage<TTool>(), ref count);

        sealed class SelectionContext
        {
            public bool HasSceneObjects;
            public bool HasTerrain;
            public bool HasSceneLights;
            public bool HasProjectAssets;
            public bool HasProjectModels;
            public bool HasMaterials;
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

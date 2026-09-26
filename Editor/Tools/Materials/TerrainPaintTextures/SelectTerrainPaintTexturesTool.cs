using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Bento
{
    sealed class SelectTerrainPaintTexturesTool
    {
        static readonly BindingFlags InstanceFields =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        public void Draw()
        {
            EditorGUILayout.LabelField("选中 Terrain 当前绘制层贴图", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "先在 Terrain Inspector 的 Paint Texture 中点击一个 Terrain Layer。工具会选中该层的基础色、法线和 Mask 贴图。",
                MessageType.Info);

            TerrainLayer layer = ResolveSelectedTerrainLayer();
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.ObjectField("当前 Terrain Layer", layer, typeof(TerrainLayer), false);
                if (layer != null) EditorGUILayout.LabelField(AssetDatabase.GetAssetPath(layer), EditorStyles.miniLabel);
            }

            using (new EditorGUI.DisabledScope(layer == null))
                if (GUILayout.Button("在 Project 中选中当前 Paint Texture 贴图", GUILayout.Height(36)))
                    SelectTextures(layer, true);

            if (layer == null)
                EditorGUILayout.HelpBox("尚未读取到当前绘制层。请选中 Terrain，进入 Paint Texture 并点击一个图层。", MessageType.Warning);
        }

        [MenuItem("GameObject/Bento/选中 Terrain 当前 Paint Texture 贴图", false, -8980)]
        internal static void SelectFromTerrainInspector()
        {
            TerrainLayer layer = ResolveSelectedTerrainLayer();
            if (layer == null)
            {
                EditorUtility.DisplayDialog("没有选中的 Terrain Layer", "请先选中 Terrain，在 Inspector 的 Paint Texture 中点击一个图层。", "确定");
                return;
            }
            SelectTextures(layer, true);
        }

        static void SelectTextures(TerrainLayer layer, bool pingFirst)
        {
            Texture[] textures = new Texture[] { layer.diffuseTexture, layer.normalMapTexture, layer.maskMapTexture }
                .Where(texture => texture != null && !string.IsNullOrEmpty(AssetDatabase.GetAssetPath(texture)))
                .Distinct()
                .OrderBy(texture => AssetDatabase.GetAssetPath(texture), StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (textures.Length == 0)
            {
                EditorUtility.DisplayDialog("没有找到贴图", "当前 Terrain Layer 没有引用基础色、法线或 Mask 贴图。", "确定");
                return;
            }
            Selection.objects = textures;
            if (pingFirst) EditorGUIUtility.PingObject(textures[0]);
        }

        static TerrainLayer ResolveSelectedTerrainLayer()
        {
            Type paintToolType = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType("UnityEditor.TerrainTools.PaintTextureTool", false))
                .FirstOrDefault(type => type != null);
            FieldInfo selectedLayerField = paintToolType?.GetField("m_SelectedTerrainLayer", InstanceFields);
            if (paintToolType == null || selectedLayerField == null) return null;
            foreach (UnityEngine.Object tool in Resources.FindObjectsOfTypeAll(paintToolType).Reverse())
            {
                var layer = selectedLayerField.GetValue(tool) as TerrainLayer;
                if (layer != null) return layer;
            }
            return null;
        }
    }
}

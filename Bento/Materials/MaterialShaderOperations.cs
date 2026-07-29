using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Bento
{
    static class MaterialShaderOperations
    {
        public static void ShowShaderDropdown(Action<Shader> onSelect, Shader current)
        {
            var menu = new GenericMenu();
            foreach (string name in ShaderUtil.GetAllShaderInfo()
                         .Select(info => info.name)
                         .Where(name => !name.StartsWith("Hidden/"))
                         .OrderBy(name => name))
            {
                string shaderName = name;
                menu.AddItem(new GUIContent(shaderName), current != null && current.name == shaderName,
                    () => onSelect(Shader.Find(shaderName)));
            }
            menu.ShowAsContext();
        }

        public static int ReplaceShaders(Material[] materials, Shader targetShader)
        {
            int count = 0;
            using (var undo = new BentoUndoScope("批量替换 Shader"))
            {
                Undo.RecordObjects(materials, "批量替换 Shader");
                foreach (var material in materials)
                {
                    if (material == null || material.shader == targetShader) continue;
                    TexRules.MigrateShader(material, targetShader);
                    EditorUtility.SetDirty(material);
                    count++;
                }
                undo.Complete();
            }
            AssetDatabase.SaveAssets();
            return count;
        }

        public static void FillMaterialSlots(Material material, Dictionary<TexType, Texture2D> slots)
        {
            var propertyMap = TexRules.GetPropertyMap(material);
            foreach (var pair in slots)
            {
                if (pair.Value == null || !propertyMap.TryGetValue(pair.Key, out string property) || !material.HasProperty(property)) continue;
                material.SetTexture(property, pair.Value);
            }
            TexRules.EnableKeywordsForTextures(material);
        }
    }
}

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Bento
{
    static class MaterialSerializedPropertyRecovery
    {
        public static int Recover(IReadOnlyList<Material> materials)
        {
            if (materials == null || materials.Count == 0) return 0;
            int total = 0;
            using (var undo = new BentoUndoScope("恢复丢失的材质属性"))
            {
                Undo.RecordObjects(ToObjects(materials), "恢复丢失的材质属性");
                foreach (var material in materials)
                {
                    if (material == null) continue;
                    int recovered = RecoverMaterial(material);
                    if (recovered == 0) continue;
                    TexRules.EnableKeywordsForTextures(material);
                    EditorUtility.SetDirty(material);
                    total += recovered;
                }
                undo.Complete();
            }
            AssetDatabase.SaveAssets();
            return total;
        }

        static int RecoverMaterial(Material material)
        {
            var serialized = new SerializedObject(material);
            var textures = ReadTextures(serialized);
            var colors = ReadColors(serialized);
            var floats = ReadFloats(serialized);
            int recovered = 0;
            foreach (var pair in TexRules.PropertyMigrations)
            {
                string source = pair.src;
                string target = pair.dst;
                if (!material.HasProperty(target)) continue;
                if (textures.TryGetValue(source, out var texture) && material.GetTexture(target) == null)
                {
                    material.SetTexture(target, texture);
                    recovered++;
                }
                if (colors.TryGetValue(source, out var color) && material.GetColor(target) != color && color != Color.clear)
                {
                    material.SetColor(target, color);
                    recovered++;
                }
                if (floats.TryGetValue(source, out float value) && Mathf.Abs(material.GetFloat(target) - value) > 0.001f)
                {
                    material.SetFloat(target, value);
                    recovered++;
                }
            }
            return recovered;
        }

        static Dictionary<string, Texture> ReadTextures(SerializedObject serialized)
        {
            var result = new Dictionary<string, Texture>();
            var values = serialized.FindProperty("m_SavedProperties.m_TexEnvs");
            if (values == null) return result;
            for (int index = 0; index < values.arraySize; index++)
            {
                var item = values.GetArrayElementAtIndex(index);
                var texture = item.FindPropertyRelative("second.m_Texture").objectReferenceValue as Texture;
                if (texture != null) result[item.FindPropertyRelative("first").stringValue] = texture;
            }
            return result;
        }

        static Dictionary<string, Color> ReadColors(SerializedObject serialized)
        {
            var result = new Dictionary<string, Color>();
            var values = serialized.FindProperty("m_SavedProperties.m_Colors");
            if (values == null) return result;
            for (int index = 0; index < values.arraySize; index++)
            {
                var item = values.GetArrayElementAtIndex(index);
                var value = item.FindPropertyRelative("second");
                result[item.FindPropertyRelative("first").stringValue] = new Color(
                    value.FindPropertyRelative("r").floatValue,
                    value.FindPropertyRelative("g").floatValue,
                    value.FindPropertyRelative("b").floatValue,
                    value.FindPropertyRelative("a").floatValue);
            }
            return result;
        }

        static Dictionary<string, float> ReadFloats(SerializedObject serialized)
        {
            var result = new Dictionary<string, float>();
            var values = serialized.FindProperty("m_SavedProperties.m_Floats");
            if (values == null) return result;
            for (int index = 0; index < values.arraySize; index++)
            {
                var item = values.GetArrayElementAtIndex(index);
                result[item.FindPropertyRelative("first").stringValue] = item.FindPropertyRelative("second").floatValue;
            }
            return result;
        }

        static Object[] ToObjects(IReadOnlyList<Material> materials)
        {
            var result = new Object[materials.Count];
            for (int index = 0; index < materials.Count; index++) result[index] = materials[index];
            return result;
        }
    }
}

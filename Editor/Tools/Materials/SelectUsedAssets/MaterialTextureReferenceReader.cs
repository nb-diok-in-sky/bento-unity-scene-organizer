using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Bento
{
    static class MaterialTextureReferenceReader
    {
        public static IEnumerable<MaterialTextureReference> Read(Material material)
        {
            if (material == null) yield break;

            var serialized = new SerializedObject(material);
            SerializedProperty textures = serialized.FindProperty("m_SavedProperties.m_TexEnvs");
            if (textures == null || !textures.isArray) yield break;

            for (int index = 0; index < textures.arraySize; index++)
            {
                SerializedProperty entry = textures.GetArrayElementAtIndex(index);
                string propertyName = entry.FindPropertyRelative("first")?.stringValue;
                Texture texture = entry.FindPropertyRelative("second.m_Texture")?.objectReferenceValue as Texture;
                if (texture != null)
                    yield return new MaterialTextureReference(propertyName, texture);
            }
        }
    }
}

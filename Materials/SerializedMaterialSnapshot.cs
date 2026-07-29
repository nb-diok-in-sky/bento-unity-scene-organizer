using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Bento
{
    sealed class SerializedMaterialSnapshot
    {
        readonly Dictionary<string, Texture> _textures = new();
        readonly Dictionary<string, float> _floats = new();
        readonly Dictionary<string, Color> _colors = new();

        public SerializedMaterialSnapshot(Material material)
        {
            var serialized = new SerializedObject(material);
            ReadTextures(serialized.FindProperty("m_SavedProperties.m_TexEnvs"));
            ReadFloats(serialized.FindProperty("m_SavedProperties.m_Floats"));
            ReadColors(serialized.FindProperty("m_SavedProperties.m_Colors"));
        }

        public Texture FirstTexture(IEnumerable<string> names)
        {
            foreach (string name in names)
                if (_textures.TryGetValue(name, out var value) && value != null)
                    return value;
            return null;
        }

        public float FirstFloat(float fallback, params string[] names)
        {
            foreach (string name in names)
                if (_floats.TryGetValue(name, out var value))
                    return value;
            return fallback;
        }

        public Color FirstColor(Color fallback, params string[] names)
        {
            foreach (string name in names)
                if (_colors.TryGetValue(name, out var value))
                    return value;
            return fallback;
        }

        void ReadTextures(SerializedProperty array)
        {
            if (array == null) return;
            for (int i = 0; i < array.arraySize; i++)
            {
                var item = array.GetArrayElementAtIndex(i);
                _textures[item.FindPropertyRelative("first").stringValue] =
                    item.FindPropertyRelative("second.m_Texture").objectReferenceValue as Texture;
            }
        }

        void ReadFloats(SerializedProperty array)
        {
            if (array == null) return;
            for (int i = 0; i < array.arraySize; i++)
            {
                var item = array.GetArrayElementAtIndex(i);
                _floats[item.FindPropertyRelative("first").stringValue] =
                    item.FindPropertyRelative("second").floatValue;
            }
        }

        void ReadColors(SerializedProperty array)
        {
            if (array == null) return;
            for (int i = 0; i < array.arraySize; i++)
            {
                var item = array.GetArrayElementAtIndex(i);
                _colors[item.FindPropertyRelative("first").stringValue] =
                    item.FindPropertyRelative("second").colorValue;
            }
        }
    }
}

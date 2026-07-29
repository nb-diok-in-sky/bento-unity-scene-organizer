using UnityEditor;

namespace Bento
{
    static class BentoGUI
    {
        public static DefaultAsset DrawProjectFolder(string label, DefaultAsset current, out string path)
        {
            var next = (DefaultAsset)EditorGUILayout.ObjectField(label, current, typeof(DefaultAsset), false);
            path = next == null ? string.Empty : AssetDatabase.GetAssetPath(next);
            if (next == null || AssetDatabase.IsValidFolder(path)) return next;
            EditorGUILayout.HelpBox($"{label} 只能选择 Project 文件夹。", MessageType.Warning);
            path = current == null ? string.Empty : AssetDatabase.GetAssetPath(current);
            return current;
        }

        public static void DrawNamingFields(AssetNamingOptions options, string baseNameLabel)
        {
            options.prefix = EditorGUILayout.TextField("前缀", options.prefix);
            options.baseName = EditorGUILayout.TextField(baseNameLabel, options.baseName);
            options.suffix = EditorGUILayout.TextField("后缀", options.suffix);
            options.appendNumber = EditorGUILayout.Toggle("自动编号", options.appendNumber);
            if (!options.appendNumber) return;
            options.startNumber = EditorGUILayout.IntField("起始编号", options.startNumber);
            options.numberDigits = EditorGUILayout.IntSlider("编号位数", options.numberDigits, 1, 6);
        }
    }
}

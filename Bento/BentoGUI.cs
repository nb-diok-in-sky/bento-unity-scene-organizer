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
    }
}

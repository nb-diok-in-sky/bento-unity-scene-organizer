using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Bento
{
    class ShaderReplaceTab
    {
        Shader _targetShader;
        Vector2 _scroll;
        bool _showPreview = true;

        public void Draw()
        {
            var materials = Selection.objects.OfType<Material>().ToArray();
            EditorGUILayout.HelpBox(materials.Length == 0
                ? "在 Project 面板中选中一个或多个材质球"
                : $"已选中 {materials.Length} 个材质球", materials.Length == 0 ? MessageType.Info : MessageType.None);
            EditorGUILayout.Space(4);
            _targetShader = EditorGUILayout.ObjectField("目标 Shader", _targetShader, typeof(Shader), false) as Shader;
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel("从列表选择");
                if (EditorGUILayout.DropdownButton(new GUIContent(_targetShader != null ? _targetShader.name : "(选择 Shader)"), FocusType.Keyboard))
                    MaterialShaderOperations.ShowShaderDropdown(shader => _targetShader = shader, _targetShader);
            }
            DrawPreview(materials);
            using (new EditorGUI.DisabledScope(materials.Length == 0 || _targetShader == null))
            if (GUILayout.Button($"替换选中的 {materials.Length} 个材质球的 Shader", GUILayout.Height(32)))
            {
                int changed = MaterialShaderOperations.ReplaceShaders(materials, _targetShader);
                Debug.Log($"[Bento] 已替换 {changed} 个材质球的 Shader → {_targetShader.name}");
            }
            DrawRecovery(materials);
        }

        void DrawPreview(Material[] materials)
        {
            if (materials.Length == 0) return;
            EditorGUILayout.Space(8);
            _showPreview = EditorGUILayout.Foldout(_showPreview, $"预览 ({materials.Length} 个材质)");
            if (!_showPreview) return;
            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.MaxHeight(200));
            foreach (var material in materials)
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.ObjectField(material, typeof(Material), false, GUILayout.Width(160));
                GUILayout.Label(material.shader != null ? material.shader.name : "(null)", GUILayout.Width(140));
                GUILayout.Label("→", GUILayout.Width(20));
                GUILayout.Label(_targetShader != null ? _targetShader.name : "—", material.shader != _targetShader ? EditorStyles.label : EditorStyles.miniLabel);
            }
            EditorGUILayout.EndScrollView();
        }

        static void DrawRecovery(Material[] materials)
        {
            EditorGUILayout.Space(12);
            EditorGUILayout.LabelField("修复工具", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("如果之前换 Shader 丢了贴图，可以尝试从材质序列化数据中恢复", EditorStyles.miniLabel);
            using (new EditorGUI.DisabledScope(materials.Length == 0))
            if (GUILayout.Button($"恢复选中 {materials.Length} 个材质球的丢失属性", GUILayout.Height(28)))
            {
                int recovered = MaterialSerializedPropertyRecovery.Recover(materials);
                Debug.Log($"[Bento] 恢复完成，共恢复 {recovered} 个属性");
                if (recovered == 0) EditorUtility.DisplayDialog("恢复结果", "未找到可恢复的属性。", "知道了");
            }
        }
    }
}

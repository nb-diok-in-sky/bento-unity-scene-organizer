using System.Collections.Generic;
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
            var mats = Selection.objects.OfType<Material>().ToArray();

            if (mats.Length == 0)
                EditorGUILayout.HelpBox("在 Project 面板中选中一个或多个材质球", MessageType.Info);
            else
                EditorGUILayout.HelpBox($"已选中 {mats.Length} 个材质球", MessageType.None);

            EditorGUILayout.Space(4);

            _targetShader = EditorGUILayout.ObjectField("目标 Shader", _targetShader, typeof(Shader), false) as Shader;

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel("从列表选择");
            if (EditorGUILayout.DropdownButton(
                    new GUIContent(_targetShader != null ? _targetShader.name : "(选择 Shader)"),
                    FocusType.Keyboard))
                ShowShaderDropdown();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(8);

            if (mats.Length > 0)
            {
                _showPreview = EditorGUILayout.Foldout(_showPreview, $"预览 ({mats.Length} 个材质)");
                if (_showPreview)
                {
                    _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.MaxHeight(200));
                    foreach (var m in mats)
                    {
                        var current = m.shader != null ? m.shader.name : "(null)";
                        var target = _targetShader != null ? _targetShader.name : "—";
                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.ObjectField(m, typeof(Material), false, GUILayout.Width(160));
                        GUILayout.Label(current, GUILayout.Width(140));
                        GUILayout.Label("→", GUILayout.Width(20));
                        GUILayout.Label(target, m.shader != _targetShader ? EditorStyles.label : EditorStyles.miniLabel);
                        EditorGUILayout.EndHorizontal();
                    }
                    EditorGUILayout.EndScrollView();
                }
            }

            EditorGUILayout.Space(8);

            EditorGUI.BeginDisabledGroup(mats.Length == 0 || _targetShader == null);
            if (GUILayout.Button($"替换选中的 {mats.Length} 个材质球的 Shader", GUILayout.Height(32)))
                ReplaceShaders(mats);
            EditorGUI.EndDisabledGroup();

            DrawRecoverSection(mats);
        }

        void ReplaceShaders(Material[] mats)
        {
            int count = TexRules.ReplaceShaders(mats, _targetShader);
            Debug.Log($"[Bento] 已替换 {count} 个材质球的 Shader → {_targetShader.name}");
        }

        // ── Recover lost textures ──
        void DrawRecoverSection(Material[] mats)
        {
            EditorGUILayout.Space(12);
            EditorGUILayout.LabelField("修复工具", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("如果之前换 Shader 丢了贴图，可以尝试从材质文件中恢复", EditorStyles.miniLabel);

            EditorGUI.BeginDisabledGroup(mats.Length == 0);
            if (GUILayout.Button($"恢复选中 {mats.Length} 个材质球的丢失贴图", GUILayout.Height(28)))
                RecoverLostTextures(mats);
            EditorGUI.EndDisabledGroup();
        }

        void RecoverLostTextures(Material[] mats)
        {
            Undo.RecordObjects(mats, "Recover Lost Textures");
            int totalRecovered = 0;

            foreach (var mat in mats)
            {
                var so = new SerializedObject(mat);
                int matRecovered = 0;

                var savedTex = ReadSerializedTextures(so);
                var savedColors = ReadSerializedColors(so);
                var savedFloats = ReadSerializedFloats(so);

                foreach (var (src, dst) in TexRules.PropertyMigrations)
                {
                    if (!mat.HasProperty(dst)) continue;

                    if (savedTex.TryGetValue(src, out var tex) && mat.GetTexture(dst) == null)
                    {
                        mat.SetTexture(dst, tex);
                        matRecovered++;
                        Debug.Log($"[Bento] {mat.name}: 贴图 {src} → {dst} ({tex.name})");
                    }

                    if (savedColors.TryGetValue(src, out var col))
                    {
                        var currentCol = mat.GetColor(dst);
                        if (currentCol != col && col != Color.clear)
                        {
                            mat.SetColor(dst, col);
                            matRecovered++;
                            Debug.Log($"[Bento] {mat.name}: 颜色 {src} → {dst}");
                        }
                    }

                    if (savedFloats.TryGetValue(src, out var f))
                    {
                        float currentF = mat.GetFloat(dst);
                        if (Mathf.Abs(currentF - f) > 0.001f)
                        {
                            mat.SetFloat(dst, f);
                            matRecovered++;
                            Debug.Log($"[Bento] {mat.name}: 数值 {src} → {dst} ({f})");
                        }
                    }
                }

                if (matRecovered > 0)
                {
                    EnableKeywordsForTextures(mat);
                    EditorUtility.SetDirty(mat);
                    totalRecovered += matRecovered;
                }
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[Bento] 恢复完成，共恢复 {totalRecovered} 个属性");

            if (totalRecovered == 0)
                EditorUtility.DisplayDialog("恢复结果",
                    "未找到可恢复的属性。\n\n可能原因：\n• 旧数据已被清理\n• 属性已在正确位置\n\n可以用「填充材质球」标签页手动填充。", "知道了");
        }

        static Dictionary<string, Texture> ReadSerializedTextures(SerializedObject so)
        {
            var dict = new Dictionary<string, Texture>();
            var arr = so.FindProperty("m_SavedProperties.m_TexEnvs");
            if (arr == null) return dict;
            for (int i = 0; i < arr.arraySize; i++)
            {
                var el = arr.GetArrayElementAtIndex(i);
                var name = el.FindPropertyRelative("first").stringValue;
                var tex = el.FindPropertyRelative("second.m_Texture").objectReferenceValue as Texture;
                if (tex != null) dict[name] = tex;
            }
            return dict;
        }

        static Dictionary<string, Color> ReadSerializedColors(SerializedObject so)
        {
            var dict = new Dictionary<string, Color>();
            var arr = so.FindProperty("m_SavedProperties.m_Colors");
            if (arr == null) return dict;
            for (int i = 0; i < arr.arraySize; i++)
            {
                var el = arr.GetArrayElementAtIndex(i);
                var name = el.FindPropertyRelative("first").stringValue;
                var col = el.FindPropertyRelative("second");
                dict[name] = new Color(
                    col.FindPropertyRelative("r").floatValue,
                    col.FindPropertyRelative("g").floatValue,
                    col.FindPropertyRelative("b").floatValue,
                    col.FindPropertyRelative("a").floatValue);
            }
            return dict;
        }

        static Dictionary<string, float> ReadSerializedFloats(SerializedObject so)
        {
            var dict = new Dictionary<string, float>();
            var arr = so.FindProperty("m_SavedProperties.m_Floats");
            if (arr == null) return dict;
            for (int i = 0; i < arr.arraySize; i++)
            {
                var el = arr.GetArrayElementAtIndex(i);
                var name = el.FindPropertyRelative("first").stringValue;
                dict[name] = el.FindPropertyRelative("second").floatValue;
            }
            return dict;
        }

        static void EnableKeywordsForTextures(Material mat)
        {
            TexRules.EnableKeywordsForTextures(mat);
        }

        void ShowShaderDropdown()
        {
            TexRules.ShowShaderDropdown(s => _targetShader = s, _targetShader);
        }
    }
}

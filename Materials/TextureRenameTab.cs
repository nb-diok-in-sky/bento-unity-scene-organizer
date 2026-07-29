using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Bento
{
    class TextureRenameTab
    {
        readonly SharedContext _ctx;
        Vector2 _scroll;

        public TextureRenameTab(SharedContext ctx) => _ctx = ctx;

        public void Draw()
        {
            if (!_ctx.scanDone)
            {
                EditorGUILayout.HelpBox("请先在「贴图扫描」标签页中扫描贴图", MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField("改名预览", EditorStyles.boldLabel);
            EditorGUILayout.Space(2);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            foreach (var kv in _ctx.groups.OrderBy(g => g.Key))
            {
                EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
                EditorGUILayout.LabelField("■ " + kv.Key, EditorStyles.boldLabel);
                EditorGUILayout.EndHorizontal();

                foreach (var tex in kv.Value)
                {
                    string newName = GetNewName(tex);
                    bool changed = tex.nameNoExt != newName;

                    EditorGUILayout.BeginHorizontal();
                    tex.include = EditorGUILayout.Toggle(tex.include, GUILayout.Width(18));

                    if (tex.type == TexType.Unknown)
                        GUI.contentColor = new Color(1f, 0.7f, 0.2f);
                    else if (changed)
                        GUI.contentColor = new Color(0.5f, 1f, 0.5f);

                    string ext = Path.GetExtension(tex.assetPath);
                    EditorGUILayout.LabelField(tex.nameNoExt + ext, GUILayout.Width(230));
                    EditorGUILayout.LabelField("→", GUILayout.Width(18));
                    EditorGUILayout.LabelField(changed ? newName + ext : "(不变)");

                    GUI.contentColor = Color.white;
                    EditorGUILayout.EndHorizontal();
                }
                EditorGUILayout.Space(2);
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.Space(6);

            int renameCount = _ctx.textures.Count(t => t.include && t.type != TexType.Unknown && t.nameNoExt != GetNewName(t));
            EditorGUI.BeginDisabledGroup(renameCount == 0);
            if (GUILayout.Button($"执行改名（{renameCount} 个文件）", GUILayout.Height(30)))
                ExecuteRename();
            EditorGUI.EndDisabledGroup();
        }

        string GetNewName(TexInfo tex)
        {
            if (tex.type == TexType.Unknown) return tex.nameNoExt;
            if (!_ctx.outputSuffixes.TryGetValue(tex.type, out var outSuffix)) return tex.nameNoExt;
            if (string.IsNullOrEmpty(outSuffix)) return tex.baseName;
            return tex.baseName + "_" + outSuffix;
        }

        void ExecuteRename()
        {
            var targets = _ctx.textures
                .Where(tex => tex.include && tex.type != TexType.Unknown && tex.nameNoExt != GetNewName(tex))
                .ToList();
            var plan = targets.Select(tex => SafeAssetRenameService.BuildEntry(tex.assetPath, GetNewName(tex))).ToList();
            var history = new AssetRenameHistory();
            try
            {
                SafeAssetRenameService.Execute(plan, history);
                for (int index = 0; index < targets.Count; index++)
                {
                    targets[index].assetPath = plan[index].targetPath;
                    targets[index].nameNoExt = Path.GetFileNameWithoutExtension(plan[index].targetPath);
                }
                Debug.Log($"[Bento] 已安全改名 {targets.Count} 个文件，GUID 验证通过");
            }
            catch (System.Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("重命名中断", exception.Message, "确定");
            }
        }
    }
}

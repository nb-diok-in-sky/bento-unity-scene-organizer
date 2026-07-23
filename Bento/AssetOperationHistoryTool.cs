using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Bento
{
    sealed class AssetOperationHistoryTool
    {
        IReadOnlyList<AssetOperationRecord> _records = Array.Empty<AssetOperationRecord>();
        Vector2 _scroll;

        public void Draw()
        {
            EditorGUILayout.LabelField("资产操作历史", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "历史保存在本机 Library/Bento，不会上传 Git。当前支持恢复资产移动；后续重命名和材质操作可接入同一套历史。",
                MessageType.Info);

            if (GUILayout.Button("刷新历史")) Refresh();
            if (_records.Count == 0) Refresh();

            _scroll = EditorGUILayout.BeginScrollView(_scroll, EditorStyles.helpBox, GUILayout.MinHeight(260));
            foreach (var record in _records) DrawRecord(record);
            if (_records.Count == 0) EditorGUILayout.LabelField("暂无可用历史。", EditorStyles.miniLabel);
            EditorGUILayout.EndScrollView();
        }

        void DrawRecord(AssetOperationRecord record)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(record.displayName, EditorStyles.boldLabel);
                EditorGUILayout.LabelField(ToLocalTime(record.createdAtUtc), EditorStyles.miniLabel);
                EditorGUILayout.LabelField($"类型：{record.operationType}    数量：{record.items.Count}", EditorStyles.miniLabel);
                if (record.restored)
                {
                    EditorGUILayout.HelpBox("已恢复", MessageType.Info);
                    return;
                }

                if (GUILayout.Button("按 GUID 恢复此批操作") && BentoCommand.Confirm(
                    "恢复资产操作",
                    $"将恢复“{record.displayName}”。\n工具会按 GUID 定位当前资产，并恢复到操作前路径。",
                    "开始恢复"))
                {
                    BentoCommand.Run("恢复失败", () => AssetOperationHistory.Restore(record.id), () => "恢复完成，GUID 验证通过。");
                    Refresh();
                }
            }
        }

        void Refresh() => _records = AssetOperationHistory.LoadAll();

        static string ToLocalTime(string utc)
        {
            return DateTime.TryParse(utc, out var value) ? value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") : utc;
        }
    }
}

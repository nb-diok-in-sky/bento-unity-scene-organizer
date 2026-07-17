using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Bento
{
    static class AssetMoveManifestService
    {

        public static string ExportPlan(IReadOnlyList<AssetMoveEntry> entries)
        {
            var moves = entries.Where(entry => entry.include).Select(entry => new AssetMoveHistoryEntry
            {
                guid = entry.sourceGuid,
                oldPath = entry.sourcePath,
                newPath = entry.targetPath
            });
            return Export("移动前预览", moves, false);
        }


        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string outputRoot = Path.Combine(projectRoot, OutputFolder);
            Directory.CreateDirectory(outputRoot);
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var manifest = new AssetMoveManifest
            {
                createdAtUtc = DateTime.UtcNow.ToString("O"),
                projectRoot = projectRoot,
                moves = sourceMoves.Select(Clone).ToList()
            };
            string baseName = $"{stamp}_{label}";
            string jsonPath = Path.Combine(outputRoot, baseName + ".json");
            File.WriteAllText(jsonPath, JsonUtility.ToJson(manifest, true), new UTF8Encoding(true));
            File.WriteAllText(Path.Combine(outputRoot, baseName + ".txt"), BuildReadableList(manifest), new UTF8Encoding(true));
            AssetDatabase.Refresh();
            EditorUtility.RevealInFinder(jsonPath);
            return jsonPath;
        }

        static AssetMoveHistoryEntry Clone(AssetMoveHistoryEntry move) => new AssetMoveHistoryEntry
        {
            guid = move.guid,
            oldPath = move.oldPath,
            newPath = move.newPath
        };

        static string BuildReadableList(AssetMoveManifest manifest)
        {
            var builder = new StringBuilder();
            builder.AppendLine($"生成时间：{manifest.createdAtUtc}");
            builder.AppendLine($"项目根目录：{manifest.projectRoot}");
            builder.AppendLine($"移动数量：{manifest.moves.Count}");
            builder.AppendLine();
            for (int i = 0; i < manifest.moves.Count; i++)
            {
                var move = manifest.moves[i];
                builder.AppendLine($"[{i + 1}] {move.guid}");
                builder.AppendLine($"  原位置：{move.oldPath}");
                builder.AppendLine($"  新位置：{move.newPath}");
            }
            return builder.ToString();
        }

    }
}

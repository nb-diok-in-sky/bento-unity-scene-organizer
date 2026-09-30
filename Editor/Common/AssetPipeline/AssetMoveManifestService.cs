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
        const string LastFolderKey = "Bento.AssetMoveManifest.LastFolder";

        /// <summary>
        /// Asks where to save the manifest of the included entries and writes a JSON file plus a readable
        /// .txt next to it. Returns the JSON path, or null when the user cancels.
        /// </summary>
        public static string ExportPlan(IReadOnlyList<AssetMoveEntry> entries)
        {
            var moves = entries.Where(entry => entry.include).Select(entry => new AssetMoveHistoryEntry
            {
                guid = entry.sourceGuid,
                oldPath = entry.sourcePath,
                newPath = entry.targetPath
            });
            return Export("移动前预览", moves);
        }

        static string Export(string label, IEnumerable<AssetMoveHistoryEntry> sourceMoves)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string lastFolder = EditorPrefs.GetString(LastFolderKey, string.Empty);
            string initialFolder = Directory.Exists(lastFolder) ? lastFolder : projectRoot;
            string defaultName = $"{DateTime.Now:yyyyMMdd_HHmmss}_{label}";
            string jsonPath = EditorUtility.SaveFilePanel("保存资产移动清单", initialFolder, defaultName, "json");
            if (string.IsNullOrEmpty(jsonPath)) return null;

            EditorPrefs.SetString(LastFolderKey, Path.GetDirectoryName(jsonPath));
            var manifest = new AssetMoveManifest
            {
                createdAtUtc = DateTime.UtcNow.ToString("O"),
                projectRoot = projectRoot,
                moves = sourceMoves.Select(Clone).ToList()
            };
            File.WriteAllText(jsonPath, JsonUtility.ToJson(manifest, true), new UTF8Encoding(true));
            File.WriteAllText(Path.ChangeExtension(jsonPath, ".txt"), BuildReadableList(manifest), new UTF8Encoding(true));
            if (IsInsideAssets(jsonPath)) AssetDatabase.Refresh();
            EditorUtility.RevealInFinder(jsonPath);
            return jsonPath;
        }

        static bool IsInsideAssets(string path)
        {
            string assets = Path.GetFullPath(Application.dataPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return Path.GetFullPath(path).StartsWith(assets, StringComparison.OrdinalIgnoreCase);
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

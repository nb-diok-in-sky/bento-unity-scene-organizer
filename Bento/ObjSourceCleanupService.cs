using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;

namespace Bento
{
    public sealed class ObjSourceCleanupPlan
    {
        public string objAssetPath;
        public string mtlAssetPath;
        public string fbxAssetPath;
        public string[] deletePaths = Array.Empty<string>();
        public string[] protectedDependencies = Array.Empty<string>();
    }

    public static class ObjSourceCleanupService
    {
        public static ObjSourceCleanupPlan BuildPlan(string objAssetPath, string mtlAssetPath, string fbxAssetPath)
        {
            ValidateSourcePath(objAssetPath, ".obj", "OBJ");
            ValidateSourcePath(mtlAssetPath, ".mtl", "MTL");
            if (string.IsNullOrEmpty(fbxAssetPath) || !fbxAssetPath.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase) ||
                AssetDatabase.LoadMainAssetAtPath(fbxAssetPath) == null)
                throw new InvalidOperationException("请选择已生成并可正常加载的 FBX。");

            string[] deletePaths = { objAssetPath, mtlAssetPath };
            string[] protectedDependencies = AssetDatabase.GetDependencies(fbxAssetPath, true)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var dependencySet = new HashSet<string>(protectedDependencies, StringComparer.OrdinalIgnoreCase);
            foreach (string deletePath in deletePaths)
            {
                if (dependencySet.Contains(deletePath))
                    throw new InvalidOperationException($"FBX 仍依赖待删除源文件，已中止：{deletePath}");
            }

            return new ObjSourceCleanupPlan
            {
                objAssetPath = objAssetPath,
                mtlAssetPath = mtlAssetPath,
                fbxAssetPath = fbxAssetPath,
                deletePaths = deletePaths,
                protectedDependencies = protectedDependencies
            };
        }

        public static void Execute(ObjSourceCleanupPlan plan)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            ObjSourceCleanupPlan freshPlan = BuildPlan(plan.objAssetPath, plan.mtlAssetPath, plan.fbxAssetPath);

            foreach (string path in freshPlan.deletePaths)
            {
                if (AssetDatabase.LoadMainAssetAtPath(path) == null)
                    throw new InvalidOperationException("待删除源文件已发生变化，请重新扫描：" + path);
            }

            var failed = new List<string>();
            foreach (string path in freshPlan.deletePaths)
            {
                if (!AssetDatabase.DeleteAsset(path)) failed.Add(path);
            }
            AssetDatabase.Refresh();

            if (failed.Count > 0)
                throw new InvalidOperationException("以下源文件删除失败：\n" + string.Join("\n", failed));
            foreach (string path in freshPlan.deletePaths)
            {
                if (AssetDatabase.LoadMainAssetAtPath(path) != null || File.Exists(ToAbsolutePath(path)))
                    throw new InvalidOperationException("源文件删除后仍然存在：" + path);
            }
            if (AssetDatabase.LoadMainAssetAtPath(freshPlan.fbxAssetPath) == null)
                throw new InvalidOperationException("删除后 FBX 无法加载，请立即从版本控制或回收站恢复源文件。");
        }

        static void ValidateSourcePath(string path, string extension, string label)
        {
            if (string.IsNullOrEmpty(path) || !path.EndsWith(extension, StringComparison.OrdinalIgnoreCase) ||
                AssetDatabase.LoadMainAssetAtPath(path) == null)
                throw new InvalidOperationException($"请选择有效的 {label} 源文件。");
        }

        static string ToAbsolutePath(string assetPath)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, ".."));
            return Path.Combine(projectRoot, assetPath);
        }
    }
}

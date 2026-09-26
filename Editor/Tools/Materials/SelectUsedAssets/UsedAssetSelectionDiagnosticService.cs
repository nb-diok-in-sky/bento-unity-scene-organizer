using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Bento
{
    static class UsedAssetSelectionDiagnosticService
    {
        public static string BuildFromCurrentSelection()
        {
            return Build(UsedAssetSelectionScanner.ResolveCurrentRoots());
        }

        public static string BuildByObjectName(string objectName)
        {
            List<GameObject> roots = UnityEngine.Object.FindObjectsOfType<GameObject>(true)
                .Where(gameObject => gameObject.scene.IsValid() &&
                                     string.Equals(gameObject.name, objectName, System.StringComparison.Ordinal))
                .ToList();
            return Build(roots);
        }

        static string Build(IReadOnlyList<GameObject> roots)
        {
            UsedAssetSelectionResult result = UsedAssetSelectionScanner.Scan(roots);
            var lines = new List<string>
            {
                $"[Bento] 贴图扫描诊断：根物体 {result.roots.Count}"
            };
            lines.AddRange(result.textureTraces.Select(trace =>
                $"{(trace.included ? "保留" : "排除")} | " +
                $"Renderer={trace.renderer} | Material={trace.material} | Property={trace.property} | " +
                $"Texture={trace.texture} | Type={trace.type} | SubAsset={trace.isSubAsset} | Path={trace.path}"));
            return string.Join("\n", lines);
        }
    }
}

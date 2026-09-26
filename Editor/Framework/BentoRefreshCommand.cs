using System;
using System.Diagnostics;
using UnityEditor;
using Debug = UnityEngine.Debug;

namespace Bento
{
    static class BentoRefreshCommand
    {
        [MenuItem("Tools/Bento/刷新并输出日志", priority = -9990)]
        public static void RefreshWithFeedback()
        {
            var stopwatch = Stopwatch.StartNew();
            Debug.Log("[Bento Refresh] 开始刷新 AssetDatabase……");
            try
            {
                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
                stopwatch.Stop();
                Debug.Log($"[Bento Refresh] 刷新完成，耗时 {stopwatch.Elapsed.TotalSeconds:F2} 秒。");
            }
            catch (Exception exception)
            {
                stopwatch.Stop();
                Debug.LogError($"[Bento Refresh] 刷新失败，耗时 {stopwatch.Elapsed.TotalSeconds:F2} 秒。\n{exception}");
            }
        }
    }
}

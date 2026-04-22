#if FBF_NDMF
using UnityEditor;
using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace Triturbo.FaceBlendShapeFix
{
    [InitializeOnLoad]
    internal static class FaceBlendShapeFixPreviewProfiling
    {
        private const string MenuPath = "Tools/Triturbo/Face BlendShape Fix/Preview Profiling Logs";
        private const string PrefKey = "triturbo.face-blendshape-fix.preview-profiling-logs";
        private const int SampleWindow = 120;

        private static bool s_enabled;
        private static int s_frameSamples;
        private static long s_frameTicks;
        private static int s_refreshSamples;
        private static long s_refreshTicks;
        private static int s_meshRebuildCount;
        private static int s_cacheRebuildCount;
        private static int s_appliedTargetCount;
        private static int s_cachedTargetCount;
        private static int s_generatedBlendShapeCount;

        internal static bool Enabled => s_enabled;

        static FaceBlendShapeFixPreviewProfiling()
        {
            s_enabled = EditorPrefs.GetBool(PrefKey, false);
            Menu.SetChecked(MenuPath, s_enabled);
        }

        [MenuItem(MenuPath, false, 2000)]
        private static void ToggleLogging()
        {
            s_enabled = !s_enabled;
            EditorPrefs.SetBool(PrefKey, s_enabled);
            Menu.SetChecked(MenuPath, s_enabled);
            ResetSamples();

            Debug.Log(
                $"[FaceBlendShapeFixPreview] Profiling logs {(s_enabled ? "enabled" : "disabled")}." +
                " Use the Unity Profiler for precise timing; logs are aggregated over 120 frames.");
        }

        [MenuItem(MenuPath, true)]
        private static bool ToggleLoggingValidate()
        {
            Menu.SetChecked(MenuPath, s_enabled);
            return true;
        }

        internal static void RecordFrame(
            long elapsedTicks,
            int appliedTargetCount,
            int cachedTargetCount,
            int generatedBlendShapeCount)
        {
            if (!s_enabled)
            {
                return;
            }

            s_frameSamples++;
            s_frameTicks += elapsedTicks;
            s_appliedTargetCount += appliedTargetCount;
            s_cachedTargetCount += cachedTargetCount;
            s_generatedBlendShapeCount += generatedBlendShapeCount;

            FlushIfReady();
        }

        internal static void RecordRefresh(
            long elapsedTicks,
            bool meshRebuilt,
            bool cacheRebuilt)
        {
            if (!s_enabled)
            {
                return;
            }

            s_refreshSamples++;
            s_refreshTicks += elapsedTicks;
            if (meshRebuilt)
            {
                s_meshRebuildCount++;
            }

            if (cacheRebuilt)
            {
                s_cacheRebuildCount++;
            }
        }

        private static void FlushIfReady()
        {
            if (s_frameSamples < SampleWindow)
            {
                return;
            }

            double averageFrameMs = ToMilliseconds(s_frameTicks) / s_frameSamples;
            double averageRefreshMs = s_refreshSamples > 0
                ? ToMilliseconds(s_refreshTicks) / s_refreshSamples
                : 0d;
            float averageAppliedTargets = s_frameSamples > 0
                ? (float)s_appliedTargetCount / s_frameSamples
                : 0f;
            float averageCachedTargets = s_frameSamples > 0
                ? (float)s_cachedTargetCount / s_frameSamples
                : 0f;
            float averageGeneratedBlendShapes = s_frameSamples > 0
                ? (float)s_generatedBlendShapeCount / s_frameSamples
                : 0f;

            Debug.Log(
                "[FaceBlendShapeFixPreview] " +
                $"samples={s_frameSamples}, " +
                $"avg OnFrame={averageFrameMs:F4} ms, " +
                $"refresh samples={s_refreshSamples}, " +
                $"avg Refresh={averageRefreshMs:F4} ms, " +
                $"mesh rebuilds={s_meshRebuildCount}, " +
                $"cache rebuilds={s_cacheRebuildCount}, " +
                $"avg applied targets/frame={averageAppliedTargets:F2}, " +
                $"avg cached targets/frame={averageCachedTargets:F2}, " +
                $"avg generated blendshapes/frame={averageGeneratedBlendShapes:F2}");

            ResetSamples();
        }

        private static double ToMilliseconds(long ticks)
        {
            return ticks * 1000d / Stopwatch.Frequency;
        }

        private static void ResetSamples()
        {
            s_frameSamples = 0;
            s_frameTicks = 0;
            s_refreshSamples = 0;
            s_refreshTicks = 0;
            s_meshRebuildCount = 0;
            s_cacheRebuildCount = 0;
            s_appliedTargetCount = 0;
            s_cachedTargetCount = 0;
            s_generatedBlendShapeCount = 0;
        }
    }
}
#endif

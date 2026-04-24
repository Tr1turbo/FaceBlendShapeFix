using System.Diagnostics;
using UnityEngine;

namespace Triturbo.FaceBlendShapeFix
{
    internal static class FaceBlendShapeFixDiagnostics
    {
        internal const double DefaultSlowOperationThresholdMs = 8d;

        internal static double ToMilliseconds(long ticks)
        {
            return ticks * 1000d / Stopwatch.Frequency;
        }

        internal static void LogIfSlow(
            string category,
            double elapsedMilliseconds,
            string details = null,
            double thresholdMilliseconds = DefaultSlowOperationThresholdMs)
        {
            if (!FaceBlendShapeFixEditorSettings.DiagnosticsLoggingEnabled ||
                elapsedMilliseconds < thresholdMilliseconds)
            {
                return;
            }

            if (string.IsNullOrEmpty(details))
            {
                UnityEngine.Debug.Log($"[FaceBlendShapeFix] {category}: {elapsedMilliseconds:F2} ms");
                return;
            }

            UnityEngine.Debug.Log($"[FaceBlendShapeFix] {category}: {elapsedMilliseconds:F2} ms ({details})");
        }
    }
}

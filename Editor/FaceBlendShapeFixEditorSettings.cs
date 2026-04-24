using UnityEditor;
using UnityEngine;

namespace Triturbo.FaceBlendShapeFix
{
    internal static class FaceBlendShapeFixEditorSettings
    {
        private const string PassivePreviewPrefKey = "triturbo.face-blendshape-fix.passive-preview-enabled";
        private const string DiagnosticsLoggingPrefKey = "triturbo.face-blendshape-fix.diagnostics-logging-enabled";
        private const string EnableBlendDataScrollPrefKey = "triturbo.face-blendshape-fix.enable-blend-data-scroll";
        private const string BlendDataScrollHeightPrefKey = "triturbo.face-blendshape-fix.blend-data-scroll-height";
        private const float DefaultBlendDataScrollHeight = 240f;
        internal const float MinimumBlendDataScrollHeight = 64f;

        internal static bool PassivePreviewEnabled
        {
            get => EditorPrefs.GetBool(PassivePreviewPrefKey, true);
            set => EditorPrefs.SetBool(PassivePreviewPrefKey, value);
        }

        internal static bool DiagnosticsLoggingEnabled
        {
            get => EditorPrefs.GetBool(DiagnosticsLoggingPrefKey, false);
            set => EditorPrefs.SetBool(DiagnosticsLoggingPrefKey, value);
        }

        internal static bool BlendDataScrollEnabled
        {
            get => EditorPrefs.GetBool(EnableBlendDataScrollPrefKey, true);
            set => EditorPrefs.SetBool(EnableBlendDataScrollPrefKey, value);
        }

        internal static float BlendDataScrollHeight
        {
            get => Mathf.Max(
                MinimumBlendDataScrollHeight,
                EditorPrefs.GetFloat(BlendDataScrollHeightPrefKey, DefaultBlendDataScrollHeight));
            set => EditorPrefs.SetFloat(
                BlendDataScrollHeightPrefKey,
                Mathf.Max(MinimumBlendDataScrollHeight, value));
        }

        internal static GUIContent PassivePreviewContent => Localization.G("editor.preview.passive_preview");
        internal static GUIContent DiagnosticsLoggingContent => Localization.G("editor.diagnostics_logging");
        internal static GUIContent BlendDataScrollEnabledContent => Localization.G("editor.blend_data_scroll_enabled");
        internal static GUIContent BlendDataScrollHeightContent => Localization.G("editor.blend_data_scroll_height");
    }
}

#if FBF_NDMF
using UnityEditor;
using UnityEngine;

namespace Triturbo.FaceBlendShapeFix
{
    internal static class FaceBlendShapeFixPreviewSettings
    {
        // Stored in EditorPrefs so the passive-preview behavior is global to the editor rather than
        // serialized onto each component, prefab, or scene.
        private const string PassivePreviewPrefKey = "triturbo.face-blendshape-fix.passive-preview-enabled";

        internal static bool PassivePreviewEnabled
        {
            get => EditorPrefs.GetBool(PassivePreviewPrefKey, true);
            set => EditorPrefs.SetBool(PassivePreviewPrefKey, value);
        }

        internal static GUIContent PassivePreviewContent => Localization.G("editor.preview.passive_preview");
    }
}
#endif

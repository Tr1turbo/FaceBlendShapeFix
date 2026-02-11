using System;
using UnityEngine;

#if FBF_VRCSDK_AVATARS
using VRC.SDKBase;
#endif

namespace Triturbo.FaceBlendShapeFix.Runtime
{
    [AddComponentMenu("Triturbo/Face BlendShape Fix")]
    public class FaceBlendShapeFixComponent : MonoBehaviour
#if FBF_VRCSDK_AVATARS
    , IEditorOnly
#endif
    {
        public SkinnedMeshRenderer m_TargetRenderer;
        public SkinnedMeshRenderer TargetRenderer
        {
            get
            {
                if (m_TargetRenderer == null)
                    return transform.GetComponent<SkinnedMeshRenderer>();

                return m_TargetRenderer;
            }
        }

        public BlendShapeDefinition[] m_BlendShapeDefinitions;

        [Tooltip("Databases used to categorize BlendShapes when auto-filling target entries.")]
        public BlendShapeCategoryDatabase[] m_CategoryDatabases;

        public BlendShapeCategoryDatabase m_CategoryDatabase;

        [Tooltip("Additional category names that can be assigned directly on the component.")]
        public string[] m_CustomCategoryNames;

        [NonReorderable]
        public TargetShape[] m_TargetShapes;

        [Tooltip("Width of the transition zone for L/R blend shape splitting. 0 = hard cut at center.")]
        [Range(0f, 0.1f)]
        public float m_SmoothWidth = 0f;


        public InspectorSettings m_InspectorSettings;

        private void Reset()
        {
            m_CategoryDatabases = new[] { m_CategoryDatabase };
        }



        private int _previewTargetIndex = -1;
        private float _previewWeight = 0f;

        public void BeginPreview(int targetIndex, float weight)
        {
            _previewTargetIndex = targetIndex;
            _previewWeight = weight;
        }

        public void UpdateWeight(float weight)
        {
            _previewWeight = weight;
        }

        public void EndPreview()
        {
            _previewTargetIndex = -1;
            _previewWeight = 0f;
        }

        public bool TryGetPreviewRequest(out PreviewRequest request)
        {

            if (m_TargetShapes != null &&
                _previewTargetIndex >= 0 &&
                _previewTargetIndex < m_TargetShapes.Length)
            {
                request = new PreviewRequest(this, _previewTargetIndex, _previewWeight);
                return true;
            }
            request = default;

            return false;
        }
    }

    public readonly struct PreviewRequest
    {
        private readonly int _targetIndex;

        public int TargetIndex => IsPreviewEnable ?  _targetIndex : -1;

        public FaceBlendShapeFixComponent Component { get; }

        public float Weight { get; }

        public TargetShape Target =>
            Component?.m_TargetShapes != null &&
            _targetIndex >= 0 &&
            _targetIndex < Component.m_TargetShapes.Length
                ? Component.m_TargetShapes[_targetIndex]
                : null;


        public bool IsPreviewEnable => Component?.TargetRenderer != null && Component.isActiveAndEnabled;

        public PreviewRequest(FaceBlendShapeFixComponent component, int targetIndex, float weight)
        {
            Component = component;
            _targetIndex = targetIndex;
            Weight = weight;
        }
    }

    [Serializable]
    public class InspectorSettings
    {
        public bool m_EnableBlendDataScroll = true;
        public float m_BlendDataScrollHeight = 240f;
        public bool m_UseBlendDataReorderableList = false;
        public bool m_EnableDrawProfiling = false;
    }
}

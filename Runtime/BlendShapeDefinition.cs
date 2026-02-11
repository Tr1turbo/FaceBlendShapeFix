using System;
using UnityEngine;

namespace Triturbo.FaceBlendShapeFix.Runtime
{
    [Serializable]
    public class BlendShapeDefinition
    {
        public string m_BlendShapeName;
        public float m_LeftEyeWeight;
        public float m_RightEyeWeight;
        public float m_MouthWeight;
        public bool m_Protected;

        public BlendData ResolveBlendData(ShapeType targetShapeType)
        {
            BlendData data;
            switch (targetShapeType)
            {
                case ShapeType.BothEyes:
                    data = new BlendData
                    {
                        m_TargetShapeName =  m_BlendShapeName,
                        m_Weight = (m_LeftEyeWeight + m_RightEyeWeight) * 0.5f,
                        m_LeftWeight = m_LeftEyeWeight,
                        m_RightWeight = m_RightEyeWeight,
                        m_SplitLeftRight = !Mathf.Approximately(m_LeftEyeWeight, m_RightEyeWeight),
                    };
                    break;
                case ShapeType.LeftEye:
                    data = new BlendData
                    {
                        m_TargetShapeName =  m_BlendShapeName,
                        m_Weight = m_LeftEyeWeight,
                        m_LeftWeight = m_LeftEyeWeight,
                        m_RightWeight = 0f,
                        m_SplitLeftRight = true
                    };
                    break;
                case ShapeType.RightEye:
                    data = new BlendData
                    {
                        m_TargetShapeName =  m_BlendShapeName,
                        m_Weight = m_RightEyeWeight,
                        m_LeftWeight = 0f,
                        m_RightWeight = m_RightEyeWeight,
                        m_SplitLeftRight = true
                    };
                    break;
                case ShapeType.Mouth:
                    data = new BlendData
                    {
                        m_TargetShapeName =  m_BlendShapeName,
                        m_Weight = m_MouthWeight,
                        m_LeftWeight = m_MouthWeight,
                        m_RightWeight = m_MouthWeight,
                        m_SplitLeftRight = false
                    };
                    break;
                case ShapeType.Others:
                default:
                    data = new BlendData
                    {
                        m_TargetShapeName =  m_BlendShapeName,
                        m_Weight = 0f,
                        m_LeftWeight = 0f,
                        m_RightWeight = 0f,
                        m_SplitLeftRight = false
                    };
                    break;
            }

            return data;
        }
    }
}

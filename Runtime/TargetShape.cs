using System;
using System.Collections.Generic;
using System.Linq;
//
namespace Triturbo.FaceBlendShapeFix.Runtime
{
    public enum ShapeType
    {
        BothEyes = 0,
        LeftEye = 1,
        RightEye = 2,
        Mouth = 3,
        Others = 4
    }

    [Serializable]
    public class TargetShape
    {
        public string m_TargetShapeName;
        public float m_Weight = 1;
        public string m_CategoryName;
        public bool m_UseGlobalDefinitions;
        public ShapeType m_TargetShapeType;

        //Blend with active blendshapes: subtractive blend
        [UnityEngine.SerializeField]
        public BlendData[] m_BlendData;

        //Blend with additional blendshapes: additive  blend
        //m_AdditiveBlendData
        public BlendData[] m_AdditiveBlendData;

        public List<BlendData> GetBlendData(BlendShapeDefinition[] definitions)
        {
            if (definitions == null)
            {
                return m_BlendData.ToList();
            }

            Dictionary<string, BlendShapeDefinition> definitionLookup = new();
            foreach (var definition in definitions)
            {
                definitionLookup[definition.m_BlendShapeName] = definition;
            }

            return GetBlendData(definitionLookup);
        }

        public List<BlendData> GetBlendData(IReadOnlyDictionary<string, BlendShapeDefinition> definitionLookup)
        {
            if (definitionLookup == null)
            {
                return m_BlendData.ToList();
            }

            List<BlendData> blendDataList = new List<BlendData>(m_BlendData.Length);
            foreach (var data in m_BlendData)
            {
                string currentName = data.m_TargetShapeName;

                if(currentName == m_TargetShapeName) continue;

                bool isProtected = false;

                if (definitionLookup.TryGetValue(currentName, out BlendShapeDefinition definition))
                {
                    isProtected =  definition.m_Protected;

                    if (!isProtected && m_UseGlobalDefinitions)
                    {
                        blendDataList.Add(definition.ResolveBlendData(m_TargetShapeType));
                        continue;
                    }
                }

                if (isProtected)
                {
                    blendDataList.Add(new BlendData()
                    {
                        m_TargetShapeName = currentName,
                        m_Weight = 0,
                        m_LeftWeight = 0,
                        m_RightWeight = 0,
                        m_SplitLeftRight = false
                    });
                }
                else
                {
                    blendDataList.Add(data);
                }
            }

            return blendDataList;
        }
    }
}

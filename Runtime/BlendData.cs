using System.Collections.Generic;
using System;


namespace Triturbo.FaceBlendShapeFix.Runtime
{
    [Serializable]
    public class BlendData
    {
        public string m_TargetShapeName;
        public float m_Weight;
        public float m_LeftWeight;
        public float m_RightWeight;
        public bool m_SplitLeftRight;
        
    
        
        public bool IsProtected(BlendShapeDefinition[] definitions)
        {
            if (definitions == null || string.IsNullOrEmpty(m_TargetShapeName))
            {
                return false;
            }
        
            for (int i = 0; i < definitions.Length; i++)
            {
                BlendShapeDefinition definition = definitions[i];
                if (definition != null && definition.m_BlendShapeName == m_TargetShapeName)
                {
                    return definition.m_Protected;
                }
            }
        
            return false;
        }
        
        public bool IsProtected(IReadOnlyDictionary<string, BlendShapeDefinition> definitionLookup)
        {
            if (definitionLookup == null || string.IsNullOrEmpty(m_TargetShapeName))
            {
                return false;
            }
        
            if (definitionLookup.TryGetValue(m_TargetShapeName, out BlendShapeDefinition definition))
            {
                return definition.m_Protected;
            }
            return false;
        }
    }
}
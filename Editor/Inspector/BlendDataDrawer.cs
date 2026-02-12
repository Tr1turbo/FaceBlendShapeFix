using Triturbo.FaceBlendShapeFix.Runtime;
using UnityEngine;
using UnityEditor;
using System.Drawing;

namespace Triturbo.FaceBlendShapeFix.Inspector
{
    [CustomPropertyDrawer(typeof(BlendData))]
    public class BlendDataDrawer : PropertyDrawer
    {
        private static GUIContent UnlinkedLabel =>EditorGUIUtility.IconContent("UnLinked");
        private static GUIContent LinkedLabel => EditorGUIUtility.IconContent("Linked");

        private static string LeftLabel => Localization.Get("editor.blend_data.left", "L");
        private static string RightLabel => Localization.Get("editor.blend_data.right", "R");


        private struct LayoutMetrics
        {
            private const float Padding = 3f;
            private const float ButtonWidth = 16f;
            private const float ContentIndent = 8f;
            private const float SideLabelWidth = 36f;
            private const float SideLabelGap = 10f;


            public Rect buttonRect;
            public Rect labelRect;

            public Rect sideBoxRect;
            public Rect sliderRect;


            public Rect ButtonRect(float y) => new Rect(buttonRect.x, buttonRect.y + y, buttonRect.width, buttonRect.height);
            public Rect LabelRect(float y) => new Rect(labelRect.x, labelRect.y+ y, labelRect.width, labelRect.height);
            public Rect SideBoxRect(float y) => new Rect(sideBoxRect.x, sideBoxRect.y+ y, sideBoxRect.width, sideBoxRect.height);
            public Rect SliderRect(float y) => new Rect(sliderRect.x, sliderRect.y + y, sliderRect.width, sliderRect.height);



            public LayoutMetrics(Rect position)
            {
                float lineHeight = EditorGUIUtility.singleLineHeight;
                float labelWidth = EditorGUIUtility.labelWidth;
                const float kSpacing = 5f;

                float contentX = ContentIndent + position.x;
                float contentY = position.y + EditorGUIUtility.standardVerticalSpacing * 0.5f;

                float indent = EditorGUI.indentLevel * 15;

                buttonRect = new Rect(contentX, contentY, ButtonWidth, lineHeight);
                labelRect = new Rect(buttonRect.xMax + Padding, contentY, labelWidth, lineHeight);
                sliderRect = new Rect(
                    contentX + labelWidth, 
                    contentY, 
                    position.width - labelWidth - kSpacing - 2f, 
                    lineHeight
                );

                //minimum label width = 120
                float sidelabelWidth = Mathf.Min(SideLabelWidth, labelWidth - 120f);
                sideBoxRect = new Rect(
                    sliderRect.x - sidelabelWidth - SideLabelGap + indent, 
                    contentY, 
                    sidelabelWidth, 
                    lineHeight
                );
            }
        }



        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var nameProp = property.FindPropertyRelative(nameof(BlendData.m_TargetShapeName));
            var weightProp = property.FindPropertyRelative(nameof(BlendData.m_Weight));
            var leftProp = property.FindPropertyRelative(nameof(BlendData.m_LeftWeight));
            var rightProp = property.FindPropertyRelative(nameof(BlendData.m_RightWeight));
            var splitProp = property.FindPropertyRelative(nameof(BlendData.m_SplitLeftRight));

            string displayName = string.IsNullOrEmpty(nameProp.stringValue) ? "N/A" : nameProp.stringValue;

            if (string.IsNullOrEmpty(nameProp.stringValue))
            {
                EditorGUI.BeginProperty(position, null, property);
                EditorGUI.HelpBox(position, Localization.Get("editor.blend_data.invalid"), MessageType.Warning);
                EditorGUI.EndProperty();
                return;
            }

            var metrics = new LayoutMetrics(position);

            var value = new BlendData
            {
                m_TargetShapeName = nameProp.stringValue,
                m_Weight = weightProp.floatValue,
                m_LeftWeight = leftProp.floatValue,
                m_RightWeight = rightProp.floatValue,
                m_SplitLeftRight = splitProp.boolValue
            };

            EditorGUI.BeginProperty(position, null, property);
            EditorGUI.BeginChangeCheck();
            var newValue = DrawInternal(position, metrics, displayName, value);
            if (EditorGUI.EndChangeCheck())
            {
                splitProp.boolValue = newValue.m_SplitLeftRight;
                weightProp.floatValue = newValue.m_Weight;
                leftProp.floatValue = newValue.m_LeftWeight;
                rightProp.floatValue = newValue.m_RightWeight;
            }
            EditorGUI.EndProperty();
        }

        /// <summary>
        /// Draws a BlendData value directly (non-serialized) and returns the modified value.
        /// </summary>
        /// <param name="position">The rect to draw in.</param>
        /// <param name="value">The BlendData value to draw.</param>
        /// <param name="undoTarget">Optional object to record for Undo. If null, no undo is recorded.</param>
        /// <param name="undoName">The name for the undo operation.</param>
        internal static BlendData Draw(Rect position, BlendData value)
        {
            string displayName = string.IsNullOrEmpty(value.m_TargetShapeName) ? "N/A" : value.m_TargetShapeName;
            var metrics = new LayoutMetrics(position);

            var newValue = new BlendData
            {
                m_TargetShapeName = value.m_TargetShapeName,
                m_SplitLeftRight = value.m_SplitLeftRight,
                m_LeftWeight = value.m_LeftWeight,
                m_RightWeight = value.m_RightWeight,
                m_Weight = value.m_Weight
            };
            return DrawInternal(position, metrics, displayName, newValue);
        } 

        internal static void DrawProtected(Rect position, GUIContent button, GUIContent displayName)
        {
            var metrics = new LayoutMetrics(position);
            GUI.Label(metrics.buttonRect, button, EditorStyles.iconButton);
            GUI.Label(metrics.labelRect, displayName, EditorStyles.largeLabel);
            EditorGUI.Slider(metrics.sliderRect, 0, 0f, 1f);
        } 

        private static BlendData DrawInternal(Rect position, LayoutMetrics metrics, string displayName, BlendData value)
        {
            if (value.m_SplitLeftRight)
            {
                return DrawSplitMode(position, metrics, displayName, value);
            }
            else
            {
                return DrawUnifiedMode(position, metrics, displayName, value);
            }
        }


        private static BlendData DrawSplitMode(Rect position, LayoutMetrics metrics, string displayName, BlendData value)
        {
            // Line 1
            if (GUI.Button(metrics.buttonRect, UnlinkedLabel, EditorStyles.iconButton))
            {
                value.m_SplitLeftRight = false;
                
                value.m_LeftWeight = value.m_Weight;
                value.m_RightWeight = value.m_Weight;
            }

            GUI.Label(metrics.labelRect, displayName, EditorStyles.largeLabel);
            
            GUI.Box(metrics.sideBoxRect, GUIContent.none, EditorStyles.helpBox);
            GUI.Label(metrics.sideBoxRect, LeftLabel, EditorStyles.centeredGreyMiniLabel);
            value.m_LeftWeight = EditorGUI.Slider(metrics.sliderRect, value.m_LeftWeight, 0f, 1f);

            // Line 2
            float y = EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;

            GUI.Label(metrics.LabelRect(y), displayName, EditorStyles.largeLabel);
            GUI.Box(metrics.SideBoxRect(y), GUIContent.none, EditorStyles.helpBox);
            GUI.Label(metrics.SideBoxRect(y), RightLabel, EditorStyles.centeredGreyMiniLabel);
            value.m_RightWeight = EditorGUI.Slider(metrics.SliderRect(y), value.m_RightWeight, 0f, 1f);
            value.m_Weight = (value.m_LeftWeight + value.m_RightWeight) * 0.5f;

            return value;
        }

        private static BlendData DrawUnifiedMode(Rect position, LayoutMetrics metrics, string displayName, BlendData value)
        {
            if (GUI.Button(metrics.buttonRect, LinkedLabel, EditorStyles.iconButton))
            {
                value.m_SplitLeftRight = true;
            }
            GUI.Label(metrics.labelRect, displayName, EditorStyles.largeLabel);
            value.m_Weight = EditorGUI.Slider(metrics.sliderRect, value.m_Weight, 0f, 1f);
            value.m_LeftWeight = value.m_Weight;
            value.m_RightWeight = value.m_Weight;

            return value;
        }

        /// <summary>
        /// Gets the height for a BlendData value.
        /// </summary>
        public static float GetHeight(BlendData value)
        {
            float lineHeight = EditorGUIUtility.singleLineHeight;
            int lineCount = value.m_SplitLeftRight ? 2 : 1;
            return lineHeight * lineCount + EditorGUIUtility.standardVerticalSpacing * (lineCount - 1);
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            var splitProp = property.FindPropertyRelative(nameof(BlendData.m_SplitLeftRight));
            float lineHeight = EditorGUIUtility.singleLineHeight;
            int lineCount = splitProp.boolValue ? 2 : 1;
            return lineHeight * lineCount + EditorGUIUtility.standardVerticalSpacing * (lineCount - 1);
        }
    }
}

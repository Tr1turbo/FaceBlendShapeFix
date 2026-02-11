using UnityEditor;
using UnityEngine;

namespace Triturbo.FaceBlendShapeFix.Inspector
{
    internal static class Widgets
    {

        public static void SliderLayout(SerializedProperty property, GUIContent label, float leftValue=0f,  float rightValue=1f)
        {
            Rect rect = EditorGUILayout.GetControlRect();
            label = EditorGUI.BeginProperty(rect, label, property);
            property.floatValue = EditorGUI.Slider(rect, label, property.floatValue, leftValue, rightValue);
            EditorGUI.EndProperty();
        }
        public static void SliderLayout(SerializedProperty property, string label, float leftValue=0f,  float rightValue=1f)
        {
            SliderLayout(property,  new GUIContent(label), leftValue, rightValue);
        }

        public static float ReserveSpaceSlider(Rect rect, float value, GUIContent label, float leftValue,  float rightValue, 
        out Rect reserveRect, float reserveWidth, float spacing = 10f)
        {
            float indentOffset = EditorGUI.indentLevel * 15f;
            float labelWidth = EditorStyles.largeLabel.CalcSize(label).x + 7f;

            Rect sliderRect = EditorGUI.PrefixLabel(rect, label);

            if(sliderRect.x - rect.x < reserveWidth)
            {
                sliderRect.xMin += EditorGUIUtility.labelWidth - 13f;
            }


            sliderRect.xMin -= indentOffset;

            reserveWidth = Mathf.Min(reserveWidth, EditorGUIUtility.labelWidth - labelWidth - indentOffset);
            reserveWidth = Mathf.Max(reserveWidth, 0);

            reserveRect = new Rect(sliderRect.x - reserveWidth + indentOffset - spacing, rect.y, reserveWidth, rect.height);

            return EditorGUI.Slider(sliderRect, value, leftValue, rightValue);
        }

        public static void ToggleButton(SerializedProperty property, GUIContent label, GUIStyle style = null)
        {
            Rect rect = EditorGUILayout.GetControlRect();
            ToggleButton(rect, property, label, style);
        }

        public static void ToggleButton(Rect rect, SerializedProperty property, GUIContent label, GUIStyle style = null)
        {
            style ??= EditorStyles.miniButton;
            //Color prevColor = GUI.backgroundColor;
            // if (property.boolValue)
            // {
            //     GUI.backgroundColor = new Color(0.35f, 0.75f, 0.35f);
            // }

            //label = EditorGUI.BeginProperty(rect, label, property);
            if (GUI.Button(rect, label, style))
            {
                property.boolValue = !property.boolValue;
                property.serializedObject?.ApplyModifiedProperties();
            }

            //GUI.backgroundColor = prevColor;
            //EditorGUI.EndProperty();
        }

        public static float ToggleButtonWidth(GUIContent label, GUIStyle style = null)
        {
            style ??= EditorStyles.miniButton;
            Vector2 size = style.CalcSize(label);
            float paddedWidth = size.x + style.margin.horizontal + 6f;
            float minWidth = EditorGUIUtility.singleLineHeight + style.margin.horizontal;
            return Mathf.Max(paddedWidth, minWidth);
        }

        public static float ToggleButtonWidth(GUIContent onLabel, GUIContent offLabel, GUIStyle style = null)
        {
            return Mathf.Max(ToggleButtonWidth(onLabel, style), ToggleButtonWidth(offLabel, style));
        }

        public static void ToggleButton(Rect rect, SerializedProperty property, GUIContent onLabel, GUIContent offLabel, GUIStyle style = null)
        {
            GUIContent label = property.boolValue ? onLabel : offLabel;
            ToggleButton(rect, property, label, style);
        }
        
    }
}

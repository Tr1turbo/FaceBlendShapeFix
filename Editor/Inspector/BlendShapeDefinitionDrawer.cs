using System.Collections.Generic;
using Triturbo.FaceBlendShapeFix.Runtime;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using L = Triturbo.FaceBlendShapeFix.Localization;

namespace Triturbo.FaceBlendShapeFix.Inspector
{
    internal sealed class BlendShapeDefinitionDrawer
    {
        private static GUIContent SetAllZeroContent => L.G("editor.blend_data.zero_all");
        private static GUIContent SetAllOneContent => L.G("editor.blend_data.max_all");
        private static GUIContent SetNonZeroOneContent => L.G("editor.blend_data.non_zero_to_one");
        private static GUIContent AutoCalculateContent => L.G("editor.blend_data.auto_calculate");

        private readonly FaceBlendShapeFixEditor editor;
        private readonly List<int> activeDefinitionIndices = new List<int>();

        private SerializedProperty definitionsProperty;
        private ReorderableList activeDefinitionList;

        public BlendShapeDefinitionDrawer(FaceBlendShapeFixEditor editor)
        {
            this.editor = editor;
        }

        public void Initialize(SerializedProperty property)
        {
            definitionsProperty = property;
            if (definitionsProperty == null)
            {
                activeDefinitionList = null;
                return;
            }

            activeDefinitionList = new ReorderableList(activeDefinitionIndices, typeof(int), false, false, false, false)
            {
                //drawHeaderCallback = DrawHeader,
                drawElementCallback = DrawActiveDefinition,
                elementHeightCallback = GetElementHeight
            };
        }

        public void Draw(HashSet<string> activeBlendShapes)
        {
            if (definitionsProperty == null ||
                activeDefinitionList == null ||
                activeBlendShapes == null ||
                activeBlendShapes.Count == 0)
            {
                return;
            }

            Rect rect = EditorGUILayout.GetControlRect();
            
            
            var label = EditorGUI.BeginProperty(rect, L.G("editor.blend_shape_definitions"), definitionsProperty);
            
            
            definitionsProperty.isExpanded = EditorGUI.BeginFoldoutHeaderGroup(rect, definitionsProperty.isExpanded, label);
            if (definitionsProperty.isExpanded)
            {
                UpdateActiveIndices(activeBlendShapes);
                if (activeDefinitionIndices.Count > 0)
                {
                    using (new EditorGUI.DisabledScope(editor == null || editor.component == null))
                    {
                        DrawDefinitionBulkButtons();
                        activeDefinitionList.DoLayoutList();
                    }
                }
            }
            EditorGUI.EndFoldoutHeaderGroup();
            EditorGUI.EndProperty();
        }

        public void Reset()
        {
            activeDefinitionIndices.Clear();
            activeDefinitionList = null;
            definitionsProperty = null;
        }

        private void UpdateActiveIndices(HashSet<string> activeBlendShapes)
        {
            activeDefinitionIndices.Clear();
            if (definitionsProperty == null || !definitionsProperty.isArray)
            {
                return;
            }

            for (int i = 0; i < definitionsProperty.arraySize; i++)
            {
                SerializedProperty element = definitionsProperty.GetArrayElementAtIndex(i);
                if (element == null)
                {
                    continue;
                }

                SerializedProperty nameProp = element.FindPropertyRelative(nameof(BlendShapeDefinition.m_BlendShapeName));
                string blendShapeName = nameProp?.stringValue;
                if (!string.IsNullOrEmpty(blendShapeName) && activeBlendShapes.Contains(blendShapeName))
                {
                    activeDefinitionIndices.Add(i);
                }
            }
        }

        private static void DrawHeader(Rect rect)
        {
            EditorGUI.LabelField(rect, L.Get("editor.active_blend_shape_definitions"));
        }

        private float GetElementHeight(int index)
        {
            const int lineCount = 5;
            return lineCount * EditorGUIUtility.singleLineHeight +
                   (lineCount + 1) * EditorGUIUtility.standardVerticalSpacing +
                   6f;
        }

        private void DrawActiveDefinition(Rect rect, int listIndex, bool isActive, bool isFocused)
        {
            if (definitionsProperty == null ||
                listIndex < 0 ||
                listIndex >= activeDefinitionIndices.Count)
            {
                return;
            }

            SerializedProperty definitionProp = definitionsProperty.GetArrayElementAtIndex(activeDefinitionIndices[listIndex]);
            if (definitionProp == null)
            {
                return;
            }

            SerializedProperty nameProp = definitionProp.FindPropertyRelative(nameof(BlendShapeDefinition.m_BlendShapeName));
            SerializedProperty leftProp = definitionProp.FindPropertyRelative(nameof(BlendShapeDefinition.m_LeftEyeWeight));
            SerializedProperty rightProp = definitionProp.FindPropertyRelative(nameof(BlendShapeDefinition.m_RightEyeWeight));
            SerializedProperty mouthProp = definitionProp.FindPropertyRelative(nameof(BlendShapeDefinition.m_MouthWeight));
            SerializedProperty protectedProp = definitionProp.FindPropertyRelative(nameof(BlendShapeDefinition.m_Protected));

            Rect lineRect = new Rect(rect.x, rect.y + EditorGUIUtility.standardVerticalSpacing, rect.width, EditorGUIUtility.singleLineHeight);
            EditorGUI.LabelField(lineRect, nameProp?.stringValue ?? L.Get("editor.unnamed"), EditorStyles.miniBoldLabel);

            DrawSlider(ref lineRect, leftProp, L.Get("editor.definition.left_eye_weight"));
            DrawSlider(ref lineRect, rightProp, L.Get("editor.definition.right_eye_weight"));
            DrawSlider(ref lineRect, mouthProp, L.Get("editor.definition.mouth_weight"));
            DrawProtectedToggle(ref lineRect, nameProp?.stringValue, protectedProp);
            
        }

        private static void DrawSlider(ref Rect lineRect, SerializedProperty property, string label)
        {
            if (property == null)
            {
                return;
            }

            lineRect.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
            EditorGUI.Slider(lineRect, property, 0f, 1f, label);
           
        }

        private void DrawProtectedToggle(ref Rect lineRect, string blendShapeName, SerializedProperty property)
        {
            if (property == null)
            {
                return;
            }

            lineRect.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
            EditorGUI.BeginChangeCheck();
            EditorGUI.PropertyField(lineRect, property, L.G("editor.protected"));
            if (EditorGUI.EndChangeCheck())
            {
                //SyncBlendDataProtection(blendShapeName, property.boolValue);
            }
        }
        
        private void DrawDefinitionBulkButtons()
        {
            if (definitionsProperty == null || activeDefinitionIndices.Count == 0)
            {
                return;
            }

            Rect rowRect = EditorGUILayout.GetControlRect();
            float spacing = EditorGUIUtility.standardVerticalSpacing;
            GUIContent[] contents =
            {
                SetAllZeroContent,
                SetAllOneContent,
                SetNonZeroOneContent,
                AutoCalculateContent
            };
            GUIStyle buttonStyle = GUI.skin.button;
            const float padding = 12f;
            const float minWidth = 80f;
            float[] desiredWidths = new float[contents.Length];
            for (int i = 0; i < contents.Length; i++)
            {
                Vector2 size = buttonStyle.CalcSize(contents[i]);
                desiredWidths[i] = Mathf.Max(minWidth, size.x + padding);
            }

            float available = Mathf.Max(0f, rowRect.width - spacing * (contents.Length - 1));
            float minTotal = 0f;
            for (int i = 0; i < desiredWidths.Length; i++)
            {
                minTotal += desiredWidths[i];
            }
            float scale = minTotal > 0f ? available / minTotal : 1f;
            float cursor = rowRect.x;
            float height = rowRect.height;

            Rect NextRect(int index)
            {
                float width = Mathf.Max(minWidth, desiredWidths[index] * scale);
                if (index == contents.Length - 1)
                {
                    width = Mathf.Max(0f, rowRect.xMax - cursor);
                }

                var rect = new Rect(cursor, rowRect.y, width, height);
                cursor = rect.xMax + spacing;
                return rect;
            }

            Rect setZeroRect = NextRect(0);
            Rect setOneRect = NextRect(1);
            Rect setNonZeroOneRect = NextRect(2);
            Rect autoCalcRect = NextRect(3);

            if (GUI.Button(setZeroRect, SetAllZeroContent))
            {
                ApplyDefinitionWeights(_ => 0f);
            }

            if (GUI.Button(setOneRect, SetAllOneContent))
            {
                ApplyDefinitionWeights(_ => 1f);
            }

            if (GUI.Button(setNonZeroOneRect, SetNonZeroOneContent))
            {
                ApplyDefinitionWeights(value => Mathf.Approximately(value, 0f) ? 0f : 1f);
            }

            using (new EditorGUI.DisabledScope(!CanAutoCalculateDefinitions()))
            {
                if (GUI.Button(autoCalcRect, AutoCalculateContent))
                {
                    AutoCalculateDefinitions();
                }
            }
        }

        private bool CanAutoCalculateDefinitions()
        {
            FaceBlendShapeFixComponent component = editor?.component;
            SkinnedMeshRenderer smr = component?.TargetRenderer;
            Mesh mesh = smr?.sharedMesh;
            if (smr == null || mesh == null)
            {
                return false;
            }

            List<int> eyeReferences = BlendShapeDataUtil.GetBlendShapeIndices(
                mesh,
                BlendShapeDataUtil.GetBlendShapesFromType(component?.m_TargetShapes, ShapeType.BothEyes));
            List<int> mouthReferences = BlendShapeDataUtil.GetBlendShapeIndices(
                mesh,
                BlendShapeDataUtil.GetBlendShapesFromType(component?.m_TargetShapes, ShapeType.Mouth));

            return eyeReferences.Count > 0 && mouthReferences.Count > 0;
        }

        private void AutoCalculateDefinitions()
        {
            if (definitionsProperty == null || activeDefinitionIndices.Count == 0)
            {
                return;
            }

            FaceBlendShapeFixComponent component = editor?.component;
            SkinnedMeshRenderer smr = component?.TargetRenderer;
            Mesh mesh = smr?.sharedMesh;
            if (smr == null || mesh == null)
            {
                return;
            }

            List<int> eyeReferences = BlendShapeDataUtil.GetBlendShapeIndices(
                mesh,
                BlendShapeDataUtil.GetBlendShapesFromType(component?.m_TargetShapes, ShapeType.BothEyes));
            List<int> mouthReferences = BlendShapeDataUtil.GetBlendShapeIndices(
                mesh,
                BlendShapeDataUtil.GetBlendShapesFromType(component?.m_TargetShapes, ShapeType.Mouth));
            if (eyeReferences.Count == 0 || mouthReferences.Count == 0)
            {
                return;
            }

            SerializedObject serializedObject = definitionsProperty.serializedObject;
            if (serializedObject == null)
            {
                return;
            }

            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Auto Calculate BlendShape Definitions");
            Undo.RecordObject(serializedObject.targetObject, "Auto Calculate BlendShape Definitions");
            foreach (int index in activeDefinitionIndices)
            {
                SerializedProperty element = definitionsProperty.GetArrayElementAtIndex(index);
                if (element == null)
                {
                    continue;
                }

                string blendName = element.FindPropertyRelative(nameof(BlendShapeDefinition.m_BlendShapeName))?.stringValue;
                if (string.IsNullOrEmpty(blendName))
                {
                    continue;
                }

                int shapeIndex = mesh.GetBlendShapeIndex(blendName);
                if (shapeIndex < 0)
                {
                    continue;
                }

                BlendShapeDefinition calculated = BlendShapeDataUtil.CreateDefinition(
                    smr,
                    shapeIndex,
                    eyeReferences,
                    mouthReferences,
                    BlendShapeDataUtil.BlendShapeComparisonMode.Max);
                if (calculated == null)
                {
                    continue;
                }

                ApplyCalculatedDefinition(element, calculated);
            }

            serializedObject.ApplyModifiedProperties();
            Undo.CollapseUndoOperations(undoGroup);
        }

        private void ApplyDefinitionWeights(System.Func<float, float> weightFunc)
        {
            if (definitionsProperty == null || weightFunc == null || activeDefinitionIndices.Count == 0)
            {
                return;
            }

            SerializedObject serializedObject = definitionsProperty.serializedObject;
            if (serializedObject == null)
            {
                return;
            }

            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Adjust BlendShape Definitions");
            Undo.RecordObject(serializedObject.targetObject, "Adjust BlendShape Definitions");
            foreach (int index in activeDefinitionIndices)
            {
                SerializedProperty element = definitionsProperty.GetArrayElementAtIndex(index);
                ApplyDefinitionWeightToElement(element, weightFunc);
            }

            serializedObject.ApplyModifiedProperties();
            Undo.CollapseUndoOperations(undoGroup);
        }

        private static void ApplyDefinitionWeightToElement(SerializedProperty element, System.Func<float, float> weightFunc)
        {
            if (element == null)
            {
                return;
            }

            SerializedProperty leftProp = element.FindPropertyRelative(nameof(BlendShapeDefinition.m_LeftEyeWeight));
            SerializedProperty rightProp = element.FindPropertyRelative(nameof(BlendShapeDefinition.m_RightEyeWeight));
            SerializedProperty mouthProp = element.FindPropertyRelative(nameof(BlendShapeDefinition.m_MouthWeight));

            if (leftProp != null)
            {
                leftProp.floatValue = weightFunc(leftProp.floatValue);
            }

            if (rightProp != null)
            {
                rightProp.floatValue = weightFunc(rightProp.floatValue);
            }

            if (mouthProp != null)
            {
                mouthProp.floatValue = weightFunc(mouthProp.floatValue);
            }
        }

        private static void ApplyCalculatedDefinition(SerializedProperty element, BlendShapeDefinition calculated)
        {
            if (element == null || calculated == null)
            {
                return;
            }

            SerializedProperty leftProp = element.FindPropertyRelative(nameof(BlendShapeDefinition.m_LeftEyeWeight));
            SerializedProperty rightProp = element.FindPropertyRelative(nameof(BlendShapeDefinition.m_RightEyeWeight));
            SerializedProperty mouthProp = element.FindPropertyRelative(nameof(BlendShapeDefinition.m_MouthWeight));

            if (leftProp != null)
            {
                leftProp.floatValue = calculated.m_LeftEyeWeight;
            }

            if (rightProp != null)
            {
                rightProp.floatValue = calculated.m_RightEyeWeight;
            }

            if (mouthProp != null)
            {
                mouthProp.floatValue = calculated.m_MouthWeight;
            }
        }
    }
}

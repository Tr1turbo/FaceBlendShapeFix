using System;
using System.Collections.Generic;
using System.Linq;
using Triturbo.FaceBlendShapeFix.Runtime;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEditorInternal;
using UnityEngine;
using L = Triturbo.FaceBlendShapeFix.Localization;

namespace Triturbo.FaceBlendShapeFix.Inspector
{
    /// <summary>
    /// Handles drawing the inspector UI for an array of <see cref="TargetShape"/> entries.
    /// Provides preview controls, blend data editing, bulk operations, and scrollable lists.
    /// </summary>
    public class TargetShapeArrayDrawer
    {
        /// <summary>The component being inspected.</summary>
        private readonly FaceBlendShapeFixComponent _component;

        /// <summary>Serialized property for the target shapes array.</summary>
        private readonly SerializedProperty _targetShapeArrayProp;

        /// <summary>Observer tracking active blend shape weights on the renderer.</summary>
        private readonly BlendShapeActivationObserver _blendShapeActivationObserver;

        /// <summary>State object for the additional blend data dropdown.</summary>
        private readonly AdvancedDropdownState _additionalBlendDataDropdownState = new AdvancedDropdownState();

        /// <summary>Cached reorderable lists for additional blend data, keyed by property path.</summary>
        private readonly Dictionary<string, ReorderableList> _additionalBlendDataLists = new();

        private static GUIContent SetAllZeroContent => L.G("editor.blend_data.zero_all");
        private static GUIContent SetAllOneContent => L.G("editor.blend_data.max_all");
        private static GUIContent SetNonZeroOneContent => L.G("editor.blend_data.non_zero_to_one");
        private static GUIContent SetToGlobalContent => L.G("editor.blend_data.apply_global");
        private static GUIContent AutoCalculateContent => L.G("editor.blend_data.auto_calculate");
        
        #region Cache
        /// <summary>Set of blend shape names currently active on the renderer.</summary>
        private HashSet<string> _activeBlendShapes =  new HashSet<string>();

        /// <summary>Cache of visibility data per target shape, keyed by property path.</summary>
        private readonly Dictionary<string, BlendDataVisibilityCache> _activeBlendData = new ();

        /// <summary>Scroll positions for blend data lists, keyed by property path.</summary>
        private readonly Dictionary<string, Vector2> _blendDataScrollPositions = new();

        /// <summary>
        /// Stores cached visibility information for a blend data list including active indices and calculated height.
        /// </summary>
        private struct BlendDataVisibilityCache
        {
            /// <summary>Indices of blend data entries to display.</summary>
            public List<int> _activeBlendData;

            /// <summary>Array size when the cache was built.</summary>
            public int _arraySize;

            /// <summary>Cached total height of the visible list.</summary>
            public float _listHeight;
        }
        
        /// <summary>
        /// Gets or rebuilds the visibility cache for the target's blend data list, tracking active indices and height.
        /// </summary>
        /// <param name="blendDataArrayProp">Serialized array of blend data elements.</param>
        /// <param name="propertyPath">Property path used as the cache key.</param>
        /// <param name="targetIndex">Index of the target shape in the component array.</param>
        /// <returns>Cached visibility data for the current blend data list.</returns>
        private BlendDataVisibilityCache GetBlendDataVisibilityCache(SerializedProperty blendDataArrayProp, string propertyPath, int targetIndex)
        {
            int currentArraySize = blendDataArrayProp != null ? blendDataArrayProp.arraySize : 0;
            
            if (!_activeBlendData.TryGetValue(propertyPath, out BlendDataVisibilityCache blendDataVisibility))
            {
                blendDataVisibility = CreateBlendDataVisibilityCache(blendDataArrayProp, _activeBlendShapes, targetIndex);
                _activeBlendData.Add(propertyPath, blendDataVisibility);
                return blendDataVisibility;
            }

            if (blendDataVisibility._arraySize != currentArraySize)
            {
                blendDataVisibility = CreateBlendDataVisibilityCache(blendDataArrayProp, _activeBlendShapes, targetIndex);
                _activeBlendData[propertyPath] = blendDataVisibility;
                return blendDataVisibility;
            }

            blendDataVisibility._listHeight = CalculateBlendDataListHeight(targetIndex, blendDataVisibility._activeBlendData);
            _activeBlendData[propertyPath] = blendDataVisibility;

            return blendDataVisibility;
        }

        /// <summary>
        /// Builds a visibility cache from the current blend data array and active blend shape list.
        /// </summary>
        /// <param name="blendDataArrayProp">Serialized array of blend data elements.</param>
        /// <param name="activeBlendShapeNames">Active blend shape names to keep visible.</param>
        /// <param name="targetIndex">Index of the target shape in the component array.</param>
        /// <returns>New visibility cache instance.</returns>
        private BlendDataVisibilityCache CreateBlendDataVisibilityCache(SerializedProperty blendDataArrayProp, IReadOnlyCollection<string> activeBlendShapeNames, int targetIndex)
        {
            List<int> activeIndices = GetActiveBlendDataIndices(blendDataArrayProp, activeBlendShapeNames ?? Array.Empty<string>());
            return new BlendDataVisibilityCache
            {
                _activeBlendData = activeIndices,
                _arraySize = blendDataArrayProp != null ? blendDataArrayProp.arraySize : 0,
                _listHeight = CalculateBlendDataListHeight(targetIndex, activeIndices)
            };
        }

        /// <summary>
        /// Rebuilds the active blend data index cache for all targets based on the active names.
        /// </summary>
        /// <param name="activeBlendShapeNames">Active blend shape names to keep visible.</param>
        private void BuildBlendDataActiveStatus(IReadOnlyCollection<string> activeBlendShapeNames)
        {
            _activeBlendShapes = (activeBlendShapeNames ?? Array.Empty<string>()).ToHashSet();
            _activeBlendData.Clear();
            //Build active index cache
            for (int i = 0; i < _targetShapeArrayProp.arraySize; i++)
            {
                var prop =_targetShapeArrayProp.GetArrayElementAtIndex(i);
                if (prop == null)
                {
                    continue;
                }
                
                var blendDataArrayProp=  prop.FindPropertyRelative(nameof(TargetShape.m_BlendData));
                

                _activeBlendData.Add(prop.propertyPath, CreateBlendDataVisibilityCache(blendDataArrayProp, activeBlendShapeNames, i));
            }
        }
        #endregion

        //private TargetShapesCache _targetShapesCache;

        /// <summary>Delegate invoked to draw the category selector UI for a target shape.</summary>
        internal Action<SerializedProperty> DrawCategorySelector;

        /// <summary>
        /// Initializes a new instance of the <see cref="TargetShapeArrayDrawer"/> class.
        /// </summary>
        /// <param name="component">The component being inspected.</param>
        /// <param name="targetShapeArray">Serialized property for the target shapes array.</param>
        /// <param name="blendShapeActivationObserver">Observer tracking active blend shape weights.</param>
        public TargetShapeArrayDrawer(FaceBlendShapeFixComponent component, SerializedProperty targetShapeArray,
            BlendShapeActivationObserver blendShapeActivationObserver)
        {
            _component = component;
            _targetShapeArrayProp = targetShapeArray;
            _blendShapeActivationObserver = blendShapeActivationObserver;
        }
        
        /// <summary>
        /// Updates cached visibility data when the active blend shape list changes.
        /// </summary>
        /// <param name="activeBlendShapeNames">New active blend shape names.</param>
        internal void OnActiveBlendShapesChanged(IReadOnlyCollection<string> activeBlendShapeNames)
        {
            _activeBlendShapes = (activeBlendShapeNames ?? Array.Empty<string>()).ToHashSet();
            BuildBlendDataActiveStatus(_activeBlendShapes);
        }
        
        
        /// <summary>
        /// Collapses the blend data foldouts for all target shapes.
        /// </summary>
        internal void CollapseAllBlendDataFoldouts()
        {
            if (_targetShapeArrayProp == null)
            {
                return;
            }

            for (int i = 0; i < _targetShapeArrayProp.arraySize; i++)
            {
                var targetShapeProp = _targetShapeArrayProp.GetArrayElementAtIndex(i);
                if (targetShapeProp == null)
                {
                    continue;
                }

                var blendDataArrayProp = targetShapeProp.FindPropertyRelative(nameof(TargetShape.m_BlendData));
                if (blendDataArrayProp != null)
                {
                    blendDataArrayProp.isExpanded = false;
                }

                var aBlendDataArrayProp = targetShapeProp.FindPropertyRelative(nameof(TargetShape.m_AdditiveBlendData));
                if (aBlendDataArrayProp != null)
                {
                    aBlendDataArrayProp.isExpanded = false;
                }
            }
        }

        /// <summary>
        /// Renders the inspector UI for a specific target shape, including preview and blend data sections.
        /// </summary>
        /// <param name="targetIndex">Index of the target shape to draw.</param>
        public void OnGUI(int targetIndex)
        {
            var targetShapeProp =_targetShapeArrayProp.GetArrayElementAtIndex(targetIndex);
            SerializedProperty nameProp = targetShapeProp.FindPropertyRelative(nameof(TargetShape.m_TargetShapeName));
            SerializedProperty mainWeightProp = targetShapeProp.FindPropertyRelative(nameof(TargetShape.m_Weight));
            SerializedProperty categoryProp = targetShapeProp.FindPropertyRelative(nameof(TargetShape.m_CategoryName));
            SerializedProperty useGlobalDefinitionsProp = targetShapeProp.FindPropertyRelative(nameof(TargetShape.m_UseGlobalDefinitions));
            SerializedProperty targetShapeTypeProp = targetShapeProp.FindPropertyRelative(nameof(TargetShape.m_TargetShapeType));
            SerializedProperty blendDataArrayProp = targetShapeProp.FindPropertyRelative(nameof(TargetShape.m_BlendData));
            SerializedProperty additionalBlendDataArrayProp = targetShapeProp.FindPropertyRelative(nameof(TargetShape.m_AdditiveBlendData));
            
            float previewWeight = 0;
            int previewIndex = -1;

            bool isPreviewEnabled;
            
            if (_component.TryGetPreviewRequest(out PreviewRequest request))
            {
                previewWeight = request.Weight;
                previewIndex = request.TargetIndex;
                isPreviewEnabled = request.IsPreviewEnable;
            }
            else
            {
                isPreviewEnabled = _component.TargetRenderer != null && _component.isActiveAndEnabled;
            }


            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            Rect previewSliderRect = EditorGUILayout.GetControlRect();
            bool isCurrentPreview = previewIndex != -1 && targetIndex == previewIndex;

            
            string currentName = nameProp.stringValue;
            var shapeNameLabel = EditorGUI.BeginProperty(previewSliderRect, new GUIContent(currentName), targetShapeProp);
            float displayedWeight = isCurrentPreview ? previewWeight : 0f;

            float previewMinimumWeight = 0f;
            if (_activeBlendShapes.Contains(currentName))
            {
                EditorGUILayout.HelpBox(L.Get("editor.warning.blendshape_not_zero"), MessageType.Warning);
                previewMinimumWeight = _blendShapeActivationObserver.GetWeight(currentName);
                
                if(displayedWeight < previewMinimumWeight)
                    displayedWeight = previewMinimumWeight;
            }
            
            // Preview Slider
            EditorGUI.BeginChangeCheck();

            float minimumButtonWidth = Mathf.Max(
                EditorStyles.largeLabel.CalcSize(L.G("editor.preview.enter")).x,
                EditorStyles.largeLabel.CalcSize(L.G("editor.preview.exit")).x) + 5f;

            float newPreviewWeight = Widgets.ReserveSpaceSlider(previewSliderRect, displayedWeight, shapeNameLabel, 0f, 100f, 
            out Rect nbuttonRect, minimumButtonWidth);

            if (EditorGUI.EndChangeCheck())
            {
                newPreviewWeight = Mathf.Clamp(newPreviewWeight, previewMinimumWeight, 100f);
                if (previewIndex != targetIndex)
                {
                    _component.BeginPreview(targetIndex, newPreviewWeight);
                }
                else
                {
                    _component.UpdateWeight(newPreviewWeight);
                }
            }

            // preview button


            Color prevBackground = GUI.backgroundColor;
            if (isCurrentPreview)
            {
                GUI.backgroundColor = new Color(0.35f, 0.75f, 0.35f); // active preview -> green tint
            }
            else if (!isPreviewEnabled)
            {
                GUI.backgroundColor = new Color(0.6f, 0.6f, 0.6f); // unavailable preview -> grey tint
            }
        
            using (new EditorGUI.DisabledScope(!isPreviewEnabled && !isCurrentPreview))
            {
                var buttonLabel = isCurrentPreview ? L.G("editor.preview.exit") : L.G("editor.preview.enter");
                if (GUI.Button(nbuttonRect, buttonLabel))
                {
                    if (isCurrentPreview)
                    {
                        _component.EndPreview();
                    }
                    else if (isPreviewEnabled)
                    {
                       _component.BeginPreview(targetIndex, 100f);
                    }
                }
            }

            GUI.backgroundColor = prevBackground;



            
            EditorGUI.EndProperty();
            
            Widgets.SliderLayout(mainWeightProp, L.G("editor.main_weight"));

            DrawCategorySelector(categoryProp);

            GUIContent useGlobalLabelOn =L.G("editor.use_local_definitions");
            GUIContent useGlobalLabelOff = L.G("editor.use_global_definitions");

            float useGlobalToggleWidth = Widgets.ToggleButtonWidth(useGlobalLabelOn, useGlobalLabelOff);
            Rect currentRect = EditorGUILayout.GetControlRect();

            float fieldWidth = currentRect.width - EditorGUIUtility.labelWidth - EditorGUI.indentLevel * 15f + 13f;

            if (useGlobalDefinitionsProp.boolValue)
            {
                float toggleWidth = Mathf.Min(useGlobalToggleWidth, fieldWidth * 0.5f);
                float typeWidth = Mathf.Max(0f, currentRect.width - toggleWidth - 4f);
                Rect toggleRect = new Rect(currentRect.xMax - toggleWidth, currentRect.y, toggleWidth, currentRect.height);

                Rect typeRect = new Rect(currentRect.x, currentRect.y, typeWidth, currentRect.height);

                L.LocalizedEnumPropertyField(typeRect, targetShapeTypeProp, L.G("editor.target_shape_type"), "enum.shapetype");
                Widgets.ToggleButton(toggleRect, useGlobalDefinitionsProp, useGlobalLabelOn, useGlobalLabelOff);
            }
            else
            {
                if(fieldWidth < useGlobalToggleWidth)
                {
                    useGlobalToggleWidth = fieldWidth;
                }

                SerializedProperty blendDataArray = blendDataArrayProp;
                float toggleWidth = Mathf.Min(useGlobalToggleWidth, currentRect.width);
                float foldoutWidth = Mathf.Max(0f, currentRect.width - toggleWidth - 4f);
                Rect useGlobalRect = new Rect(currentRect.xMax - toggleWidth, currentRect.y, toggleWidth,
                    currentRect.height);
                Rect foldoutRect = new Rect(currentRect.x, currentRect.y, foldoutWidth, currentRect.height);
                
                var label = EditorGUI.BeginProperty(currentRect, L.G("editor.reset_blend"), blendDataArray);
                bool isExpanded = EditorGUI.Foldout(foldoutRect, blendDataArray.isExpanded, label, true);
                EditorGUI.EndProperty();

                blendDataArray.isExpanded =  isExpanded;
                Widgets.ToggleButton(useGlobalRect, useGlobalDefinitionsProp, useGlobalLabelOn, useGlobalLabelOff);
                
                if (isExpanded)
                {
                    DrawBlendDataBulkButtons(targetShapeProp, blendDataArray);
                    
                    BlendDataVisibilityCache blendDataVisibility = GetBlendDataVisibilityCache(
                        blendDataArray,
                        targetShapeProp.propertyPath,
                        targetIndex);
                    List<int> activeIndices = blendDataVisibility._activeBlendData;
                    
   
                    
                    //EditorGUILayout.PropertyField(blendDataArray);
                    if (activeIndices.Count == 0 && isPreviewEnabled)
                    {
                        EditorGUILayout.HelpBox(L.Get("editor.no_active_blendshapes"), MessageType.Info);
                    }
                    else
                    {
                        bool allowScroll = _component?.m_InspectorSettings?.m_EnableBlendDataScroll ?? true;
                        float scrollHeight = Mathf.Max(64f, _component?.m_InspectorSettings?.m_BlendDataScrollHeight ?? 240f);
                        bool useScroll = allowScroll && blendDataVisibility._listHeight > scrollHeight;
                        string scrollKey = targetShapeProp.propertyPath;
                        bool isUpdated = false;
                        
                        if (useScroll)
                        {
                            Vector2 scrollPos = _blendDataScrollPositions.TryGetValue(scrollKey, out var pos) ? pos : Vector2.zero;
                            using (var scroll = new EditorGUILayout.ScrollViewScope(scrollPos, GUILayout.Height(scrollHeight), GUILayout.ExpandWidth(true)))
                            {
                                isUpdated = DrawBlendDataList(activeIndices, targetIndex, blendDataArray);
                                _blendDataScrollPositions[scrollKey] = scroll.scrollPosition;
                            }
                        }
                        else
                        {
                            isUpdated = DrawBlendDataList(activeIndices, targetIndex, blendDataArray);
                            _blendDataScrollPositions.Remove(scrollKey);
                        }

                        if (isUpdated && isCurrentPreview)
                        {
                            SceneView.RepaintAll();
                        }
                    }
                    
                    
                    EditorGUILayout.LabelField($"Blend Data: {blendDataArray.arraySize}");
                }
                
            }

            DrawAdditionalBlendData(additionalBlendDataArrayProp, currentName);
            EditorGUILayout.EndVertical();
        }

        /// <summary>
        /// Draws the additional blend data section for a target shape.
        /// </summary>
        /// <param name="additionalBlendDataArray">Serialized array of additional blend data.</param>
        /// <param name="targetShapeName">Name of the target shape.</param>
        private void DrawAdditionalBlendData(SerializedProperty additionalBlendDataArray, string targetShapeName)
        {
            if (additionalBlendDataArray == null)
            {
                return;
            }

            ReorderableList list = GetAdditionalBlendDataList(additionalBlendDataArray);
            if (list == null)
            {
                return;
            }

            list.onAddDropdownCallback = (rect, _) =>
                ShowAdditionalBlendDataDropdown(rect, additionalBlendDataArray, targetShapeName);

            Rect rect = EditorGUILayout.GetControlRect();
            GUIContent label = EditorGUI.BeginProperty(rect, L.G("editor.additive_blendshapes"), additionalBlendDataArray);
            
            additionalBlendDataArray.isExpanded = EditorGUI.Foldout(rect, additionalBlendDataArray.isExpanded, label, true);

            EditorGUI.EndProperty();
            
            
            if(additionalBlendDataArray.isExpanded)
                list.DoLayoutList();
        }

        /// <summary>
        /// Retrieves or creates a cached reorderable list for the additional blend data array.
        /// </summary>
        /// <param name="additionalBlendDataArray">Serialized array of additional blend data.</param>
        /// <returns>Cached or newly created reorderable list.</returns>
        private ReorderableList GetAdditionalBlendDataList(SerializedProperty additionalBlendDataArray)
        {
            string key = additionalBlendDataArray.propertyPath;
            if (_additionalBlendDataLists.TryGetValue(key, out ReorderableList cached) &&
                cached != null &&
                cached.serializedProperty != null &&
                cached.serializedProperty.propertyPath == key &&
                cached.serializedProperty.serializedObject == additionalBlendDataArray.serializedObject)
            {
                return cached;
            }

            var list = new ReorderableList(additionalBlendDataArray.serializedObject, additionalBlendDataArray, true, false, true, true)
            {
                elementHeightCallback = index =>
                {
                    var element = additionalBlendDataArray.GetArrayElementAtIndex(index);
                    return EditorGUI.GetPropertyHeight(element, true);// + EditorGUIUtility.standardVerticalSpacing;
                },
                drawElementCallback = (rect, index, _, _) =>
                {
                    var element = additionalBlendDataArray.GetArrayElementAtIndex(index);
                    //rect.height = EditorGUI.GetPropertyHeight(element, true);
                    EditorGUI.PropertyField(rect, element, GUIContent.none, true);
                    //EditorGUI.LabelField(rect, element.propertyPath);
                },
            };
            
            _additionalBlendDataLists[key] = list;
            return list;
        }

        /// <summary>
        /// Shows a dropdown for selecting a blend shape to add as additional blend data.
        /// </summary>
        /// <param name="buttonRect">Rect where the dropdown button was clicked.</param>
        /// <param name="additionalBlendDataArray">Serialized array of additional blend data.</param>
        /// <param name="targetShapeName">Name of the target shape to exclude from options.</param>
        private void ShowAdditionalBlendDataDropdown(
            Rect buttonRect,
            SerializedProperty additionalBlendDataArray,
            string targetShapeName)
        {
            var options = BuildAvailableAdditionalBlendDataOptions(additionalBlendDataArray, targetShapeName);
            if (options.Count == 0)
            {
                EditorUtility.DisplayDialog(
                    L.Get("editor.dialog.no_blendshapes.title"),
                    L.Get("editor.dialog.no_additional_blendshapes.message"),
                    L.Get("editor.ok"));
                return;
            }

            var dropdown = new TargetShapeDropdown(_additionalBlendDataDropdownState, options,
                blendShapeName => AddAdditionalBlendData(additionalBlendDataArray, blendShapeName));
            dropdown.Show(buttonRect);
        }

        /// <summary>
        /// Builds a list of blend shapes available to add as additional blend data.
        /// </summary>
        /// <param name="additionalBlendDataArray">Serialized array of additional blend data.</param>
        /// <param name="targetShapeName">Name of the target shape to exclude.</param>
        /// <returns>List of available blend shape options.</returns>
        private List<TargetShapeDropdown.Option> BuildAvailableAdditionalBlendDataOptions(
            SerializedProperty additionalBlendDataArray,
            string targetShapeName)
        {
            var options = new List<TargetShapeDropdown.Option>();
            Mesh mesh = _component?.TargetRenderer?.sharedMesh;
            if (mesh == null)
            {
                return options;
            }

            var existingNames = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < additionalBlendDataArray.arraySize; i++)
            {
                var element = additionalBlendDataArray.GetArrayElementAtIndex(i);
                string name = element.FindPropertyRelative(nameof(BlendData.m_TargetShapeName))?.stringValue;
                if (!string.IsNullOrEmpty(name))
                {
                    existingNames.Add(name);
                }
            }

            for (int i = 0; i < mesh.blendShapeCount; i++)
            {
                string blendShapeName = mesh.GetBlendShapeName(i);
                if (string.IsNullOrEmpty(blendShapeName) ||
                    existingNames.Contains(blendShapeName) ||
                    string.Equals(blendShapeName, targetShapeName, StringComparison.Ordinal))
                {
                    continue;
                }

                options.Add(new TargetShapeDropdown.Option(blendShapeName, blendShapeName, true));
            }

            return options;
        }

        /// <summary>
        /// Adds a new entry to the additional blend data array with default values.
        /// </summary>
        /// <param name="additionalBlendDataArray">Serialized array of additional blend data.</param>
        /// <param name="blendShapeName">Name of the blend shape to add.</param>
        private static void AddAdditionalBlendData(SerializedProperty additionalBlendDataArray, string blendShapeName)
        {
            if (additionalBlendDataArray == null || string.IsNullOrEmpty(blendShapeName))
            {
                return;
            }

            SerializedObject serializedObject = additionalBlendDataArray.serializedObject;
            if (serializedObject == null)
            {
                return;
            }

            Undo.RecordObject(serializedObject.targetObject, "Add Additional Blend Data");
            serializedObject.Update();

            int insertIndex = additionalBlendDataArray.arraySize;
            additionalBlendDataArray.InsertArrayElementAtIndex(insertIndex);
            SerializedProperty element = additionalBlendDataArray.GetArrayElementAtIndex(insertIndex);

            element.FindPropertyRelative(nameof(BlendData.m_TargetShapeName)).stringValue = blendShapeName;
            element.FindPropertyRelative(nameof(BlendData.m_Weight)).floatValue = 1f;
            element.FindPropertyRelative(nameof(BlendData.m_LeftWeight)).floatValue = 1f;
            element.FindPropertyRelative(nameof(BlendData.m_RightWeight)).floatValue = 1f;
            element.FindPropertyRelative(nameof(BlendData.m_SplitLeftRight)).boolValue = false;

            serializedObject.ApplyModifiedProperties();
        }
        #region BlendDataBulkButtons

        /// <summary>
        /// Draws bulk action buttons for adjusting blend data weights and applying definitions.
        /// </summary>
        /// <param name="targetShape">Serialized target shape property.</param>
        /// <param name="blendDataArray">Serialized blend data array property.</param>
        private void DrawBlendDataBulkButtons(SerializedProperty targetShape, SerializedProperty blendDataArray)
        {
            if (blendDataArray == null)
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
                AutoCalculateContent,
                SetToGlobalContent
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
            float minTotal = desiredWidths.Sum();
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
            Rect setGlobalRect = NextRect(4);

            if (GUI.Button(setZeroRect, SetAllZeroContent))
            {
                ApplyBlendDataWeights(targetShape, blendDataArray, _ => 0f);
            }

            if (GUI.Button(setOneRect, SetAllOneContent))
            {
                ApplyBlendDataWeights(targetShape, blendDataArray, _ => 1f);
            }

            if (GUI.Button(setNonZeroOneRect, SetNonZeroOneContent))
            {
                ApplyBlendDataWeights(targetShape, blendDataArray, value => Mathf.Approximately(value, 0f) ? 0f : 1f);
            }

            using (new EditorGUI.DisabledScope(!CanAutoCalculateBlendData(targetShape)))
            {
                if (GUI.Button(autoCalcRect, AutoCalculateContent))
                {
                    AutoCalculateBlendData(targetShape, blendDataArray);
                }
            }

            using (new EditorGUI.DisabledScope(!HasGlobalDefinitions()))
            {
                if (GUI.Button(setGlobalRect, SetToGlobalContent))
                {
                    ShapeType targetShapeType = ShapeType.BothEyes;
                    SerializedProperty typeProp = targetShape?.FindPropertyRelative(nameof(TargetShape.m_TargetShapeType));
                    if (typeProp != null)
                    {
                        targetShapeType = (ShapeType)typeProp.enumValueIndex;
                    }

                    ApplyGlobalBlendData(targetShape, blendDataArray, targetShapeType);
                }
            }
        }

        /// <summary>
        /// Checks whether auto-calculation can run for the given target shape.
        /// </summary>
        /// <param name="targetShape">Serialized target shape property.</param>
        /// <returns>True when the target shape exists in the renderer mesh.</returns>
        private bool CanAutoCalculateBlendData(SerializedProperty targetShape)
        {
            SkinnedMeshRenderer smr = _component?.TargetRenderer;
            if (smr == null || smr.sharedMesh == null || targetShape == null)
            {
                return false;
            }

            SerializedProperty nameProp = targetShape.FindPropertyRelative(nameof(TargetShape.m_TargetShapeName));
            string targetShapeName = nameProp?.stringValue;
            if (string.IsNullOrEmpty(targetShapeName))
            {
                return false;
            }

            return smr.sharedMesh.GetBlendShapeIndex(targetShapeName) >= 0;
        }

        /// <summary>
        /// Recomputes blend data weights from the mesh blend shape deltas.
        /// </summary>
        /// <param name="targetShape">Serialized target shape property.</param>
        /// <param name="blendDataArray">Serialized blend data array property.</param>
        private void AutoCalculateBlendData(SerializedProperty targetShape, SerializedProperty blendDataArray)
        {
            if (blendDataArray == null || targetShape == null)
            {
                return;
            }

            SkinnedMeshRenderer smr = _component?.TargetRenderer;
            Mesh mesh = smr?.sharedMesh;
            if (smr == null || mesh == null)
            {
                return;
            }

            string mainShapeName = targetShape.FindPropertyRelative(nameof(TargetShape.m_TargetShapeName))?.stringValue;
            if (string.IsNullOrEmpty(mainShapeName))
            {
                return;
            }

            int mainShapeIndex = mesh.GetBlendShapeIndex(mainShapeName);
            if (mainShapeIndex < 0)
            {
                return;
            }

            SerializedObject serializedObject = blendDataArray.serializedObject;
            if (serializedObject == null)
            {
                return;
            }

            IReadOnlyDictionary<string, BlendShapeDefinition> definitions = GetDefinitionLookup();

            float mainWeight = targetShape.FindPropertyRelative(nameof(TargetShape.m_Weight)).floatValue;

            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Auto Calculate Blend Data");
            Undo.RecordObject(serializedObject.targetObject, "Auto Calculate Blend Data");
            for (int i = 0; i < blendDataArray.arraySize; i++)
            {
                SerializedProperty element = blendDataArray.GetArrayElementAtIndex(i);
                if (element == null)
                {
                    continue;
                }

                if (IsProtectedBlendData(definitions, element))
                {
                    continue;
                }

                string blendName = element.FindPropertyRelative(nameof(BlendData.m_TargetShapeName))?.stringValue;
                if (string.IsNullOrEmpty(blendName))
                {
                    continue;
                }

                int blendIndex = mesh.GetBlendShapeIndex(blendName);
                if (blendIndex < 0)
                {
                    continue;
                }

                BlendData calculated = BlendShapeDataUtil.CreateBlendData(smr, mainShapeIndex, blendIndex, mainWeight);
                if (calculated == null)
                {
                    continue;
                }

                ApplyBlendDataValue(element, calculated);
            }

            serializedObject.ApplyModifiedProperties();
            Undo.CollapseUndoOperations(undoGroup);
        }

        /// <summary>
        /// Copies blend data values into a serialized blend data element.
        /// </summary>
        /// <param name="element">Serialized blend data element.</param>
        /// <param name="src">Source blend data values.</param>
        private static void ApplyBlendDataValue(SerializedProperty element, BlendData src)
        {
            if (element == null || src == null)
            {
                return;
            }

            SerializedProperty weightProp = element.FindPropertyRelative(nameof(BlendData.m_Weight));
            SerializedProperty leftProp = element.FindPropertyRelative(nameof(BlendData.m_LeftWeight));
            SerializedProperty rightProp = element.FindPropertyRelative(nameof(BlendData.m_RightWeight));
            SerializedProperty splitProp = element.FindPropertyRelative(nameof(BlendData.m_SplitLeftRight));

            if (weightProp != null)
            {
                weightProp.floatValue = src.m_Weight;
            }

            if (leftProp != null)
            {
                leftProp.floatValue = src.m_LeftWeight;
            }

            if (rightProp != null)
            {
                rightProp.floatValue = src.m_RightWeight;
            }

            if (splitProp != null)
            {
                splitProp.boolValue = src.m_SplitLeftRight;
            }

        }

        /// <summary>
        /// Applies a weight transform across all blend data entries, respecting protected entries.
        /// </summary>
        /// <param name="targetShape">Serialized target shape property.</param>
        /// <param name="blendDataArray">Serialized blend data array property.</param>
        /// <param name="weightFunc">Function to remap weights.</param>
        private void ApplyBlendDataWeights(SerializedProperty targetShape, SerializedProperty blendDataArray, Func<float, float> weightFunc)
        {
            if (blendDataArray == null || weightFunc == null)
            {
                return;
            }

            IReadOnlyDictionary<string, BlendShapeDefinition> definitions = GetDefinitionLookup();
            SerializedObject serializedObject = blendDataArray.serializedObject;
            if (serializedObject == null)
            {
                return;
            }

            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Adjust Blend Data Weights");
            Undo.RecordObject(serializedObject.targetObject, "Adjust Blend Data Weights");
            for (int i = 0; i < blendDataArray.arraySize; i++)
            {
                SerializedProperty element = blendDataArray.GetArrayElementAtIndex(i);
                bool isProtected = IsProtectedBlendData(definitions, element);
                ApplyBlendDataWeightToElement(element, weightFunc, isProtected);
            }

            serializedObject.ApplyModifiedProperties();
            Undo.CollapseUndoOperations(undoGroup);
        }

        /// <summary>
        /// Applies a weight transform to a single serialized blend data element.
        /// </summary>
        /// <param name="element">Serialized blend data element.</param>
        /// <param name="weightFunc">Function to remap weights.</param>
        /// <param name="isProtected">Whether the element is protected from edits.</param>
        private static void ApplyBlendDataWeightToElement(SerializedProperty element, Func<float, float> weightFunc, bool isProtected)
        {
            if (element == null)
            {
                return;
            }

            //SetProtectedFlag(element, isProtected);
            if (isProtected)
            {
                return;
            }

            SerializedProperty weightProp = element.FindPropertyRelative(nameof(BlendData.m_Weight));
            SerializedProperty leftProp = element.FindPropertyRelative(nameof(BlendData.m_LeftWeight));
            SerializedProperty rightProp = element.FindPropertyRelative(nameof(BlendData.m_RightWeight));

            if (weightProp != null)
            {
                weightProp.floatValue = weightFunc(weightProp.floatValue);
            }
            if (leftProp != null)
            {
                leftProp.floatValue = weightFunc(leftProp.floatValue);
            }
            if (rightProp != null)
            {
                rightProp.floatValue = weightFunc(rightProp.floatValue);
            }
        }

        /// <summary>
        /// Applies global blend definitions to all entries in the blend data array.
        /// </summary>
        /// <param name="targetShape">Serialized target shape property.</param>
        /// <param name="blendDataArray">Serialized blend data array property.</param>
        /// <param name="targetShapeType">Shape type used to resolve per-side values.</param>
        private void ApplyGlobalBlendData(SerializedProperty targetShape, SerializedProperty blendDataArray, ShapeType targetShapeType)
        {
            var definitions = GetDefinitionLookup();
            if (definitions == null || definitions.Count == 0)
            {
                return;
            }

            SerializedObject serializedObject = blendDataArray.serializedObject;
            if (serializedObject == null)
            {
                return;
            }

            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Apply Global Blend Definitions");
            Undo.RecordObject(serializedObject.targetObject, "Apply Global Blend Definitions");
            for (int i = 0; i < blendDataArray.arraySize; i++)
            {
                SerializedProperty element = blendDataArray.GetArrayElementAtIndex(i);
                if (element == null)
                {
                    continue;
                }
                string blendName = element.FindPropertyRelative(nameof(BlendData.m_TargetShapeName))?.stringValue;
                if (string.IsNullOrEmpty(blendName) || !definitions.TryGetValue(blendName, out BlendShapeDefinition definition))
                {
                    continue;
                }
                if (definition == null)
                {
                    continue;
                }
                ApplyBlendDataValue(element, definition.ResolveBlendData(targetShapeType));            
            }
            serializedObject.ApplyModifiedProperties();
            Undo.CollapseUndoOperations(undoGroup);
        }

        /// <summary>
        /// Builds a lookup table for global blend definitions by blend shape name.
        /// </summary>
        /// <returns>Dictionary keyed by blend shape name, or null if none exist.</returns>
        private IReadOnlyDictionary<string, BlendShapeDefinition> GetDefinitionLookup()
        {
            BlendShapeDefinition[] definitions = _component?.m_BlendShapeDefinitions;
            if (definitions == null || definitions.Length == 0)
            {
                return null;
            }

            var lookup = new Dictionary<string, BlendShapeDefinition>(definitions.Length, StringComparer.Ordinal);
            foreach (BlendShapeDefinition def in definitions)
            {
                if (def == null || string.IsNullOrEmpty(def.m_BlendShapeName))
                {
                    continue;
                }

                lookup[def.m_BlendShapeName] = def;
            }

            return lookup;
        }

        /// <summary>
        /// Checks whether global blend shape definitions are configured on the component.
        /// </summary>
        /// <returns>True if at least one global definition exists.</returns>
        private bool HasGlobalDefinitions()
        {
            BlendShapeDefinition[] definitions = _component?.m_BlendShapeDefinitions;
            return definitions != null && definitions.Length > 0;
        }
        

        /// <summary>
        /// Checks whether the serialized blend data is marked protected in the global definitions.
        /// </summary>
        /// <param name="definitions">Lookup table of global blend shape definitions.</param>
        /// <param name="element">Serialized blend data element to check.</param>
        /// <returns>True if the element is protected from edits.</returns>
        private static bool IsProtectedBlendData(IReadOnlyDictionary<string, BlendShapeDefinition> definitions, SerializedProperty element)
        {
            if (definitions == null || element == null)
            {
                return false;
            }
        
            SerializedProperty nameProp = element.FindPropertyRelative(nameof(BlendData.m_TargetShapeName));
            string blendName = nameProp?.stringValue;
            if (string.IsNullOrEmpty(blendName)) return false;


            if (definitions.TryGetValue(blendName, out BlendShapeDefinition definition))
            {
                return definition.m_Protected;
            }
            
            return false; 
        }
        
        #endregion
        
        
        /// <summary>
        /// Draws the filtered blend data list for a target and applies edits to the data source.
        /// </summary>
        /// <param name="activeIndices">Blend data indices to display.</param>
        /// <param name="targetIndex">Index of the target shape to draw.</param>
        /// <param name="blendDataArray">Serialized blend data array property.</param>
        /// <returns>True if any data was modified.</returns>
        private bool DrawBlendDataList(List<int> activeIndices, int targetIndex, SerializedProperty blendDataArray)
        {
            if (activeIndices.Count == 0)
            {
                return DrawBlendDataList(targetIndex, blendDataArray);
            }
            bool isUpdated = false;
            IReadOnlyDictionary<string, BlendShapeDefinition> definitions = GetDefinitionLookup();
            foreach (int index in activeIndices)
            {
                if (_component.m_TargetShapes[targetIndex].m_BlendData.Length > index)
                {
                    var data = _component.m_TargetShapes[targetIndex].m_BlendData[index];
                    bool isProtected = data.IsProtected(definitions);
                    bool isSelf = _component.m_TargetShapes[targetIndex].m_TargetShapeName == data.m_TargetShapeName;
                    
                    var height = BlendDataDrawer.GetHeight(data);
                    var rect = EditorGUILayout.GetControlRect(true, height);
                    
                    
                    //EditorGUI.BeginProperty(rect, GUIContent.none, blendDataArray.GetArrayElementAtIndex(index));

                    if (isProtected || isSelf)
                    {
                        using (new EditorGUI.DisabledScope(true))
                        {
                            BlendDataDrawer.Draw(rect,
                                new BlendData()
                                {
                                    m_TargetShapeName = data.m_TargetShapeName,
                                    m_Weight = 0,
                                    m_LeftWeight = 0,
                                    m_RightWeight = 0,
                                    m_SplitLeftRight = false
                                });
                        }
                    }
                    else
                    {
                        EditorGUI.BeginChangeCheck();
                        var newValue = BlendDataDrawer.Draw(rect, _component.m_TargetShapes[targetIndex].m_BlendData[index]);
                        if (EditorGUI.EndChangeCheck())
                        {
                            Undo.RecordObject(blendDataArray.serializedObject.targetObject, 
                                $"Modified {blendDataArray.propertyPath}[{index}] in {blendDataArray.serializedObject.targetObject.name}");
                            _component.m_TargetShapes[targetIndex].m_BlendData[index] = newValue;
                            blendDataArray.serializedObject.Update();
                            isUpdated = true;
                        }
                    }
                    //EditorGUI.EndProperty();
                }
                else
                {
                    SerializedProperty element = blendDataArray.GetArrayElementAtIndex(index);
                    bool isProtected = IsProtectedBlendData(definitions, element);
                    using (new EditorGUI.DisabledScope(isProtected))
                    {
                        EditorGUILayout.PropertyField(element);
                    }
                }
            }
            
            return isUpdated;
        }
        
        
        /// <summary>
        /// Draws the blend data list for all entries when no active filter is applied.
        /// </summary>
        /// <param name="targetIndex">Index of the target shape to draw.</param>
        /// <param name="blendDataArray">Serialized blend data array property.</param>
        /// <returns>True if any data was modified.</returns>
        private bool DrawBlendDataList(int targetIndex, SerializedProperty blendDataArray)
        {
            bool isUpdated = false;
            IReadOnlyDictionary<string, BlendShapeDefinition> definitions = GetDefinitionLookup();
            
            for (int index= 0; index < _component.m_TargetShapes[targetIndex].m_BlendData.Length; index++)
            { 
                var data = _component.m_TargetShapes[targetIndex].m_BlendData[index];
                bool isProtected = data.IsProtected(definitions);
                bool isSelf = _component.m_TargetShapes[targetIndex].m_TargetShapeName == data.m_TargetShapeName;
                
                var height = BlendDataDrawer.GetHeight(data);
                var rect = EditorGUILayout.GetControlRect(true, height);
                
                if (isProtected || isSelf)
                {
                    using (new EditorGUI.DisabledScope(true))
                    {
                        BlendDataDrawer.Draw(rect,
                            new BlendData()
                            {
                                m_TargetShapeName = data.m_TargetShapeName,
                                m_Weight = 0,
                                m_LeftWeight = 0,
                                m_RightWeight = 0,
                                m_SplitLeftRight = false
                            });
                    }
                }
                else
                {
                    EditorGUI.BeginChangeCheck();
                    var newValue = BlendDataDrawer.Draw(rect,
                        _component.m_TargetShapes[targetIndex].m_BlendData[index]);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(blendDataArray.serializedObject.targetObject, $"Modified {blendDataArray.propertyPath}[{index}] in {blendDataArray.serializedObject.targetObject.name}");
                        _component.m_TargetShapes[targetIndex].m_BlendData[index] = newValue;
                        blendDataArray.serializedObject.Update();
                        isUpdated = true;
                    }
                }
            }
            
            return isUpdated;
        }

        /// <summary>
        /// Calculates the total height needed to draw the visible blend data entries.
        /// </summary>
        /// <param name="targetIndex">Index of the target shape to measure.</param>
        /// <param name="activeIndices">Visible blend data indices.</param>
        /// <returns>Total height including spacing.</returns>
        private float CalculateBlendDataListHeight(int targetIndex, List<int> activeIndices)
        {
            if (_component == null ||
                _component.m_TargetShapes == null ||
                activeIndices == null ||
                targetIndex < 0 ||
                targetIndex >= _component.m_TargetShapes.Length)
            {
                return 0f;
            }

            BlendData[] blendDataArray = _component.m_TargetShapes[targetIndex].m_BlendData;
            if (blendDataArray == null)
            {
                return 0f;
            }

            float totalHeight = 0f;
            foreach (int index in activeIndices)
            {
                if (index < 0 || index >= blendDataArray.Length)
                {
                    continue;
                }

                BlendData blendData = blendDataArray[index];
                if (blendData == null)
                {
                    continue;
                }

                float propertyHeight = BlendDataDrawer.GetHeight(blendData);
                totalHeight += propertyHeight + EditorGUIUtility.standardVerticalSpacing;
            }

            return totalHeight;
        }

        /// <summary>
        /// Maps active blend shape names to their indices in the serialized blend data array.
        /// </summary>
        /// <param name="blendDataArray">Serialized blend data array property.</param>
        /// <param name="activeBlendShapeNames">Active blend shape names to keep visible.</param>
        /// <returns>List of indices ordered by the active name list.</returns>
        private List<int> GetActiveBlendDataIndices(SerializedProperty blendDataArray, IReadOnlyCollection<string> activeBlendShapeNames)
        {
            var result = new List<int>();
            if (blendDataArray == null)
            {
                return result;
            }
            if (activeBlendShapeNames == null)
            {
                return result;
            }
            // Build name → index map for quick lookup
            var nameToIndex = new Dictionary<string, int>(blendDataArray.arraySize);
            for (int i = 0; i < blendDataArray.arraySize; i++)
            {
                var elementProp = blendDataArray.GetArrayElementAtIndex(i);
                string blendName = elementProp.FindPropertyRelative(nameof(BlendData.m_TargetShapeName)).stringValue;
                if (!string.IsNullOrEmpty(blendName))
                {
                    nameToIndex[blendName] = i;
                }
            }

            // Follow the order of activeRendererBlendShapes
            foreach (string activeName in activeBlendShapeNames)
            {
                if (nameToIndex.TryGetValue(activeName, out int index))
                {
                    result.Add(index);
                }
            }
            return result;
        }
    }
}

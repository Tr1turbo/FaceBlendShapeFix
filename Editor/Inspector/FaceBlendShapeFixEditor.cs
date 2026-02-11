using System;
using System.Collections.Generic;
using System.Linq;
using Triturbo.FaceBlendShapeFix.Runtime;
using UnityEngine;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using Debug = UnityEngine.Debug;
using L = Triturbo.FaceBlendShapeFix.Localization;

namespace Triturbo.FaceBlendShapeFix.Inspector
{
    [CustomEditor(typeof(FaceBlendShapeFixComponent))]
    public class FaceBlendShapeFixEditor : Editor
    {
        internal FaceBlendShapeFixComponent component;
        
        private TargetShapeArrayDrawer _targetShapeArrayDrawer;
        private BlendShapeDefinitionDrawer blendShapeDefinitionDrawer;

        private SerializedProperty targetRendererProp;
        private SerializedProperty smoothWidthProp;
        private SerializedProperty targetShapesProp;
        private SerializedProperty blendShapeDefinitionsProp;
        private SerializedProperty inspectorSettingsProp;
        private SerializedProperty categoryDatabasesProp;
        private SerializedProperty customCategoryNamesProp;

        private readonly AdvancedDropdownState _addTargetShapeDropdownState = new AdvancedDropdownState();

        private bool _uncategorizedFoldoutState = true;

        private readonly List<CategoryDescriptor> _orderedCategoryDescriptors = new List<CategoryDescriptor>();
        private readonly HashSet<string> _descriptorNameSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _categoryNameBuffer = new List<string>();

        private string[] _categoryPopupOptions = { "<Unassigned>" };

        private bool _settingsFoldout = false;
        private readonly Dictionary<string, bool> _categoryFoldoutState = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        private string _renamingCategoryKey = null;
        private string _renamingCategoryNewName = "";

        private static GUIContent CategoryDatabasesContent => L.G("editor.category_databases");
        private static GUIContent CustomCategoryNamesContent => L.G("editor.custom_categories");
        private static GUIContent CategoryFieldContent => L.G("editor.category");
        private static GUIContent AutoFillButtonContent => L.G("editor.auto_fill_from_categories");
        private static GUIContent AddTargetShapeButtonContent => L.G("editor.add_target_shape");
        private static GUIContent CollapseAllBlendDataContent => L.G("editor.collapse_all_blend_data");

        private BlendShapeActivationObserver _blendShapeActivationObserver;

        [SerializeField]
        private Texture2D BannerIcon;

        private static string _cachedVersion;

        private static string GetPackageVersion()
        {
            if (_cachedVersion != null)
                return _cachedVersion;

            var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/com.triturbo.face-blendshape-fix");
            _cachedVersion = packageInfo?.version ?? "unknown";
            return _cachedVersion;
        }

        // Unity lifecycle
        void OnEnable()
        { 
            targetShapesProp = serializedObject.FindProperty(nameof(FaceBlendShapeFixComponent.m_TargetShapes));
            blendShapeDefinitionsProp = serializedObject.FindProperty(nameof(FaceBlendShapeFixComponent.m_BlendShapeDefinitions));
            inspectorSettingsProp = serializedObject.FindProperty(nameof(FaceBlendShapeFixComponent.m_InspectorSettings));
            targetRendererProp = serializedObject.FindProperty(nameof(FaceBlendShapeFixComponent.m_TargetRenderer));
            smoothWidthProp = serializedObject.FindProperty(nameof(FaceBlendShapeFixComponent.m_SmoothWidth));
            categoryDatabasesProp = serializedObject.FindProperty(nameof(FaceBlendShapeFixComponent.m_CategoryDatabases));
            customCategoryNamesProp = serializedObject.FindProperty(nameof(FaceBlendShapeFixComponent.m_CustomCategoryNames));

            blendShapeDefinitionDrawer ??= new BlendShapeDefinitionDrawer(this);


            component = target as FaceBlendShapeFixComponent;
            Debug.Assert(component != null);
            
            

            _blendShapeActivationObserver = new BlendShapeActivationObserver(component.TargetRenderer);
            _targetShapeArrayDrawer = new TargetShapeArrayDrawer(component, targetShapesProp, _blendShapeActivationObserver);
            _targetShapeArrayDrawer.DrawCategorySelector = DrawCategorySelector;
            
            
            //targetShapeDrawer ??= new TargetShapeDrawer(this, targetShapesProp);

            _blendShapeActivationObserver.OnActiveBlendShapesChanged += (change) =>
            {
                EnsureBlendDataForActiveShapes(change.Added);
                EnsureBlendShapeDefinitionsForActiveShapes(change.Added);
                _targetShapeArrayDrawer.OnActiveBlendShapesChanged(change.Active);
                Repaint();
            };
            
            _targetShapeArrayDrawer.OnActiveBlendShapesChanged(_blendShapeActivationObserver.ActiveShapes);
            EnsureBlendDataForActiveShapes(_blendShapeActivationObserver.ActiveShapes);
            EnsureBlendShapeDefinitionsForActiveShapes(_blendShapeActivationObserver.ActiveShapes);

            blendShapeDefinitionDrawer.Initialize(blendShapeDefinitionsProp);
            component.m_BlendShapeDefinitions ??= Array.Empty<BlendShapeDefinition>();
        }

        private void OnDisable()
        {
            component.EndPreview();
            //targetShapeDrawer?.Reset();
            blendShapeDefinitionDrawer?.Reset();
            _blendShapeActivationObserver.Dispose();
        }

        // Inspector rendering
        public override void OnInspectorGUI()
        {
      
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace(); // Pushes the label to the center
            GUILayout.Label(BannerIcon, GUILayout.Height(64), GUILayout.Width(256));
            GUILayout.FlexibleSpace(); // Pushes the label to the center
            GUILayout.EndHorizontal();

            // Version label
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            GUILayout.Label($"v{GetPackageVersion()}", EditorStyles.miniLabel);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            // Warning if GameObject is disabled
            if (component != null && !component.isActiveAndEnabled)
            {
                EditorGUILayout.HelpBox(L.Get("editor.warning.component_disabled"), MessageType.Warning);
            }

            L.DrawLanguagePopup(L.Get("editor.language"));
            
            serializedObject.Update();
            UpdateCategoryCaches();
            
            Debug.Assert(_blendShapeActivationObserver != null);
            Debug.Assert(_blendShapeActivationObserver.ActiveShapes != null);

            blendShapeDefinitionDrawer?.Draw(_blendShapeActivationObserver.ActiveShapes.ToHashSet());
            //EditorGUILayout.Space();
            DrawTargetShapesSection();
            //DrawBlendDataTreeView();
            DrawSettingsFoldout();

            serializedObject.ApplyModifiedProperties();
        }


        // Target shape UI
        private void DrawSettingsFoldout()
        {
            
            _settingsFoldout = EditorGUILayout.BeginFoldoutHeaderGroup(_settingsFoldout, L.Get("editor.settings"));
            EditorGUILayout.EndFoldoutHeaderGroup();

            if (_settingsFoldout)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUI.IndentLevelScope())
                    {
                        EditorGUI.BeginChangeCheck();
                        EditorGUILayout.PropertyField(targetRendererProp);
                        if (EditorGUI.EndChangeCheck())
                        {
                            serializedObject.ApplyModifiedProperties();
                            serializedObject.Update();
                        }

                        EditorGUILayout.PropertyField(smoothWidthProp, L.G("editor.smooth_width"));
                        EditorGUILayout.PropertyField(inspectorSettingsProp);
                        EditorGUILayout.PropertyField(categoryDatabasesProp, CategoryDatabasesContent, true);
                        EditorGUILayout.PropertyField(customCategoryNamesProp, CustomCategoryNamesContent, true);

                        
                        

                    }
                }
            }

            EditorGUILayout.Space();
            
            using (new EditorGUI.DisabledScope(component?.m_CategoryDatabases == null || component.m_CategoryDatabases.Length == 0 ||
            component.TargetRenderer == null || component.TargetRenderer.sharedMesh == null))
            {
                if (GUILayout.Button(AutoFillButtonContent))
                {
                    serializedObject.ApplyModifiedProperties();
                    AutoFillFromCategories();
                    EnsureBlendDataForActiveShapes(_blendShapeActivationObserver.ActiveShapes.ToHashSet());
                    
                    serializedObject.ApplyModifiedProperties();
                    EnsureBlendShapeDefinitionsForActiveShapes(_blendShapeActivationObserver.ActiveShapes.ToHashSet());
                    serializedObject.Update();
                }
            }
        }

        private void DrawTargetShapesSection()
        {
            Debug.Assert(targetShapesProp != null);

            Rect currentRect = EditorGUILayout.GetControlRect();
            GUIContent label = EditorGUI.BeginProperty(currentRect, 
            new GUIContent($"{L.Get("editor.target_shapes")} ({targetShapesProp.arraySize})"), targetShapesProp);

            targetShapesProp.isExpanded = EditorGUI.BeginFoldoutHeaderGroup(currentRect, targetShapesProp.isExpanded, label);
            EditorGUI.EndProperty();

            if (targetShapesProp.isExpanded)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    Rect buttonAnchorRect = EditorGUILayout.GetControlRect(false, 0f);
                    DrawCollapseBlendDataButton(buttonAnchorRect);
                    
                    using (new EditorGUI.IndentLevelScope())
                    {
                        DrawTargetShapeGroups();
                    }
                }
                DrawAddTargetShapeButton();
            }
                
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawCollapseBlendDataButton(Rect anchorRect)
        {
            using (new EditorGUI.DisabledScope(targetShapesProp == null || targetShapesProp.arraySize == 0))
            {
                GUIStyle style = EditorStyles.miniButton;
                Vector2 size = style.CalcSize(CollapseAllBlendDataContent);
                const float padding = 4f;
                
                float width = Mathf.Min(size.x, anchorRect.width * 0.4f);

                Rect buttonRect = new Rect(
                    anchorRect.xMax - width - padding,
                    anchorRect.y + padding,
                    width,
                    size.y);

                if (GUI.Button(buttonRect, CollapseAllBlendDataContent, style))
                {
                    _targetShapeArrayDrawer?.CollapseAllBlendDataFoldouts();
                }
            }
        }

        private void DrawTargetShapeGroups()
        {
            if (targetShapesProp == null)
            {
                return;
            }
            
            if (targetShapesProp.arraySize == 0)
            {
                EditorGUILayout.HelpBox(L.Get("editor.no_target_shapes"), MessageType.Info);
                return;
            }

            var groups = BuildCategoryGroups(targetShapesProp);
            
            EditorGUILayout.Space();

            for (int i = 0; i < groups.Count; i++)
            {
                var group = groups[i];
                if (group.Indices.Count == 0)
                {
                    continue;
                }

                bool expanded = GetCategoryFoldoutState(group.Key);
                bool newState = expanded;

                string label = $"{group.DisplayName} ({group.Indices.Count})";

                Rect foldoutRect = EditorGUILayout.GetControlRect();

                // Check if this category is being renamed
                bool isRenaming = !string.IsNullOrEmpty(_renamingCategoryKey) && _renamingCategoryKey == group.Key;

                if (isRenaming)
                {
                    // Calculate button sizes
                    const float buttonWidth = 24f;
                    const float buttonSpacing = 2f;
                    float totalButtonWidth = (buttonWidth * 2) + buttonSpacing;

                    // Text field rect (leave space for buttons)
                    Rect textFieldRect = new Rect(foldoutRect.x, foldoutRect.y, foldoutRect.width * 0.6f - totalButtonWidth - buttonSpacing, foldoutRect.height);

                    // Confirm button rect
                    Rect confirmRect = new Rect(textFieldRect.xMax + buttonSpacing, foldoutRect.y, buttonWidth, foldoutRect.height);

                    // Cancel button rect
                    Rect cancelRect = new Rect(confirmRect.xMax + buttonSpacing, foldoutRect.y, buttonWidth, foldoutRect.height);
                    
                    newState = EditorGUI.Foldout(foldoutRect, expanded, GUIContent.none);
                    // Draw text field for rename

                    _renamingCategoryNewName = EditorGUI.TextField(textFieldRect, _renamingCategoryNewName);
                   
                    // Draw confirm button (checkmark)
                    if (GUI.Button(confirmRect, "✓", EditorStyles.miniButton))
                    {
                        ConfirmCategoryRename();
                    }

                    // Draw cancel button (X)
                    if (GUI.Button(cancelRect, "✕", EditorStyles.miniButton))
                    {
                        CancelCategoryRename();
                    }

                    // Handle Enter to confirm, Escape to cancel
                    if (Event.current.type == EventType.KeyDown)
                    {
                        if (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter)
                        {
                            ConfirmCategoryRename();
                            Event.current.Use();
                        }
                        else if (Event.current.keyCode == KeyCode.Escape)
                        {
                            CancelCategoryRename();
                            Event.current.Use();
                        }
                    }
                }
                else
                {
                    newState = EditorGUI.Foldout(foldoutRect, expanded, label, true);

                    // Handle right-click context menu on foldout
                    if (Event.current.type == EventType.ContextClick && foldoutRect.Contains(Event.current.mousePosition))
                    {
                        var menu = new GenericMenu();
                        var indicesToClear = new List<int>(group.Indices);

                        // Only show Rename for non-empty category keys (not Uncategorized)
                        if (!string.IsNullOrEmpty(group.Key))
                        {
                            string categoryKeyCapture = group.Key;
                            menu.AddItem(new GUIContent(L.Get("editor.rename")), false, () => StartCategoryRename(categoryKeyCapture));
                        }

                        menu.AddItem(new GUIContent(L.Get("editor.delete")), false, () => DeleteCategoryTargetShapes(indicesToClear));
                        menu.ShowAsContext();
                        Event.current.Use();
                    }

    
                }

                if (newState != expanded)
                {
                    SetCategoryFoldoutState(group.Key, newState);
                    expanded = newState;
                }

                if (expanded)
                {
                    foreach (int index in group.Indices)
                    {
                        //targetShapeDrawer.Draw(targetShapesProp, index);
                        _targetShapeArrayDrawer.OnGUI(index);
                        
                        EditorGUILayout.Space();
                    }
                }

                if(i != groups.Count - 1)
                {
                    DrawCategorySeparator();
                }
            }
            EditorGUILayout.Space();
        }
        
        private void DrawAddTargetShapeButton()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();

                bool hasRenderer = component?.TargetRenderer != null;
                using (new EditorGUI.DisabledScope(!hasRenderer))
                {
                    Rect buttonRect = GUILayoutUtility.GetRect(AddTargetShapeButtonContent, EditorStyles.miniButton, GUILayout.Width(150f));
                    if (GUI.Button(buttonRect, AddTargetShapeButtonContent, EditorStyles.miniButton))
                    {
                        ShowAddTargetShapeDropdown(buttonRect);
                    }
                }
            }
        }

        private void ShowAddTargetShapeDropdown(Rect buttonRect)
        {
            var options = BuildAvailableTargetShapeOptions();
            if (options.Count == 0)
            {
                EditorUtility.DisplayDialog(
                    L.Get("editor.dialog.no_blendshapes.title"),
                    L.Get("editor.dialog.no_blendshapes.message"),
                    L.Get("editor.ok"));
                return;
            }
            var dropdown = new TargetShapeDropdown(_addTargetShapeDropdownState, options, OnTargetShapeSelected);
            dropdown.Show(buttonRect);
        }

        private List<TargetShapeDropdown.Option> BuildAvailableTargetShapeOptions()
        {
            var result = new List<TargetShapeDropdown.Option>();

            Mesh mesh = component?.TargetRenderer?.sharedMesh;
            if (mesh == null)
            {
                return result;
            }

            var existingTargets = component.m_TargetShapes?
                .Select(t => t?.m_TargetShapeName)
                .Where(n => !string.IsNullOrEmpty(n))
                .ToHashSet(StringComparer.Ordinal) ?? new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < mesh.blendShapeCount; i++)
            {
                string blendShapeName = mesh.GetBlendShapeName(i);
                if (string.IsNullOrEmpty(blendShapeName) || existingTargets.Contains(blendShapeName))
                {
                    continue;
                }

                bool isActive = !Mathf.Approximately(component.TargetRenderer.GetBlendShapeWeight(i), 0f);

                isActive = false;
                string label = isActive ? $"{blendShapeName} (Active)" : blendShapeName;

                result.Add(new TargetShapeDropdown.Option(blendShapeName, label, !isActive));
            }

            return result;
        }

        private void OnTargetShapeSelected(string blendShapeName)
        {
            if (string.IsNullOrEmpty(blendShapeName) || targetShapesProp == null || component == null)
            {
                return;
            }

            Undo.RecordObject(component, "Add Target Shape");
            serializedObject.Update();

            int insertIndex = targetShapesProp.arraySize;
            targetShapesProp.InsertArrayElementAtIndex(insertIndex);
            SerializedProperty newElement = targetShapesProp.GetArrayElementAtIndex(insertIndex);
            InitializeTargetShape(newElement, blendShapeName);

            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(component);
            
            
            EnsureBlendDataForActiveShapes(_blendShapeActivationObserver.ActiveShapes.ToHashSet());
            EnsureBlendShapeDefinitionsForActiveShapes(_blendShapeActivationObserver.ActiveShapes.ToHashSet());
            serializedObject.Update();
        }

        private static void InitializeTargetShape(SerializedProperty targetShapeProp, string blendShapeName)
        {
            if (targetShapeProp == null)
            {
                return;
            }

            SerializedProperty nameProp = targetShapeProp.FindPropertyRelative(nameof(TargetShape.m_TargetShapeName));
            if (nameProp != null)
            {
                nameProp.stringValue = blendShapeName;
            }

            SerializedProperty weightProp = targetShapeProp.FindPropertyRelative(nameof(TargetShape.m_Weight));
            if (weightProp != null)
            {
                weightProp.floatValue = 1f;
            }

            SerializedProperty categoryProp = targetShapeProp.FindPropertyRelative(nameof(TargetShape.m_CategoryName));
            if (categoryProp != null)
            {
                categoryProp.stringValue = string.Empty;
            }

            SerializedProperty useGlobalDefinitionsProp = targetShapeProp.FindPropertyRelative(nameof(TargetShape.m_UseGlobalDefinitions));
            if (useGlobalDefinitionsProp != null)
            {
                useGlobalDefinitionsProp.boolValue = false;
            }

            SerializedProperty shapeTypeProp = targetShapeProp.FindPropertyRelative(nameof(TargetShape.m_TargetShapeType));
            if (shapeTypeProp != null)
            {
                shapeTypeProp.enumValueIndex = (int)ShapeType.BothEyes;
            }

            SerializedProperty blendDataProp = targetShapeProp.FindPropertyRelative(nameof(TargetShape.m_BlendData));
            if (blendDataProp != null)
            {
                blendDataProp.arraySize = 0;
            }

            SerializedProperty additionalBlendDataProp = targetShapeProp.FindPropertyRelative(nameof(TargetShape.m_AdditiveBlendData));
            if (additionalBlendDataProp != null)
            {
                additionalBlendDataProp.arraySize = 0;
            }
        }

        internal void DeleteTargetShape(SerializedProperty arrayProp, int index)
        {
            if (component == null || arrayProp == null || index < 0 || index >= arrayProp.arraySize)
            {
                return;
            }

            Undo.RecordObject(component, "Delete Target Shape");
            serializedObject.Update();
            arrayProp.DeleteArrayElementAtIndex(index);
            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(component);
        }

        private void DeleteCategoryTargetShapes(List<int> indices)
        {
            if (component == null || targetShapesProp == null || indices == null || indices.Count == 0)
            {
                return;
            }

            if (!EditorUtility.DisplayDialog(
                L.Get("editor.dialog.delete_category.title"),
                L.GetFormat("editor.dialog.delete_category.message", indices.Count),
                L.Get("editor.delete"),
                L.Get("editor.cancel")))
            {
                return;
            }

            Undo.RecordObject(component, "Delete Category Target Shapes");
            serializedObject.Update();

            // Sort indices in descending order to delete from end first
            indices.Sort((a, b) => b.CompareTo(a));

            foreach (int index in indices)
            {
                if (index >= 0 && index < targetShapesProp.arraySize)
                {
                    targetShapesProp.DeleteArrayElementAtIndex(index);
                }
            }

            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(component);
        }

        // Category cache and selection
        private void UpdateCategoryCaches()
        {
            _orderedCategoryDescriptors.Clear();
            _descriptorNameSet.Clear();
            _categoryNameBuffer.Clear();

            if (component?.m_CategoryDatabases != null)
            {
                foreach (var database in component.m_CategoryDatabases)
                {
                    if (database == null)
                    {
                        continue;
                    }

                    var categories = database.Categories;
                    if (categories == null)
                    {
                        continue;
                    }

                    foreach (var category in categories)
                    {
                        if (category == null)
                        {
                            continue;
                        }

                        string name = category.CategoryName;
                        if (string.IsNullOrEmpty(name))
                        {
                            continue;
                        }

                        if (_descriptorNameSet.Add(name))
                        {
                            _orderedCategoryDescriptors.Add(new CategoryDescriptor
                            {
                                Name = name
                            });
                        }
                    }
                }
            }

            if (component?.m_CustomCategoryNames != null)
            {
                foreach (var categoryName in component.m_CustomCategoryNames)
                {
                    if (string.IsNullOrEmpty(categoryName))
                    {
                        continue;
                    }

                    if (_descriptorNameSet.Add(categoryName))
                    {
                        _orderedCategoryDescriptors.Add(new CategoryDescriptor
                        {
                            Name = categoryName
                        });
                    }
                }
            }

            if (component?.m_TargetShapes != null)
            {
                foreach (var targetShape in component.m_TargetShapes)
                {
                    if (targetShape == null || string.IsNullOrEmpty(targetShape.m_CategoryName))
                    {
                        continue;
                    }

                    if (_descriptorNameSet.Add(targetShape.m_CategoryName))
                    {
                        _orderedCategoryDescriptors.Add(new CategoryDescriptor
                        {
                            Name = targetShape.m_CategoryName
                        });
                    }
                }
            }

            _categoryNameBuffer.AddRange(_descriptorNameSet);
            _categoryNameBuffer.Sort(StringComparer.Ordinal);
            _categoryPopupOptions = BuildCategoryPopupOptions(_categoryNameBuffer);
        }

        private IReadOnlyList<CategoryDisplayGroup> BuildCategoryGroups(SerializedProperty arrayProp)
        {
            Dictionary<string, CategoryDisplayGroup> _categoryDisplayGroupLookup = new Dictionary<string, CategoryDisplayGroup>(StringComparer.OrdinalIgnoreCase);

            foreach (var descriptor in _orderedCategoryDescriptors)
            {
                var group = new CategoryDisplayGroup(descriptor.Name, descriptor.Name, null);
                if (!string.IsNullOrEmpty(descriptor.Name))
                {
                    _categoryDisplayGroupLookup[descriptor.Name] = group;
                }
            }

            CategoryDisplayGroup uncategorized = null;

            if (arrayProp != null)
            {
                for (int i = 0; i < arrayProp.arraySize; i++)
                {
                    SerializedProperty element = arrayProp.GetArrayElementAtIndex(i);
                    string categoryName = element.FindPropertyRelative(nameof(TargetShape.m_CategoryName)).stringValue;

                    if (string.IsNullOrEmpty(categoryName))
                    {
                        uncategorized ??= new CategoryDisplayGroup(string.Empty, L.Get("editor.uncategorized"), null);
                        uncategorized.Indices.Add(i);
                        continue;
                    }

                    if (!_categoryDisplayGroupLookup.TryGetValue(categoryName, out var group))
                    {
                        group = new CategoryDisplayGroup(categoryName, categoryName, null);
                        _categoryDisplayGroupLookup[categoryName] = group;
                    }
                    group.Indices.Add(i);
                }
            }

            List<CategoryDisplayGroup> displayGroups = _categoryDisplayGroupLookup.Values.Where(g =>g.Indices.Count > 0).ToList();
            if (uncategorized != null)
            {
                displayGroups.Add(uncategorized);
            }

            return displayGroups;
        }

        private bool GetCategoryFoldoutState(string categoryKey, bool defaultValue = false)
        {
            if (string.IsNullOrEmpty(categoryKey))
            {
                return _uncategorizedFoldoutState;
            }

            if (!_categoryFoldoutState.TryGetValue(categoryKey, out bool value))
            {
                value = defaultValue;
                _categoryFoldoutState[categoryKey] = value;
            }

            return value;
        }

        private void SetCategoryFoldoutState(string categoryKey, bool value)
        {
            if (string.IsNullOrEmpty(categoryKey))
            {
                _uncategorizedFoldoutState = value;
            }
            else
            {
                _categoryFoldoutState[categoryKey] = value;
            }
        }

        private void StartCategoryRename(string categoryKey)
        {
            if (string.IsNullOrEmpty(categoryKey))
            {
                return;
            }

            // Clear focus to reset any existing text field state
            GUI.FocusControl(null);
            _renamingCategoryKey = categoryKey;
            _renamingCategoryNewName = categoryKey;
        }

        private void ConfirmCategoryRename()
        {
            if (string.IsNullOrEmpty(_renamingCategoryNewName) ||
                _renamingCategoryNewName == _renamingCategoryKey)
            {
                CancelCategoryRename();
                return;
            }

            Undo.RecordObject(component, "Rename Category");
            foreach (var targetShape in component.m_TargetShapes ?? Array.Empty<TargetShape>())
            {
                if (targetShape != null &&
                    string.Equals(targetShape.m_CategoryName, _renamingCategoryKey, StringComparison.OrdinalIgnoreCase))
                {
                    targetShape.m_CategoryName = _renamingCategoryNewName;
                }
            }
            EditorUtility.SetDirty(component);
            serializedObject.Update();

            bool wasExpanded = GetCategoryFoldoutState(_renamingCategoryKey);
            _categoryFoldoutState.Remove(_renamingCategoryKey);
            SetCategoryFoldoutState(_renamingCategoryNewName, wasExpanded);

            _renamingCategoryKey = null;
            _renamingCategoryNewName = "";
        }

        private void CancelCategoryRename()
        {
            _renamingCategoryKey = null;
            _renamingCategoryNewName = "";
        }

        private static void DrawCategorySeparator()
        {
            EditorGUILayout.Space();
            Rect rect = EditorGUILayout.GetControlRect(false, 1f);
            rect.height = 1f;
            rect.xMin -= 2f;
            rect.xMax += 2f;
            Color separator = EditorGUIUtility.isProSkin
                ? new Color(1f, 1f, 1f, 0.1f)
                : new Color(0f, 0f, 0f, 0.2f);
            EditorGUI.DrawRect(rect, separator);
            EditorGUILayout.Space();
        }

        private static string[] BuildCategoryPopupOptions(List<string> categoryNames)
        {
            int count = categoryNames?.Count ?? 0;
            // +1 for "Unassigned", +1 for "Create New Category"
            var options = new string[count + 2];
            options[0] = L.Get("editor.unassigned");
            if (categoryNames != null)
            {
                for (int i = 0; i < categoryNames.Count; i++)
                {
                    options[i + 1] = categoryNames[i];
                }
            }
            // Last option is "Create New Category"
            options[count + 1] = L.Get("editor.create_new_category");
            return options;
        }

        internal void DrawCategorySelector(SerializedProperty categoryProperty)
        {
            if (categoryProperty == null)
            {
                return;
            }

            string[] options = _categoryPopupOptions;
            if (options == null || options.Length < 2)
            {
                EditorGUILayout.PropertyField(categoryProperty, CategoryFieldContent);
                return;
            }

            int currentIndex = 0;
            string currentValue = categoryProperty.stringValue;

            if (!string.IsNullOrEmpty(currentValue))
            {
                // Search in category options (skip first "Unassigned" and last "Create New Category")
                for (int i = 1; i < options.Length - 1; i++)
                {
                    if (string.Equals(options[i], currentValue, StringComparison.Ordinal))
                    {
                        currentIndex = i;
                        break;
                    }
                }
            }
            GUIContent[] displayedOptions = options.Select(o => new GUIContent(o)).ToArray();

            Rect currentRect = EditorGUILayout.GetControlRect();
            GUIContent label = EditorGUI.BeginProperty(currentRect, CategoryFieldContent, categoryProperty);
            EditorGUI.BeginChangeCheck();
            int newIndex = EditorGUI.Popup(currentRect, label, currentIndex, displayedOptions);
            if (EditorGUI.EndChangeCheck())
            {
                int createNewCategoryIndex = options.Length - 1;
                if (newIndex == createNewCategoryIndex)
                {
                    // Defer dialog to avoid disrupting IMGUI stack
                    string propertyPath = categoryProperty.propertyPath;
                    EditorApplication.delayCall += () => ShowCreateCategoryDialog(propertyPath);
                }
                else
                {
                    categoryProperty.stringValue = newIndex <= 0 ? string.Empty : options[newIndex];
                }
            }

            EditorGUI.EndProperty();
        }

        private void ShowCreateCategoryDialog(string categoryPropertyPath)
        {
            string newCategoryName = EditorInputDialog.Show(
                L.Get("editor.dialog.create_category.title"),
                L.Get("editor.dialog.create_category.message"),
                string.Empty);

            if (string.IsNullOrEmpty(newCategoryName))
            {
                return;
            }

            // Check if category already exists
            bool alreadyExists = _categoryNameBuffer.Any(c => string.Equals(c, newCategoryName, StringComparison.OrdinalIgnoreCase));

            if (!alreadyExists)
            {
                // Add to custom categories
                Undo.RecordObject(component, "Add Custom Category");
                var customCategories = component.m_CustomCategoryNames?.ToList() ?? new List<string>();
                customCategories.Add(newCategoryName);
                component.m_CustomCategoryNames = customCategories.ToArray();
                EditorUtility.SetDirty(component);
            }

            // Set the category on the property
            serializedObject.Update();
            var categoryProperty = serializedObject.FindProperty(categoryPropertyPath);
            if (categoryProperty != null)
            {
                categoryProperty.stringValue = newCategoryName;
                serializedObject.ApplyModifiedProperties();
            }

            UpdateCategoryCaches();
        }

        // Category matching and auto-fill
        private bool TryResolveCategory(BlendShapeMatchContext context, out BlendShapeCategoryMatch match)
        {
            match = default;

            if (component?.m_CategoryDatabases == null || component.m_CategoryDatabases.Length == 0)
            {
                return false;
            }

            bool hasMatch = false;
            int bestPriority = int.MinValue;
            int bestDatabaseIndex = int.MaxValue;

            for (int i = 0; i < component.m_CategoryDatabases.Length; i++)
            {
                var database = component.m_CategoryDatabases[i];
                if (database == null)
                {
                    continue;
                }

                if (!database.TryMatch(context, out var candidate))
                {
                    continue;
                }

                int candidatePriority = candidate.Priority;
                if (!hasMatch || candidatePriority > bestPriority || (candidatePriority == bestPriority && i < bestDatabaseIndex))
                {
                    match = candidate;
                    hasMatch = true;
                    bestPriority = candidatePriority;
                    bestDatabaseIndex = i;
                }
            }
            return hasMatch;
        }

        private void AutoFillFromCategories()
        {
            if (component == null)
            {
                return;
            }

            SkinnedMeshRenderer smr = component.TargetRenderer != null
                ? component.TargetRenderer
                : component.GetComponent<SkinnedMeshRenderer>();

            if (smr == null || smr.sharedMesh == null)
            {
                Debug.LogWarning("No SkinnedMeshRenderer with a valid mesh was found for auto-fill.");
                return;
            }

            Mesh mesh = smr.sharedMesh;

            if (mesh == null || mesh.blendShapeCount == 0)
            {
                Debug.LogWarning("The selected mesh does not contain any blendshapes.");
                return;
            }

            var targets = component.m_TargetShapes?.ToList() ?? new List<TargetShape>();
            var lookup = new Dictionary<string, TargetShape>(StringComparer.Ordinal);

            foreach (var targetShape in targets)
            {
                if (targetShape == null || string.IsNullOrEmpty(targetShape.m_TargetShapeName))
                {
                    continue;
                }

                lookup[targetShape.m_TargetShapeName] = targetShape;
            }

            int added = 0;
            int updated = 0;

            for (int i = 0; i < mesh.blendShapeCount; i++)
            {
                string blendShapeName = mesh.GetBlendShapeName(i);
                if (string.IsNullOrEmpty(blendShapeName))
                {
                    continue;
                }

                var context = new BlendShapeMatchContext(blendShapeName, smr, mesh, i);
                if (!TryResolveCategory(context, out var match))
                {
                    continue;
                }

                if (!lookup.TryGetValue(blendShapeName, out var targetShape) || targetShape == null)
                {
                    targetShape = new TargetShape
                    {
                        m_TargetShapeName = blendShapeName,
                        m_CategoryName = match.CategoryName,
                        m_TargetShapeType = match.TargetShapeType,
                        m_UseGlobalDefinitions = match.UseGlobalDefinitions,
                        m_BlendData = Array.Empty<BlendData>(),
                        m_AdditiveBlendData = Array.Empty<BlendData>()
                    };

                    targets.Add(targetShape);
                    lookup[blendShapeName] = targetShape;
                    added++;
                }
                else
                {
                    if (!string.Equals(targetShape.m_CategoryName, match.CategoryName, StringComparison.Ordinal))
                    {
                        targetShape.m_CategoryName = match.CategoryName;
                        updated++;
                    }

                    if (targetShape.m_TargetShapeType != match.TargetShapeType)
                    {
                        targetShape.m_TargetShapeType = match.TargetShapeType;
                        updated++;
                    }

                    if (targetShape.m_UseGlobalDefinitions != match.UseGlobalDefinitions)
                    {
                        targetShape.m_UseGlobalDefinitions = match.UseGlobalDefinitions;
                        updated++;
                    }
                }
            }

            if (added == 0 && updated == 0)
            {
                Debug.Log("No blendshapes matched any configured category database.");
                return;
            }

            Undo.RecordObject(component, "Auto Fill Target Shapes");
            component.m_TargetShapes = targets.ToArray();
            EditorUtility.SetDirty(component);
            serializedObject.Update();
            Debug.Log($"Auto-filled {added} target shape(s); updated {updated} existing entries.");
        }

        // Data sync
        private void EnsureBlendShapeDefinitionsForActiveShapes(IReadOnlyCollection<string> newActiveBlendShapes)
        {
            if (component == null || component.TargetRenderer == null || component.TargetRenderer.sharedMesh == null)
            {
                return;
            }

            Mesh mesh = component.TargetRenderer.sharedMesh;
            var definitions = component.m_BlendShapeDefinitions?.ToList() ?? new List<BlendShapeDefinition>();
            List<int> eyeReferences = BlendShapeDataUtil.GetBlendShapeIndices(
                mesh,
                BlendShapeDataUtil.GetBlendShapesFromType(component.m_TargetShapes, ShapeType.BothEyes));
            List<int> mouthReferences = BlendShapeDataUtil.GetBlendShapeIndices(
                mesh,
                BlendShapeDataUtil.GetBlendShapesFromType(component.m_TargetShapes, ShapeType.Mouth));
            if (eyeReferences.Count == 0 || mouthReferences.Count == 0)
            {
                return;
            }

            bool recordedUndo = false;
            bool addedDefinition = false;

            foreach (string shapeName in newActiveBlendShapes)
            {
                if(definitions.Any(d=>d.m_BlendShapeName == shapeName)) continue;
                
                int index = mesh.GetBlendShapeIndex(shapeName);
                if (index == -1)
                {
                    continue;
                }
                BlendShapeDefinition definition = BlendShapeDataUtil.CreateDefinition(
                    component.TargetRenderer,
                    index,
                    eyeReferences,
                    mouthReferences,
                    BlendShapeDataUtil.BlendShapeComparisonMode.Max);
                if (definition == null)
                {
                    continue;
                }

                if (!recordedUndo)
                {
                    Undo.RecordObject(component, "Add BlendShape Definition");
                    recordedUndo = true;
                }
                definitions.Add(definition);
                addedDefinition = true;
            }

            if (addedDefinition)
            {
                component.m_BlendShapeDefinitions = definitions.ToArray();
                EditorUtility.SetDirty(component);
                serializedObject.Update();
            }
            
        }
        
        private void EnsureBlendDataForActiveShapes(IReadOnlyCollection<string> newActiveBlendShapes)
        {
            if (component == null || component.TargetRenderer == null || component.TargetRenderer.sharedMesh == null)
                return;
            if (newActiveBlendShapes.Count == 0)
            {
                return;
            }
            
            bool recordedUndo = false;
            bool addedBlendData = false;
            var smr = component.TargetRenderer;
            
            foreach (var targetShape in component.m_TargetShapes ?? Enumerable.Empty<TargetShape>())
            {
                if (targetShape == null)
                {
                    continue;
                }

                if (!recordedUndo)
                {
                    Undo.RecordObject(component, "Sync BlendData");
                    recordedUndo = true;
                }

                bool updatedShape = BlendShapeDataUtil.EnsureBlendDataForActiveShapes(targetShape, newActiveBlendShapes, smr);
                if (updatedShape)
                {
                    addedBlendData = true;
                }
            }

            if (addedBlendData)
            {
                EditorUtility.SetDirty(component);
                serializedObject.Update();
            }
        }

        private sealed class CategoryDescriptor
        {
            public string Name;
        }

        private sealed class CategoryDisplayGroup
        {
            public string Key { get; }
            public string DisplayName { get; }
            public Color? Color { get; }
            public List<int> Indices { get; } = new List<int>();

            public CategoryDisplayGroup(string key, string displayName, Color? color)
            {
                Key = key;
                DisplayName = string.IsNullOrEmpty(displayName) ? L.Get("editor.uncategorized") : displayName;
                Color = color;
            }
        }
    }
}

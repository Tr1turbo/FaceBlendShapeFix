// using System.Collections.Generic;
// using Triturbo.FaceBlendShapeFix.Runtime;
// using UnityEditor;
// using UnityEditorInternal;
// using UnityEngine;
// using L = Triturbo.FaceBlendShapeFix.Localization;

// namespace Triturbo.FaceBlendShapeFix.Inspector
// {
//     [CustomEditor(typeof(BlendShapeCategoryDatabase))]
//     internal sealed class BlendShapeCategoryDatabaseEditor : Editor
//     {
//         private SerializedProperty m_CategoriesProperty;
//         private ReorderableList m_CategoryList;
//         private readonly Dictionary<string, ReorderableList> m_EntryLists = new();
//         private readonly Dictionary<string, ReorderableList> m_AliasLists = new();

//         private void OnEnable()
//         {
//             m_CategoriesProperty = serializedObject.FindProperty("m_Categories");

//             m_CategoryList = new ReorderableList(serializedObject, m_CategoriesProperty, true, true, true, true)
//             {
//                 drawHeaderCallback = DrawCategoryHeader,
//                 drawElementCallback = DrawCategoryElement,
//                 elementHeightCallback = GetCategoryElementHeight,
//                 onAddCallback = OnAddCategory,
//                 onRemoveCallback = OnRemoveCategory
//             };
//         }

//         private void OnDisable()
//         {
//             m_EntryLists.Clear();
//             m_AliasLists.Clear();
//         }

//         public override void OnInspectorGUI()
//         {
//             L.DrawLanguagePopup(L.G("editor.language"));
//             serializedObject.Update();
//             EditorGUILayout.Space(4);
//             m_CategoryList.DoLayoutList();
//             serializedObject.ApplyModifiedProperties();
//         }

//         private void DrawCategoryHeader(Rect rect)
//         {
//             EditorGUI.LabelField(rect, L.G("editor.category_database.categories"));
//         }

//         private float GetCategoryElementHeight(int index)
//         {
//             if (index < 0 || index >= m_CategoriesProperty.arraySize)
//             {
//                 return EditorGUIUtility.singleLineHeight;
//             }

//             SerializedProperty categoryProp = m_CategoriesProperty.GetArrayElementAtIndex(index);
//             if (categoryProp == null || !categoryProp.isExpanded)
//             {
//                 return EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
//             }

//             float height = EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
//             height += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing; // Name
//             height += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing; // Color
//             height += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing; // Priority

//             SerializedProperty entriesProp = categoryProp.FindPropertyRelative("m_TargetShapeEntries");
//             if (entriesProp != null)
//             {
//                 height += GetEntriesListHeight(categoryProp.propertyPath, entriesProp);
//             }

//             height += EditorGUIUtility.standardVerticalSpacing * 2;
//             return height;
//         }

//         private float GetEntriesListHeight(string categoryPath, SerializedProperty entriesProp)
//         {
//             float height = EditorGUIUtility.singleLineHeight * 2 + EditorGUIUtility.standardVerticalSpacing * 3;

//             for (int i = 0; i < entriesProp.arraySize; i++)
//             {
//                 height += GetEntryElementHeight(categoryPath, entriesProp, i);
//             }

//             //button and footer
//             height += EditorGUIUtility.singleLineHeight* 2 + EditorGUIUtility.standardVerticalSpacing;

//             return height;
//         }

//         private float GetEntryElementHeight(string categoryPath, SerializedProperty entriesProp, int index)
//         {
//             if (index < 0 || index >= entriesProp.arraySize)
//             {
//                 return EditorGUIUtility.singleLineHeight;
//             }

//             SerializedProperty entryProp = entriesProp.GetArrayElementAtIndex(index);
//             if (entryProp == null || !entryProp.isExpanded)
//             {
//                 return EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
//             }

//             float height = EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
//             height += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing; // Display Name
//             height += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing; // Shape Type
//             height += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing; // Use Global Definitions
//             height += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing; // Case Sensitive

//             SerializedProperty aliasesProp = entryProp.FindPropertyRelative("m_Aliases");
//             if (aliasesProp != null)
//             {
//                 height += GetAliasesListHeight(aliasesProp);
//             }

//             height += EditorGUIUtility.standardVerticalSpacing;
//             return height;
//         }

//         private float GetAliasesListHeight(SerializedProperty aliasesProp)
//         {
//             // Header
//             float height = EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;

//             // Elements
//             height += (EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing) * aliasesProp.arraySize;

//             // Footer with +/- buttons
//             height += EditorGUIUtility.singleLineHeight * 2 + EditorGUIUtility.standardVerticalSpacing;

//             return height;
//         }

//         private void DrawCategoryElement(Rect rect, int index, bool isActive, bool isFocused)
//         {
//             if (index < 0 || index >= m_CategoriesProperty.arraySize)
//             {
//                 return;
//             }

//             SerializedProperty categoryProp = m_CategoriesProperty.GetArrayElementAtIndex(index);
//             if (categoryProp == null)
//             {
//                 return;
//             }

//             SerializedProperty nameProp = categoryProp.FindPropertyRelative("m_CategoryName");
//             SerializedProperty colorProp = categoryProp.FindPropertyRelative("m_CategoryColor");
//             SerializedProperty priorityProp = categoryProp.FindPropertyRelative("m_Priority");
//             SerializedProperty entriesProp = categoryProp.FindPropertyRelative("m_TargetShapeEntries");

//             Rect foldoutRect = new Rect(rect.x + 15f, rect.y, rect.width - 15f, EditorGUIUtility.singleLineHeight);

//             string displayName = string.IsNullOrEmpty(nameProp?.stringValue)
//                 ? L.Get("editor.unnamed")
//                 : nameProp.stringValue;

//             categoryProp.isExpanded = EditorGUI.Foldout(foldoutRect, categoryProp.isExpanded, displayName, true);

//             if (!categoryProp.isExpanded)
//             {
//                 return;
//             }

//             float y = rect.y + EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
//             float indentedX = rect.x + 15f;
//             float indentedWidth = rect.width - 15f;

//             Rect nameRect = new Rect(indentedX, y, indentedWidth, EditorGUIUtility.singleLineHeight);
//             EditorGUI.PropertyField(nameRect, nameProp, L.G("editor.category_database.category_name"));
//             y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;

//             Rect colorRect = new Rect(indentedX, y, indentedWidth, EditorGUIUtility.singleLineHeight);
//             EditorGUI.PropertyField(colorRect, colorProp, L.G("editor.category_database.category_color"));
//             y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;

//             Rect priorityRect = new Rect(indentedX, y, indentedWidth, EditorGUIUtility.singleLineHeight);
//             EditorGUI.PropertyField(priorityRect, priorityProp, L.G("editor.category_database.priority"));
//             y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;

//             if (entriesProp != null)
//             {
//                 Rect entriesRect = new Rect(indentedX, y, indentedWidth, GetEntriesListHeight(categoryProp.propertyPath, entriesProp));
//                 DrawEntriesList(entriesRect, categoryProp.propertyPath, entriesProp);
//             }
//         }

//         private void DrawEntriesList(Rect rect, string categoryPath, SerializedProperty entriesProp)
//         {
//             string key = categoryPath + ".entries";

//             if (!m_EntryLists.TryGetValue(key, out ReorderableList list))
//             {
//                 list = new ReorderableList(entriesProp.serializedObject, entriesProp, true, true, true, true)
//                 {
//                     drawHeaderCallback = r => EditorGUI.LabelField(r, L.G("editor.category_database.entries")),
//                     drawElementCallback = (r, i, a, f) => DrawEntryElement(r, categoryPath, entriesProp, i, a, f),
//                     elementHeightCallback = i => GetEntryElementHeight(categoryPath, entriesProp, i)
//                 };
//                 m_EntryLists[key] = list;
//             }
//             else
//             {
//                 list.serializedProperty = entriesProp;
//             }

//             list.DoList(rect);
//         }

//         private void DrawEntryElement(Rect rect, string categoryPath, SerializedProperty entriesProp, int index, bool isActive, bool isFocused)
//         {
//             if (index < 0 || index >= entriesProp.arraySize)
//             {
//                 return;
//             }

//             SerializedProperty entryProp = entriesProp.GetArrayElementAtIndex(index);
//             if (entryProp == null)
//             {
//                 return;
//             }

//             SerializedProperty displayNameProp = entryProp.FindPropertyRelative("m_DisplayName");
//             SerializedProperty shapeTypeProp = entryProp.FindPropertyRelative("m_TargetShapeType");
//             SerializedProperty useGlobalProp = entryProp.FindPropertyRelative("m_UseGlobalDefinitions");
//             SerializedProperty caseSensitiveProp = entryProp.FindPropertyRelative("m_CaseSensitive");
//             SerializedProperty aliasesProp = entryProp.FindPropertyRelative("m_Aliases");

//             Rect foldoutRect = new Rect(rect.x, rect.y, rect.width, EditorGUIUtility.singleLineHeight);

//             string displayName = string.IsNullOrEmpty(displayNameProp?.stringValue)
//                 ? L.Get("editor.unnamed")
//                 : displayNameProp.stringValue;

//             entryProp.isExpanded = EditorGUI.Foldout(foldoutRect, entryProp.isExpanded, displayName, true);

//             if (!entryProp.isExpanded)
//             {
//                 return;
//             }

//             float y = rect.y + EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
//             float indentedX = rect.x + 15f;
//             float indentedWidth = rect.width - 15f;

//             Rect displayNameRect = new Rect(indentedX, y, indentedWidth, EditorGUIUtility.singleLineHeight);
//             EditorGUI.PropertyField(displayNameRect, displayNameProp, L.G("editor.category_database.display_name"));
//             y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;

//             Rect shapeTypeRect = new Rect(indentedX, y, indentedWidth, EditorGUIUtility.singleLineHeight);
//             L.LocalizedEnumPropertyField(shapeTypeRect, shapeTypeProp, L.G("editor.target_shape_type"), "enum.shapetype");
//             y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;

//             Rect useGlobalRect = new Rect(indentedX, y, indentedWidth, EditorGUIUtility.singleLineHeight);
//             EditorGUI.PropertyField(useGlobalRect, useGlobalProp, L.G("editor.category_database.use_global_definitions"));
//             y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;

//             Rect caseSensitiveRect = new Rect(indentedX, y, indentedWidth, EditorGUIUtility.singleLineHeight);
//             EditorGUI.PropertyField(caseSensitiveRect, caseSensitiveProp, L.G("editor.category_database.case_sensitive"));
//             y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;

//             if (aliasesProp != null)
//             {
//                 Rect aliasesRect = new Rect(indentedX, y, indentedWidth, GetAliasesListHeight(aliasesProp));
//                 DrawAliasesList(aliasesRect, entryProp.propertyPath, aliasesProp);
//             }
//         }

//         private void DrawAliasesList(Rect rect, string entryPath, SerializedProperty aliasesProp)
//         {
//             string key = entryPath + ".aliases";

//             if (!m_AliasLists.TryGetValue(key, out ReorderableList list))
//             {
//                 list = new ReorderableList(aliasesProp.serializedObject, aliasesProp, true, true, true, true)
//                 {
//                     drawHeaderCallback = r => EditorGUI.LabelField(r, L.G("editor.category_database.aliases")),
//                     drawElementCallback = (r, i, a, f) => DrawAliasElement(r, aliasesProp, i),
//                     elementHeight = EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing
//                 };
//                 m_AliasLists[key] = list;
//             }
//             else
//             {
//                 list.serializedProperty = aliasesProp;
//             }

//             list.DoList(rect);
//         }

//         private void DrawAliasElement(Rect rect, SerializedProperty aliasesProp, int index)
//         {
//             if (index < 0 || index >= aliasesProp.arraySize)
//             {
//                 return;
//             }

//             SerializedProperty aliasProp = aliasesProp.GetArrayElementAtIndex(index);
//             if (aliasProp == null)
//             {
//                 return;
//             }

//             Rect fieldRect = new Rect(rect.x, rect.y, rect.width, EditorGUIUtility.singleLineHeight);
//             EditorGUI.PropertyField(fieldRect, aliasProp, GUIContent.none);
//         }

//         private void OnAddCategory(ReorderableList list)
//         {
//             int newIndex = m_CategoriesProperty.arraySize;
//             m_CategoriesProperty.InsertArrayElementAtIndex(newIndex);

//             SerializedProperty newCategory = m_CategoriesProperty.GetArrayElementAtIndex(newIndex);
//             if (newCategory != null)
//             {
//                 SerializedProperty nameProp = newCategory.FindPropertyRelative("m_CategoryName");
//                 SerializedProperty colorProp = newCategory.FindPropertyRelative("m_CategoryColor");
//                 SerializedProperty priorityProp = newCategory.FindPropertyRelative("m_Priority");
//                 SerializedProperty entriesProp = newCategory.FindPropertyRelative("m_TargetShapeEntries");

//                 if (nameProp != null) nameProp.stringValue = L.Get("editor.category_database.new_category");
//                 if (colorProp != null) colorProp.colorValue = Color.white;
//                 if (priorityProp != null) priorityProp.intValue = 0;
//                 if (entriesProp != null) entriesProp.ClearArray();
//             }
//         }

//         private void OnRemoveCategory(ReorderableList list)
//         {
//             if (list.index >= 0 && list.index < m_CategoriesProperty.arraySize)
//             {
//                 SerializedProperty categoryProp = m_CategoriesProperty.GetArrayElementAtIndex(list.index);
//                 string key = categoryProp?.propertyPath + ".entries";
//                 m_EntryLists.Remove(key);

//                 m_CategoriesProperty.DeleteArrayElementAtIndex(list.index);
//             }
//         }
//     }
// }

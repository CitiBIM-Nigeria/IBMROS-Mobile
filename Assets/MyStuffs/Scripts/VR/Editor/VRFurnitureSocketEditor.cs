using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Custom editor for VRFurnitureSocket.
/// Replaces the raw "categoryId" text field with a user-friendly dropdown
/// powered by the VRSubcategoryCatalog.
/// </summary>
[CustomEditor(typeof(VRFurnitureSocket))]
[CanEditMultipleObjects]
public class VRFurnitureSocketEditor : Editor
{
    private SerializedProperty _categoryIdProp;
    private SerializedProperty _displayLabelProp;
    private SerializedProperty _catalogProp;

    private SerializedProperty _modelAnchorProp;
    private SerializedProperty _alignToFloorProp;
    private SerializedProperty _modelScaleProp;
    private SerializedProperty _yawOffsetProp;

    private SerializedProperty _infoPanelProp;
    private SerializedProperty _loadingIndicatorProp;
    private SerializedProperty _errorIndicatorProp;

    private SerializedProperty _autoLoadOnStartProp;
    private SerializedProperty _verboseLoggingProp;

    private void OnEnable()
    {
        _categoryIdProp = serializedObject.FindProperty("categoryId");
        _displayLabelProp = serializedObject.FindProperty("displayLabel");
        _catalogProp = serializedObject.FindProperty("catalog");

        _modelAnchorProp = serializedObject.FindProperty("modelAnchor");
        _alignToFloorProp = serializedObject.FindProperty("alignToFloor");
        _modelScaleProp = serializedObject.FindProperty("modelScale");
        _yawOffsetProp = serializedObject.FindProperty("yawOffset");

        _infoPanelProp = serializedObject.FindProperty("infoPanel");
        _loadingIndicatorProp = serializedObject.FindProperty("loadingIndicator");
        _errorIndicatorProp = serializedObject.FindProperty("errorIndicator");

        _autoLoadOnStartProp = serializedObject.FindProperty("autoLoadOnStart");
        _verboseLoggingProp = serializedObject.FindProperty("verboseLogging");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.Space();

        // 1. Draw the Subcategory Section with the Dropdown
        EditorGUILayout.LabelField("Subcategory Assignment", EditorStyles.boldLabel);

        // Allow overriding the catalog per-socket, but fallback to a default if none assigned
        EditorGUILayout.PropertyField(_catalogProp);

        VRSubcategoryCatalog catalog = _catalogProp.objectReferenceValue as VRSubcategoryCatalog;

        // If not assigned on the object, try to find one in the project
        if (catalog == null)
        {
            string[] guids = AssetDatabase.FindAssets("t:VRSubcategoryCatalog");
            if (guids.Length > 0)
            {
                catalog = AssetDatabase.LoadAssetAtPath<VRSubcategoryCatalog>(AssetDatabase.GUIDToAssetPath(guids[0]));
            }
        }

        if (catalog != null && catalog.entries != null && catalog.entries.Count > 0)
        {
            // Build the dropdown options with a placeholder at index 0
            var optionsList = catalog.entries.Select(s => s.DropdownLabel).ToList();
            optionsList.Insert(0, "— Select Subcategory —");
            string[] options = optionsList.ToArray();

            var valuesList = catalog.entries.Select(s => s.categoryId).ToList();
            valuesList.Insert(0, ""); // empty value for placeholder
            string[] values = valuesList.ToArray();

            int currentIndex = System.Array.IndexOf(values, _categoryIdProp.stringValue);
            if (currentIndex == -1) currentIndex = 0; // default to placeholder if not found
            
            EditorGUI.BeginChangeCheck();
            int newIndex = EditorGUILayout.Popup("Subcategory", currentIndex, options);
            if (EditorGUI.EndChangeCheck())
            {
                _categoryIdProp.stringValue = values[newIndex];
                
                // Auto-fill the display label if it matches the old category or is empty
                if (string.IsNullOrEmpty(_displayLabelProp.stringValue) || 
                    (currentIndex > 0 && _displayLabelProp.stringValue == options[currentIndex]))
                {
                    _displayLabelProp.stringValue = newIndex > 0 ? options[newIndex] : "";
                }
            }
        }
        else
        {
            EditorGUILayout.HelpBox("No VRSubcategoryCatalog found or catalog is empty. Create one using 'Assets > Create > IBMROS > VR Subcategory Catalog' and use the Refresher tool.", MessageType.Warning);
            EditorGUILayout.PropertyField(_categoryIdProp);
        }

        EditorGUILayout.PropertyField(_displayLabelProp);

        EditorGUILayout.Space();

        // 2. Draw the rest of the properties
        EditorGUILayout.LabelField("Placement Settings", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(_modelAnchorProp);
        EditorGUILayout.PropertyField(_alignToFloorProp);
        EditorGUILayout.PropertyField(_modelScaleProp);
        EditorGUILayout.PropertyField(_yawOffsetProp);

        EditorGUILayout.Space();
        
        EditorGUILayout.LabelField("View & Feedback", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(_infoPanelProp);
        EditorGUILayout.PropertyField(_loadingIndicatorProp);
        EditorGUILayout.PropertyField(_errorIndicatorProp);

        EditorGUILayout.Space();

        EditorGUILayout.LabelField("Behaviour & Debug", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(_autoLoadOnStartProp);
        EditorGUILayout.PropertyField(_verboseLoggingProp);

        serializedObject.ApplyModifiedProperties();

        EditorGUILayout.Space();
        
        var socket = (VRFurnitureSocket)target;
        
        if (Application.isPlaying)
        {
            string prodName = socket.CurrentProduct != null ? socket.CurrentProduct.Name : "None";
            EditorGUILayout.HelpBox($"Currently Showing: {prodName}\nClick to cycle models and save your choice.", MessageType.Info);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("< Previous Product", GUILayout.Height(30)))
            {
                socket.PreviousProduct();
            }
            if (GUILayout.Button("Next Product >", GUILayout.Height(30)))
            {
                socket.NextProduct();
            }
            GUILayout.EndHorizontal();
        }
        else
        {
            if (!string.IsNullOrEmpty(socket.SelectedProductId))
            {
                EditorGUILayout.HelpBox($"Persistent Preview: {socket.SelectedProductId}\n(Enter Play Mode to cycle products)", MessageType.None);
            }
        }
    }
}

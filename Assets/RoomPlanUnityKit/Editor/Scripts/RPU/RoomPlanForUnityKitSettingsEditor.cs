using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SilverTau.RoomPlanUnity
{
    [CustomEditor(typeof(RoomPlanUnityKitSettings))]
    public class RoomPlanForUnityKitSettingsEditor : Editor
    {
        private RoomPlanUnityKitSettings _target;
        
        protected GUIStyle helpBox;
        protected GUIStyle labelHeader;
        protected GUIStyle labelHeader2;
        protected GUIStyle labelHeader3;
        protected Color colorDarkBlue;
        protected Color colorBlack;
        protected Color colorDodgerBlue;
        
        private SerializedProperty _isDefaultScanEnabled;
        private SerializedProperty _isDefaultMiniatureModelEnabled;
        private SerializedProperty _isDefaultScanDataTransferEnabled;
        
        private SerializedProperty _isCoachingEnabled;
        private SerializedProperty _isInstructionEnabled;
        
        private SerializedProperty _createFloorToAR;
        
        private SerializedProperty _createFloorToRoomBuilder;
        
        private SerializedProperty _floorConcavity;
        private SerializedProperty _typeFloorConstructor;
        
        private SerializedProperty _isNativeScreenshot;
        private SerializedProperty _isNativeScreenshotPreview;
        
        private SerializedProperty _isNativeScreenRecorder;
        private SerializedProperty _isNativeScreenRecorderPreview;
        
        private SerializedProperty _editorAR;
        //private SerializedProperty _useFloorVisualizer;
        
        private SerializedProperty _resetObjectsIfNewRoomIsAdded;
        
        private SerializedProperty _usdExportOption;
        
        private SerializedProperty _useMeshBooleanOperationsForRoomBuilder;
        private SerializedProperty _meshBooleanOperationsForRoomBuilder;
        
        private SerializedProperty _isCustomXREnabled;
        
        private void Awake()
        {
            if (target) _target = (RoomPlanUnityKitSettings)target;
            
            ColorUtility.TryParseHtmlString("#212121", out colorBlack);
            ColorUtility.TryParseHtmlString("#174ead", out colorDodgerBlue);
            ColorUtility.TryParseHtmlString("#122d4a", out colorDarkBlue);
            
            labelHeader = new GUIStyle
            {
                alignment = TextAnchor.MiddleCenter,
                fixedHeight = 26.0f,
                richText = true,
                fontSize = 14,
                normal = new GUIStyleState
                {
                    background = CreateTexture2D(2, 2, colorBlack),
                    textColor = Color.white
                }
            };
            
            labelHeader2 = new GUIStyle
            {
                alignment = TextAnchor.MiddleCenter,
                fixedHeight = 30.0f,
                richText = true,
                fontSize = 14,
                normal = new GUIStyleState
                {
                    background = CreateTexture2D(2, 2, colorDodgerBlue),
                    textColor = Color.white
                }
            };
            
            labelHeader3 = new GUIStyle
            {
                alignment = TextAnchor.MiddleCenter,
                fixedHeight = 30.0f,
                richText = true,
                fontSize = 16,
                normal = new GUIStyleState
                {
                    background = CreateTexture2D(2, 2, colorBlack),
                    textColor = Color.white
                }
            };
        }

        private Texture2D CreateTexture2D(int width, int height, Color col)
        {
            var pix = new Color[width * height];
            for(var i = 0; i < pix.Length; ++i)
            {
                pix[i] = col;
            }
            var result = new Texture2D(width, height);
            result.SetPixels(pix);
            result.Apply();
            return result;
        }
        
        public override void OnInspectorGUI()
        {
            helpBox = new GUIStyle(GUI.skin.FindStyle("HelpBox"))
            {
                alignment = TextAnchor.MiddleLeft,
                richText = true,
                fontSize = 12,
                imagePosition = ImagePosition.ImageLeft,
                padding = new RectOffset(10, 10, 0, 0),
                normal = 
                {
                    background = CreateTexture2D(2, 2, colorDarkBlue), 
                    textColor = Color.white
                }
            };
            
            _isDefaultScanEnabled = serializedObject.FindProperty("isDefaultScanEnabled");
            _isDefaultMiniatureModelEnabled = serializedObject.FindProperty("isDefaultMiniatureModelEnabled");
            _isDefaultScanDataTransferEnabled = serializedObject.FindProperty("isDefaultScanDataTransferEnabled");
            
            _isCoachingEnabled = serializedObject.FindProperty("isCoachingEnabled");
            _isInstructionEnabled = serializedObject.FindProperty("isInstructionEnabled");
            
            _createFloorToAR = serializedObject.FindProperty("createFloorToAR");
            
            _createFloorToRoomBuilder = serializedObject.FindProperty("createFloorToRoomBuilder");
            
            _floorConcavity = serializedObject.FindProperty("floorConcavity");
            _typeFloorConstructor = serializedObject.FindProperty("typeFloorConstructor");
            
            _isNativeScreenshot = serializedObject.FindProperty("isNativeScreenshot");
            _isNativeScreenshotPreview = serializedObject.FindProperty("isNativeScreenshotPreview");
            
            _isNativeScreenRecorder = serializedObject.FindProperty("isNativeScreenRecorder");
            _isNativeScreenRecorderPreview = serializedObject.FindProperty("isNativeScreenRecorderPreview");
            
            _editorAR = serializedObject.FindProperty("editorAR");
            //_useFloorVisualizer = serializedObject.FindProperty("useFloorVisualizer");
            
            _resetObjectsIfNewRoomIsAdded = serializedObject.FindProperty("resetObjectsIfNewRoomIsAdded");
            
            _usdExportOption = serializedObject.FindProperty("usdExportOption");
            
            _useMeshBooleanOperationsForRoomBuilder = serializedObject.FindProperty("useMeshBooleanOperationsForRoomBuilder");
            _meshBooleanOperationsForRoomBuilder = serializedObject.FindProperty("meshBooleanOperationsForRoomBuilder");
            
            _isCustomXREnabled = serializedObject.FindProperty("isCustomXREnabled");
            
            serializedObject.Update();
            
            EditorGUI.BeginChangeCheck();
            
            GUILayout.Space(10);
            
            GUILayout.Box(new GUIContent("<b>RoomPlan for Unity Kit Settings</b>", EditorGUIUtility.IconContent("GameObject On Icon").image), labelHeader3);
            
            GUILayout.Space(5);
            GUILayout.Box(new GUIContent("<b> 16+ Features</b>", EditorGUIUtility.IconContent("BuildSettings.iPhone On@2x").image), labelHeader2);
            
            GUILayout.Space(5);
            GUILayout.Box(new GUIContent("\n" 
                                         + "This is a script object that has characteristics for customizing <b>RoomPlan for Unity Engine</b>. "
                                         + "Here you can customize the availability of main features for your case and your needs."
                                         + "\n", EditorGUIUtility.IconContent("console.infoicon").image), helpBox);
            
            GUILayout.Space(5);
            GUILayout.Box(new GUIContent("<b>Scanning features</b>", EditorGUIUtility.IconContent("d_Preset.Context@2x").image), labelHeader);
            GUILayout.Space(10);
            EditorGUILayout.PropertyField(_isDefaultScanEnabled);
            GUILayout.Space(5);
            EditorGUILayout.PropertyField(_isDefaultMiniatureModelEnabled);
            GUILayout.Space(5);
            EditorGUILayout.PropertyField(_isDefaultScanDataTransferEnabled);
            GUILayout.Space(5);
            EditorGUILayout.PropertyField(_isCoachingEnabled);
            GUILayout.Space(5);
            EditorGUILayout.PropertyField(_isInstructionEnabled);
            GUILayout.Space(5);
            
            GUILayout.Box(new GUIContent("<b>Advanced features</b>", EditorGUIUtility.IconContent("d_Preset.Context@2x").image), labelHeader);
            GUILayout.Space(10);
            EditorGUILayout.PropertyField(_createFloorToAR);
            GUILayout.Space(5);
            EditorGUILayout.PropertyField(_createFloorToRoomBuilder);
            GUILayout.Space(5);

            if (_createFloorToAR.boolValue || _createFloorToRoomBuilder.boolValue)
            {
                GUILayout.Box(new GUIContent("<b>Floor settings</b>", EditorGUIUtility.IconContent("d_Preset.Context@2x").image), labelHeader);
                GUILayout.Space(10);
                EditorGUILayout.PropertyField(_floorConcavity);
                GUILayout.Space(5);
                EditorGUILayout.PropertyField(_typeFloorConstructor);
                GUILayout.Space(5);
            }
            
            GUILayout.Box(new GUIContent("<b>Room Builder</b>", EditorGUIUtility.IconContent("d_Preset.Context@2x").image), labelHeader);
            GUILayout.Space(10);
            EditorGUILayout.PropertyField(_useMeshBooleanOperationsForRoomBuilder);
            GUILayout.Space(5);
            EditorGUILayout.PropertyField(_meshBooleanOperationsForRoomBuilder);
            GUILayout.Space(5);
            
            GUILayout.Box(new GUIContent("<b>Export</b>", EditorGUIUtility.IconContent("d_Preset.Context@2x").image), labelHeader);
            GUILayout.Space(10);
            EditorGUILayout.PropertyField(_usdExportOption);
            GUILayout.Space(5);
            
            GUILayout.Box(new GUIContent("<b>Screenshot</b>", EditorGUIUtility.IconContent("d_Preset.Context@2x").image), labelHeader);
            GUILayout.Space(10);
            EditorGUILayout.PropertyField(_isNativeScreenshot);
            GUILayout.Space(5);
            EditorGUILayout.PropertyField(_isNativeScreenshotPreview);
            GUILayout.Space(5);
            
            GUILayout.Box(new GUIContent("<b>ScreenRecorder</b>", EditorGUIUtility.IconContent("d_Preset.Context@2x").image), labelHeader);
            GUILayout.Space(10);
            EditorGUILayout.PropertyField(_isNativeScreenRecorder);
            GUILayout.Space(5);
            EditorGUILayout.PropertyField(_isNativeScreenRecorderPreview);
            GUILayout.Space(5);
                
            GUILayout.Box(new GUIContent("<b>Editor</b>", EditorGUIUtility.IconContent("d_Preset.Context@2x").image), labelHeader);
            GUILayout.Space(10);
            EditorGUILayout.PropertyField(_editorAR);
            //GUILayout.Space(5);
            //EditorGUILayout.PropertyField(_useFloorVisualizer);
            
            GUILayout.Space(30);
            
            GUILayout.Box(new GUIContent("<b> 17+ Features</b>", EditorGUIUtility.IconContent("BuildSettings.iPhone On@2x").image), labelHeader2);
            
            GUILayout.Space(5);
            
            GUILayout.Box(new GUIContent("\n"
                                         + "<b>New features are already available to you.</b>"
                                         + "\n\n"
                                         + "Thank you for your attention and for being with us."
                                         + "\n\n"
                                         + "Enjoy your use and development!"
                                         + "\n", EditorGUIUtility.IconContent("console.infoicon").image), helpBox);
            
            GUILayout.Space(10);
            GUILayout.Box(new GUIContent("<b>MultiRoom capture</b>", EditorGUIUtility.IconContent("d_Preset.Context@2x").image), labelHeader);
            GUILayout.Space(10);
            EditorGUILayout.PropertyField(_resetObjectsIfNewRoomIsAdded);
            
            GUILayout.Space(10);
            GUILayout.Box(new GUIContent("<b>XR</b>", EditorGUIUtility.IconContent("d_Preset.Context@2x").image), labelHeader);
            GUILayout.Space(10);
            EditorGUILayout.PropertyField(_isCustomXREnabled);
            
            //base.OnInspectorGUI();

            if (EditorGUI.EndChangeCheck())
            {
                EditorUtility.SetDirty(_target);
                serializedObject.ApplyModifiedProperties();
            }
        }
    }
}
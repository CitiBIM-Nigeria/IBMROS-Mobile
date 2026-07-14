using UnityEditor;
using UnityEngine;

namespace SilverTau.RoomPlanUnity
{
    [CustomEditor(typeof(CapturedRoomSnapshot))]
    public class CapturedRoomSnapshotEditor : RoomPlanUnityPackageEditor
    {
        private CapturedRoomSnapshot _target;
        
        private SerializedProperty _container;
        private SerializedProperty _prefabCapturedRoomObject;
        private SerializedProperty _dataTransferRate;
        
        private void OnEnable()
        {
            if (target) _target = (CapturedRoomSnapshot)target;
            
            _container = serializedObject.FindProperty("container");
            _prefabCapturedRoomObject = serializedObject.FindProperty("prefabCapturedRoomObject");
            _dataTransferRate = serializedObject.FindProperty("dataTransferRate");
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
	        
            BoxLogo(_target, "<b> Captured Room Snapshot</b>");
            
            serializedObject.Update();
            
            EditorGUI.BeginChangeCheck();
            
            GUILayout.Box(new GUIContent("<b>Main Settings</b>", EditorGUIUtility.IconContent("d_MoreOptions@2x").image), labelHeader2);
            GUILayout.Space(10);
            
            _container.objectReferenceValue = EditorGUILayout.ObjectField(new GUIContent(" Container", EditorGUIUtility.IconContent("d_Transform Icon").image), _container.objectReferenceValue, typeof(Transform), true);
            GUILayout.Space(5);
            _prefabCapturedRoomObject.objectReferenceValue = EditorGUILayout.ObjectField(new GUIContent(" Prefab Captured Room Object", EditorGUIUtility.IconContent("GameObject On Icon").image), _prefabCapturedRoomObject.objectReferenceValue, typeof(RoomPlanObject), true);
            
            GUILayout.Space(10);
            GUILayout.Box(new GUIContent("<b>Advanced</b>", EditorGUIUtility.IconContent("d_MoreOptions@2x").image), labelHeader2);
            
            GUILayout.Space(5);
            GUILayout.Box(new GUIContent("\n" 
                                         + "A parameter that allows you to adjust the data transfer rate. The lower the value, the faster the data will be processed.\n\nThe default value is 0.4."
                                         + "\n\n"
                                         + "Be careful, the lower the value, the greater the load on the RAM and the device's processor!"
                                         + "\n", EditorGUIUtility.IconContent("console.infoicon").image), helpBox);
            
            GUILayout.Space(10);
            EditorGUILayout.PropertyField(_dataTransferRate);
            
            //base.OnInspectorGUI();

            if (EditorGUI.EndChangeCheck())
            {
                EditorUtility.SetDirty(_target);
                serializedObject.ApplyModifiedProperties();
            }
        }
    }
}
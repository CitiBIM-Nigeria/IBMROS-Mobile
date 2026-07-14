using UnityEditor;
using UnityEngine;

namespace SilverTau.RoomPlanUnity
{
    [CustomEditor(typeof(RPUSessionCamera))]
    public class RoomPlanUnitySessionCameraEditor : RoomPlanUnityPackageEditor
    {
        private RPUSessionCamera _target;
        
        private SerializedProperty _aRCamera;
        
        private void OnEnable()
        {
            if (target) _target = (RPUSessionCamera)target;
            
            _aRCamera = serializedObject.FindProperty("ARCamera");
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
	        
	        BoxLogo(_target, "<b> Session Camera</b>");
	        
            serializedObject.Update();
            
            EditorGUI.BeginChangeCheck();
            
            GUILayout.Box(new GUIContent("<b>Main Settings</b>", EditorGUIUtility.IconContent("d_MoreOptions@2x").image), labelHeader2);
            
            GUILayout.Space(5);
            GUILayout.Box(new GUIContent("\n" 
                                         + "The session camera is fully synchronized with the ARKit camera and transmits real-time position, projection, and all additional settings for a complete immersion in augmented reality."
                                         + "\n", EditorGUIUtility.IconContent("console.infoicon").image), helpBox);
            
            GUILayout.Space(10);
            
            _aRCamera.objectReferenceValue = EditorGUILayout.ObjectField(new GUIContent(" AR Camera", EditorGUIUtility.IconContent("Camera Icon").image), _aRCamera.objectReferenceValue, typeof(Camera), true);
            
            //GUILayout.Box(new GUIContent("<b>Initialization</b>", EditorGUIUtility.IconContent("d_Preset.Context@2x").image), labelHeader);
            
            //base.OnInspectorGUI();

            if (EditorGUI.EndChangeCheck())
            {
                EditorUtility.SetDirty(_target);
                serializedObject.ApplyModifiedProperties();
            }
        }
    }
}
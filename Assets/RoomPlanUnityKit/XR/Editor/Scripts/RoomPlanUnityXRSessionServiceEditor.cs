#if UNITY_XR_ARKIT_LOADER_ENABLED

using SilverTau.Sample;
using UnityEditor;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace SilverTau.RoomPlanUnity
{
    [CustomEditor(typeof(RPU_XRSessionService))]
    public class RoomPlanUnityXRSessionServiceEditor : RoomPlanUnityPackageEditor
    {
        private RPU_XRSessionService _target;
        
        private void OnEnable()
        {
            if (target) _target = (RPU_XRSessionService)target;
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
	        
	        BoxLogo(_target, "<b> XR Session Service</b>");
	        
            serializedObject.Update();
            
            EditorGUI.BeginChangeCheck();
            
            //GUILayout.Box(new GUIContent("<b>Main Settings</b>", EditorGUIUtility.IconContent("d_MoreOptions@2x").image), labelHeader2);
            
            GUILayout.Space(5);
            GUILayout.Box(new GUIContent("\n" 
                                         + "XR Session Service is a script created to manage XR sessions for RoomPlan for Unity Kit." +
                                         "\n" +
                                         "It performs the functions of combining a session with RoomPlan for Unity Kit. " +
                                         "\n\n " +
                                         "Note:" +
                                         "\n" +
                                         "If you are using the XR solution of RoomPlan for Unity Kit, you do not need to run the scan in manual mode (RoomPlanUnityKit.StartCaptureSession())." +
                                         "The scan will automatically start when the ARSession (ARFoundation) is enabled." +
                                         "\n\n " +
                                         "Only for iOS 17+"
                                         + "\n", EditorGUIUtility.IconContent("console.infoicon").image), helpBox);
            
            GUILayout.Space(10);
            //base.OnInspectorGUI();

            if (EditorGUI.EndChangeCheck())
            {
                EditorUtility.SetDirty(_target);
                serializedObject.ApplyModifiedProperties();
            }
        }
    }
}

#endif
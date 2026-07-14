using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SilverTau.RoomPlanUnity
{
    [CustomEditor(typeof(RoomPlanUnityKit))]
    public class RoomPlanUnityEditor : RoomPlanUnityPackageEditor
    {
        private RoomPlanUnityKit _target;
        
        //private SerializedProperty _sessionCamera;
        private SerializedProperty _kitSettings;
        private SerializedProperty _delayInitialization;
        private SerializedProperty _matchFrameRate;
        private SerializedProperty _useWaitForNextFrame;
        private SerializedProperty _targetFrameRate;
        
        private void OnEnable()
        {
            if (target) _target = (RoomPlanUnityKit)target;
            
            //_sessionCamera = serializedObject.FindProperty("sessionCamera");
            _kitSettings = serializedObject.FindProperty("kitSettings");
            _delayInitialization = serializedObject.FindProperty("delayInitialization");
            _matchFrameRate = serializedObject.FindProperty("matchFrameRate");
            _useWaitForNextFrame = serializedObject.FindProperty("useWaitForNextFrame");
            _targetFrameRate = serializedObject.FindProperty("targetFrameRate");
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
	        
	        BoxLogo(_target, "<b> RoomPlan for Unity Kit</b>");
	        
            serializedObject.Update();
            
            EditorGUI.BeginChangeCheck();
            
            GUILayout.Box(new GUIContent("<b>Main Settings</b>", EditorGUIUtility.IconContent("d_MoreOptions@2x").image), labelHeader2);
            GUILayout.Space(10);
            
            //EditorGUILayout.PropertyField(_sessionCamera);
            //_sessionCamera.objectReferenceValue = EditorGUILayout.ObjectField(new GUIContent(" Session Camera", EditorGUIUtility.IconContent("Camera Icon").image), _sessionCamera.objectReferenceValue, typeof(RPUSessionCamera), true);
            GUILayout.Space(5);
            _kitSettings.objectReferenceValue = EditorGUILayout.ObjectField(new GUIContent(" Settings Object", EditorGUIUtility.IconContent("d_ScriptableObject Icon").image), _kitSettings.objectReferenceValue, typeof(RoomPlanUnityKitSettings), true);
            //EditorGUILayout.PropertyField(_kitSettings);
            
            GUILayout.Space(10);
            GUILayout.Box(new GUIContent("<b>Initialization</b>", EditorGUIUtility.IconContent("d_Preset.Context@2x").image), labelHeader);
            GUILayout.Space(10);
            
            EditorGUILayout.PropertyField(_delayInitialization);
            
            GUILayout.Space(10);
            GUILayout.Box(new GUIContent("<b>Optimization (Match Frame Rate)</b>", EditorGUIUtility.IconContent("d_MoreOptions@2x").image), labelHeader2);
            
            GUILayout.Space(5);
            GUILayout.Box(new GUIContent("\n" 
                                         + "This helps to optimize the performance of the application and the framework, as well as to set the desired frame rate."
                                         + "\n", EditorGUIUtility.IconContent("console.infoicon").image), helpBox);
            
            GUILayout.Space(10);
            EditorGUILayout.PropertyField(_matchFrameRate);
            GUILayout.Space(5);
            EditorGUILayout.PropertyField(_useWaitForNextFrame);
            GUILayout.Space(5);
            EditorGUILayout.PropertyField(_targetFrameRate);
            GUILayout.Space(5);
            
            //base.OnInspectorGUI();

            if (EditorGUI.EndChangeCheck())
            {
                EditorUtility.SetDirty(_target);
                serializedObject.ApplyModifiedProperties();
            }
        }
        
        private static GameObject CreateElementRoot(string name)
        {
	        var child = new GameObject(name);
	        Undo.RegisterCreatedObjectUndo(child, "Create " + name);
	        Selection.activeGameObject = child;
	        return child;
        }

        private static GameObject CreateObject(string name, GameObject parent)
        {
	        var go = new GameObject(name);
	        GameObjectUtility.SetParentAndAlign(go, parent);
	        return go;
        }
        
        [MenuItem("GameObject/Silver Tau/RoomPlan for Unity Kit/RPU Kit", false)]
        public static void AddRoomPlanUnityKit()
		{
			var rPUKit = CreateElementRoot("RPU Kit");
			
			var roomPlanUnityKitChild = CreateObject("RoomPlan Unity Kit", rPUKit);
			roomPlanUnityKitChild.AddComponent<RoomPlanUnityKit>();
			
			var capturedRoomSnapshotChild = CreateObject("Captured Room Snapshot", rPUKit);
			var capturedRoomSnapshot = capturedRoomSnapshotChild.AddComponent<CapturedRoomSnapshot>();
			capturedRoomSnapshot.CapturedRoomContainer = capturedRoomSnapshot.transform;
			
			var sessionOriginChild = CreateObject("Session Origin", rPUKit);
			var rPUSessionCamera = sessionOriginChild.AddComponent<RPUSessionCamera>();
			
			var arCameraChild = CreateObject("AR Camera", sessionOriginChild);
			var arCamera = arCameraChild.AddComponent<Camera>();
			arCamera.depth = 3;
			arCamera.backgroundColor = new Color(0, 0, 0, 0);
			arCamera.clearFlags = CameraClearFlags.Color;
			
			rPUSessionCamera.CurrentARCamera = arCamera;
		}
        
		[MenuItem("Window/Silver Tau/RoomPlan for Unity Kit/Prepare and check the RPU")]
		public static void PrepareAndCheckRoomPlaneUnityKit()
		{
			var roomPlanUnityKit = UnityEngine.Object.FindObjectOfType<RoomPlanUnityKit>();
			if (roomPlanUnityKit == null)
			{
				AddRoomPlanUnityKit();
			}
			else
			{
				var rpuParent = roomPlanUnityKit.transform.parent;
				if (rpuParent != null)
				{
					if (roomPlanUnityKit.transform.parent.name.Contains("(XR)"))
					{
						UnityEngine.Object.DestroyImmediate(rpuParent.gameObject);
						AddRoomPlanUnityKit();
					}
				}
			}

			if (roomPlanUnityKit != null) return;
			Debug.Log("<color=cyan>RoomPlane for Unity Kit is ready to use.</color>");
		}
        
		[MenuItem("Window/Silver Tau/RoomPlan for Unity Kit/Validate plugin", false, 1)]
		public static void ValidateRoomPlaneUnityKit()
		{
			var hasWarning = false;
			
			var roomPlanUnityKit = UnityEngine.Object.FindObjectsOfType<RoomPlanUnityKit>();

			if (roomPlanUnityKit.Length == 0)
			{
				Debug.LogWarning("There is no RoomPlanUnityKit component on the scene opened in the editor.");
				hasWarning = true;
			}
			else if (roomPlanUnityKit.Length > 1)
			{
				Debug.LogWarning("There is more than one RoomPlanUnityKit component in the scene open in the editor. There should be only one RoomPlanUnityKit component on the scene.");
				hasWarning = true;
			}
			
			var capturedRoomSnapshot = UnityEngine.Object.FindObjectsOfType<CapturedRoomSnapshot>();
			
			if (capturedRoomSnapshot.Length == 0)
			{
				Debug.LogWarning("There is no CapturedRoomSnapshot component on the scene opened in the editor.");
				hasWarning = true;
			}
			else if (capturedRoomSnapshot.Length > 1)
			{
				//Debug.LogWarning("There is more than one CapturedRoomSnapshot component in the scene open in the editor. There should be only one CapturedRoomSnapshot component on the scene.");
			}
			
			var sessionCamera = UnityEngine.Object.FindObjectsOfType<SessionCamera>();
			
			if (sessionCamera.Length == 0)
			{
				Debug.LogWarning("There is no SessionCamera component on the scene opened in the editor.");
				hasWarning = true;
			}
			else if (sessionCamera.Length > 1)
			{
				//Debug.LogWarning("There is more than one CapturedRoomSnapshot component in the scene open in the editor. There should be only one CapturedRoomSnapshot component on the scene.");
			}
			
			var rpuAssets = AssetDatabase.FindAssets("RoomPlanUnityKit");

			var files = new List<string> {"RoomPlanUnityKit.framework", "RoomPlanUnity.dll", "RoomPlanUnityKit_XR_Bridge.mm"};
			
			foreach (var asset in rpuAssets)
			{
				var path = AssetDatabase.GUIDToAssetPath(asset);

				CheckAssetInProject(path, files, "RoomPlanUnityKit.framework", ref hasWarning);
				CheckAssetInProject(path, files, "RoomPlanUnity.dll", ref hasWarning);
				CheckAssetInProject(path, files, "RoomPlanUnityKit_XR_Bridge.mm", ref hasWarning);
			}
			
#if PLATFORM_IOS
			if (!ValidateTargetOsVersion())
			{
				Debug.LogWarning("The minimum recommended OS version should be 16+.");
				hasWarning = true;
			}
#endif
			if(!hasWarning) Debug.Log("<color=cyan>RoomPlane for Unity Kit is ready to use.</color>");
		}
		
#if PLATFORM_IOS
	    private static bool ValidateTargetOsVersion()
	    {
		    Version ver;
            
		    try
		    {
			    ver = Version.Parse(PlayerSettings.iOS.targetOSVersionString);
		    }
		    catch (Exception e)
		    {
			    Debug.Log(e);
			    return false;
		    }
            
		    return ver.Major >= 16;
	    }
#endif

	    private static void CheckAssetInProject(string path, ICollection<string> files, string assetName, ref bool hasWarning)
	    {
		    if (!path.Contains(assetName)) return;
		    
		    if (files.Contains(assetName))
		    {
			    files.Remove(assetName);
		    }
		    else
		    {
			    Debug.LogWarning("More than one " +assetName +" was found in the project. There should be only one " + assetName + ".\nPath: " + path);
			    hasWarning = true;
		    }
	    }
	    
		[MenuItem("Window/Silver Tau/RoomPlan for Unity Kit/Documentation (Web)", false, 3)]
		public static void DocumentationWebRoomPlaneUnityKit()
		{
			Application.OpenURL("https://silvertau.s3.eu-central-1.amazonaws.com/RoomPlanForUnityKit/Documentation/index.html");
		}
	    
		[MenuItem("Window/Silver Tau/RoomPlan for Unity Kit/Product Page", false, 2)]
		public static void ProductPageRoomPlaneUnityKit()
		{
			Application.OpenURL("https://www.silvertau.com/products/roomplan-for-unity-kit");
		}

		[MenuItem("Window/Silver Tau/RoomPlan for Unity Kit/Unity Asset Store Page", false, 3)]
		public static void UnityAssetStorePageWebRoomPlaneUnityKit()
		{
			Application.OpenURL("https://prf.hn/l/Y3jP1y5");
		}
    }
}
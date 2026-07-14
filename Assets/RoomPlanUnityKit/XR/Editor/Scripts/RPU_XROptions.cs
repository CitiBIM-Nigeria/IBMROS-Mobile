
#if UNITY_EDITOR
#if UNITY_XR_ARKIT_LOADER_ENABLED

using SilverTau.Sample;
using UnityEditor;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace SilverTau.RoomPlanUnity.XR
{
    internal static class RPU_XROptions
    {
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
        
        #region XR Options
        
        [MenuItem("CONTEXT/ARSession/Silver Tau/RoomPlan for Unity Kit/Add XR Session Service", false)]
        public static void Add_RPU_XRSessionService(MenuCommand command)
        {
            var obj = (ARSession)command.context;
            obj.gameObject.AddComponent<RPU_XRSessionService>();
        }
        
        [MenuItem("GameObject/Silver Tau/RoomPlan for Unity Kit/RPU Kit (XR)", false)]
        public static void AddRoomPlanUnityKitXR()
        {
            var rPUKit = CreateElementRoot("RPU Kit (XR)");
			
            var roomPlanUnityKitChild = CreateObject("RoomPlan Unity Kit", rPUKit);
            roomPlanUnityKitChild.AddComponent<RoomPlanUnityKit>();
			
            var capturedRoomSnapshotChild = CreateObject("Captured Room Snapshot", rPUKit);
            var capturedRoomSnapshot = capturedRoomSnapshotChild.AddComponent<CapturedRoomSnapshot>();
            capturedRoomSnapshot.CapturedRoomContainer = capturedRoomSnapshot.transform;
			
            var sessionOriginChild = CreateObject("Session Origin", rPUKit);
            var rPUSessionCamera = sessionOriginChild.AddComponent<RPUSessionCamera>();
        }
        
        [MenuItem("Window/Silver Tau/RoomPlan for Unity Kit/Prepare and check the RPU (XR)")]
        public static void PrepareAndCheckRoomPlaneUnityKitXR()
        {
            var roomPlanUnityKit = UnityEngine.Object.FindObjectOfType<RoomPlanUnityKit>();
            if (roomPlanUnityKit == null)
            {
                AddRoomPlanUnityKitXR();
            }
            else
            {
                var rpuParent = roomPlanUnityKit.transform.parent;
                if (rpuParent != null)
                {
                    if (!roomPlanUnityKit.transform.parent.name.Contains("(XR)"))
                    {
                        UnityEngine.Object.DestroyImmediate(rpuParent.gameObject);
                        AddRoomPlanUnityKitXR();
                    }
                }
            }
            
            var arSession = UnityEngine.Object.FindObjectOfType<ARSession>();
            if (arSession == null)
            {
                Debug.LogWarning("AR Session (ARFoundation) not found.");
                return;
            }

            if (!arSession.TryGetComponent<RPU_XRSessionService>(out var rpuXrSessionService))
            {
                rpuXrSessionService = arSession.gameObject.AddComponent<RPU_XRSessionService>();
            }
            
            Debug.Log("<color=cyan>RoomPlane for Unity Kit (XR) is ready to use.</color>");
        }
        
        #endregion
    }
}

#endif
#endif

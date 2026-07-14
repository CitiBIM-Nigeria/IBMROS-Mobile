#if UNITY_XR_ARKIT_LOADER_ENABLED

using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace SilverTau.RoomPlanUnity.XR
{
    public static class RoomPlanUnityKit_XR_Extensions
    {
        /// <summary>
        /// This is an extending function for ARSession (subsystem) that combines work sessions. To use it, you need to initialize (enable) the ARSession and execute the extension.
        /// </summary>
        /// <param name="arSession">Target ARSession.</param>
        public static void Combine_And_Start_RPU_XR_Session(this ARSession arSession)
        {
            if (arSession == null)
            {
                Debug.Log("AR Session is empty.");
                return;
            }
            
            var roomPlanUnityKit = UnityEngine.Object.FindObjectOfType<RoomPlanUnityKit>();
            if (roomPlanUnityKit)
            {
#if !UNITY_EDITOR && PLATFORM_IOS
                roomPlanUnityKit.CombineRoomPlanUnityKitXRSession(arSession.subsystem.nativePtr);
                roomPlanUnityKit.StartCaptureSession();
#endif
            }
        }
    }
}

#endif

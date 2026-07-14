using System;
using System.Runtime.InteropServices;
using SilverTau.RoomPlanUnity;
using UnityEngine;

#if !UNITY_EDITOR && PLATFORM_IOS
using System.IO;
using System.Runtime.InteropServices;
using AOT;
#endif

namespace SilverTau.Utilities
{
    public static class ScreenRecorder
    {
        
        #region Screen Recorder
#if !UNITY_EDITOR && PLATFORM_IOS
        [DllImport("__Internal")]
        private static extern void startScreenRecorderRPU();
        
        [DllImport("__Internal")]
        private static extern void stopScreenRecorderRPU();
        
        [DllImport("__Internal")]
        private static extern void isNativeScreenRecorder(bool value);
        
        [DllImport("__Internal")]
        private static extern void isNativeScreenRecorderPreview(bool value);

        [DllImport("__Internal")]
        private static extern string getRPUNativeScreenRecorderOutputURL();
#endif
        
        /// <summary>
        /// Values to get Screen Recorder Output URL.
        /// </summary>
        public static string RPUNativeScreenRecorderOutputURL => CheckRPUNativeScreenRecorderOutputURL();
        
        private static string CheckRPUNativeScreenRecorderOutputURL()
        {
#if !UNITY_EDITOR && PLATFORM_IOS
            var result = getRPUNativeScreenRecorderOutputURL();
            return result;
#else
            return string.Empty;
#endif
        }
        
        #endregion


        #region ScreenRecorder

        /// <summary>
        /// A method that updates the main RoomPlan settings in real time.
        /// </summary>
        public static void UpdateSettings()
        {
#if !UNITY_EDITOR && PLATFORM_IOS
            isNativeScreenRecorder(RoomPlanUnityKit.CurrentRoomPlanUnityKitSettings.isNativeScreenRecorder);
            isNativeScreenRecorderPreview(RoomPlanUnityKit.CurrentRoomPlanUnityKitSettings.isNativeScreenRecorderPreview);  
#endif
        }

        /// <summary>
        /// A method that allows you to start recording a video screen.
        /// </summary>
        public static void StartScreenRecorder()
        {
#if !UNITY_EDITOR && PLATFORM_IOS
            UpdateSettings();
            startScreenRecorderRPU();
#endif
        }

        /// <summary>
        /// A method that allows you to start recording a video screen.
        /// </summary>
        public static void StartScreenRecorder(bool nativeScreenRecorder, bool nativeScreenRecorderPreview)
        {
#if !UNITY_EDITOR && PLATFORM_IOS
            isNativeScreenRecorder(nativeScreenRecorder);
            isNativeScreenRecorderPreview(nativeScreenRecorderPreview);  
            startScreenRecorderRPU();
#endif
        }
        
        /// <summary>
        /// A method that allows you to stop recording a video screen.
        /// </summary>
        public static void StopScreenRecorder()
        {
#if !UNITY_EDITOR && PLATFORM_IOS
            stopScreenRecorderRPU();
#endif
        }

        #endregion
    }
}

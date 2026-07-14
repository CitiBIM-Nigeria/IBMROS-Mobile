using System;
using System.Runtime.InteropServices;
using SilverTau.RoomPlanUnity;

#if !UNITY_EDITOR && PLATFORM_IOS
using System.IO;
using System.Runtime.InteropServices;
using AOT;
#endif

namespace SilverTau.Utilities
{
    public static class Screenshot
    {
        #region Screenshot
#if !UNITY_EDITOR && PLATFORM_IOS
        [DllImport("__Internal")]
        private static extern void screenshotRPU();
        
        [DllImport("__Internal")]
        private static extern void isNativeScreenshot(bool value);

        [DllImport("__Internal")]
        private static extern void isNativeScreenshotPreview(bool value);

        [DllImport("__Internal")]
        private static extern string getRPUNativeScreenshotOutputURL();
        
        [DllImport("__Internal")]
        private static extern void screenshotShareRPU();
#endif
        #endregion

        #region Screenshot

        /// <summary>
        /// A method that updates the main RoomPlan settings in real time.
        /// </summary>
        public static void UpdateSettings()
        {
#if !UNITY_EDITOR && PLATFORM_IOS
            isNativeScreenshot(RoomPlanUnityKit.CurrentRoomPlanUnityKitSettings.isNativeScreenshot);
            isNativeScreenshotPreview(RoomPlanUnityKit.CurrentRoomPlanUnityKitSettings.isNativeScreenshotPreview);
#endif
        }
        
        /// <summary>
        /// A method that allows you to take a screenshot of the screen.
        /// </summary>
        public static void TakeScreenshot()
        {
#if !UNITY_EDITOR && PLATFORM_IOS
            UpdateSettings();
            screenshotRPU();
#endif
        }
        
        /// <summary>
        /// A method that allows you to take a screenshot of the screen.
        /// </summary>
        public static void TakeScreenshot(bool nativeScreenshot, bool nativeScreenshotPreview)
        {
#if !UNITY_EDITOR && PLATFORM_IOS
            isNativeScreenshot(nativeScreenshot);
            isNativeScreenshotPreview(nativeScreenshotPreview);
            screenshotRPU();
#endif
        }
        
        /// <summary>
        /// A method that allows you to share a screenshot.
        /// </summary>
        public static void ScreenshotShare()
        {
#if !UNITY_EDITOR && PLATFORM_IOS
            screenshotShareRPU();
#endif
        }

        #endregion
    }
}

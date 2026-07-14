using System;

#if !UNITY_EDITOR && PLATFORM_IOS
using System.IO;
using System.Runtime.InteropServices;
using AOT;
#endif

namespace SilverTau.Utilities
{
    public static class QuickLook
    {
        #region RPU Quick Look
#if !UNITY_EDITOR && PLATFORM_IOS
        [DllImport("__Internal")]
        private static extern void rpu_QuickLook(String path);
#endif
        #endregion
        
        #region Quick Look

        /// <summary>
        /// A function that allows you to call the Quick Look panel with the target object.
        /// </summary>
        /// <param name="path">Path to the object.</param>
        public static void OpenQuickLook(string path)
        {
            if(string.IsNullOrEmpty(path)) return;
            
#if !UNITY_EDITOR && PLATFORM_IOS
            rpu_QuickLook(path);
#endif
        }
        
        #endregion
    }
}

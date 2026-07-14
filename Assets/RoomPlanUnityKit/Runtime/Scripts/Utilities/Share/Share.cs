using System;

#if !UNITY_EDITOR && PLATFORM_IOS
using System.IO;
using System.Runtime.InteropServices;
using AOT;
#endif

namespace SilverTau.Utilities
{
    public static class Share
    {
        #region RPU Share
#if !UNITY_EDITOR && PLATFORM_IOS
        [DllImport("__Internal")]
        private static extern void rpu_shareFolder(String path);
        
        [DllImport("__Internal")]
        private static extern void rpu_shareFile(String path);
#endif
        #endregion
        
        #region Share

        /// <summary>
        /// A feature that allows you to share a folder.
        /// </summary>
        /// <param name="path">Path to the folder.</param>
        public static void ShareFolder(string path)
        {
            if(string.IsNullOrEmpty(path)) return;
            
#if !UNITY_EDITOR && PLATFORM_IOS
            rpu_shareFolder(path);
#endif
        }

        /// <summary>
        /// A function that allows you to share a file.
        /// </summary>
        /// <param name="path">Path to the file.</param>
        public static void ShareFile(string path)
        {
            if(string.IsNullOrEmpty(path)) return;
            
#if !UNITY_EDITOR && PLATFORM_IOS
            rpu_shareFile(path);
#endif
        }
        
        #endregion
    }
}

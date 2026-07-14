#if UNITY_EDITOR

#if UNITY_IOS
using UnityEditor;
using UnityEditor.Callbacks;
using System.IO;
using UnityEditor.iOS.Xcode;

#endif

namespace SilverTau.RoomPlanUnity
{
    public class BuildPostProcessor 
    {
        private const bool ENABLE = true;
        private const string CAMERA_USAGE_DESCRIPTION = "The application requires access to your phone's camera.";

#if UNITY_IOS
#pragma warning disable 0162
        [PostProcessBuild]
        public static void OnPostprocessBuild( BuildTarget target, string buildPath )
        {
            if(!ENABLE) return;
            if(!string.IsNullOrEmpty(PlayerSettings.iOS.cameraUsageDescription)) return;

            if(target == BuildTarget.iOS)
            {
                string plistPath = Path.Combine( buildPath, "Info.plist" );

                PlistDocument plist = new PlistDocument();
                plist.ReadFromString( File.ReadAllText( plistPath ) );

                PlistElementDict rootDict = plist.root;
                rootDict.SetString("NSCameraUsageDescription", CAMERA_USAGE_DESCRIPTION);

                File.WriteAllText( plistPath, plist.WriteToString() );
            }
        }
#pragma warning restore 0162
#endif
    }
}

#endif

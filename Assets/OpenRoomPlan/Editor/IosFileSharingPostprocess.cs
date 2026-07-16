#if UNITY_IOS
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;

namespace OpenRoomPlan.Editor
{
    /// <summary>
    /// Makes the app's Documents folder (recorded sessions + exported zips) visible in the iOS Files app,
    /// so a scan can be AirDropped / shared straight from the phone — the iOS counterpart of the Android
    /// MediaStore Downloads publish in SessionExporter.
    /// </summary>
    public static class IosFileSharingPostprocess
    {
        [PostProcessBuild]
        public static void OnPostProcessBuild(BuildTarget target, string buildPath)
        {
            if (target != BuildTarget.iOS) return;

            string plistPath = Path.Combine(buildPath, "Info.plist");
            var plist = new PlistDocument();
            plist.ReadFromFile(plistPath);
            plist.root.SetBoolean("UIFileSharingEnabled", true);              // iTunes/Finder file sharing
            plist.root.SetBoolean("LSSupportsOpeningDocumentsInPlace", true); // visible in Files app
            plist.WriteToFile(plistPath);
            UnityEngine.Debug.Log("[ORP] Info.plist: enabled Files-app access to Documents (session sharing).");
        }
    }
}
#endif

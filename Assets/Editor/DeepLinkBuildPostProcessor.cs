#if UNITY_EDITOR && UNITY_IOS
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;
using System.IO;

/// <summary>
/// Adds the iOS "Associated Domains" capability (applinks:&lt;domain&gt;) to the Xcode
/// project so Universal Links open the app. Runs after RoomPlan's camera-usage
/// post-processor (higher order value). The domain comes from LinkConfig (a
/// PLACEHOLDER until go-live), and the Apple Team ID + bundle id must be set in
/// Player Settings and match link_config.json / the hosted apple-app-site-association.
///
/// NOTE: ProjectCapabilityManager overloads vary across Unity versions; this uses
/// the GetUnityMainTargetGuid + targetGuid: form (Unity 2019.3+). If a future
/// Unity changes the signature, adjust here — the entitlement content is stable.
/// </summary>
public static class DeepLinkBuildPostProcessor
{
    [PostProcessBuild(100)]
    public static void OnPostprocessBuild(BuildTarget target, string buildPath)
    {
        if (target != BuildTarget.iOS) return;

        string projPath = PBXProject.GetPBXProjectPath(buildPath);
        var proj = new PBXProject();
        proj.ReadFromString(File.ReadAllText(projPath));

        const string entitlementsFile = "ibmros.entitlements";
        string mainTargetGuid = proj.GetUnityMainTargetGuid();

        var capabilities = new ProjectCapabilityManager(
            projPath, entitlementsFile, targetGuid: mainTargetGuid);
        capabilities.AddAssociatedDomains(new[] { $"applinks:{LinkConfig.LinkDomain}" });
        capabilities.WriteToFile();

        UnityEngine.Debug.Log(
            $"[DeepLinkBuildPostProcessor] Added associated domain applinks:{LinkConfig.LinkDomain}"
            + (LinkConfig.IsPlaceholder ? "  (PLACEHOLDER — update LinkConfig for production)" : ""));
    }
}
#endif

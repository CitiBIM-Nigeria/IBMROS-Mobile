using UnityEngine;

/// <summary>
/// Guarantees the QR / deep-link service singletons exist in every play session
/// without any manual scene setup: before the first scene loads, it creates a
/// persistent "IBMROS Services" GameObject carrying DeepLinkManager,
/// SavedItemsService, ScanHistoryService, and QrScannerController.
///
/// Each service already has its own singleton guard (duplicates self-destroy),
/// so this is safe even if the components are ALSO placed in a scene manually.
/// This is what makes the scanner testable straight from the Editor Play button.
/// </summary>
public static class ServicesBootstrapper
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        var go = new GameObject("IBMROS Services");
        Object.DontDestroyOnLoad(go);

        if (DeepLinkManager.Instance == null)    go.AddComponent<DeepLinkManager>();
        if (SavedItemsService.Instance == null)  go.AddComponent<SavedItemsService>();
        if (ScanHistoryService.Instance == null) go.AddComponent<ScanHistoryService>();
        if (QrScannerController.Instance == null) go.AddComponent<QrScannerController>();

        Debug.Log("[ServicesBootstrapper] QR/deep-link services ready.");
    }
}

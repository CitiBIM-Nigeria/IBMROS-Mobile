using System;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Rebuilds the <see cref="VRSubcategoryCatalog"/> from the live backend so the
/// socket Inspector dropdown always reflects what ros-categories actually has —
/// with zero hardcoded IDs anywhere.
///
/// HOW A DESIGNER USES IT (once, whenever the catalogue changes):
///   1. Drop this component anywhere in a scene that has the backend managers.
///   2. Press Play. It waits for AWS, calls FurnitureRepository.GetCategories(),
///      and (in the Editor) writes the entries into the catalog asset under a
///      Resources folder.
///   3. Stop Play. Every socket's dropdown is now populated. Delete/disable this
///      component afterwards — sockets don't need it at runtime.
///
/// Reuses the exact same category query mobile uses; it does not talk to
/// DynamoDB itself.
/// </summary>
public class VRSubcategoryCatalogRefresher : MonoBehaviour
{
    [Tooltip("Catalog asset to fill. If blank, loads the one from Resources.")]
    [SerializeField] private VRSubcategoryCatalog catalog;

    [Tooltip("Run automatically on Play. Turn off to trigger manually via Refresh().")]
    [SerializeField] private bool refreshOnStart = true;

    async void Start()
    {
        if (refreshOnStart) await Refresh();
    }

    [ContextMenu("Refresh From Backend")]
    public async void RefreshMenu() => await Refresh();

    public async Task Refresh()
    {
        if (catalog == null) catalog = VRSubcategoryCatalog.Load();
        if (catalog == null)
        {
            Debug.LogError("[VRCatalogRefresher] No VRSubcategoryCatalog assigned and " +
                           "none found in Resources. Create one via " +
                           "Assets ▸ Create ▸ IBMROS ▸ VR ▸ Subcategory Catalog " +
                           "inside a Resources folder.");
            return;
        }

        await WaitForAws();

        if (FurnitureRepository.Instance == null)
        {
            Debug.LogError("[VRCatalogRefresher] FurnitureRepository not in scene.");
            return;
        }

        Debug.Log("[VRCatalogRefresher] fetching categories from backend…");
        var categories = await FurnitureRepository.Instance.GetCategories();
        if (categories == null || categories.Count == 0)
        {
            Debug.LogWarning("[VRCatalogRefresher] backend returned no categories.");
            return;
        }

        catalog.entries.Clear();
        foreach (var room in categories)
        {
            if (room?.Subcategories == null) continue;
            foreach (var sub in room.Subcategories)
            {
                if (string.IsNullOrEmpty(sub.SubcategoryId)) continue;
                catalog.entries.Add(new VRSubcategoryCatalog.Entry
                {
                    roomName    = room.CategoryName,
                    displayName = sub.SubcategoryName,
                    categoryId  = sub.SubcategoryId,
                });
            }
        }
        catalog.lastRefreshedUtc = DateTime.UtcNow.ToString("u");

        Debug.Log($"[VRCatalogRefresher] catalog rebuilt: {catalog.entries.Count} " +
                  $"subcategories across {categories.Count} rooms.");

#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(catalog);
        UnityEditor.AssetDatabase.SaveAssets();
        Debug.Log("[VRCatalogRefresher] catalog asset saved. You can stop Play now; " +
                  "socket dropdowns are populated.");
#endif
    }

    private async Task WaitForAws()
    {
        if (AwsManager.Instance != null && AwsManager.Instance.IsInitialized) return;

        var tcs = new TaskCompletionSource<bool>();
        void Ready() { AwsManager.OnAwsReady -= Ready; tcs.TrySetResult(true); }
        AwsManager.OnAwsReady += Ready;
        if (AwsManager.Instance != null && AwsManager.Instance.IsInitialized)
        {
            AwsManager.OnAwsReady -= Ready;
            tcs.TrySetResult(true);
        }
        await tcs.Task;
    }
}

using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Routes a resolved (scanned) product to the product-detail experience.
///
/// Design (see docs/QR_IMPLEMENTATION_NOTES.md): a scan opens the PRODUCT DETAIL,
/// never a silent room placement. The only place that renders product detail +
/// AR placement today is the Room scene's ItemDetailSheet, so:
///   • In the Room scene  → open the detail sheet immediately (Preview / Save /
///                          Add to Current Room).
///   • Elsewhere (home)   → remember the product, auto-Save it (an in-store scan
///                          must never be lost), and enter the Room designer,
///                          which opens the detail on load.
/// We never fabricate a room beyond entering the app's own AR designer surface.
///
/// ASSUMPTION (documented): routing a home-screen scan into the Room scene is the
/// coherent v1 flow because scanning is fundamentally "see it in my room." A
/// dedicated standalone home detail screen is a future enhancement (TODO).
/// </summary>
public static class ScannedProductRouter
{
    private static ProductModel _pending;

    public static void Route(ProductModel product)
    {
        if (product == null) return;

        var roomUI = Object.FindFirstObjectByType<RoomUIManager>();
        if (roomUI != null)
        {
            roomUI.OpenProductDetail(product);
            return;
        }

        // Not in the Room scene — stash, auto-save, and enter the designer.
        _pending = product;
        SavedItemsService.Instance?.Add(product);
        SceneTransition.SetSkipSplash(true);
        SceneManager.LoadScene("Room");
    }

    /// <summary>RoomUIManager calls this once its UI is live to pick up a product
    /// that a scan routed it here to show. Returns null if there was none.</summary>
    public static ProductModel ConsumePending()
    {
        var p = _pending;
        _pending = null;
        return p;
    }
}

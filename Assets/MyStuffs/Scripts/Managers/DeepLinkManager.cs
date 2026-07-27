using System;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Receives incoming QR / universal / app links and the in-app scanner's decoded
/// strings, turns them into a product, and routes to the product-detail flow.
///
/// A link looks like  https://&lt;domain&gt;/p/&lt;canonicalId&gt;  (or the custom scheme
/// ibmros://p/&lt;canonicalId&gt;). The canonical id is region-independent; it resolves
/// to THIS app's configured region row via the canonical-index GSI.
///
/// Handles cold start (Application.absoluteURL), warm activation
/// (Application.deepLinkActivated), the guest-first / AWS-not-ready case (queues
/// until ready), duplicate scans (debounced), and invalid links (rejected with a
/// clear reason). No auth wall — resolution runs on guest credentials.
/// </summary>
public class DeepLinkManager : MonoBehaviour
{
    public static DeepLinkManager Instance { get; private set; }

    /// <summary>Raised when a scan/link resolved to a real product.</summary>
    public static event Action<ProductModel> OnScannedProduct;
    /// <summary>Raised when a link was invalid or its product wasn't found.</summary>
    public static event Action<string>       OnScanError;

    private string _lastHandled;
    private float  _lastHandledAt;
    private const float DedupeWindowSeconds = 3f;

    private string _pendingCanonicalId;   // held until AWS is ready (cold start)

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        Application.deepLinkActivated += OnDeepLink;
    }

    void Start()
    {
        // Cold start: the OS hands the launching URL here.
        if (!string.IsNullOrEmpty(Application.absoluteURL))
            OnDeepLink(Application.absoluteURL);
    }

    void OnDestroy()
    {
        Application.deepLinkActivated -= OnDeepLink;
        AwsManager.OnAwsReady -= OnAwsReady;
    }

    /// <summary>The in-app QR scanner calls this with a decoded string.</summary>
    public void HandleLink(string url) => OnDeepLink(url);

    private void OnDeepLink(string url)
    {
        string canonicalId = ParseCanonicalId(url);
        if (string.IsNullOrEmpty(canonicalId))
        {
            Debug.LogWarning($"[DeepLink] ignored (not an IBMROS product link): {url}");
            OnScanError?.Invoke("Not an IBMROS product code.");
            return;
        }

        // Duplicate-scan debounce (native camera + in-app scanner can double-fire).
        if (canonicalId == _lastHandled &&
            Time.realtimeSinceStartup - _lastHandledAt < DedupeWindowSeconds)
            return;
        _lastHandled   = canonicalId;
        _lastHandledAt = Time.realtimeSinceStartup;

        Resolve(canonicalId);
    }

    /// <summary>
    /// Extract the canonical id from a link. Accepts the https universal/app link
    /// (host must match) and the custom scheme. Returns null for anything else —
    /// invalid or tampered links resolve to nothing and are reported, never acted
    /// on blindly. Pure/static so it is unit-testable.
    /// </summary>
    public static string ParseCanonicalId(string url)
    {
        if (string.IsNullOrEmpty(url)) return null;
        try
        {
            var uri = new Uri(url);

            // Custom scheme: ibmros://p/<id>  or  ibmros://<id>
            if (string.Equals(uri.Scheme, LinkConfig.CustomScheme, StringComparison.OrdinalIgnoreCase))
            {
                string seg = uri.AbsolutePath.Trim('/');
                if (string.IsNullOrEmpty(seg)) seg = uri.Host;   // ibmros://<id>
                return SanitizeId(LastSegment(seg));
            }

            // HTTPS universal/app link: host must match, path must be /p/<id>.
            if (uri.Scheme == "http" || uri.Scheme == "https")
            {
                if (!string.Equals(uri.Host, LinkConfig.LinkDomain, StringComparison.OrdinalIgnoreCase))
                    return null;
                string prefix = LinkConfig.ProductPathPrefix;    // "/p/"
                string path = uri.AbsolutePath;
                if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return null;
                return SanitizeId(LastSegment(path.Substring(prefix.Length)));
            }
        }
        catch (Exception) { /* malformed URL → null */ }
        return null;
    }

    private static string LastSegment(string s)
    {
        if (string.IsNullOrEmpty(s)) return null;
        s = s.Trim('/');
        int i = s.LastIndexOf('/');
        return i >= 0 ? s.Substring(i + 1) : s;
    }

    // Canonical ids are alphanumeric (IKEA article numbers). Reject anything else
    // so a doctored link can't inject into the GSI query or navigation.
    private static string SanitizeId(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        id = id.Trim();
        foreach (char c in id)
            if (!char.IsLetterOrDigit(c)) return null;
        return id.Length == 0 ? null : id;
    }

    // ---------------------------------------------------------------
    // RESOLUTION
    // ---------------------------------------------------------------

    private void Resolve(string canonicalId)
    {
        bool ready = FurnitureDataService.Instance != null &&
                     AwsManager.Instance != null && AwsManager.Instance.IsInitialized;
        if (!ready)
        {
            // Cold start / guest bootstrap not finished — queue and retry.
            _pendingCanonicalId = canonicalId;
            AwsManager.OnAwsReady -= OnAwsReady;
            AwsManager.OnAwsReady += OnAwsReady;
            Debug.Log($"[DeepLink] queued {canonicalId} until AWS ready.");
            return;
        }
        _ = ResolveAsync(canonicalId);
    }

    private void OnAwsReady()
    {
        AwsManager.OnAwsReady -= OnAwsReady;
        if (string.IsNullOrEmpty(_pendingCanonicalId)) return;
        var id = _pendingCanonicalId;
        _pendingCanonicalId = null;
        _ = ResolveAsync(id);
    }

    private async Task ResolveAsync(string canonicalId)
    {
        Debug.Log($"[DeepLink] resolving canonical {canonicalId}");
        var product = await FurnitureDataService.Instance.ResolveByCanonical(canonicalId);
        if (product == null)
        {
            OnScanError?.Invoke("Product not available.");
            return;
        }
        OnScannedProduct?.Invoke(product);
        ScannedProductRouter.Route(product);

        // Automatic scan history. External links open the product DETAIL (never a
        // silent placement), so addedToRoom is false here; the in-app scanner path
        // records its own entry with the real placement outcome.
        ScanHistoryService.Instance?.Record(product, addedToRoom: false);
    }
}

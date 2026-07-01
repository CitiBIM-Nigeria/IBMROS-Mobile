using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The UGUI panel that appears when the player approaches a socket. It is a PURE
/// VIEW: it binds a <see cref="ProductModel"/> to on-screen widgets and forwards
/// button presses to the socket that owns the currently displayed product. It
/// contains no data-fetching, no downloading, and no catalog logic — all of that
/// lives in the backend the socket already talks to.
///
/// The product image reuses <see cref="ImageCache"/> (webp/png/jpg + disk cache),
/// so the same thumbnails mobile shows load here without a re-download.
///
/// WIRING (all optional — leave a field null to hide that widget):
///   • productImage  → RawImage        (product photo)
///   • nameText      → TextMeshProUGUI (product name)
///   • priceText     → TextMeshProUGUI (formatted price)
///   • descText      → TextMeshProUGUI (variant / description line)
///   • navText       → TextMeshProUGUI ("3 / 12")
///   • nextButton / prevButton / refreshButton → Button
/// </summary>
[DisallowMultipleComponent]
public class VRProductInfoPanel : MonoBehaviour
{
    [Header("Root (toggled on show/hide)")]
    [Tooltip("The object enabled/disabled to show the panel. Defaults to this GameObject.")]
    [SerializeField] private GameObject root;

    [Header("Product Fields")]
    [SerializeField] private RawImage       productImage;
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private TextMeshProUGUI priceText;
    [SerializeField] private TextMeshProUGUI descText;
    [SerializeField] private TextMeshProUGUI navText;      // "3 / 12"
    [SerializeField] private TextMeshProUGUI statusText;   // loading / error line

    [Header("Buttons")]
    [SerializeField] private Button nextButton;
    [SerializeField] private Button prevButton;
    [SerializeField] private Button refreshButton;

    [Header("Behaviour")]
    [Tooltip("Hide the product image widget while a new model is loading.")]
    [SerializeField] private bool dimWhileLoading = true;

    // The socket whose product is currently bound. Buttons act on THIS socket, so
    // one shared panel can serve every socket in the warehouse if desired.
    private VRFurnitureSocket _socket;

    // Guards against stale async image loads landing on the wrong product.
    private int _imageToken;

    public bool IsVisible => (root != null ? root : gameObject).activeSelf;

    void Awake()
    {
        if (root == null) root = gameObject;
        if (nextButton    != null) nextButton.onClick.AddListener(OnNext);
        if (prevButton    != null) prevButton.onClick.AddListener(OnPrev);
        if (refreshButton != null) refreshButton.onClick.AddListener(OnRefresh);
    }

    void OnDestroy()
    {
        if (nextButton    != null) nextButton.onClick.RemoveListener(OnNext);
        if (prevButton    != null) prevButton.onClick.RemoveListener(OnPrev);
        if (refreshButton != null) refreshButton.onClick.RemoveListener(OnRefresh);
    }

    // ---------------------------------------------------------------
    // BUTTON HANDLERS → forward to the bound socket
    // ---------------------------------------------------------------

    private void OnNext()    => _socket?.NextProduct();
    private void OnPrev()    => _socket?.PreviousProduct();
    private void OnRefresh() => _socket?.Refresh();

    // ---------------------------------------------------------------
    // BINDING API — called by the socket
    // ---------------------------------------------------------------

    /// <summary>Bind and show a product's details.</summary>
    public void ShowProduct(VRFurnitureSocket socket, ProductModel product, int index, int count)
    {
        _socket = socket;
        SetActive(true);

        if (product == null) { ShowMessage(socket, "No product."); return; }

        SetText(statusText, string.Empty);
        SetText(nameText,  product.Name);
        SetText(priceText, product.FormattedPrice);
        SetText(descText,  BuildDescription(product));
        UpdateNavigation(index, count);
        SetButtonsInteractable(true, count > 1);

        _ = LoadImage(product.BestImageUrl);
    }

    /// <summary>Show a loading state for the given product (keeps text, dims image).</summary>
    public void ShowLoading(ProductModel product)
    {
        SetActive(true);
        SetText(statusText, "Loading…");
        if (product != null) SetText(nameText, product.Name);
        if (dimWhileLoading) SetImageVisible(false);
        SetButtonsInteractable(false, false);
    }

    /// <summary>Show a plain message (empty socket, load failure, etc.).</summary>
    public void ShowMessage(VRFurnitureSocket socket, string message)
    {
        _socket = socket;
        SetActive(true);
        SetText(statusText, message);
        SetText(nameText,  string.Empty);
        SetText(priceText, string.Empty);
        SetText(descText,  string.Empty);
        SetText(navText,   string.Empty);
        SetImageVisible(false);
        // Only Refresh makes sense with no product to page through.
        if (nextButton    != null) nextButton.interactable = false;
        if (prevButton    != null) prevButton.interactable = false;
        if (refreshButton != null) refreshButton.interactable = true;
    }

    /// <summary>Update just the "x / y" counter (used as background paging fills in).</summary>
    public void UpdateNavigation(int index, int count)
    {
        if (navText != null && count > 0)
            navText.text = $"{index + 1} / {count}";
        // Enable paging once there's more than one item to move between.
        if (nextButton != null) nextButton.interactable = count > 1;
        if (prevButton != null) prevButton.interactable = count > 1;
    }

    public void Hide()
    {
        _imageToken++;         // cancel any in-flight image load
        SetActive(false);
    }

    // ---------------------------------------------------------------
    // INTERNALS
    // ---------------------------------------------------------------

    // Compose a richer description than the single variant line when data allows:
    // "2 seater sofa - grey · ★ 4.6 (128)".
    private static string BuildDescription(ProductModel p)
    {
        string line = p.Description ?? string.Empty;
        if (p.StarRatingValue > 0f)
        {
            string rating = $"★ {p.StarRatingValue:0.0}";
            if (p.ReviewCountValue > 0) rating += $" ({p.ReviewCountValue})";
            line = string.IsNullOrEmpty(line) ? rating : $"{line}   {rating}";
        }
        return line;
    }

    private async System.Threading.Tasks.Task LoadImage(string url)
    {
        int token = ++_imageToken;

        if (productImage == null) return;
        if (string.IsNullOrEmpty(url)) { SetImageVisible(false); return; }

        var tex = await ImageCache.GetTexture(url);

        // Panel hidden or a newer product bound while we downloaded → drop it.
        if (token != _imageToken) return;

        if (tex != null)
        {
            productImage.texture = tex;
            SetImageVisible(true);
        }
        else
        {
            SetImageVisible(false);
        }
    }

    private void SetActive(bool on)
    {
        var go = root != null ? root : gameObject;
        if (go.activeSelf != on) go.SetActive(on);
    }

    private void SetImageVisible(bool on)
    {
        if (productImage != null) productImage.enabled = on;
    }

    private void SetButtonsInteractable(bool refresh, bool nav)
    {
        if (refreshButton != null) refreshButton.interactable = refresh;
        if (nextButton    != null) nextButton.interactable    = nav;
        if (prevButton    != null) prevButton.interactable    = nav;
    }

    private static void SetText(TextMeshProUGUI field, string value)
    {
        if (field != null) field.text = value ?? string.Empty;
    }
}

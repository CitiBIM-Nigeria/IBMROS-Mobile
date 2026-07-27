using System;
using System.Collections;
using UnityEngine;
#if IBMROS_ZXING
using System.Threading.Tasks;
using ZXing;
#endif

/// <summary>
/// In-app QR scanner — the production scan-to-place flow:
///   open camera → detect QR → read payload → extract canonical id (pure local
///   parse, no dependency on the link domain existing) → resolve the product via
///   the canonical-index GSI → record scan history → add DIRECTLY into the
///   currently open room.
/// Distinct from the external universal-link flow (DeepLinkManager), which opens
/// the product-detail sheet instead.
///
/// DEPENDENCY: decoding uses ZXing.Net (Assets/Plugins/ZXing/zxing.unity.dll),
/// gated behind the IBMROS_ZXING scripting define (set in Player Settings for
/// Android/iOS/Standalone) so the project still compiles if the DLL is removed.
///
/// UI: bind <see cref="Preview"/> (a WebCamTexture) to a RawImage / UI Toolkit
/// background to show the feed; call StartScan()/StopScan() from a Scan button.
/// Camera permission is already declared for AR / RoomPlan.
/// </summary>
public class QrScannerController : MonoBehaviour
{
    public static QrScannerController Instance { get; private set; }

    /// <summary>Raised when a scan resolved to a product. The bool is whether it
    /// was actually placed into the current room (false = no room / no model).</summary>
    public static event Action<ProductModel, bool> OnProductScanned;
    /// <summary>Raised on an invalid QR or an unresolvable product.</summary>
    public static event Action<string>             OnScanError;

    /// <summary>The live camera texture — bind this to your UI to show the feed.</summary>
    public WebCamTexture Preview { get; private set; }
    public bool IsScanning { get; private set; }

#if IBMROS_ZXING
    [Tooltip("Seconds between decode attempts.")]
    [SerializeField] private float decodeIntervalSeconds = 0.4f;
    private float _lastDecode;
    private IBarcodeReader _reader;
    private Color32[] _pixelBuffer;   // reused — no per-frame allocation
    private bool _decodeBusy;         // one in-flight background decode at a time
#endif

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
#if IBMROS_ZXING
        _reader = new BarcodeReader
        {
            AutoRotate = true,
            Options = new ZXing.Common.DecodingOptions
            {
                TryHarder = true,
                PossibleFormats = new[] { BarcodeFormat.QR_CODE },
            },
        };
#endif
    }

    public void StartScan()
    {
#if IBMROS_ZXING
        if (IsScanning) return;
        StartCoroutine(StartScanRoutine());
#else
        Debug.LogWarning("[QrScanner] ZXing not enabled — in-app scanning is off. " +
                         "Import ZXing.Net and add the IBMROS_ZXING scripting define. " +
                         "(External-camera scanning via universal links still works.)");
        OnScanError?.Invoke("Scanner unavailable in this build.");
#endif
    }

#if IBMROS_ZXING
    private IEnumerator StartScanRoutine()
    {
        // Ask for camera permission the platform-correct way (required on iOS;
        // macOS/Editor prompts on first use; Android is granted via the AR
        // permission the app already requests).
        if (!Application.HasUserAuthorization(UserAuthorization.WebCam))
            yield return Application.RequestUserAuthorization(UserAuthorization.WebCam);
        if (!Application.HasUserAuthorization(UserAuthorization.WebCam))
        {
            Debug.LogWarning("[QrScanner] camera permission denied.");
            OnScanError?.Invoke("Camera permission denied.");
            yield break;
        }

        if (Preview == null)
        {
            // 1280×720 is plenty for QR and cheap to decode; the device picks the
            // nearest supported mode.
            Preview = new WebCamTexture(1280, 720, 30);
        }
        Preview.Play();
        IsScanning = true;
        Debug.Log($"[QrScanner] scanning started ({Preview.deviceName}).");
    }
#endif

    public void StopScan()
    {
        IsScanning = false;
        if (Preview != null)
        {
            // Stop() alone does not reliably release the device (macOS camera
            // light stays on) — the texture must be destroyed.
            if (Preview.isPlaying) Preview.Stop();
            Destroy(Preview);
            Preview = null;
        }
    }

#if IBMROS_ZXING
    void Update()
    {
        if (!IsScanning || Preview == null || !Preview.didUpdateThisFrame) return;
        if (_decodeBusy) return;   // previous frame still decoding
        if (Time.realtimeSinceStartup - _lastDecode < decodeIntervalSeconds) return;
        _lastDecode = Time.realtimeSinceStartup;

        // Copy the frame on the main thread (WebCamTexture requires it) into a
        // REUSED buffer, then decode on the thread pool — ZXing decode is the
        // expensive part and doing it here is what froze the Editor.
        int w = Preview.width, h = Preview.height;
        if (w <= 16) return;   // camera not ready yet (macOS reports 16×16 first)
        if (_pixelBuffer == null || _pixelBuffer.Length != w * h)
            _pixelBuffer = new Color32[w * h];
        Preview.GetPixels32(_pixelBuffer);

        var pixels = _pixelBuffer;
        _decodeBusy = true;
        Task.Run(() =>
        {
            try { return _reader.Decode(pixels, w, h); }
            catch { return null; }
        }).ContinueWith(t =>
        {
            _decodeBusy = false;
            var result = t.Status == TaskStatus.RanToCompletion ? t.Result : null;
            if (result == null || string.IsNullOrEmpty(result.Text) || !IsScanning)
                return;
            Debug.Log($"[QrScanner] decoded: {result.Text}");
            StopScan();
            HandleDecodedText(result.Text);
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }
#endif

    /// <summary>
    /// Production in-app scan flow (also callable with a simulated payload for
    /// testing, so it doesn't need ZXing or a live camera):
    ///   payload → extract canonical id (pure local parse — NO dependency on the
    ///   domain resolving) → resolve product via canonical-index → if it has a 3D
    ///   model, place it DIRECTLY into the current room; otherwise open the
    ///   product-detail sheet (info + Save) since it can't be placed in AR.
    /// The scan is always recorded to history either way.
    /// </summary>
    public async void HandleDecodedText(string payload)
    {
        string canonicalId = DeepLinkManager.ParseCanonicalId(payload);
        if (string.IsNullOrEmpty(canonicalId))
        {
            Debug.LogWarning($"[QrScanner] not an IBMROS product QR: {payload}");
            OnScanError?.Invoke("Not an IBMROS product code.");
            return;
        }

        if (FurnitureDataService.Instance == null)
        {
            OnScanError?.Invoke("App not ready.");
            return;
        }

        ProductModel product = await FurnitureDataService.Instance.ResolveByCanonical(canonicalId);
        if (product == null)
        {
            OnScanError?.Invoke("Product not available.");
            return;
        }

        // Model-backed products place straight into the room. Products IKEA hasn't
        // published a 3D model for (~a third of the catalogue) can't be placed, so
        // we fall back to the product-detail sheet — the user still sees the info,
        // specs, and can Save it. (Placement can also fail if the spawner is
        // missing, in which case detail is the sensible fallback too.)
        bool addedToRoom = false;
        var roomUI = FindFirstObjectByType<RoomUIManager>();
        if (roomUI != null)
        {
            if (product.HasModel)
                addedToRoom = roomUI.AddProductToRoom(product);
            if (!addedToRoom)
                roomUI.OpenProductDetail(product);
        }
        else
        {
            // Not in the Room scene — router opens detail / enters the designer.
            ScannedProductRouter.Route(product);
        }

        // Automatic scan history — recorded AFTER the add attempt so the outcome
        // flag is accurate. Separate from Saved Items (which stays intentional).
        ScanHistoryService.Instance?.Record(product, addedToRoom);

        OnProductScanned?.Invoke(product, addedToRoom);
    }

    void OnDisable() => StopScan();
}

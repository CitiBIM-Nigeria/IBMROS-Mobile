using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Full-screen QR scanner overlay for the Room scene.
///
/// Built programmatically (UI Toolkit) so it needs no UXML/USS assets: a live
/// camera viewfinder (the QrScannerController's WebCamTexture rendered through a
/// UIElements Image), a centred scan frame, a status line, and a close button.
///
/// Flow: Open() → camera starts → QrScannerController decodes → on success shows
/// "✓ Adding …" and closes (the product is already placed by the scanner flow);
/// on an invalid/unknown QR shows the reason and automatically resumes scanning.
/// Works in the Editor with a webcam (macOS prompts for camera permission once).
/// </summary>
public class ScannerOverlayController : MonoBehaviour
{
    public event Action OnClosed;

    private VisualElement _overlay;
    private Image         _viewfinder;
    private Label         _status;
    private VisualElement _frame;
    private bool          _isOpen;
    private int           _appliedRotation = int.MinValue;
    private Coroutine     _pending;

    private const string StatusIdle = "Point your camera at a product QR code";

    // ---------------------------------------------------------------
    // BUILD
    // ---------------------------------------------------------------

    public void Initialize(VisualElement root)
    {
        if (_overlay != null) return;   // already built (OnEnable re-entry)

        _overlay = new VisualElement { name = "ScannerOverlay" };
        var s = _overlay.style;
        s.position = Position.Absolute;
        s.top = 0; s.bottom = 0; s.left = 0; s.right = 0;
        s.backgroundColor = new Color(0f, 0f, 0f, 1f);
        s.display = DisplayStyle.None;

        // Live camera feed, filling the screen.
        _viewfinder = new Image { name = "ScannerViewfinder", scaleMode = ScaleMode.ScaleAndCrop };
        var vs = _viewfinder.style;
        vs.position = Position.Absolute;
        vs.top = 0; vs.bottom = 0; vs.left = 0; vs.right = 0;
        _overlay.Add(_viewfinder);

        // Centred scan frame — a simple rounded square so the user knows where
        // to aim. Decoding uses the whole frame; this is purely guidance.
        _frame = new VisualElement { name = "ScannerFrame" };
        var fs = _frame.style;
        fs.position = Position.Absolute;
        fs.alignSelf = Align.Center;
        fs.width = Length.Percent(62);
        fs.height = 0;                       // height set from width in geometry cb
        fs.borderTopWidth = fs.borderBottomWidth = fs.borderLeftWidth = fs.borderRightWidth = 3;
        Color frameCol = new Color(1f, 1f, 1f, 0.85f);
        fs.borderTopColor = fs.borderBottomColor = fs.borderLeftColor = fs.borderRightColor = frameCol;
        fs.borderTopLeftRadius = fs.borderTopRightRadius =
            fs.borderBottomLeftRadius = fs.borderBottomRightRadius = 24;
        _overlay.Add(_frame);
        // Keep the frame square and centred whatever the screen size.
        _overlay.RegisterCallback<GeometryChangedEvent>(_ =>
        {
            float side = Mathf.Min(_overlay.resolvedStyle.width * 0.62f,
                                   _overlay.resolvedStyle.height * 0.45f);
            _frame.style.width  = side;
            _frame.style.height = side;
            _frame.style.left   = (_overlay.resolvedStyle.width  - side) / 2f;
            _frame.style.top    = (_overlay.resolvedStyle.height - side) / 2f;
        });

        // Status line under the frame.
        _status = new Label(StatusIdle) { name = "ScannerStatus" };
        var ss = _status.style;
        ss.position = Position.Absolute;
        ss.bottom = Length.Percent(12);
        ss.left = 24; ss.right = 24;
        ss.unityTextAlign = TextAnchor.MiddleCenter;
        ss.color = Color.white;
        ss.fontSize = 15;
        ss.backgroundColor = new Color(0f, 0f, 0f, 0.45f);
        ss.paddingTop = ss.paddingBottom = 10;
        ss.paddingLeft = ss.paddingRight = 14;
        ss.borderTopLeftRadius = ss.borderTopRightRadius =
            ss.borderBottomLeftRadius = ss.borderBottomRightRadius = 12;
        _overlay.Add(_status);

        // Close button (top-left, safe-area friendly offset).
        var close = new Button(Close) { text = "✕", name = "ScannerCloseButton" };
        var cs = close.style;
        cs.position = Position.Absolute;
        cs.top = 52; cs.left = 20;
        cs.width = 44; cs.height = 44;
        cs.fontSize = 20;
        cs.color = Color.white;
        cs.backgroundColor = new Color(0f, 0f, 0f, 0.55f);
        cs.borderTopLeftRadius = cs.borderTopRightRadius =
            cs.borderBottomLeftRadius = cs.borderBottomRightRadius = 22;
        cs.borderTopWidth = cs.borderBottomWidth = cs.borderLeftWidth = cs.borderRightWidth = 0;
        cs.unityTextAlign = TextAnchor.MiddleCenter;
        _overlay.Add(close);

        root.Add(_overlay);
    }

    // ---------------------------------------------------------------
    // OPEN / CLOSE
    // ---------------------------------------------------------------

    public void Open()
    {
        if (_overlay == null || _isOpen) return;
        _isOpen = true;

        QrScannerController.OnProductScanned += HandleScanned;
        QrScannerController.OnScanError      += HandleError;

        SetStatus(StatusIdle, Color.white);
        _overlay.style.display = DisplayStyle.Flex;
        QrScannerController.Instance?.StartScan();
    }

    public void Close()
    {
        if (!_isOpen) return;
        _isOpen = false;

        if (_pending != null) { StopCoroutine(_pending); _pending = null; }
        QrScannerController.OnProductScanned -= HandleScanned;
        QrScannerController.OnScanError      -= HandleError;

        QrScannerController.Instance?.StopScan();
        _viewfinder.image = null;
        _overlay.style.display = DisplayStyle.None;
        OnClosed?.Invoke();
    }

    public bool IsOpen => _isOpen;

    // ---------------------------------------------------------------
    // LIVE FEED
    // ---------------------------------------------------------------

    void Update()
    {
        if (!_isOpen) return;
        var scanner = QrScannerController.Instance;
        var cam = scanner != null ? scanner.Preview : null;
        if (cam == null)
        {
            // Camera stopped (decode happened / StopScan destroyed the texture) —
            // drop the stale reference so the UI doesn't render a dead texture.
            if (_viewfinder.image != null) _viewfinder.image = null;
            return;
        }

        if (_viewfinder.image != cam)
            _viewfinder.image = cam;

        // Phones deliver the feed rotated; mirror/rotate the DISPLAY to match.
        // (Decoding is unaffected — ZXing AutoRotate handles rotated codes.)
        int rot = cam.videoRotationAngle;
        if (rot != _appliedRotation)
        {
            _appliedRotation = rot;
            _viewfinder.style.rotate = new Rotate(new Angle(rot, AngleUnit.Degree));
            _viewfinder.style.scale =
                new Scale(new Vector2(1f, cam.videoVerticallyMirrored ? -1f : 1f));
        }

        if (cam.didUpdateThisFrame)
            _viewfinder.MarkDirtyRepaint();
    }

    // ---------------------------------------------------------------
    // SCAN RESULTS
    // ---------------------------------------------------------------

    private void HandleScanned(ProductModel product, bool addedToRoom)
    {
        if (addedToRoom)
        {
            // Placed in the room — brief confirmation, then close.
            SetStatus($"✓ Adding {product.Name} to your room…",
                      new Color(0.55f, 0.95f, 0.55f));
            _pending = StartCoroutine(CloseAfter(1.0f));
        }
        else
        {
            // No 3D model (or placement unavailable) — the scanner already opened
            // the product-detail sheet underneath, so close the overlay right away
            // to reveal it. (This overlay renders above the sheet, so it MUST hide
            // for the sheet to be seen.)
            Close();
        }
    }

    private void HandleError(string message)
    {
        SetStatus(message, new Color(1f, 0.65f, 0.6f));
        _pending = StartCoroutine(ResumeAfter(1.6f));
    }

    private IEnumerator CloseAfter(float seconds)
    {
        yield return new WaitForSeconds(seconds);
        Close();
    }

    private IEnumerator ResumeAfter(float seconds)
    {
        yield return new WaitForSeconds(seconds);
        if (!_isOpen) yield break;
        SetStatus(StatusIdle, Color.white);
        QrScannerController.Instance?.StartScan();   // decode stopped on read; resume
    }

    private void SetStatus(string text, Color color)
    {
        if (_status == null) return;
        _status.text = text;
        _status.style.color = color;
    }

    void OnDisable()
    {
        if (_isOpen) Close();
    }
}

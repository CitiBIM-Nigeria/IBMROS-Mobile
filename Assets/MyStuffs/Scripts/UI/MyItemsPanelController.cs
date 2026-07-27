using System;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// "My Items" panel for the Room scene — two tabs over the two local stores:
///   • Saved   — products the user intentionally kept (SavedItemsService).
///   • History — every successful QR scan, automatic (ScanHistoryService).
///
/// Built programmatically (UI Toolkit, no UXML/USS assets). Tapping a row
/// re-resolves the product (canonical id first — the same production path a scan
/// uses — falling back to product id) and opens the standard detail sheet, from
/// which the user can preview / save / add to the room. Saved rows can be
/// removed; History has a Clear button. Lists refresh live via the services'
/// OnChanged events.
/// </summary>
public class MyItemsPanelController : MonoBehaviour
{
    public event Action OnClosed;
    /// <summary>Raised with the resolved product when a row is tapped.</summary>
    public event Action<ProductModel> OnItemChosen;

    private VisualElement _overlay;
    private VisualElement _panel;
    private Button        _tabSaved;
    private Button        _tabHistory;
    private Button        _clearButton;
    private ScrollView    _list;
    private Label         _empty;

    private bool _isOpen;
    private bool _showingSaved = true;

    // Palette (panel is white — dark text).
    private static readonly Color TextDark   = new Color(0.11f, 0.11f, 0.12f);
    private static readonly Color TextGrey   = new Color(0.56f, 0.56f, 0.58f);
    private static readonly Color RowBg      = new Color(0.97f, 0.97f, 0.96f);
    private static readonly Color Accent     = new Color(0.00f, 0.44f, 0.89f);

    // ---------------------------------------------------------------
    // BUILD
    // ---------------------------------------------------------------

    public void Initialize(VisualElement root)
    {
        if (_overlay != null) return;

        _overlay = new VisualElement { name = "MyItemsOverlay" };
        var os = _overlay.style;
        os.position = Position.Absolute;
        os.top = 0; os.bottom = 0; os.left = 0; os.right = 0;
        os.backgroundColor = new Color(0f, 0f, 0f, 0.45f);
        os.display = DisplayStyle.None;
        os.justifyContent = Justify.FlexEnd;   // panel slides from the bottom
        _overlay.RegisterCallback<ClickEvent>(evt =>
        {
            if (evt.target == _overlay) Close();   // tap outside dismisses
        });

        _panel = new VisualElement { name = "MyItemsPanel" };
        var ps = _panel.style;
        ps.height = Length.Percent(72);
        ps.backgroundColor = Color.white;
        ps.borderTopLeftRadius = ps.borderTopRightRadius = 22;
        ps.paddingLeft = ps.paddingRight = 18;
        ps.paddingTop = 14; ps.paddingBottom = 10;
        _panel.RegisterCallback<ClickEvent>(evt => evt.StopPropagation());
        _overlay.Add(_panel);

        // Header: title + close.
        var header = Row();
        header.style.justifyContent = Justify.SpaceBetween;
        header.style.alignItems = Align.Center;
        var title = new Label("My Items");
        title.style.fontSize = 19;
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        title.style.color = TextDark;
        header.Add(title);
        var close = new Button(Close) { text = "✕" };
        StyleGhostButton(close, 36);
        header.Add(close);
        _panel.Add(header);

        // Tabs row (+ contextual Clear for History).
        var tabs = Row();
        tabs.style.marginTop = 10;
        tabs.style.alignItems = Align.Center;
        _tabSaved   = MakeTab("Saved",   () => SwitchTab(saved: true));
        _tabHistory = MakeTab("History", () => SwitchTab(saved: false));
        tabs.Add(_tabSaved);
        tabs.Add(_tabHistory);
        var spacer = new VisualElement();
        spacer.style.flexGrow = 1;
        tabs.Add(spacer);
        _clearButton = new Button(ClearHistory) { text = "Clear" };
        StyleGhostButton(_clearButton, 30);
        _clearButton.style.fontSize = 13;
        _clearButton.style.color = TextGrey;
        _clearButton.style.width = StyleKeyword.Auto;
        _clearButton.style.paddingLeft = _clearButton.style.paddingRight = 10;
        tabs.Add(_clearButton);
        _panel.Add(tabs);

        // List.
        _list = new ScrollView(ScrollViewMode.Vertical);
        _list.style.flexGrow = 1;
        _list.style.marginTop = 8;
        _panel.Add(_list);

        // Empty-state label.
        _empty = new Label();
        _empty.style.unityTextAlign = TextAnchor.MiddleCenter;
        _empty.style.color = TextGrey;
        _empty.style.fontSize = 14;
        _empty.style.marginTop = 40;
        _empty.style.whiteSpace = WhiteSpace.Normal;
        _panel.Add(_empty);

        root.Add(_overlay);
    }

    // ---------------------------------------------------------------
    // OPEN / CLOSE
    // ---------------------------------------------------------------

    public void Open()
    {
        if (_overlay == null || _isOpen) return;
        _isOpen = true;
        SavedItemsService.OnChanged  += Refresh;
        ScanHistoryService.OnChanged += Refresh;
        _overlay.style.display = DisplayStyle.Flex;
        SwitchTab(_showingSaved);
    }

    public void Close()
    {
        if (!_isOpen) return;
        _isOpen = false;
        SavedItemsService.OnChanged  -= Refresh;
        ScanHistoryService.OnChanged -= Refresh;
        _overlay.style.display = DisplayStyle.None;
        OnClosed?.Invoke();
    }

    public bool IsOpen => _isOpen;

    // ---------------------------------------------------------------
    // TABS + LIST
    // ---------------------------------------------------------------

    private void SwitchTab(bool saved)
    {
        _showingSaved = saved;
        StyleTab(_tabSaved,   active: saved);
        StyleTab(_tabHistory, active: !saved);
        _clearButton.style.display = saved ? DisplayStyle.None : DisplayStyle.Flex;
        Refresh();
    }

    private void Refresh()
    {
        if (!_isOpen) return;
        _list.Clear();

        int shown = 0;
        if (_showingSaved)
        {
            var items = SavedItemsService.Instance?.All();
            _tabSaved.text = $"Saved ({items?.Count ?? 0})";
            _tabHistory.text = $"History ({ScanHistoryService.Instance?.Count ?? 0})";
            if (items != null)
                foreach (var it in items)
                {
                    _list.Add(BuildRow(
                        it.thumbUrl, it.name,
                        $"{it.priceText}  ·  saved {TimeAgo(it.savedAtIso)}",
                        badge: null,
                        onTap: () => OpenItem(it.canonicalId, it.productId),
                        onRemove: () => SavedItemsService.Instance.Remove(it.productId)));
                    shown++;
                }
            _empty.text = "Nothing saved yet.\nTap ☆ on any product to keep it here.";
        }
        else
        {
            var scans = ScanHistoryService.Instance?.All();
            _tabHistory.text = $"History ({scans?.Count ?? 0})";
            _tabSaved.text = $"Saved ({SavedItemsService.Instance?.Count ?? 0})";
            if (scans != null)
                foreach (var sc in scans)
                {
                    _list.Add(BuildRow(
                        sc.thumbUrl, sc.name,
                        $"scanned {TimeAgo(sc.scannedAtIso)}",
                        badge: sc.addedToRoom ? "✓ Added" : null,
                        onTap: () => OpenItem(sc.canonicalId, sc.productId),
                        onRemove: null));
                    shown++;
                }
            _empty.text = "No scans yet.\nScan a product QR to see it here.";
        }

        _empty.style.display = shown == 0 ? DisplayStyle.Flex : DisplayStyle.None;
        _list.style.display  = shown == 0 ? DisplayStyle.None : DisplayStyle.Flex;
    }

    private VisualElement BuildRow(string thumbUrl, string name, string subtitle,
                                   string badge, Action onTap, Action onRemove)
    {
        var row = Row();
        var rs = row.style;
        rs.alignItems = Align.Center;
        rs.backgroundColor = RowBg;
        rs.borderTopLeftRadius = rs.borderTopRightRadius =
            rs.borderBottomLeftRadius = rs.borderBottomRightRadius = 14;
        rs.paddingTop = rs.paddingBottom = 10;
        rs.paddingLeft = rs.paddingRight = 12;
        rs.marginBottom = 8;
        row.RegisterCallback<ClickEvent>(_ => onTap?.Invoke());

        // Thumbnail.
        var thumb = new VisualElement();
        var ts = thumb.style;
        ts.width = 56; ts.height = 56;
        ts.minWidth = 56;
        ts.backgroundColor = Color.white;
        ts.borderTopLeftRadius = ts.borderTopRightRadius =
            ts.borderBottomLeftRadius = ts.borderBottomRightRadius = 10;
        ts.backgroundSize = new StyleBackgroundSize(
            new BackgroundSize(BackgroundSizeType.Contain));
        row.Add(thumb);
        if (!string.IsNullOrEmpty(thumbUrl))
            LoadThumb(thumb, thumbUrl);

        // Name + subtitle.
        var col = new VisualElement();
        col.style.flexGrow = 1;
        col.style.marginLeft = 12;
        var nameLbl = new Label(name);
        nameLbl.style.fontSize = 14;
        nameLbl.style.unityFontStyleAndWeight = FontStyle.Bold;
        nameLbl.style.color = TextDark;
        col.Add(nameLbl);
        var subLbl = new Label(subtitle);
        subLbl.style.fontSize = 12;
        subLbl.style.color = TextGrey;
        subLbl.style.marginTop = 2;
        col.Add(subLbl);
        row.Add(col);

        // Badge ("✓ Added") — history rows.
        if (!string.IsNullOrEmpty(badge))
        {
            var b = new Label(badge);
            var bs = b.style;
            bs.fontSize = 11;
            bs.color = new Color(0.13f, 0.55f, 0.28f);
            bs.backgroundColor = new Color(0.13f, 0.55f, 0.28f, 0.12f);
            bs.paddingLeft = bs.paddingRight = 8;
            bs.paddingTop = bs.paddingBottom = 4;
            bs.borderTopLeftRadius = bs.borderTopRightRadius =
                bs.borderBottomLeftRadius = bs.borderBottomRightRadius = 8;
            row.Add(b);
        }

        // Remove (saved rows only).
        if (onRemove != null)
        {
            var rm = new Button(() => onRemove()) { text = "✕" };
            StyleGhostButton(rm, 30);
            rm.style.marginLeft = 8;
            rm.RegisterCallback<ClickEvent>(evt => evt.StopPropagation());
            row.Add(rm);
        }

        return row;
    }

    private async void LoadThumb(VisualElement target, string url)
    {
        var tex = await ImageCache.GetTexture(url);
        if (tex != null)
            target.style.backgroundImage = new StyleBackground(tex);
    }

    // ---------------------------------------------------------------
    // ACTIONS
    // ---------------------------------------------------------------

    private async void OpenItem(string canonicalId, string productId)
    {
        if (FurnitureDataService.Instance == null) return;

        // Canonical id is the production resolve path (same as a scan); fall back
        // to the region-local product id for any legacy entry without one.
        ProductModel product = null;
        if (!string.IsNullOrEmpty(canonicalId))
            product = await FurnitureDataService.Instance.ResolveByCanonical(canonicalId);
        if (product == null && !string.IsNullOrEmpty(productId))
            product = await FurnitureDataService.Instance.FetchProduct(productId);

        if (product == null)
        {
            Debug.LogWarning($"[MyItems] product no longer available: {productId}");
            return;
        }

        Close();
        OnItemChosen?.Invoke(product);
    }

    private void ClearHistory()
    {
        ScanHistoryService.Instance?.Clear();
        Refresh();
    }

    // ---------------------------------------------------------------
    // HELPERS
    // ---------------------------------------------------------------

    private static VisualElement Row()
    {
        var v = new VisualElement();
        v.style.flexDirection = FlexDirection.Row;
        return v;
    }

    private Button MakeTab(string text, Action onClick)
    {
        var b = new Button(onClick) { text = text };
        var bs = b.style;
        bs.backgroundColor = Color.clear;
        bs.borderTopWidth = bs.borderLeftWidth = bs.borderRightWidth = 0;
        bs.borderBottomWidth = 2;
        bs.borderBottomColor = Color.clear;
        bs.fontSize = 15;
        bs.paddingLeft = bs.paddingRight = 4;
        bs.paddingBottom = 6;
        bs.marginRight = 18;
        return b;
    }

    private void StyleTab(Button tab, bool active)
    {
        tab.style.color = active ? TextDark : TextGrey;
        tab.style.unityFontStyleAndWeight = active ? FontStyle.Bold : FontStyle.Normal;
        tab.style.borderBottomColor = active ? (StyleColor)Accent : (StyleColor)Color.clear;
    }

    private static void StyleGhostButton(Button b, int size)
    {
        var bs = b.style;
        bs.width = size; bs.height = size;
        bs.fontSize = size >= 34 ? 16 : 13;
        bs.color = TextGrey;
        bs.backgroundColor = new Color(0f, 0f, 0f, 0.05f);
        bs.borderTopLeftRadius = bs.borderTopRightRadius =
            bs.borderBottomLeftRadius = bs.borderBottomRightRadius = size / 2;
        bs.borderTopWidth = bs.borderBottomWidth = bs.borderLeftWidth = bs.borderRightWidth = 0;
        bs.unityTextAlign = TextAnchor.MiddleCenter;
    }

    private static string TimeAgo(string iso)
    {
        try
        {
            var t = DateTime.Parse(iso, null, System.Globalization.DateTimeStyles.RoundtripKind);
            var span = DateTime.UtcNow - t.ToUniversalTime();
            if (span.TotalMinutes < 1)  return "just now";
            if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes}m ago";
            if (span.TotalHours   < 24) return $"{(int)span.TotalHours}h ago";
            if (span.TotalDays    < 7)  return $"{(int)span.TotalDays}d ago";
            return t.ToLocalTime().ToString("d MMM yyyy");
        }
        catch { return ""; }
    }
}

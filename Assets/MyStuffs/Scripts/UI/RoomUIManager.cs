using System;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.SceneManagement;

public class RoomUIManager : MonoBehaviour
{
    [Header("UI Document")]
    [SerializeField] private UIDocument uiDocument;

    [Header("Controllers")]
    [SerializeField] private ToolbarController toolbarController;
    [SerializeField] private BottomBarController bottomBarController;
    [SerializeField] private FurniturePanelController furniturePanelController;
    [SerializeField] private ItemDetailSheetController itemDetailSheetController;

    [Header("Spawning")]
    [SerializeField] private FurnitureSpawnManager furnitureSpawnManager;

    [Header("Scene Objects to Hide")]
    [SerializeField] private GameObject joystickObject;
    
    [Header("Selection")]
    [SerializeField] private SelectionManager selectionManager;
    [SerializeField] private ActionMenuController actionMenuController;

    private VisualElement _root;
    private VisualElement _topBar;
    private VisualElement _bottomBar;
    private VisualElement _dismissOverlay;

    private Button _storeButton;
    private bool _ignoreNextOverlayClick = false;
    private bool _addedToRoom            = false;

    private string       _pendingS3ModelUrl   = "";   // chosen colour's GLB key
    private ProductModel _selectedProduct;
    private bool         _detailFromPanel;            // detail opened from the furniture panel?

    // QR feature UI — self-bootstrapped in OnEnable (no Inspector wiring needed).
    private ScannerOverlayController _scannerOverlay;
    private MyItemsPanelController   _myItemsPanel;

    void OnEnable()
    {
        if (uiDocument == null)
        {
            Debug.LogError("[RoomUIManager] UIDocument not assigned.");
            return;
        }

        _root           = uiDocument.rootVisualElement;
        _topBar         = _root.Q<VisualElement>("TopBar");
        _bottomBar      = _root.Q<VisualElement>("BottomBar");
        _dismissOverlay = _root.Q<VisualElement>("FurnitureDismissOverlay");
        _storeButton    = _root.Q<Button>("StoreButton");
        _storeButton?.RegisterCallback<ClickEvent>(OnStoreClicked);

        _dismissOverlay?.RegisterCallback<PointerDownEvent>(OnDismissOverlayTapped);

        toolbarController?.Initialize(_root);
        bottomBarController?.Initialize(_root);
        furniturePanelController?.Initialize(_root);
        itemDetailSheetController?.Initialize(_root);
        InitializeQrUI();

        // Wire up events
        if (toolbarController != null)
        {
            toolbarController.OnCloseClicked      += OnCloseClicked;
            toolbarController.OnScreenshotClicked += OnScreenshotClicked;
        }

        if (bottomBarController != null)
            bottomBarController.OnAddFurnitureClicked += OnAddFurnitureClicked;

        if (furniturePanelController != null)
        {
            furniturePanelController.OnPanelClosed  += OnFurniturePanelClosed;
            furniturePanelController.OnItemSelected += OnItemSelected;
        }

        if (itemDetailSheetController != null)
        {
            itemDetailSheetController.OnSheetClosed      += OnItemDetailClosed;
            itemDetailSheetController.OnAddToRoomClicked += OnAddToRoomHandler;
            itemDetailSheetController.OnColorSelected    += OnColorSelected;
        }

        // Preload furniture catalog as soon as Room scene loads
        // AwsManager persists from Main scene so it is already initialized
        StartCatalogPreload();

        // A QR scan may have entered THIS scene specifically to show a product
        // (ScannedProductRouter stashed it and loaded the Room designer). Open it
        // now that the detail sheet is initialized.
        var scanned = ScannedProductRouter.ConsumePending();
        if (scanned != null)
            OpenProductDetail(scanned);
    }

    // ---------------------------------------------------------------
    // QR FEATURE UI (scanner overlay + My Items panel + bottom-bar buttons)
    // Self-bootstrapping: components are added at runtime and the buttons are
    // built with the SAME classes as the UXML ones, so no scene/asset edits are
    // needed — press Play and it's there.
    // ---------------------------------------------------------------

    private void InitializeQrUI()
    {
        _scannerOverlay = GetComponent<ScannerOverlayController>()
                          ?? gameObject.AddComponent<ScannerOverlayController>();
        _myItemsPanel   = GetComponent<MyItemsPanelController>()
                          ?? gameObject.AddComponent<MyItemsPanelController>();
        _scannerOverlay.Initialize(_root);
        _myItemsPanel.Initialize(_root);

        _scannerOverlay.OnClosed     += OnQrOverlayClosed;
        _myItemsPanel.OnClosed       += OnQrOverlayClosed;
        _myItemsPanel.OnItemChosen   += OpenProductDetail;

        var bottomBar = _root.Q<VisualElement>("BottomBar");
        if (bottomBar == null) return;

        // Scan button — after the Store button (left cluster).
        if (bottomBar.Q<Button>("ScanButton") == null)
        {
            var scanBtn = MakeBarButton("ScanButton", "▣", "Scan", OnScanClicked);
            var store = bottomBar.Q<Button>("StoreButton");
            int idx = store != null ? bottomBar.IndexOf(store) + 1 : 0;
            bottomBar.Insert(idx, scanBtn);
        }

        // My Items button — before Add Furniture (right cluster).
        if (bottomBar.Q<Button>("MyItemsButton") == null)
        {
            var itemsBtn = MakeBarButton("MyItemsButton", "☆", "My Items", OnMyItemsClicked);
            var add = bottomBar.Q<Button>("AddFurnitureButton");
            int idx = add != null ? bottomBar.IndexOf(add) : bottomBar.childCount;
            bottomBar.Insert(idx, itemsBtn);
        }
    }

    // Mirrors the UXML bottom-bar button structure (icon label + text label with
    // the same USS classes) so the new buttons inherit the existing styling.
    private static Button MakeBarButton(string name, string icon, string label,
                                        Action onClick)
    {
        var b = new Button(onClick) { name = name };
        b.AddToClassList("bottom-bar__button");
        var iconLbl = new Label(icon);
        iconLbl.AddToClassList("bottom-bar__icon");
        b.Add(iconLbl);
        var textLbl = new Label(label);
        textLbl.AddToClassList("bottom-bar__label");
        b.Add(textLbl);
        return b;
    }

    private void OnScanClicked()
    {
        Debug.Log("[RoomUIManager] Scan tapped.");
        selectionManager?.DeselectObject();
        actionMenuController?.HidePanels();
        SetRoomUIVisible(false);
        _scannerOverlay?.Open();
    }

    private void OnMyItemsClicked()
    {
        Debug.Log("[RoomUIManager] My Items tapped.");
        selectionManager?.DeselectObject();
        actionMenuController?.HidePanels();
        SetRoomUIVisible(false);
        _myItemsPanel?.Open();
    }

    private void OnQrOverlayClosed()
    {
        // Don't restore the bars if the close is because a detail sheet opened.
        if (itemDetailSheetController != null && itemDetailSheetController.IsOpen)
            return;
        SetRoomUIVisible(true);
    }

    private void StartCatalogPreload()
    {
        if (AwsManager.Instance == null)
        {
            Debug.LogWarning("[RoomUIManager] AwsManager not ready yet.");
            return;
        }

        if (FurnitureDataService.Instance == null)
        {
            Debug.LogWarning("[RoomUIManager] FurnitureDataService not ready yet.");
            return;
        }

        if (AwsManager.Instance.IsInitialized)
        {
            _ = FurnitureDataService.Instance.LoadCategories();
            Debug.Log("[RoomUIManager] Started catalog preload.");
        }
        else
        {
            AwsManager.OnAwsReady += OnAwsReadyForPreload;
            Debug.Log("[RoomUIManager] Waiting for AWS before preload.");
        }
    }

    private void OnAwsReadyForPreload()
    {
        AwsManager.OnAwsReady -= OnAwsReadyForPreload;

        if (FurnitureDataService.Instance == null)
        {
            Debug.LogWarning("[RoomUIManager] FurnitureDataService still null on AWS ready.");
            return;
        }

        _ = FurnitureDataService.Instance.LoadCategories();
        Debug.Log("[RoomUIManager] AWS ready — started catalog preload.");
    }

    void OnDisable()
    {
        AwsManager.OnAwsReady -= OnAwsReadyForPreload;
        _dismissOverlay?.UnregisterCallback<PointerDownEvent>(OnDismissOverlayTapped);
        toolbarController?.Cleanup();
        _storeButton?.UnregisterCallback<ClickEvent>(OnStoreClicked);

        if (toolbarController != null)
        {
            toolbarController.OnCloseClicked      -= OnCloseClicked;
            toolbarController.OnScreenshotClicked -= OnScreenshotClicked;
        }

        if (bottomBarController != null)
            bottomBarController.OnAddFurnitureClicked -= OnAddFurnitureClicked;

        if (furniturePanelController != null)
        {
            furniturePanelController.OnPanelClosed  -= OnFurniturePanelClosed;
            furniturePanelController.OnItemSelected -= OnItemSelected;
        }

        if (itemDetailSheetController != null)
        {
            itemDetailSheetController.OnSheetClosed      -= OnItemDetailClosed;
            itemDetailSheetController.OnAddToRoomClicked -= OnAddToRoomHandler;
        }

        if (_scannerOverlay != null)
            _scannerOverlay.OnClosed -= OnQrOverlayClosed;
        if (_myItemsPanel != null)
        {
            _myItemsPanel.OnClosed     -= OnQrOverlayClosed;
            _myItemsPanel.OnItemChosen -= OpenProductDetail;
        }
    }
    

    // ---------------------------------------------------------------
    // DISMISS OVERLAY
    // ---------------------------------------------------------------

    private void OnDismissOverlayTapped(PointerDownEvent evt)
    {
        if (_ignoreNextOverlayClick)
        {
            _ignoreNextOverlayClick = false;
            return;
        }

        Debug.Log("[RoomUIManager] Outside tap — closing panel.");
        SetDismissOverlayVisible(false);
        furniturePanelController?.Close();
    }

    private void SetDismissOverlayVisible(bool visible)
    {
        if (_dismissOverlay != null)
            _dismissOverlay.style.display = visible
                ? DisplayStyle.Flex
                : DisplayStyle.None;
    }

    // ---------------------------------------------------------------
    // TOOLBAR
    // ---------------------------------------------------------------

    private void OnCloseClicked()
    {
        UndoRedoManager.Instance?.Clear();
        SceneTransition.SetSkipSplash(true);
        SceneManager.LoadScene("Main");
    }

    private void OnScreenshotClicked()
    {
        Debug.Log("[RoomUIManager] Screenshot tapped.");
        ScreenCapture.CaptureScreenshot("Screenshot.png");
    }

    // ---------------------------------------------------------------
    // BOTTOM BAR
    // ---------------------------------------------------------------

    private void OnAddFurnitureClicked()
    {
        Debug.Log("[RoomUIManager] Add furniture tapped.");
    
        // Deselect any selected furniture and hide action menu
        selectionManager?.DeselectObject();
        actionMenuController?.HidePanels();
    
        SetRoomUIVisible(false);
        SetDismissOverlayVisible(true);
        furniturePanelController?.Open();
    }

    // ---------------------------------------------------------------
    // PANEL CALLBACKS
    // ---------------------------------------------------------------

    private void OnFurniturePanelClosed()
    {
        Debug.Log("[RoomUIManager] Panel closed — restoring UI.");
        SetDismissOverlayVisible(false);
        SetRoomUIVisible(true);
    }

    private void OnItemSelected(ProductModel product)
    {
        OpenProductDetailInternal(product, fromPanel: true);
    }

    // Opens the product-detail sheet for a product. Public so a QR scan
    // (ScannedProductRouter) or the My Items panel can drive the SAME detail +
    // Add-to-Room flow the furniture panel uses — one code path everywhere.
    public void OpenProductDetail(ProductModel product)
    {
        OpenProductDetailInternal(product, fromPanel: false);
    }

    private void OpenProductDetailInternal(ProductModel product, bool fromPanel)
    {
        if (product == null) return;

        _detailFromPanel     = fromPanel;
        _selectedProduct     = product;
        _pendingS3ModelUrl   = product.S3ModelUrl;   // default = primary colour's GLB

        string emoji   = CategoryMapper.GetCategoryEmoji(product.CategoryId);
        string variant = product.Description ?? "";
        if (!string.IsNullOrEmpty(product.Name) && variant.StartsWith(product.Name))
            variant = variant.Substring(product.Name.Length).Trim(' ', '-');

        SetDismissOverlayVisible(false);
        furniturePanelController?.HideWithoutReset();
        itemDetailSheetController?.Open(
            emoji, product.Name, variant, product.ProductId,
            product.BestImageUrl, product.Variants, product);
    }

    // Fired when the user taps a colour swatch in the detail sheet. Each colour
    // is its own GLB now, so the chosen colour just changes which model spawns
    // (falling back to the primary if that colour didn't get a model).
    private void OnColorSelected(ProductVariant variant)
    {
        if (variant == null) return;
        _pendingS3ModelUrl = string.IsNullOrEmpty(variant.ModelUrl)
            ? _selectedProduct?.S3ModelUrl ?? ""
            : variant.ModelUrl;
    }

    // ---------------------------------------------------------------
    // ITEM DETAIL CALLBACKS
    // ---------------------------------------------------------------

    private void OnAddToRoomHandler(string productId)
    {
        _addedToRoom = true;
        Debug.Log($"[RoomUIManager] Add to Room: {productId}");
        // Spawn the chosen colour's own GLB.
        furnitureSpawnManager?.SpawnItem(_pendingS3ModelUrl);
    }

    // Place a product's default model DIRECTLY into the currently open room — the
    // production in-app QR scanner flow (scan → add to room, no detail sheet).
    // Public so QrScannerController can drive it. Uses the SAME spawn path as the
    // detail-sheet Add button. Returns true when the spawn was dispatched (used
    // by ScanHistoryService's addedToRoom flag).
    public bool AddProductToRoom(ProductModel product)
    {
        if (product == null) return false;

        _selectedProduct   = product;
        _pendingS3ModelUrl = product.S3ModelUrl;   // default = primary colour's GLB
        if (string.IsNullOrEmpty(_pendingS3ModelUrl))
        {
            Debug.LogWarning($"[RoomUIManager] Scanned '{product.Name}' has no 3D model.");
            return false;
        }
        if (furnitureSpawnManager == null)
        {
            Debug.LogWarning("[RoomUIManager] No FurnitureSpawnManager — cannot place.");
            return false;
        }

        Debug.Log($"[RoomUIManager] Scan → add to room: {product.Name}");
        furnitureSpawnManager.SpawnItem(_pendingS3ModelUrl);
        return true;
    }

    private void OnItemDetailClosed()
    {
        if (_addedToRoom)
        {
            _addedToRoom = false;
            SetRoomUIVisible(true);
            return;
        }

        // Detail opened from a scan / deep link / My Items — closing it should
        // just return to the room, NOT surface the furniture browse panel.
        if (!_detailFromPanel)
        {
            SetRoomUIVisible(true);
            return;
        }

        SetDismissOverlayVisible(true);
        furniturePanelController?.Open();
    }

    // ---------------------------------------------------------------
    // VISIBILITY
    // ---------------------------------------------------------------

    private void SetRoomUIVisible(bool visible)
    {
        if (_topBar != null)
            _topBar.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

        if (_bottomBar != null)
            _bottomBar.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

        if (joystickObject != null)
            joystickObject.SetActive(visible);
    }
    
    private void OnStoreClicked(ClickEvent evt)
    {
        Debug.Log("[RoomUIManager] Store tapped — loading IKEA Store scene.");
        SceneManager.LoadScene("IKEAStore");
    }
}
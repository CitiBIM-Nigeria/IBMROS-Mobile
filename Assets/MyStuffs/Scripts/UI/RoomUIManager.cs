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
        if (product == null) return;

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

    private void OnItemDetailClosed()
    {
        if (_addedToRoom)
        {
            _addedToRoom = false;
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
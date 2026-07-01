using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// One display socket in the VR IKEA warehouse. Represents a single subcategory
/// (e.g. "Living Room › Sofas") and shows one product from it at a time; the
/// player can cycle through the rest.
///
/// ── DESIGNER WORKFLOW (no code required) ────────────────────────────────────
///   1. Drag the Socket prefab into the scene.
///   2. Pick a subcategory from the dropdown in the Inspector.
///   3. Press Play. The socket downloads and displays furniture from that
///      subcategory automatically.
///
/// ── ARCHITECTURE ────────────────────────────────────────────────────────────
///   This is a THIN CLIENT of the existing backend. It owns NO business logic
///   that already lives elsewhere — it only orchestrates:
///     • data     → FurnitureRepository   (DynamoDB query + per-category cache +
///                  in-flight dedup + paging — shared with mobile)
///     • models   → FurnitureModelLoader  (CloudFront download + disk cache +
///                  glTFast parse — the SAME cache mobile fills, so a model
///                  mobile already downloaded loads here instantly)
///     • view     → VRProductInfoPanel     (pure UGUI, no logic)
///
///   It talks to the Repository directly rather than FurnitureDataService on
///   purpose: the DataService tracks a single "active category" and cancels the
///   background paging of any other category when a new one loads — correct for
///   mobile (one open category at a time), wrong for VR (many sockets live at
///   once). The Repository is the shared cache/dedup layer underneath it, so we
///   still reuse every bit of caching, dedup, and paging with none of the
///   single-active-category coupling.
///
///   NO hardcoded product IDs. NO hardcoded model URLs. Everything comes from
///   the backend via the assigned subcategory.
/// </summary>
[DisallowMultipleComponent]
[ExecuteAlways]
public class VRFurnitureSocket : MonoBehaviour
{
    // ---------------------------------------------------------------
    // INSPECTOR
    // ---------------------------------------------------------------

    [Header("Subcategory (set via the dropdown above)")]
    [Tooltip("The product category_id this socket displays. Chosen from the " +
             "backend-sourced dropdown — do not type raw IDs by hand.")]
    [SerializeField] private string categoryId;

    [Tooltip("Human label for logs / editor gizmo. Auto-filled from the catalog.")]
    [SerializeField] private string displayLabel;

    [Tooltip("Optional catalog asset that powers the Inspector dropdown. If left " +
             "blank the editor falls back to the one in Resources.")]
    [SerializeField] private VRSubcategoryCatalog catalog;

    [Header("Placement")]
    [Tooltip("Where the model is parented. Defaults to this transform if unset.")]
    [SerializeField] private Transform modelAnchor;

    [Tooltip("Drop the model so its base sits on the anchor (good for floor sockets).")]
    [SerializeField] private bool alignToFloor = true;

    [Tooltip("Uniform scale applied to the spawned model (1 = real-world size).")]
    [SerializeField] private float modelScale = 1f;

    [Tooltip("Extra yaw (Y rotation, degrees) so the product faces the aisle.")]
    [SerializeField] private float yawOffset;

    [Header("View & Feedback")]
    [Tooltip("Optional info panel shown when the player is near (VRSocketProximity).")]
    [SerializeField] private VRProductInfoPanel infoPanel;

    [Tooltip("Optional object shown while a model downloads/loads (spinner, hologram).")]
    [SerializeField] private GameObject loadingIndicator;

    [Tooltip("Optional object shown when the socket has no products or a load fails.")]
    [SerializeField] private GameObject errorIndicator;

    [Header("Behaviour")]
    [Tooltip("Load the first product automatically on Start.")]
    [SerializeField] private bool autoLoadOnStart = true;

    [Tooltip("Verbose per-socket logging. Errors/warnings always log regardless.")]
    [SerializeField] private bool verboseLogging = true;

    [SerializeField, HideInInspector] private string socketId;
    public string SocketId
    {
        get
        {
            if (string.IsNullOrEmpty(socketId)) socketId = System.Guid.NewGuid().ToString();
            return socketId;
        }
    }

    [SerializeField, HideInInspector] private string selectedProductId;
    public string SelectedProductId => selectedProductId;

    [SerializeField, HideInInspector] private string selectedModelUrl;
    public string SelectedModelUrl => selectedModelUrl;

    public void SetSelectedProduct(string productId, string modelUrl)
    {
        selectedProductId = productId;
        selectedModelUrl = modelUrl;
    }

    // ---------------------------------------------------------------
    // STATE MACHINE
    // ---------------------------------------------------------------

    public enum SocketState { Idle, FetchingProducts, LoadingModel, Displaying, Empty, Error }

    /// <summary>Raised whenever the socket state changes (for custom VR feedback).</summary>
    public event Action<SocketState> OnStateChanged;

    private SocketState _state = SocketState.Idle;
    public SocketState State => _state;

    // ---------------------------------------------------------------
    // PUBLIC API — safe to wire to VR buttons / interactables / triggers
    // ---------------------------------------------------------------

    public string  CategoryId     => categoryId;
    public string  DisplayLabel   => string.IsNullOrEmpty(displayLabel) ? categoryId : displayLabel;
    public ProductModel CurrentProduct => _currentProduct;
    public int     CurrentIndex   => _currentIndex;
    public int     ProductCount   => _products.Count;
    public bool    IsBusy         => _state == SocketState.FetchingProducts ||
                                     _state == SocketState.LoadingModel;

    /// <summary>Show the next product (wraps around).</summary>
    public void NextProduct()     => _ = ShowProductAt(_currentIndex + 1);

    /// <summary>Show the previous product (wraps around).</summary>
    public void PreviousProduct() => _ = ShowProductAt(_currentIndex - 1);

    /// <summary>Jump to a specific product index (wraps around).</summary>
    public void ShowProduct(int index) => _ = ShowProductAt(index);

    /// <summary>Re-fetch this subcategory from the backend and reload from the top.</summary>
    public void Refresh() => _ = InitializeAsync();

    /// <summary>Called by VRSocketProximity when the player approaches.</summary>
    public void ShowInfoPanel()
    {
        if (infoPanel == null) return;
        if (_currentProduct != null)
            infoPanel.ShowProduct(this, _currentProduct, _currentIndex, _products.Count);
        else if (_state == SocketState.Error || _state == SocketState.Empty)
            infoPanel.ShowMessage(this, EmptyMessage());
    }

    /// <summary>Called by VRSocketProximity when the player leaves.</summary>
    public void HideInfoPanel()
    {
        if (infoPanel != null) infoPanel.Hide();
    }

    // ---------------------------------------------------------------
    // INTERNAL STATE
    // ---------------------------------------------------------------

    private readonly List<ProductModel> _products = new();
    private ProductModel _currentProduct;
    private GameObject   _currentModel;
    private int _currentIndex = -1;

    // Monotonic token: every model request bumps it, and only the request whose
    // token still matches when its await completes is allowed to commit. This is
    // what keeps rapid Next/Prev spamming from leaving orphaned models behind.
    private int _loadToken;

    // Cancels the background paging loop when the socket is destroyed / refreshed.
    private CancellationTokenSource _pagingCts;

    private string Tag => $"[VRSocket:{DisplayLabel}]";

    // ---------------------------------------------------------------
    // LIFECYCLE
    // ---------------------------------------------------------------

    private void Awake()
    {
        if (modelAnchor == null) modelAnchor = transform;
    }

    private void OnEnable()
    {
#if UNITY_EDITOR
        // If we are in Edit Mode and have a saved model URL, spawn the preview automatically!
        if (!Application.isPlaying && !string.IsNullOrEmpty(selectedModelUrl))
        {
            _ = SpawnEditModePreviewAsync();
        }
#endif
    }

#if UNITY_EDITOR
    private async Task SpawnEditModePreviewAsync()
    {
        // Don't try to load if the user is in the middle of a domain reload or play mode transition
        if (Application.isPlaying || UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode) return;

        // We need FurnitureModelLoader to spawn the preview. It might be null in Edit Mode.
        var loader = FurnitureModelLoader.Instance;
        bool tempLoader = false;
        
        if (loader == null)
        {
            var go = new GameObject("TempFurnitureModelLoader_EditMode");
            go.hideFlags = HideFlags.HideAndDontSave;
            loader = go.AddComponent<FurnitureModelLoader>();
            tempLoader = true;
        }

        try
        {
            var model = await loader.LoadModel(selectedModelUrl);
            if (model != null)
            {
                DestroyCurrentModel();
                _currentModel = model;
                PlaceModel(model);

                // VERY IMPORTANT: Ensure the spawned preview meshes are NOT saved into the scene file!
                SetHideFlagsRecursive(model, HideFlags.DontSave);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[VRSocket] Could not spawn Edit Mode preview for '{selectedProductId}': {e.Message}");
        }
        finally
        {
            if (tempLoader && loader != null) DestroyImmediate(loader.gameObject);
        }
    }

    private void SetHideFlagsRecursive(GameObject go, HideFlags flags)
    {
        go.hideFlags = flags;
        foreach (Transform child in go.transform) SetHideFlagsRecursive(child.gameObject, flags);
    }
#endif

    public void RegeneratePreview()
    {
#if UNITY_EDITOR
        if (!Application.isPlaying && !string.IsNullOrEmpty(selectedModelUrl))
        {
            _ = SpawnEditModePreviewAsync();
        }
#endif
    }

    private async void Start()
    {
        if (!Application.isPlaying) return; // Skip runtime init if in Edit Mode

        // Rapidly spawn the cached model so the socket isn't empty while AWS authenticates!
        if (!string.IsNullOrEmpty(selectedModelUrl) && _currentModel == null)
        {
            try
            {
                var model = await FurnitureModelLoader.Instance.LoadModel(selectedModelUrl);
                if (model != null)
                {
                    if (_currentModel == null) // In case AWS finished instantly
                    {
                        _currentModel = model;
                        PlaceModel(model);
                        // We set a fake current product so that InitializeAsync skips reloading the model
                        _currentProduct = new ProductModel { ProductId = selectedProductId, S3ModelUrl = selectedModelUrl };
                    }
                    else
                    {
                        Destroy(model);
                    }
                }
            }
            catch {}
        }

        SetIndicator(loadingIndicator, false);
        SetIndicator(errorIndicator, false);
        if (infoPanel != null) infoPanel.Hide();

        if (!autoLoadOnStart) return;

        await WaitForAws();
        await InitializeAsync();
    }

    void OnDestroy()
    {
        _pagingCts?.Cancel();
        _pagingCts?.Dispose();
        DestroyCurrentModel();
    }

    // Block until AWS credentials are ready — the backend needs them for the query.
    private async Task WaitForAws()
    {
        if (AwsManager.Instance != null && AwsManager.Instance.IsInitialized) return;

        Log("waiting for AWS credentials…");
        var tcs = new TaskCompletionSource<bool>();
        void Ready() { AwsManager.OnAwsReady -= Ready; tcs.TrySetResult(true); }
        AwsManager.OnAwsReady += Ready;

        // Guard the race where AWS finished between the check and the subscribe.
        if (AwsManager.Instance != null && AwsManager.Instance.IsInitialized)
        {
            AwsManager.OnAwsReady -= Ready;
            tcs.TrySetResult(true);
        }
        await tcs.Task;
    }

    // ---------------------------------------------------------------
    // INITIALISATION — fetch the subcategory's products, show the first
    // ---------------------------------------------------------------

    private async Task InitializeAsync()
    {
        if (string.IsNullOrWhiteSpace(categoryId))
        {
            Debug.LogError($"{Tag} no subcategory assigned. Pick one from the " +
                           "Inspector dropdown before pressing Play.");
            SetState(SocketState.Error);
            SetIndicator(errorIndicator, true);
            return;
        }

        if (FurnitureRepository.Instance == null)
        {
            Debug.LogError($"{Tag} FurnitureRepository not in scene — cannot load. " +
                           "Ensure the backend managers are present.");
            SetState(SocketState.Error);
            SetIndicator(errorIndicator, true);
            return;
        }

        // Fresh start (also covers Refresh()).
        _pagingCts?.Cancel();
        _pagingCts = new CancellationTokenSource();
        _products.Clear();
        _currentIndex = -1;

        SetState(SocketState.FetchingProducts);
        SetIndicator(errorIndicator, false);
        SetIndicator(loadingIndicator, true);

        var sw = Stopwatch.StartNew();
        try
        {
            // Shared repository cache + in-flight dedup: if another socket (or a
            // prior visit) already fetched this subcategory, this returns instantly.
            var firstPage = await FurnitureRepository.Instance
                .GetFirstPage(categoryId, FurnitureRepository.PageSize);
            sw.Stop();

            if (firstPage == null || firstPage.Count == 0)
            {
                Debug.LogWarning($"{Tag} no products returned for '{categoryId}' " +
                                 $"({sw.ElapsedMilliseconds}ms).");
                SetState(SocketState.Empty);
                SetIndicator(loadingIndicator, false);
                SetIndicator(errorIndicator, true);
                return;
            }

            _products.AddRange(firstPage);
            Log($"fetched {_products.Count} products ({sw.ElapsedMilliseconds}ms).");

            int targetIndex = 0;
            if (!string.IsNullOrEmpty(selectedProductId))
            {
                // If the selected product isn't in the first page, page until we find it
                while (!FurnitureRepository.Instance.IsCategoryFullyLoaded(categoryId) &&
                       !_products.Exists(p => p.ProductId == selectedProductId))
                {
                    var added = await FurnitureRepository.Instance.GetNextPage(categoryId, FurnitureRepository.NextPageSize);
                    if (added != null && added.Count > 0) _products.AddRange(added);
                }

                int found = _products.FindIndex(p => p.ProductId == selectedProductId);
                if (found >= 0) targetIndex = found;
            }

            await ShowProductAt(targetIndex);

            // Page the rest in quietly; the panel's count updates as they arrive.
            _ = PageRemainingProducts(_pagingCts.Token);
        }
        catch (Exception e)
        {
            sw.Stop();
            Debug.LogError($"{Tag} init failed ({sw.ElapsedMilliseconds}ms): {e.Message}");
            SetState(SocketState.Error);
            SetIndicator(loadingIndicator, false);
            SetIndicator(errorIndicator, true);
        }
    }

    // Reuses the repository's paging cursor — one network round trip per page,
    // appended to the shared cache. Cancelled cleanly when the socket dies.
    private async Task PageRemainingProducts(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested &&
               !FurnitureRepository.Instance.IsCategoryFullyLoaded(categoryId))
        {
            List<ProductModel> added;
            try
            {
                added = await FurnitureRepository.Instance
                    .GetNextPage(categoryId, FurnitureRepository.NextPageSize);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"{Tag} paging stopped: {e.Message}");
                return;
            }

            if (ct.IsCancellationRequested) return;

            if (added != null && added.Count > 0)
            {
                _products.AddRange(added);
                Log($"+{added.Count} products paged in (total {_products.Count}).");
                // Keep the open panel's "x / y" counter honest as more arrive.
                if (infoPanel != null && _currentProduct != null)
                    infoPanel.UpdateNavigation(_currentIndex, _products.Count);
            }
            await Task.Yield();
        }
    }

    // ---------------------------------------------------------------
    // MODEL DISPLAY
    // ---------------------------------------------------------------

    private async Task ShowProductAt(int index)
    {
        if (_products.Count == 0) return;

        // Wrap around so Next/Prev loop endlessly through the aisle.
        index = ((index % _products.Count) + _products.Count) % _products.Count;

        // Already showing exactly this product — nothing to do.
        if (index == _currentIndex && _currentModel != null) return;

        var product = _products[index];
        string modelUrl = ResolveModelUrl(product);
        if (string.IsNullOrEmpty(modelUrl))
        {
            Debug.LogWarning($"{Tag} '{product.Name}' has no model URL — skipping.");
            _products.RemoveAt(index);
            if (_products.Count > 0) await ShowProductAt(index);
            else { SetState(SocketState.Empty); SetIndicator(errorIndicator, true); }
            return;
        }

        // Latest-wins: capture this request's token; if a newer request starts
        // while we await the download, we discard our result instead of fighting.
        int token = ++_loadToken;

        SetState(SocketState.LoadingModel);
        SetIndicator(loadingIndicator, true);
        if (infoPanel != null && infoPanel.IsVisible) infoPanel.ShowLoading(product);

        var sw = Stopwatch.StartNew();
        GameObject model;
        try
        {
            // Shared model pipeline: download → disk cache → glTFast. A cache hit
            // (mobile or a prior socket already fetched it) skips the download.
            model = await FurnitureModelLoader.Instance.LoadModel(modelUrl);
        }
        catch (Exception e)
        {
            sw.Stop();
            Debug.LogError($"{Tag} load threw for '{product.Name}' ({sw.ElapsedMilliseconds}ms): {e.Message}");
            model = null;
        }
        sw.Stop();

        // A newer Next/Prev superseded us: bin whatever we just built and bail.
        if (token != _loadToken)
        {
            if (model != null) Destroy(model);
            Log($"discarded stale load of '{product.Name}' (superseded).");
            return;
        }

        if (model == null)
        {
            Debug.LogError($"{Tag} failed to load '{product.Name}' ({sw.ElapsedMilliseconds}ms).");
            SetState(SocketState.Error);
            SetIndicator(loadingIndicator, false);
            SetIndicator(errorIndicator, true);
            if (infoPanel != null && infoPanel.IsVisible)
                infoPanel.ShowMessage(this, $"Couldn't load {product.Name}. Try Refresh.");
            return;
        }

        // Swap old → new only after the new one is safely in hand (no empty gap).
        DestroyCurrentModel();
        _currentModel   = model;
        _currentIndex   = index;
        _currentProduct = product;

#if UNITY_EDITOR
        if (Application.isPlaying)
        {
            UnityEditor.EditorPrefs.SetString($"VRSocket_{SocketId}_SelectedProduct", product.ProductId);
            UnityEditor.EditorPrefs.SetString($"VRSocket_{SocketId}_SelectedModelUrl", ResolveModelUrl(product));
        }
#endif

        PlaceModel(model);

        SetIndicator(loadingIndicator, false);
        SetIndicator(errorIndicator, false);
        SetState(SocketState.Displaying);
        Log($"displaying '{product.Name}' [{index + 1}/{_products.Count}] ({sw.ElapsedMilliseconds}ms).");

        // Refresh the panel if the player is standing here.
        if (infoPanel != null && infoPanel.IsVisible)
            infoPanel.ShowProduct(this, product, _currentIndex, _products.Count);
    }

    // Parent, orient, scale, and (optionally) drop the model onto the socket.
    private void PlaceModel(GameObject model)
    {
        var t = model.transform;
        t.SetParent(modelAnchor, false);
        t.localPosition = Vector3.zero;
        t.localRotation = Quaternion.Euler(0f, yawOffset, 0f);
        t.localScale    = Vector3.one * Mathf.Max(0.0001f, modelScale);

        if (alignToFloor && TryGetWorldBounds(model, out var bounds))
        {
            // Lift/lower so the model's lowest point rests on the anchor origin.
            float lift = modelAnchor.position.y - bounds.min.y;
            t.position += new Vector3(0f, lift, 0f);
        }
    }

    private static bool TryGetWorldBounds(GameObject go, out Bounds bounds)
    {
        var renderers = go.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) { bounds = default; return false; }
        bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        return true;
    }

    /// <summary>
    /// Model URL for a product: the primary variant's own GLB (per-colour clean
    /// pipeline), falling back to the product-level model key.
    /// </summary>
    private static string ResolveModelUrl(ProductModel p)
    {
        string primary = p.PrimaryModelUrl;
        return !string.IsNullOrEmpty(primary) ? primary : p.S3ModelUrl;
    }

    private void DestroyCurrentModel()
    {
        if (_currentModel != null)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                DestroyImmediate(_currentModel);
            }
            else
            {
                Destroy(_currentModel);
            }
#else
            Destroy(_currentModel);
#endif
            _currentModel = null;
        }
    }

    // ---------------------------------------------------------------
    // HELPERS
    // ---------------------------------------------------------------

    private string EmptyMessage() =>
        _state == SocketState.Error
            ? "Couldn't load this display. Try Refresh."
            : $"No products available in {DisplayLabel}.";

    private void SetState(SocketState s)
    {
        if (_state == s) return;
        _state = s;
        OnStateChanged?.Invoke(s);
    }

    private static void SetIndicator(GameObject go, bool on)
    {
        if (go != null && go.activeSelf != on) go.SetActive(on);
    }

    private void Log(string msg)
    {
        if (verboseLogging) Debug.Log($"{Tag} {msg}");
    }

    // ---------------------------------------------------------------
    // EDITOR GIZMO — makes empty sockets easy to spot & label in Scene view
    // ---------------------------------------------------------------

    void OnDrawGizmos()
    {
        bool hasModel = _currentModel != null;
        Gizmos.color = hasModel
            ? new Color(0.2f, 0.8f, 0.2f, 0.25f)
            : new Color(0.9f, 0.75f, 0.2f, 0.25f);
        Vector3 centre = (modelAnchor != null ? modelAnchor.position : transform.position)
                         + Vector3.up * 0.5f;
        Gizmos.DrawWireCube(centre, Vector3.one);

#if UNITY_EDITOR
        string label = !string.IsNullOrEmpty(displayLabel) ? displayLabel
                     : !string.IsNullOrEmpty(categoryId)   ? categoryId
                     : "⚠ No subcategory";
        UnityEditor.Handles.Label(
            centre + Vector3.up * 0.8f, $"🔌 {label}",
            new GUIStyle
            {
                fontSize  = 12,
                normal    = { textColor = Color.white },
                alignment = TextAnchor.MiddleCenter
            });
#endif
    }
}

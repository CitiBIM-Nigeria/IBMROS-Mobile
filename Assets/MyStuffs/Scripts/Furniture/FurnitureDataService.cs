using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Event-driven service layer for furniture catalog data.
/// Wraps FurnitureRepository exactly as FurnitureService wraps FurnitureModelLoader.
/// UI controllers subscribe to events here — never call FurnitureRepository directly.
/// </summary>
public class FurnitureDataService : MonoBehaviour
{
    public static FurnitureDataService Instance { get; private set; }

    // ---------------------------------------------------------------
    // EVENTS
    // ---------------------------------------------------------------

    public static event Action<List<CategoryModel>>        OnCategoriesLoaded;
    public static event Action<string>                     OnCategoriesFailed;

    public static event Action<List<ProductModel>, string> OnProductsLoaded;
    public static event Action<string>                     OnProductsFailed;

    // Additional products paged in AFTER the first page (background fill-in).
    // string = categoryId (the subcategory id the user tapped).
    public static event Action<List<ProductModel>, string> OnProductsAppended;

    public static event Action<ProductModel>               OnProductLoaded;
    public static event Action<string>                     OnProductFailed;

    public static event Action<List<ProductModel>>         OnSearchResultsLoaded;
    public static event Action<string>                     OnSearchFailed;

    // bool = isLoading, string = message
    public static event Action<bool, string>               OnLoadingChanged;

    // ---------------------------------------------------------------
    // LIFECYCLE
    // ---------------------------------------------------------------

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    // ---------------------------------------------------------------
    // CATEGORIES
    // ---------------------------------------------------------------

    private Task               _categoriesTask;     // in-flight load (dedup)
    private List<CategoryModel> _categoriesCache;    // last successful result

    public Task LoadCategories()
    {
        // Already loaded this session → serve instantly, no network call.
        if (_categoriesCache != null && _categoriesCache.Count > 0)
        {
            OnCategoriesLoaded?.Invoke(_categoriesCache);
            return Task.CompletedTask;
        }
        // A load is already running (e.g. the room-entry preload) → reuse it
        // instead of starting a second fetch (the bug the logs showed).
        if (_categoriesTask != null && !_categoriesTask.IsCompleted)
            return _categoriesTask;

        _categoriesTask = LoadCategoriesInternal();
        return _categoriesTask;
    }

    private async Task LoadCategoriesInternal()
    {
        if (!IsReady()) return;

        OnLoadingChanged?.Invoke(true, "Loading categories...");
        var categories = await FurnitureRepository.Instance.GetCategories();
        OnLoadingChanged?.Invoke(false, string.Empty);

        if (categories == null || categories.Count == 0)
        {
            OnCategoriesFailed?.Invoke("No categories available. Please try again.");
            return;
        }

        _categoriesCache = categories;
        Debug.Log($"[FurnitureDataService] Categories loaded: {categories.Count}");
        OnCategoriesLoaded?.Invoke(categories);

        // NOTE: we deliberately DON'T warm the whole catalogue here. The furniture
        // panel prefetches only the room the user opens (cancellable, cancelled on
        // the user's next tap), and every other category loads on demand with
        // in-flight de-duplication. A full up-front sweep just competed with user
        // requests for the AWS connection — the second burst of categories in the
        // logcat — and delayed the tapped item.
    }

    // ---------------------------------------------------------------
    // PRODUCTS BY CATEGORY  (paged: fast first page, then background fill-in)
    // ---------------------------------------------------------------

    // The category whose products are currently on screen. Background paging for
    // any other category is cancelled, so a user tap always wins the connection.
    private string _activeCategory;
    private System.Threading.CancellationTokenSource _pagingCts;

    // User-initiated load. Shows the first page as fast as possible (instant if
    // the panel prefetch already warmed it), then pages in the rest in the
    // background, appending as each page arrives. Switching categories cancels
    // the previous background paging.
    public async Task LoadProductsByCategory(string categoryId)
    {
        if (!IsReady()) return;

        if (string.IsNullOrEmpty(categoryId))
        {
            OnProductsFailed?.Invoke("Invalid category.");
            return;
        }

        // Cancel any other category's background paging — this load wins.
        _pagingCts?.Cancel();
        _pagingCts = new System.Threading.CancellationTokenSource();
        var ct = _pagingCts.Token;
        _activeCategory = categoryId;

        // First page already cached (maybe by the panel prefetch) → show instantly.
        if (FurnitureRepository.Instance.IsCategoryCached(categoryId))
        {
            var cached = FurnitureRepository.Instance.GetCachedCategoryItems(categoryId);
            Debug.Log($"[CatLoad] {categoryId} | first page cache HIT ({cached.Count})");
            OnProductsLoaded?.Invoke(cached, categoryId);
        }
        else
        {
            Debug.Log($"[CatLoad] {categoryId} | first page cache MISS → fetching");
            OnLoadingChanged?.Invoke(true, "Loading products...");

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var firstPage = await FurnitureRepository.Instance
                .GetFirstPage(categoryId, FurnitureRepository.PageSize);
            sw.Stop();

            OnLoadingChanged?.Invoke(false, string.Empty);

            if (ct.IsCancellationRequested) return;          // user moved to another category
            if (firstPage == null || firstPage.Count == 0)
            {
                Debug.LogWarning($"[CatLoad] {categoryId} | no products | {sw.ElapsedMilliseconds} ms");
                OnProductsFailed?.Invoke("No products found in this category.");
                return;
            }

            Debug.Log($"[CatLoad] {categoryId} | first page {firstPage.Count} | {sw.ElapsedMilliseconds} ms");
            OnProductsLoaded?.Invoke(firstPage, categoryId);
        }

        // Page in the rest in the background, appending as each page arrives.
        // Auto-cancelled when the user loads a different category.
        _ = PageRemainingProducts(categoryId, ct);
    }

    private async Task PageRemainingProducts(string categoryId, System.Threading.CancellationToken ct)
    {
        while (!ct.IsCancellationRequested
               && !FurnitureRepository.Instance.IsCategoryFullyLoaded(categoryId))
        {
            if (_activeCategory != categoryId) return;       // user moved on

            List<ProductModel> added;
            try
            {
                added = await FurnitureRepository.Instance
                    .GetNextPage(categoryId, FurnitureRepository.NextPageSize);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[CatLoad] {categoryId} | paging error: {e.Message}");
                return;
            }

            if (ct.IsCancellationRequested) return;
            if (_activeCategory != categoryId) return;
            if (added != null && added.Count > 0)
            {
                Debug.Log($"[CatLoad] {categoryId} | +{added.Count} appended");
                OnProductsAppended?.Invoke(added, categoryId);
            }
            await Task.Yield();
        }

        if (_activeCategory == categoryId && !ct.IsCancellationRequested)
            Debug.Log($"[CatLoad] {categoryId} | fully loaded");
    }

    // ---------------------------------------------------------------
    // PRODUCTS BY SUBCATEGORY
    // ---------------------------------------------------------------

    public async Task LoadProductsBySubcategory(
        string categoryId, string subcategoryId)
    {
        if (!IsReady()) return;

        if (string.IsNullOrEmpty(categoryId) ||
            string.IsNullOrEmpty(subcategoryId))
        {
            OnProductsFailed?.Invoke("Invalid category or subcategory.");
            return;
        }

        OnLoadingChanged?.Invoke(true, "Loading products...");

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var products = await FurnitureRepository.Instance
            .GetProductsBySubcategory(categoryId, subcategoryId);
        sw.Stop();

        OnLoadingChanged?.Invoke(false, string.Empty);

        if (products == null || products.Count == 0)
        {
            Debug.LogWarning($"[CatLoad] {categoryId}_{subcategoryId} | no products | {sw.ElapsedMilliseconds} ms");
            OnProductsFailed?.Invoke("No products found in this subcategory.");
            return;
        }

        Debug.Log($"[CatLoad] {categoryId}_{subcategoryId} | repository returned {products.Count} products | {sw.ElapsedMilliseconds} ms");
        OnProductsLoaded?.Invoke(products, subcategoryId);
    }

    // ---------------------------------------------------------------
    // SINGLE PRODUCT
    // ---------------------------------------------------------------

    public async Task LoadProduct(string productId)
    {
        if (!IsReady()) return;

        if (string.IsNullOrEmpty(productId))
        {
            OnProductFailed?.Invoke("Invalid product ID.");
            return;
        }

        OnLoadingChanged?.Invoke(true, "Loading product...");

        var product = await FurnitureRepository.Instance.GetProduct(productId);

        OnLoadingChanged?.Invoke(false, string.Empty);

        if (product == null)
        {
            OnProductFailed?.Invoke("Product not found.");
            return;
        }

        OnProductLoaded?.Invoke(product);
    }

    // ---------------------------------------------------------------
    // SEARCH
    // ---------------------------------------------------------------

    public async Task Search(string searchTerm)
    {
        if (!IsReady()) return;

        if (string.IsNullOrEmpty(searchTerm) || searchTerm.Trim().Length < 2)
        {
            OnSearchFailed?.Invoke("Please enter at least 2 characters.");
            return;
        }

        OnLoadingChanged?.Invoke(true, $"Searching for \"{searchTerm}\"...");

        var results = await FurnitureRepository.Instance
            .SearchProducts(searchTerm.Trim());

        OnLoadingChanged?.Invoke(false, string.Empty);

        if (results == null || results.Count == 0)
        {
            OnSearchFailed?.Invoke($"No results found for \"{searchTerm}\".");
            return;
        }

        Debug.Log($"[FurnitureDataService] Search returned {results.Count} results.");
        OnSearchResultsLoaded?.Invoke(results);
    }

    // ---------------------------------------------------------------
    // HELPER
    // ---------------------------------------------------------------

    private bool IsReady()
    {
        if (FurnitureRepository.Instance == null)
        {
            Debug.LogError("[FurnitureDataService] FurnitureRepository not in scene.");
            return false;
        }

        if (AwsManager.Instance == null || !AwsManager.Instance.IsInitialized)
        {
            Debug.LogWarning("[FurnitureDataService] AWS not ready yet.");
            OnLoadingChanged?.Invoke(false, string.Empty);
            return false;
        }

        return true;
    }
}
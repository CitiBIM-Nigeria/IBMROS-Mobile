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

        // Warm every category's products in the background so navigating is
        // instant (the room-entry preload now warms the WHOLE catalog).
        PreloadProducts(categories);
    }

    // ---------------------------------------------------------------
    // PRODUCTS BY CATEGORY
    // ---------------------------------------------------------------

    private readonly Dictionary<string, List<ProductModel>> _productsCache = new();

    public async Task LoadProductsByCategory(string categoryId)
    {
        if (!IsReady()) return;

        if (string.IsNullOrEmpty(categoryId))
        {
            OnProductsFailed?.Invoke("Invalid category.");
            return;
        }

        // Served this session already → instant, no network call.
        if (_productsCache.TryGetValue(categoryId, out var hit))
        {
            OnProductsLoaded?.Invoke(hit, categoryId);
            return;
        }

        OnLoadingChanged?.Invoke(true, "Loading products...");

        var products = await FurnitureRepository.Instance
            .GetProductsByCategory(categoryId);

        OnLoadingChanged?.Invoke(false, string.Empty);

        if (products == null || products.Count == 0)
        {
            OnProductsFailed?.Invoke("No products found in this category.");
            return;
        }

        _productsCache[categoryId] = products;
        OnProductsLoaded?.Invoke(products, categoryId);
    }

    // Background pre-fetch: warm the product cache for every furniture type so
    // opening any category later is instant. Called after categories load (room
    // entry). Fire-and-forget; failures are harmless (it'll load on demand).
    public async void PreloadProducts(List<CategoryModel> categories)
    {
        if (categories == null) return;
        foreach (var room in categories)
        {
            if (room.Subcategories == null) continue;
            foreach (var type in room.Subcategories)
            {
                if (_productsCache.ContainsKey(type.SubcategoryId)) continue;
                try
                {
                    var products = await FurnitureRepository.Instance
                        .GetProductsByCategory(type.SubcategoryId);
                    if (products != null && products.Count > 0)
                        _productsCache[type.SubcategoryId] = products;
                }
                catch { /* on-demand load will retry */ }
            }
        }
        Debug.Log("[FurnitureDataService] Product preload complete.");
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

        var products = await FurnitureRepository.Instance
            .GetProductsBySubcategory(categoryId, subcategoryId);

        OnLoadingChanged?.Invoke(false, string.Empty);

        if (products == null || products.Count == 0)
        {
            Debug.LogWarning($"[FurnitureDataService] No products for {subcategoryId}.");
            OnProductsFailed?.Invoke("No products found in this subcategory.");
            return;
        }

        Debug.Log($"[FurnitureDataService] Products loaded: {products.Count}");
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
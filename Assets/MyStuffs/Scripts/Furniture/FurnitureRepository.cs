using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Amazon.DynamoDBv2.Model;
using Amazon.Runtime;
using UnityEngine;

/// <summary>
/// Direct DynamoDB access layer for the furniture catalog.
/// Only this class talks to DynamoDB — nothing else should.
///
/// Backend (set in AwsConfig): the ros-products / ros-categories generation
/// written by ikea_pipeline.py.
///   • ros-categories — category tree (departments=0, categories=1, breadcrumb
///     subcategories=2). Names + icons come straight from here.
///   • ros-products   — composite PK (merchant_id + product_id); products are
///     indexed for browse on the `category-index` GSI keyed on
///     merchant_category = "ikea#{category_id}".
/// </summary>
public class FurnitureRepository : MonoBehaviour
{
    public static FurnitureRepository Instance { get; private set; }

    // In-memory cache — cleared when app restarts
    private readonly Dictionary<string, List<ProductModel>> _productCache = new();

    private static string Merchant => AwsConfig.FurnitureCatalogMerchantId;

    // DynamoDB reserves common words (e.g. "name"), so every attribute used in a
    // ProjectionExpression / FilterExpression goes through an #placeholder. These
    // shared maps keep the projections consistent (and reserved-word-safe) across
    // queries. Pass a COPY to each request so the static maps are never mutated.
    private static readonly Dictionary<string, string> ProductAttrNames = new()
    {
        { "#pid",  "product_id" },
        { "#mid",  "merchant_id" },
        { "#nm",   "name" },
        { "#tn",   "type_name" },
        { "#cid",  "category_id" },
        { "#sid",  "subcategory_id" },
        { "#mesh", "mesh_glb_url" },
        { "#pmin", "price_min" },
        { "#pmax", "price_max" },
        { "#star", "star_rating" },
        { "#rev",  "review_count" },
        { "#hid",  "hidden" },
        { "#var",  "variants" },
    };
    private const string ProductProjection =
        "#pid, #mid, #nm, #tn, #cid, #sid, #mesh, #pmin, #pmax, #star, #rev, #hid, #var";

    private static readonly Dictionary<string, string> CategoryAttrNames = new()
    {
        { "#cid", "category_id" },
        { "#nm",  "name" },
        { "#ico", "icon" },
        { "#pid", "parent_category_id" },
        { "#lvl", "level" },
        { "#ord", "display_order" },
        { "#lt",  "live_types" },
    };
    private const string CategoryProjection = "#cid, #nm, #ico, #pid, #lvl, #ord, #lt";


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
    // CATEGORIES  (read straight from the ros-categories tree)
    // Top-level cards = level-1 categories; each carries its level-2
    // breadcrumb subcategories. Departments (level 0) are not surfaced
    // by the current 2-tier UI.
    // ---------------------------------------------------------------

    public async Task<List<CategoryModel>> GetCategories()
    {
        try
        {
            await AwsManager.Instance.RefreshCredentialsIfNeeded();

            var request = new QueryRequest
            {
                TableName              = AwsConfig.CategoriesTableName,
                KeyConditionExpression = "merchant_id = :mid",
                ProjectionExpression   = CategoryProjection,
                ExpressionAttributeNames = new Dictionary<string, string>(CategoryAttrNames),
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    { ":mid", new AttributeValue { S = Merchant } }
                }
            };

            var response = await DbWithRetry(
                () => AwsManager.Instance.DynamoDBClient.QueryAsync(request), "categories");

            // Parse every node once. The pipeline writes a special
            // "__live_types__" item listing the categories that have models, so
            // we get that set FOR FREE here — no separate full-table scan.
            var nodes = new List<CatNode>();
            HashSet<string> live = null;
            foreach (var item in response.Items)
            {
                string id = GetString(item, "category_id");
                if (string.IsNullOrEmpty(id)) continue;

                if (id == "__live_types__")
                {
                    if (item.TryGetValue("live_types", out var lt) && lt.SS != null)
                        live = new HashSet<string>(lt.SS);
                    continue;   // not a real tree node
                }

                nodes.Add(new CatNode
                {
                    Id     = id,
                    Name   = GetString(item, "name"),
                    Icon   = GetString(item, "icon"),
                    Parent = GetString(item, "parent_category_id"),
                    Level  = (int)GetNumber(item, "level"),
                    Order  = (int)GetNumber(item, "display_order"),
                });
            }

            var childrenByParent = nodes
                .GroupBy(n => n.Parent)
                .ToDictionary(g => g.Key, g => g.OrderBy(n => n.Order).ToList());

            // Use the live set from the query; only fall back to the (cached)
            // full-table scan for older data that lacks the __live_types__ item.
            if (live == null)
                live = await GetCategoryIdsWithModels();

            // Top level = ROOMS (level 0). Their level-1 children (furniture
            // types like Sofas/Beds) become the second tier; tapping one loads
            // products by that type's id (= the product category_id). Empty
            // types — and rooms left with none — are hidden.
            var rooms = nodes
                .Where(n => n.Level == 0)
                .OrderBy(n => n.Order)
                .Select(n => new CategoryModel
                {
                    CategoryId   = n.Id,
                    CategoryName = n.Name,
                    Icon         = n.Icon,
                    ParentId     = n.Parent,
                    MerchantId   = Merchant,
                    Subcategories = (childrenByParent.TryGetValue(n.Id, out var kids)
                        ? kids
                        : new List<CatNode>())
                        .Where(c => live.Contains(c.Id))
                        .OrderBy(c => c.Order)
                        .Select(c => new SubcategoryModel
                        {
                            SubcategoryId   = c.Id,   // = product category_id
                            SubcategoryName = c.Name,
                            Icon            = c.Icon,
                        })
                        .ToList()
                })
                .Where(room => room.Subcategories.Count > 0)
                .ToList();

            Debug.Log($"[FurnitureRepository] Fetched {rooms.Count} rooms.");
            return rooms;
        }
        catch (Exception e)
        {
            Debug.LogError($"[FurnitureRepository] GetCategories error: {e.Message}");
            return new List<CategoryModel>();
        }
    }

    private struct CatNode
    {
        public string Id, Name, Icon, Parent;
        public int    Level, Order;
    }

    private const string LiveCatPrefsKey = "ros_live_categories_v1";

    // Set of category_ids that have at least one product with a 3D model.
    // CACHED: served instantly from PlayerPrefs (the full-table scan is the slow
    // part of opening the catalog), then refreshed in the background so the next
    // launch is current. Categories barely change, so the cache is near-always right.
    private async Task<HashSet<string>> GetCategoryIdsWithModels()
    {
        string cached = PlayerPrefs.GetString(LiveCatPrefsKey, "");
        if (!string.IsNullOrEmpty(cached))
        {
            _ = RefreshLiveCategories();                 // fire-and-forget refresh
            return new HashSet<string>(cached.Split(','));
        }
        var fresh = await ScanCategoryIdsWithModels();   // first run: must scan once
        if (fresh.Count > 0)
            PlayerPrefs.SetString(LiveCatPrefsKey, string.Join(",", fresh));
        return fresh;
    }

    private async Task RefreshLiveCategories()
    {
        var live = await ScanCategoryIdsWithModels();
        if (live.Count > 0)
            PlayerPrefs.SetString(LiveCatPrefsKey, string.Join(",", live));
    }

    private async Task<HashSet<string>> ScanCategoryIdsWithModels()
    {
        var live = new HashSet<string>();
        try
        {
            Dictionary<string, AttributeValue> startKey = null;
            do
            {
                var req = new ScanRequest
                {
                    TableName            = AwsConfig.FurnitureCatalogTableName,
                    ProjectionExpression = "category_id, mesh_glb_url",
                };
                if (startKey != null) req.ExclusiveStartKey = startKey;

                var resp = await DbWithRetry(
                    () => AwsManager.Instance.DynamoDBClient.ScanAsync(req), "live-categories-scan");
                foreach (var it in resp.Items)
                {
                    string cat  = it.TryGetValue("category_id", out var c) ? c.S : null;
                    string mesh = it.TryGetValue("mesh_glb_url", out var m) ? m.S : null;
                    if (!string.IsNullOrEmpty(cat) && !string.IsNullOrEmpty(mesh))
                        live.Add(cat);
                }
                startKey = (resp.LastEvaluatedKey != null && resp.LastEvaluatedKey.Count > 0)
                    ? resp.LastEvaluatedKey : null;
            }
            while (startKey != null);
        }
        catch (Exception e)
        {
            Debug.LogError($"[FurnitureRepository] GetCategoryIdsWithModels error: {e.Message}");
        }
        return live;
    }

    // ---------------------------------------------------------------
    // PRODUCTS BY CATEGORY  (category-index, merchant_category leaf key)
    // PAGED: a fast first page renders immediately, the rest is paged in by
    // FurnitureDataService in the background. One DynamoDB call per page,
    // ProjectionExpression trims the payload, FilterExpression drops no-model
    // items server-side, and DbWithRetry logs every attempt.
    // ---------------------------------------------------------------

    // How many raw items to read per page. The with-model count per page is
    // lower (FilterExpression is applied after the Limit is read). Tunable.
    // First page is small for fast first paint; later pages are large so the
    // background fill-in needs fewer round trips over the high-RTT link.
    public const int PageSize = 20;        // first page
    public const int NextPageSize = 100;   // subsequent pages

    // One page of results + the cursor for the next page (null = fully loaded).
    public class ProductPage
    {
        public List<ProductModel> Items = new();
        public Dictionary<string, AttributeValue> LastEvaluatedKey;
        public bool HasMore => LastEvaluatedKey != null && LastEvaluatedKey.Count > 0;
    }

    // Accumulated products for a category + its pagination cursor. Grows page by
    // page until NextKey is null (fully loaded).
    private class CategoryCache
    {
        public List<ProductModel> Items = new();
        public Dictionary<string, AttributeValue> NextKey;
        public bool FullyLoaded => NextKey == null;
    }

    private readonly Dictionary<string, CategoryCache> _categoryCache = new();

    // In-flight FIRST-page fetches, keyed by category. When the panel prefetch
    // and the user's tap both ask for "sofas" at once, they share ONE query.
    private readonly Dictionary<string, Task<List<ProductModel>>> _inFlightFirstPage = new();

    // True once at least the first page is cached this session.
    public bool IsCategoryCached(string categoryId)
        => _categoryCache.TryGetValue(categoryId, out var c) && c.Items.Count > 0;

    // True once every page has been fetched.
    public bool IsCategoryFullyLoaded(string categoryId)
        => _categoryCache.TryGetValue(categoryId, out var c) && c.FullyLoaded;

    // All products fetched so far for a category (first page only, or the full
    // set once background paging has caught up). null if never fetched. Returns
    // a COPY so callers can never mutate the internal cache list.
    public List<ProductModel> GetCachedCategoryItems(string categoryId)
        => _categoryCache.TryGetValue(categoryId, out var c) ? new List<ProductModel>(c.Items) : null;

    // Fetch (or reuse) the FIRST page. Dedup'd across concurrent callers. Caches
    // the page. Returns all cached items for the category (the first page).
    public Task<List<ProductModel>> GetFirstPage(string categoryId, int limit)
    {
        if (_categoryCache.TryGetValue(categoryId, out var c) && c.Items.Count > 0)
        {
            Debug.Log($"[FurnRepo] {categoryId} | cache HIT first page ({c.Items.Count})");
            return Task.FromResult(c.Items);
        }

        if (_inFlightFirstPage.TryGetValue(categoryId, out var pending)
            && !pending.IsFaulted && !pending.IsCanceled)
        {
            Debug.Log($"[FurnRepo] {categoryId} | in-flight reuse (first page)");
            return pending;
        }

        Debug.Log($"[FurnRepo] {categoryId} | cache MISS → fetching first page");
        var task = FetchFirstPage(categoryId, limit);
        _inFlightFirstPage[categoryId] = task;
        return task;
    }

    private async Task<List<ProductModel>> FetchFirstPage(string categoryId, int limit)
    {
        try
        {
            var page = await QueryCategoryPage(categoryId, limit, null);
            _categoryCache[categoryId] = new CategoryCache
            {
                Items   = page.Items,
                NextKey = page.LastEvaluatedKey
            };
            Debug.Log($"[FurnRepo] {categoryId} | first page cached ({page.Items.Count} items, " +
                      $"{(page.HasMore ? "more available" : "fully loaded")})");
            return new List<ProductModel>(_categoryCache[categoryId].Items);
        }
        catch (Exception e)
        {
            Debug.LogError($"[FurnRepo] {categoryId} | first page error: {e.Message}");
            return new List<ProductModel>();
        }
        finally
        {
            _inFlightFirstPage.Remove(categoryId);
        }
    }

    // Fetch the NEXT page and append it to the cache. Returns only the newly
    // added items (empty if already fully loaded). Owned by the DataService
    // background loop — not dedup'd (the loop is single-threaded per category).
    public async Task<List<ProductModel>> GetNextPage(string categoryId, int limit)
    {
        if (!_categoryCache.TryGetValue(categoryId, out var c))
            return new List<ProductModel>();
        if (c.FullyLoaded)
            return new List<ProductModel>();

        try
        {
            var page = await QueryCategoryPage(categoryId, limit, c.NextKey);
            c.Items.AddRange(page.Items);
            c.NextKey = page.LastEvaluatedKey;
            Debug.Log($"[FurnRepo] {categoryId} | next page +{page.Items.Count} (total {c.Items.Count}, " +
                      $"{(c.FullyLoaded ? "fully loaded" : "more available")})");
            return page.Items;
        }
        catch (Exception e)
        {
            Debug.LogError($"[FurnRepo] {categoryId} | next page error: {e.Message}");
            return new List<ProductModel>();
        }
    }

    // One DynamoDB Query for one page. ProjectionExpression trims the payload to
    // only what ParseProduct reads; FilterExpression drops no-model items
    // server-side so they're never transferred.
    private async Task<ProductPage> QueryCategoryPage(
        string categoryId, int limit, Dictionary<string, AttributeValue> startKey)
    {
        bool refreshed = await AwsManager.Instance.RefreshCredentialsIfNeeded();
        Debug.Log($"[FurnRepo] {categoryId} | credentials: {(refreshed ? "REFRESHED" : "fresh")}");

        var request = new QueryRequest
        {
            TableName              = AwsConfig.FurnitureCatalogTableName,
            IndexName              = "category-index",
            KeyConditionExpression = "merchant_category = :mc",
            FilterExpression       = "attribute_exists(#mesh)",
            ProjectionExpression   = ProductProjection,
            ExpressionAttributeNames = new Dictionary<string, string>(ProductAttrNames),
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                { ":mc", new AttributeValue { S = $"{Merchant}#{categoryId}" } }
            },
            Limit = limit
        };
        if (startKey != null) request.ExclusiveStartKey = startKey;

        var ctx = $"{categoryId} | page {(startKey == null ? "1" : "next")}";
        var response = await DbWithRetry(
            () => AwsManager.Instance.DynamoDBClient.QueryAsync(request), ctx);

        var items = response.Items
            .Select(ParseProduct)
            .Where(p => p != null && p.HasModel)   // belt-and-suspenders; filter already applied
            .ToList();

        var lek = (response.LastEvaluatedKey != null && response.LastEvaluatedKey.Count > 0)
            ? response.LastEvaluatedKey : null;

        return new ProductPage { Items = items, LastEvaluatedKey = lek };
    }

    // ---------------------------------------------------------------
    // PRODUCTS BY SUBCATEGORY  (category-index + subcategory_id filter)
    // ---------------------------------------------------------------

    private readonly Dictionary<string, Task<List<ProductModel>>> _inFlightBySubcategory = new();

    public Task<List<ProductModel>> GetProductsBySubcategory(
        string categoryId, string subcategoryId)
    {
        string cacheKey = $"{categoryId}_{subcategoryId}";

        if (_productCache.TryGetValue(cacheKey, out var cached))
        {
            Debug.Log($"[FurnRepo] {cacheKey} | cache HIT ({cached.Count} products)");
            return Task.FromResult(cached);
        }

        if (_inFlightBySubcategory.TryGetValue(cacheKey, out var pending))
        {
            if (!pending.IsFaulted && !pending.IsCanceled)
            {
                Debug.Log($"[FurnRepo] {cacheKey} | in-flight reuse");
                return pending;
            }
            _inFlightBySubcategory.Remove(cacheKey);
        }

        Debug.Log($"[FurnRepo] {cacheKey} | cache MISS → fetching");
        var task = FetchProductsBySubcategory(categoryId, subcategoryId, cacheKey);
        _inFlightBySubcategory[cacheKey] = task;
        return task;
    }

    private async Task<List<ProductModel>> FetchProductsBySubcategory(
        string categoryId, string subcategoryId, string cacheKey)
    {
        try
        {
            bool refreshed = await AwsManager.Instance.RefreshCredentialsIfNeeded();
            Debug.Log($"[FurnRepo] {cacheKey} | credentials: {(refreshed ? "REFRESHED" : "fresh")}");

            var request = new QueryRequest
            {
                TableName              = AwsConfig.FurnitureCatalogTableName,
                IndexName              = "category-index",
                KeyConditionExpression = "merchant_category = :mc",
                FilterExpression       = "attribute_exists(#mesh) AND #sid = :subId",
                ProjectionExpression   = ProductProjection,
                ExpressionAttributeNames = new Dictionary<string, string>(ProductAttrNames),
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    { ":mc",    new AttributeValue { S = $"{Merchant}#{categoryId}" } },
                    { ":subId", new AttributeValue { S = subcategoryId } }
                }
            };

            var response = await DbWithRetry(
                () => AwsManager.Instance.DynamoDBClient.QueryAsync(request), cacheKey);

            var products = response.Items
                .Select(ParseProduct)
                .Where(p => p != null && p.HasModel)
                .ToList();

            _productCache[cacheKey] = products;
            Debug.Log($"[FurnRepo] {cacheKey} | {response.Items.Count} raw → {products.Count} with-model");
            return products;
        }
        catch (Exception e)
        {
            Debug.LogError($"[FurnRepo] {cacheKey} | GetProductsBySubcategory error: {e.Message}");
            return new List<ProductModel>();
        }
        finally
        {
            _inFlightBySubcategory.Remove(cacheKey);
        }
    }

    // ---------------------------------------------------------------
    // SINGLE PRODUCT  (composite key)
    // ---------------------------------------------------------------

    public async Task<ProductModel> GetProduct(string productId)
    {
        try
        {
            var request = new GetItemRequest
            {
                TableName = AwsConfig.FurnitureCatalogTableName,
                ProjectionExpression = ProductProjection,
                ExpressionAttributeNames = new Dictionary<string, string>(ProductAttrNames),
                Key = new Dictionary<string, AttributeValue>
                {
                    { "merchant_id", new AttributeValue { S = Merchant } },
                    { "product_id",  new AttributeValue { S = productId } }
                }
            };

            var response = await DbWithRetry(
                () => AwsManager.Instance.DynamoDBClient.GetItemAsync(request), $"product:{productId}");

            if (!response.IsItemSet)
            {
                Debug.LogWarning($"[FurnitureRepository] Product {productId} not found.");
                return null;
            }

            return ParseProduct(response.Item);
        }
        catch (Exception e)
        {
            Debug.LogError($"[FurnitureRepository] GetProduct error: {e.Message}");
            return null;
        }
    }

    // ---------------------------------------------------------------
    // SEARCH  (table scan, case-sensitive contains on name)
    // ---------------------------------------------------------------

    public async Task<List<ProductModel>> SearchProducts(string searchTerm)
    {
        try
        {
            var request = new ScanRequest
            {
                TableName        = AwsConfig.FurnitureCatalogTableName,
                FilterExpression = "attribute_exists(#mesh) AND contains(#nm, :term)",
                ProjectionExpression = ProductProjection,
                ExpressionAttributeNames = new Dictionary<string, string>(ProductAttrNames),
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    { ":term", new AttributeValue { S = searchTerm } }
                }
            };

            var response = await DbWithRetry(
                () => AwsManager.Instance.DynamoDBClient.ScanAsync(request), $"search:{searchTerm}");

            var products = response.Items
                .Select(ParseProduct)
                .Where(p => p != null && p.HasModel)   // only placeable products
                .ToList();

            Debug.Log($"[FurnitureRepository] Search '{searchTerm}' " +
                      $"returned {products.Count} results.");
            return products;
        }
        catch (Exception e)
        {
            Debug.LogError($"[FurnitureRepository] SearchProducts error: {e.Message}");
            return new List<ProductModel>();
        }
    }

    // ---------------------------------------------------------------
    // PARSER
    // ---------------------------------------------------------------

    private ProductModel ParseProduct(Dictionary<string, AttributeValue> item)
    {
        try
        {
            // Boss-review prune: hidden products stay in the DB but never show.
            if (item.TryGetValue("hidden", out var h) && h.BOOL == true)
                return null;

            var variants = ParseVariants(item);
            var primary  = variants.Find(v => v.IsPrimary)
                           ?? (variants.Count > 0 ? variants[0] : null);

            // UI variant line: "type_name - primary colour" (matches the format
            // ItemDetailSheetController expects).
            string type   = GetString(item, "type_name");
            string colour = primary?.ColourName ?? string.Empty;
            string desc =
                !string.IsNullOrEmpty(type) && !string.IsNullOrEmpty(colour)
                    ? $"{type} - {colour}"
                    : !string.IsNullOrEmpty(type) ? type : colour;

            return new ProductModel
            {
                ProductId        = GetString(item, "product_id"),
                MerchantId       = GetString(item, "merchant_id"),
                Name             = GetString(item, "name"),
                Description      = desc,
                CategoryId       = GetString(item, "category_id"),
                SubcategoryId    = GetString(item, "subcategory_id"),
                S3ModelUrl       = StripCloudFront(GetString(item, "mesh_glb_url")),
                PriceMin         = GetNumber(item, "price_min"),
                PriceMax         = GetNumber(item, "price_max"),
                StarRatingValue  = GetNumber(item, "star_rating"),
                ReviewCountValue = (int)GetNumber(item, "review_count"),
                Variants         = variants,
            };
        }
        catch (Exception e)
        {
            Debug.LogError($"[FurnitureRepository] ParseProduct error: {e.Message}");
            return null;
        }
    }

    private List<ProductVariant> ParseVariants(Dictionary<string, AttributeValue> item)
    {
        var list = new List<ProductVariant>();
        if (!item.TryGetValue("variants", out var attr) || attr.L == null)
            return list;

        foreach (var entry in attr.L)
        {
            var m = entry.M;
            if (m == null) continue;
            list.Add(new ProductVariant
            {
                VariantId          = GetString(m, "variant_id"),
                ColourName         = GetString(m, "colour_name"),
                IsPrimary          = m.TryGetValue("is_primary", out var p) && p.BOOL == true,
                Price              = GetNumber(m, "price"),
                ModelUrl           = StripCloudFront(GetString(m, "model_url")),
                SwatchUrl          = GetString(m, "swatch_url"),
                DominantColor      = GetString(m, "dominant_color"),
                DisplayUrl         = GetString(m, "display_url"),
                DisplayThumbUrl    = GetString(m, "display_thumb_url"),
                DisplayOriginalUrl = GetString(m, "display_original_url"),
            });
        }
        return list;
    }

    // ---------------------------------------------------------------
    // HELPERS
    // ---------------------------------------------------------------

    // Logs one DynamoDB call's elapsed time, HTTP status, and payload size. The
    // AWS SDK retries internally (default Standard mode); this wrapper does NOT
    // add its own retries on top, so the total elapsed time reveals whether the
    // SDK retried (a multi-second call on a small response = it did). Explicit
    // per-attempt logging would require disabling the SDK retries via the v4
    // MaxAttempts API — held off until the exact property name is confirmed.
    private async Task<T> DbWithRetry<T>(Func<Task<T>> op, string ctx)
        where T : AmazonWebServiceResponse
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var resp = await op();
            sw.Stop();
            Debug.Log($"[FurnRepo] {ctx} | DB OK | {sw.ElapsedMilliseconds} ms" +
                      $" | HTTP {resp.HttpStatusCode} | {resp.ContentLength} bytes");
            return resp;
        }
        catch (AmazonServiceException ex)
        {
            sw.Stop();
            Debug.LogWarning($"[FurnRepo] {ctx} | DB FAIL ({sw.ElapsedMilliseconds} ms)" +
                             $" HTTP {ex.StatusCode} {ex.GetType().Name}: {ex.Message}");
            throw;
        }
        catch (AmazonClientException ex)
        {
            sw.Stop();
            Debug.LogWarning($"[FurnRepo] {ctx} | DB network FAIL ({sw.ElapsedMilliseconds} ms)" +
                             $" {ex.GetType().Name}: {ex.Message}");
            throw;
        }
        catch (Exception ex)
        {
            sw.Stop();
            Debug.LogError($"[FurnRepo] {ctx} | DB FAIL ({sw.ElapsedMilliseconds} ms)" +
                           $" {ex.GetType().Name}: {ex.Message}");
            throw;
        }
    }

    private string GetString(Dictionary<string, AttributeValue> item, string key)
    {
        if (item.TryGetValue(key, out var attr))
            return attr.S ?? string.Empty;
        return string.Empty;
    }

    private float GetNumber(Dictionary<string, AttributeValue> item, string key)
    {
        if (item.TryGetValue(key, out var attr) && !string.IsNullOrEmpty(attr.N))
            return float.TryParse(attr.N, out var v) ? v : 0f;
        return 0f;
    }

    // Strip the CloudFront domain off a full asset URL, leaving the bucket key
    // (FurnitureModelLoader prepends the domain again and uses it as a cache path).
    private string StripCloudFront(string url)
    {
        if (string.IsNullOrEmpty(url)) return string.Empty;
        string domain = AwsConfig.CloudFrontDomain;
        if (url.StartsWith(domain))
            return url.Substring(domain.Length).TrimStart('/');
        return url;
    }
}

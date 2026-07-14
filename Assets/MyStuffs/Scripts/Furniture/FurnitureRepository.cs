using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Amazon.DynamoDBv2.Model;
using Amazon.Runtime;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// Direct DynamoDB access layer for the furniture catalog.
/// Only this class talks to DynamoDB — nothing else should.
///
/// Backend (set in AwsConfig): the ros-products / ros-categories generation
/// written by ikea_pipeline.py (merchant-ingestion redesign).
///   • ros-categories — the merchant's REAL category tree, one node per
///     breadcrumb crumb, unlimited depth. Node ids are IKEA's own category ids
///     (e.g. "19086" = PAX system); each node stores its parent + level.
///     The root node has id "products".
///   • ros-products   — composite PK (merchant_id + product_id); each product
///     attaches to the DEEPEST crumb (its leaf). Browse queries use the
///     `category-index` GSI keyed on merchant_category = "ikea#{leaf_id}".
///
/// The app presents this N-level tree as a simple 2-tier UI:
///   tier 1 = departments  (children of the "products" root node)
///   tier 2 = that department's LEAF descendants, flattened
/// Products only live at leaves, so flattening the leaves guarantees every
/// product with a model is reachable in exactly two taps.
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
        { "#sub",  "subtitle" },
        { "#desc", "description" },
        { "#cid",  "category_id" },
        { "#mesh", "mesh_glb_url" },
        { "#pmin", "price_min" },
        { "#pmax", "price_max" },
        { "#star", "star_rating" },
        { "#rev",  "review_count" },
        { "#hid",  "hidden" },
        { "#var",  "variants" },
        { "#meas", "measurements" },
        { "#pkg",  "packages" },
        { "#st",   "status" },     // catalogue lifecycle (sync engine)
    };

    // Discontinued products (removed from the merchant's storefront and past
    // the sync grace window) never show; rows without a status (pre-sync data)
    // are treated as active.
    private const string NotDiscontinued =
        "(attribute_not_exists(#st) OR #st <> :disc)";
    private const string ProductProjection =
        "#pid, #mid, #nm, #sub, #desc, #cid, #mesh, #pmin, #pmax, #star, #rev, " +
        "#hid, #var, #meas, #pkg";

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
    // CATEGORIES  (read straight from the ros-categories real tree)
    // Tier 1 cards = departments (children of the "products" root node,
    // plus any other root-level node such as "uncategorized").
    // Tier 2 = the department's LEAF descendants, flattened — products
    // attach only to leaves, so this covers the entire catalogue.
    // Legacy seeded nodes (old rooms/types) are unreachable from the
    // "products" root and are ignored automatically.
    // ---------------------------------------------------------------

    public async Task<List<CategoryModel>> GetCategories()
    {
        var total = Stopwatch.StartNew();
        try
        {
            bool refreshed = await AwsManager.Instance.RefreshCredentialsIfNeeded();
            long credMs = AwsManager.Instance.LastRefreshElapsedMs;

            long preQuery = total.ElapsedMilliseconds;
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
            long queryMs = total.ElapsedMilliseconds - preQuery;

            long preParse = total.ElapsedMilliseconds;
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

            // Tier 1 = DEPARTMENTS: children of the "products" root node,
            // plus any other root-level node (e.g. "uncategorized"). Legacy
            // seeded nodes hang off other parents and are never reached.
            var departments = new List<CatNode>();
            bool hasProductsRoot = nodes.Any(n => n.Id == "products");
            foreach (var root in nodes.Where(n => n.Parent == "root")
                                      .OrderBy(n => n.Order))
            {
                if (root.Id == "products")
                {
                    if (childrenByParent.TryGetValue("products", out var deps))
                        departments.AddRange(deps);
                }
                else
                {
                    departments.Add(root);   // e.g. "uncategorized"
                }
            }
            // Robustness for partial data: no "products" root yet → treat
            // level-1 nodes as departments directly.
            if (!hasProductsRoot && departments.Count == 0)
                departments = nodes.Where(n => n.Level == 1)
                                   .OrderBy(n => n.Order).ToList();

            // Tier 2 = each department's LEAF descendants (flattened), kept
            // only when they actually have products with models. Departments
            // left with no live leaves are hidden.
            var rooms = new List<CategoryModel>();
            foreach (var dept in departments)
            {
                var leaves = CollectLeaves(dept, childrenByParent)
                    .Where(l => live.Contains(l.Id))
                    .OrderBy(l => l.Name)
                    .Select(l => new SubcategoryModel
                    {
                        SubcategoryId   = l.Id,   // = product category_id (leaf)
                        SubcategoryName = l.Name,
                        Icon            = l.Icon,
                    })
                    .ToList();

                if (leaves.Count == 0) continue;
                rooms.Add(new CategoryModel
                {
                    CategoryId    = dept.Id,
                    CategoryName  = dept.Name,
                    Icon          = dept.Icon,
                    ParentId      = dept.Parent,
                    MerchantId    = Merchant,
                    Subcategories = leaves,
                });
            }

            long parseMs = total.ElapsedMilliseconds - preParse;
            total.Stop();

            int totalSubs = rooms.Sum(r => r.Subcategories.Count);
            Debug.Log($"[FurnRepo] CATEGORIES" +
                      $" | cred-refresh {credMs}ms ({(refreshed ? "REFRESHED" : "fresh")})" +
                      $" | db-query {queryMs}ms" +
                      $" | parse {parseMs}ms" +
                      $" | TOTAL {total.ElapsedMilliseconds}ms" +
                      $" | {rooms.Count} departments, {totalSubs} leaf subcategories");
            return rooms;
        }
        catch (Exception e)
        {
            total.Stop();
            Debug.LogError($"[FurnitureRepository] GetCategories error ({total.ElapsedMilliseconds}ms): {e.Message}");
            return new List<CategoryModel>();
        }
    }

    private struct CatNode
    {
        public string Id, Name, Icon, Parent;
        public int    Level, Order;
    }

    // Every LEAF node (no children) in the subtree under `dept`, including the
    // department itself when it is childless (e.g. "uncategorized"). Iterative
    // DFS with a visited-set so a malformed tree can never loop.
    private static List<CatNode> CollectLeaves(
        CatNode dept, Dictionary<string, List<CatNode>> childrenByParent)
    {
        var leaves  = new List<CatNode>();
        var visited = new HashSet<string>();
        var stack   = new Stack<CatNode>();
        stack.Push(dept);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (!visited.Add(node.Id)) continue;
            if (childrenByParent.TryGetValue(node.Id, out var kids) && kids.Count > 0)
            {
                foreach (var kid in kids) stack.Push(kid);
            }
            else
            {
                leaves.Add(node);
            }
        }
        return leaves;
    }

    // v2: bumped when the backend moved to real IKEA category ids — a v1 cache
    // holds the old flat ids and would filter every new leaf out.
    private const string LiveCatPrefsKey = "ros_live_categories_v2";

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
            FilterExpression       = "attribute_exists(#mesh) AND " + NotDiscontinued,
            ProjectionExpression   = ProductProjection,
            ExpressionAttributeNames = new Dictionary<string, string>(ProductAttrNames),
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                { ":mc",   new AttributeValue { S = $"{Merchant}#{categoryId}" } },
                { ":disc", new AttributeValue { S = "discontinued" } }
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
                FilterExpression = "attribute_exists(#mesh) AND contains(#nm, :term) AND "
                                   + NotDiscontinued,
                ProjectionExpression = ProductProjection,
                ExpressionAttributeNames = new Dictionary<string, string>(ProductAttrNames),
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    { ":term", new AttributeValue { S = searchTerm } },
                    { ":disc", new AttributeValue { S = "discontinued" } }
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

            // UI variant line: "subtitle - primary colour" (matches the format
            // ItemDetailSheetController expects). subtitle replaced the legacy
            // type_name field; read the old name as fallback for un-migrated rows.
            string type = GetString(item, "subtitle");
            if (string.IsNullOrEmpty(type))
                type = GetString(item, "type_name");
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
                FullDescription  = GetString(item, "description"),
                CategoryId       = GetString(item, "category_id"),
                S3ModelUrl       = StripCloudFront(GetString(item, "mesh_glb_url")),
                PriceMin         = GetNumber(item, "price_min"),
                PriceMax         = GetNumber(item, "price_max"),
                StarRatingValue  = GetNumber(item, "star_rating"),
                ReviewCountValue = (int)GetNumber(item, "review_count"),
                Variants         = variants,
                Measurements     = ParseMeasurements(item),
                Packages         = ParsePackages(item),
            };
        }
        catch (Exception e)
        {
            Debug.LogError($"[FurnitureRepository] ParseProduct error: {e.Message}");
            return null;
        }
    }

    // Verbatim page measurements: list of {name, value} maps, labels exactly
    // as IKEA writes them ("Width", "Free height under furniture", …).
    private List<ProductMeasurement> ParseMeasurements(
        Dictionary<string, AttributeValue> item)
    {
        var list = new List<ProductMeasurement>();
        if (!item.TryGetValue("measurements", out var attr) || attr.L == null)
            return list;
        foreach (var entry in attr.L)
        {
            var m = entry.M;
            if (m == null) continue;
            list.Add(new ProductMeasurement
            {
                Name  = GetString(m, "name"),
                Value = GetString(m, "value"),
            });
        }
        return list;
    }

    // Shipping packages: map {count, packages: [{name, type, article_number,
    // measurements: [{name, value}]}]}.
    private List<ProductPackage> ParsePackages(
        Dictionary<string, AttributeValue> item)
    {
        var list = new List<ProductPackage>();
        if (!item.TryGetValue("packages", out var attr) || attr.M == null)
            return list;
        if (!attr.M.TryGetValue("packages", out var arr) || arr.L == null)
            return list;
        foreach (var entry in arr.L)
        {
            var m = entry.M;
            if (m == null) continue;
            var pkg = new ProductPackage
            {
                Name          = GetString(m, "name"),
                TypeName      = GetString(m, "type"),
                ArticleNumber = GetString(m, "article_number"),
            };
            if (m.TryGetValue("measurements", out var pm) && pm.L != null)
            {
                foreach (var me in pm.L)
                {
                    if (me.M == null) continue;
                    pkg.Measurements.Add(new ProductMeasurement
                    {
                        Name  = GetString(me.M, "name"),
                        Value = GetString(me.M, "value"),
                    });
                }
            }
            list.Add(pkg);
        }
        return list;
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

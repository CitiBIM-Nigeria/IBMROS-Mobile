using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Amazon.DynamoDBv2.Model;
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
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    { ":mid", new AttributeValue { S = Merchant } }
                }
            };

            var response = await AwsManager.Instance.DynamoDBClient.QueryAsync(request);

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

                var resp = await AwsManager.Instance.DynamoDBClient.ScanAsync(req);
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
    // ---------------------------------------------------------------

    public async Task<List<ProductModel>> GetProductsByCategory(string categoryId)
    {
        if (_productCache.TryGetValue(categoryId, out var cached))
        {
            Debug.Log($"[FurnitureRepository] CACHE HIT for category {categoryId}");
            return cached;
        }

        try
        {
            await AwsManager.Instance.RefreshCredentialsIfNeeded();

            var request = new QueryRequest
            {
                TableName              = AwsConfig.FurnitureCatalogTableName,
                IndexName              = "category-index",
                KeyConditionExpression = "merchant_category = :mc",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    { ":mc", new AttributeValue { S = $"{Merchant}#{categoryId}" } }
                }
            };

            var response = await AwsManager.Instance.DynamoDBClient.QueryAsync(request);

            var products = response.Items
                .Select(ParseProduct)
                .Where(p => p != null && p.HasModel)   // only placeable products
                .ToList();

            Debug.Log($"[FurnitureRepository] Fetched {products.Count} " +
                      $"products for category {categoryId}.");

            _productCache[categoryId] = products;
            return products;
        }
        catch (Exception e)
        {
            Debug.LogError($"[FurnitureRepository] GetProductsByCategory error: {e.Message}");
            return new List<ProductModel>();
        }
    }

    // ---------------------------------------------------------------
    // PRODUCTS BY SUBCATEGORY  (category-index + subcategory_id filter)
    // ---------------------------------------------------------------

    public async Task<List<ProductModel>> GetProductsBySubcategory(
        string categoryId, string subcategoryId)
    {
        string cacheKey = $"{categoryId}_{subcategoryId}";

        if (_productCache.TryGetValue(cacheKey, out var cached))
        {
            Debug.Log($"[FurnitureRepository] CACHE HIT — {cached.Count} products.");
            return cached;
        }

        try
        {
            await AwsManager.Instance.RefreshCredentialsIfNeeded();

            var request = new QueryRequest
            {
                TableName              = AwsConfig.FurnitureCatalogTableName,
                IndexName              = "category-index",
                KeyConditionExpression = "merchant_category = :mc",
                FilterExpression       = "subcategory_id = :subId",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    { ":mc",    new AttributeValue { S = $"{Merchant}#{categoryId}" } },
                    { ":subId", new AttributeValue { S = subcategoryId } }
                }
            };

            var response = await AwsManager.Instance.DynamoDBClient.QueryAsync(request);

            var products = response.Items
                .Select(ParseProduct)
                .Where(p => p != null && p.HasModel)   // only placeable products
                .ToList();

            _productCache[cacheKey] = products;

            Debug.Log($"[FurnitureRepository] Fetched {products.Count} " +
                      $"products for subcategory {subcategoryId}.");
            return products;
        }
        catch (Exception e)
        {
            Debug.LogError($"[FurnitureRepository] GetProductsBySubcategory error: {e.Message}");
            return new List<ProductModel>();
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
                Key = new Dictionary<string, AttributeValue>
                {
                    { "merchant_id", new AttributeValue { S = Merchant } },
                    { "product_id",  new AttributeValue { S = productId } }
                }
            };

            var response = await AwsManager.Instance.DynamoDBClient.GetItemAsync(request);

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
                FilterExpression = "contains(#nm, :term)",
                ExpressionAttributeNames = new Dictionary<string, string>
                {
                    { "#nm", "name" }
                },
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    { ":term", new AttributeValue { S = searchTerm } }
                }
            };

            var response = await AwsManager.Instance.DynamoDBClient.ScanAsync(request);

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

# Furniture Backend Migration Plan

Migrate the IBMROS app from the legacy backend (`ibm-ros-furniture-models` /
`ibm-ros-furniture-catalog`, merchant `0001`) to the current pipeline backend
(`ros-furniture-assets` / `ros-products`, merchant `ikea`), **and** adopt a
breadcrumb-derived hierarchical category tree.

Guiding rule: **don't break the working app.** The legacy DB stays live and
untouched; we build the new path on a branch and only flip `AwsConfig` once it's
verified in Unity. Rollback = revert the `AwsConfig` constants.

---

## 1. Data model (DynamoDB)

### 1a. `ros-categories` — flexible category tree
One row per node, any depth (IKEA is ~3: Department → Category → Subcategory).

| Attribute        | Type | Example                              |
| ---------------- | ---- | ------------------------------------ |
| `merchant_id` PK | S    | `ikea`                               |
| `category_id` SK | S    | `corner-sofas`                       |
| `parent_id`      | S    | `sofas` (top-level nodes use `root`) |
| `name`           | S    | `Corner sofas`  (from breadcrumb)    |
| `level`          | N    | `0` dept, `1` category, `2` leaf     |
| `sort_order`     | N    | stable UI ordering                   |
| `product_count`  | N    | optional, for badges                 |

**New GSI `parent-index`:** HASH `merchant_parent` = `"ikea#{parent_id}"`,
RANGE `sort_order` (or `category_id`). Lets the app fetch the direct children of
any node in one query — including top-level departments via parent `root`.

### 1b. `ros-products` — additions
Keep the existing composite PK (`merchant_id`+`product_id`) and `category-index`
GSI. Add:

| Attribute       | Type | Notes                                                     |
| --------------- | ---- | -------------------------------------------------------- |
| `category_path` | L\<S\> | `["living-room","sofas","corner-sofas"]` (full ancestry) |
| `category_id`   | S    | **leaf** id — already drives `merchant_category` = `"ikea#{leaf}"` |

The existing `category-index` keeps working unchanged once `category_id` holds
the leaf id. Products are indexed at their leaf.

### 1c. Browsing at any level (the one real design tradeoff)
DynamoDB single-key GSIs can't "query everything under a non-leaf" directly.
Recommended (matches IKEA's own UX — users drill down, they don't see a
department flattened into thousands of products):

1. Load the category tree from `ros-categories` (small, cache it).
2. UI drills Department → Category → Subcategory(leaf).
3. Query products for the chosen **leaf** via `category-index`.

If you later want a true "all products under Living Room" view, resolve that
node's descendant leaves from the cached tree and batch the leaf queries. Avoid
scanning `category_path`.

---

## 2. Pipeline changes (Python, in `~/Documents/ros-pipeline`)

Re-scrape is acceptable, so this is additive to `ikea_pipeline.py`:

1. **Scrape the breadcrumb** in `scrape_product_page`: select
   `nav[aria-label*="breadcrumb"]` (verify IKEA's current markup), collect the
   trail minus "Home" and the product itself → `category_path` (slugified ids)
   and the human names.
2. **Upsert category nodes** into `ros-categories` as products are scraped: for
   each breadcrumb segment, `put_item` the node with `parent_id`, `name`,
   `level`, `merchant_parent`. Idempotent (overwrite-safe).
3. **Write product fields:** set `category_id` = leaf, add `category_path`.
4. Everything else (GLB, textures, variants, bg removal) is unchanged.
5. Optional: a second pass to compute `product_count` per node.

Keep `setup_aws.py` in sync — add the `parent-index` GSI to the `ros-categories`
table definition and the `merchant_parent` attribute.

---

## 3. Unity changes (C#) — file by file

### `Scripts/Managers/AwsConfig.cs`
- `FurnitureBucketName` → `"ros-furniture-assets"`
- `FurnitureCatalogTableName` → `"ros-products"`
- add `CategoriesTableName = "ros-categories"`
- `FurnitureCatalogMerchantId` → `"ikea"`
- `CloudFrontDomain` — **unchanged** (`d3lz5hvxtvmgbq.cloudfront.net`)
- Keep the old values commented for one-line rollback.

### `Scripts/Models/CategoryModel.cs`
Replace the fixed 2-tier `CategoryModel`/`SubcategoryModel` with a recursive
node (depth-agnostic), e.g.:
```csharp
public class CategoryNode {
    public string CategoryId, ParentId, Name;
    public int    Level, SortOrder;
    public List<CategoryNode> Children = new();
}
```
Provide thin compatibility so the existing panel (which thinks in
category→subcategory) maps onto tree depth without a UI rewrite.

### `Scripts/Models/ProductModel.cs`
- Rename/add: `S3ModelUrl` → `MeshGlbUrl`; add `NormalMapUrl`; replace `Price`
  with `PriceMin`/`PriceMax`; add `List<Variant>` and `CategoryPath`.
- `BestImageUrl` now derives from the primary variant's `display_url`
  (fallback `display_original_url`).
- Add a `Variant { VariantId, ColourName, IsPrimary, Price, BaseColorUrl,
  DisplayUrl, DisplayOriginalUrl }`.

### `Scripts/Furniture/FurnitureRepository.cs`
- `GetCategoryTree()` — query `ros-categories` (via `parent-index`, walking from
  `root`, or one scan + build in memory). Replaces `GetCategories` +
  `GetSubcategories`.
- `GetProductsByCategory(leafId)` — query `category-index`,
  `merchant_category = "ikea#{leafId}"`. (Replaces both the old by-category and
  by-subcategory methods — there's only "by leaf" now.)
- `GetProduct(productId)` — `GetItem` with **composite** key
  `{ merchant_id:"ikea", product_id }`.
- `ParseProduct` — map new attribute names; parse the `variants` list
  (DynamoDB `L` of `M`); read `price_min`/`price_max`, `mesh_glb_url`,
  `normal_map_url`, `category_path`.

### `Scripts/Furniture/FurnitureDataService.cs`
- `GetCategories` event → emits the tree.
- Collapse `GetProductsBySubcategory` into `GetProductsByLeaf` (or keep the name,
  pass the leaf id).

### `Scripts/UI/FurniturePanelController.cs`
- Lines 152/320/322/361/438/683 — `CategoryMapper.GetAppCategoryName` becomes
  identity (names come from the tree's `Name`); keep `GetCategoryEmoji`.
- Line 693 — the spawn pipe string `…|{product.S3ModelUrl}|…` → `MeshGlbUrl`.
- Line 1059 — `GetProductsBySubcategory(...)` → leaf query.
- Lines 535/1007 — `BestImageUrl` unchanged in shape, new source.

### `Scripts/UI/RoomUIManager.cs`
- Cosmetic: `_pendingS3ModelUrl` → `_pendingModelUrl` (value still the GLB URL
  flowing through the spawn pipe; line 35/238/253).

### `Scripts/Furniture/CategoryMapper.cs`
- **Drop the hardcoded name map** (names are now data). **Keep** the
  category→emoji map (`GetCategoryEmoji`) — still nice UI sugar, now keyed on the
  real category name/id.

### `Scripts/Furniture/CatalogSubcategoryMapper.cs`
- **Delete** — subcategory names are now data from `ros-categories`. Remove its
  `.meta` too.

### `Scripts/Furniture/FurnitureModelLoader.cs`
- Consumes the GLB URL string; just confirm it reads `MeshGlbUrl`. If it applies
  a normal map, wire `NormalMapUrl`.

### `Scripts/UI/ItemDetailSheetController.cs`
- Price display: `Price` → format `PriceMin`–`PriceMax` (or "from £X").
- Variant images: pull from the new `Variants` list.

---

## 4. Rollout sequence (no-downtime)

1. Branch off `furniture-panel-stable`.
2. Pipeline: add breadcrumb + tree upsert; **re-scrape** into `ros-*`.
3. Verify `ros-categories` / `ros-products` look right (use
   `tools/test_single_product.py` for a single product end-to-end).
4. Apply the Unity changes; compile.
5. In Unity, flip `AwsConfig` to the `ros-*` constants and play-test browse →
   detail → spawn → variants.
6. If anything's off, revert the `AwsConfig` constants (instant rollback to the
   live legacy DB).
7. Merge once verified.

---

## 5. My added recommendations

- **Kill the mappers, not just trim them.** Once names come from the breadcrumb
  tree, `CatalogSubcategoryMapper` and the *name* half of `CategoryMapper`
  become pure liability. Keep only emoji.
- **Reconsider the hard-coded IAM key.** The app already has a Cognito Identity
  Pool issuing temp credentials. If the pool's role grants S3/DynamoDB read,
  drop the static `AccessKey`/`SecretKey` from `AwsConfig.cs` entirely. At
  minimum, rotate the currently-committed key. Same for the Python scripts → env
  vars / `.env`.
- **Store `sort_order` from breadcrumb position** so the UI ordering is stable
  and matches IKEA's own ordering instead of DynamoDB's arbitrary order.
- **Cache the category tree** on `FurnitureRepository` (it's tiny and changes
  rarely) — one fetch per session.
- **VR-team UGUI delivery:** instead of manual Unity-package export, consider a
  UPM package consumed by git URL (`Packages/manifest.json`) with an `.asmdef`,
  or a shared git submodule — both give the VR team versioned, pull-to-update
  auth without re-exporting by hand.

using System.Collections.Generic;

/// <summary>
/// A single colour variant of a product (from the ros-products `variants` list).
/// Image URLs are full CloudFront URLs as written by the pipeline.
/// </summary>
public class ProductVariant
{
    public string VariantId          { get; set; }
    public string ColourName         { get; set; }
    public bool   IsPrimary          { get; set; }
    public float  Price              { get; set; }
    public string ModelUrl           { get; set; }   // this colour's own GLB (bucket KEY, domain stripped)
    public string SwatchUrl          { get; set; }   // IKEA 80×80 chip for the colour circle
    public string DominantColor      { get; set; }   // "#rrggbb" flat-colour fallback
    public string DisplayUrl         { get; set; }   // transparent .webp (bg removed)
    public string DisplayThumbUrl    { get; set; }   // 400px grid thumbnail
    public string DisplayOriginalUrl { get; set; }   // original .jpg
}

/// <summary>
/// A catalog product, parsed from a ros-products DynamoDB item.
/// </summary>
public class ProductModel
{
    public string ProductId      { get; set; }
    public string MerchantId     { get; set; }
    public string Name           { get; set; }

    // Short variant line shown in the UI, e.g. "2 seater sofa - Lejde grey/black".
    // Built from type_name + primary colour by FurnitureRepository.
    public string Description    { get; set; }

    public string CategoryId     { get; set; }
    public string SubcategoryId  { get; set; }

    // Bucket KEY of the GLB (CloudFront domain stripped) so FurnitureModelLoader
    // can prepend the domain and use it as a local cache path, unchanged.
    public string S3ModelUrl     { get; set; }

    public float  PriceMin       { get; set; }
    public float  PriceMax       { get; set; }
    public float  StarRatingValue  { get; set; }
    public int    ReviewCountValue { get; set; }

    public List<ProductVariant> Variants { get; set; } = new();

    // True if a 3D model is available.
    public bool HasModel => !string.IsNullOrEmpty(S3ModelUrl);

    private ProductVariant PrimaryVariant =>
        Variants.Find(v => v.IsPrimary) ??
        (Variants.Count > 0 ? Variants[0] : null);

    // The default/primary colour's own GLB key. Equals S3ModelUrl in practice
    // (the product's default model is the primary colour).
    public string PrimaryModelUrl => PrimaryVariant?.ModelUrl ?? string.Empty;

    // Best FULL image (detail screen) — transparent .webp, else original .jpg.
    public string BestImageUrl
    {
        get
        {
            var v = PrimaryVariant;
            if (v == null) return string.Empty;
            if (!string.IsNullOrEmpty(v.DisplayUrl))         return v.DisplayUrl;
            if (!string.IsNullOrEmpty(v.DisplayOriginalUrl)) return v.DisplayOriginalUrl;
            return string.Empty;
        }
    }

    // Best SMALL image for the catalog grid — prefers the 400px thumbnail, then
    // the full transparent image, then the original. Cuts grid bandwidth ~50×.
    public string BestThumbnailUrl
    {
        get
        {
            var v = PrimaryVariant;
            if (v == null) return string.Empty;
            if (!string.IsNullOrEmpty(v.DisplayThumbUrl))    return v.DisplayThumbUrl;
            if (!string.IsNullOrEmpty(v.DisplayUrl))         return v.DisplayUrl;
            if (!string.IsNullOrEmpty(v.DisplayOriginalUrl)) return v.DisplayOriginalUrl;
            return string.Empty;
        }
    }

    public string FormattedPrice
    {
        get
        {
            if (PriceMin <= 0f)        return "Price unavailable";
            if (PriceMax > PriceMin)   return $"From £{PriceMin:F2}";
            return $"£{PriceMin:F2}";
        }
    }
}

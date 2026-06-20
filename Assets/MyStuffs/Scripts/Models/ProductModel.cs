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
    public string BaseColorUrl       { get; set; }   // GLB base-colour texture
    public string DisplayUrl         { get; set; }   // transparent .webp (bg removed)
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

    // Best available product image — full CloudFront URL. Prefers the transparent
    // .webp (filled by the background-removal worker); falls back to the original
    // .jpg while that's still pending.
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

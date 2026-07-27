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

/// <summary>One verbatim measurement from the product page, exactly as IKEA
/// labels it ("Width", "System depth", "Free height under furniture", …).</summary>
public class ProductMeasurement
{
    public string Name  { get; set; }
    public string Value { get; set; }
}

/// <summary>One shipping package ("4 × PAX · Wardrobe frame" + its box
/// dimensions/weight). Products can ship as many packages.</summary>
public class ProductPackage
{
    public string Name          { get; set; }   // "1 × AKTERSPRING"
    public string TypeName      { get; set; }   // "Pendant lamp"
    public string ArticleNumber { get; set; }
    public List<ProductMeasurement> Measurements { get; set; } = new();
}

/// <summary>
/// A catalog product, parsed from a ros-products DynamoDB item.
/// </summary>
public class ProductModel
{
    public string ProductId      { get; set; }
    public string MerchantId     { get; set; }
    public string Name           { get; set; }

    // Region-independent identity (worldwide IKEA article) — the id a physical
    // QR encodes and resolves through the canonical-index GSI. QrUrl is the
    // product's deep link as written by the pipeline (empty on un-backfilled rows).
    public string CanonicalProductId { get; set; }
    public string QrUrl              { get; set; }

    // Short variant line shown in the UI, e.g. "2 seater sofa - Lejde grey/black".
    // Built from subtitle + primary colour by FurnitureRepository.
    public string Description    { get; set; }

    // The full marketing description from the product page.
    public string FullDescription { get; set; }

    public string CategoryId     { get; set; }

    // Verbatim page measurements + shipping packages (rich detail screen data).
    public List<ProductMeasurement> Measurements { get; set; } = new();
    public List<ProductPackage>     Packages     { get; set; } = new();

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

    // "Diameter 73 cm · Height 70 cm · Shade diameter 14 cm" — for the detail
    // sheet's dimensions line. Empty when the page had no measurements.
    public string MeasurementsLine
    {
        get
        {
            if (Measurements == null || Measurements.Count == 0) return string.Empty;
            var parts = new List<string>();
            foreach (var m in Measurements)
                if (!string.IsNullOrEmpty(m.Name) && !string.IsNullOrEmpty(m.Value))
                    parts.Add($"{m.Name} {m.Value}");
            return string.Join("  ·  ", parts);
        }
    }

    // "Delivered as 8 packages" / "1 package · 66×25×15 cm · 3.22 kg".
    public string PackagesLine
    {
        get
        {
            if (Packages == null || Packages.Count == 0) return string.Empty;
            if (Packages.Count > 1) return $"Delivered as {Packages.Count} packages";
            var p = Packages[0];
            string dims = "", weight = "";
            string w = "", h = "", l = "";
            foreach (var m in p.Measurements)
            {
                switch (m.Name)
                {
                    case "Width":  w = m.Value; break;
                    case "Height": h = m.Value; break;
                    case "Length": l = m.Value; break;
                    case "Weight": weight = m.Value; break;
                }
            }
            if (!string.IsNullOrEmpty(l) && !string.IsNullOrEmpty(w) && !string.IsNullOrEmpty(h))
                dims = $"{l} × {w} × {h}".Replace(" cm ×", " ×");
            var bits = new List<string> { "1 package" };
            if (!string.IsNullOrEmpty(dims))   bits.Add(dims);
            if (!string.IsNullOrEmpty(weight)) bits.Add(weight);
            return string.Join("  ·  ", bits);
        }
    }
}

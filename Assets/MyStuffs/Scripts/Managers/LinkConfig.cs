/// <summary>
/// Centralized deep-link / QR configuration for the app side.
///
/// This MUST mirror ros-pipeline/link_config.json: the physical QR codes encode
/// links built from THAT file; this file tells the app which links to accept and
/// how to resolve them. Every production-dependent value here is a PLACEHOLDER
/// until go-live — search the codebase for "PLACEHOLDER" or "ibmros.local" to
/// find every dependent spot. Changing these is a config edit, never a code
/// change.
///
/// Go-live checklist (all one-line edits):
///   • LinkDomain           → the real domain (matches link_config.json)
///   • (native build files)  Assets/Editor/DeepLinkBuildPostProcessor.cs and
///                           Assets/Plugins/Android/AndroidManifest.xml read the
///                           SAME domain — update those too (they are separate
///                           because Xcode/Gradle can't read this C# at build).
/// </summary>
public static class LinkConfig
{
    // The host the QR deep links use. PLACEHOLDER — must equal link_config.json
    // "link_domain".
    public const string LinkDomain = "placeholder.ibmros.local";
    public const string LinkScheme = "https";

    // Product links look like  {scheme}://{domain}/p/{canonicalId}
    public const string ProductPathPrefix = "/p/";

    // Optional custom scheme (ibmros://p/<id>) — handy for in-app/testing and a
    // secondary Android intent-filter; the STORE-fallback flow uses the https
    // universal/app link, not this.
    public const string CustomScheme = "ibmros";

    // The canonical-index GSI on ros-products (HASH group_canonical,
    // RANGE merchant_id) — how a scanned canonical id resolves to THIS region's
    // row. Region-independent by design.
    public const string CanonicalIndexName = "canonical-index";

    // group_canonical is "{merchantGroup}#{canonicalId}"; for IKEA the group is
    // "ikea" across every regional partition.
    public const string MerchantGroup = "ikea";

    /// <summary>The HTTPS deep link for a product's canonical id.</summary>
    public static string BuildProductLink(string canonicalId) =>
        $"{LinkScheme}://{LinkDomain}{ProductPathPrefix}{canonicalId}";

    /// <summary>The canonical-index partition key for a canonical id.</summary>
    public static string GroupCanonical(string canonicalId) =>
        $"{MerchantGroup}#{canonicalId}";

    /// <summary>True while the domain is still the shipped placeholder.</summary>
    public static bool IsPlaceholder =>
        LinkDomain.Contains("placeholder") || LinkDomain.EndsWith(".ibmros.local");
}

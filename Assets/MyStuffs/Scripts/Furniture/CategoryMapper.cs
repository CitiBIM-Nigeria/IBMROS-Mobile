/// <summary>
/// Emoji fallback for category / subcategory cards.
///
/// Category names and icons now come straight from the ros-categories table
/// (CategoryModel.Icon / SubcategoryModel.Icon), so prefer those. This mapper is
/// only the keyword-based fallback used when a node has no icon — e.g. the
/// breadcrumb-derived leaf subcategories the pipeline writes with an empty icon.
/// It matches against either a display name ("Corner sofas") or an id ("sofas").
/// </summary>
public static class CategoryMapper
{
    // First keyword found (in order) wins, so put more specific terms first.
    private static readonly (string keyword, string emoji)[] Keywords =
    {
        // Department-level names (real IKEA tree: "Kitchens", "Outdoor", …)
        ("kitchen",   "🍳"),
        ("outdoor",   "🌳"),
        ("garden",    "🌳"),
        ("children",  "🧸"),
        ("kids",      "🧸"),
        ("baby",      "🧸"),
        ("nursery",   "🧸"),
        ("decoration","🖼️"),
        ("laundry",   "🧺"),
        ("textile",   "🧵"),
        ("uncategor", "📦"),
        // Type-level names
        ("sofa",      "🛋️"),
        ("armchair",  "🛋️"),
        ("footstool", "🛋️"),
        ("ottoman",   "🛋️"),
        ("office",    "💼"),
        ("desk",      "🖥️"),
        ("gaming",    "🎮"),
        ("dining",    "🍽️"),
        ("chair",     "🪑"),
        ("stool",     "🪑"),
        ("coffee",    "☕"),
        ("side table","🪵"),
        ("table",     "🪵"),
        ("wardrobe",  "👔"),
        ("book",      "📚"),
        ("shelf",     "📚"),
        ("shelving",  "📚"),
        ("drawer",    "🗄️"),
        ("cabinet",   "🗄️"),
        ("storage",   "🗄️"),
        ("tv",        "📺"),
        ("media",     "📺"),
        ("bed",       "🛏️"),
        ("mattress",  "🛏️"),
        ("nightstand","🕯️"),
        ("bedside",   "🕯️"),
        ("floor lamp","🔦"),
        ("lamp",      "💡"),
        ("light",     "💡"),
        ("bath",      "🚿"),
        ("shower",    "🚿"),
        ("mirror",    "🪞"),
    };

    /// <summary>Legacy id→name map is obsolete (names come from data). Kept so
    /// callers using the `?? CategoryName` fallback still compile; returns null
    /// for the current pipeline's ids.</summary>
    public static string GetAppCategoryName(string categoryId) => null;

    /// <summary>Pick an emoji by scanning a name or id for known keywords.</summary>
    public static string GetCategoryEmoji(string nameOrId)
    {
        if (string.IsNullOrEmpty(nameOrId)) return "📦";
        string s = nameOrId.ToLowerInvariant();
        foreach (var (keyword, emoji) in Keywords)
            if (s.Contains(keyword))
                return emoji;
        return "📦";
    }
}

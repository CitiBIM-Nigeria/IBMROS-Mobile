using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A backend-sourced list of the subcategories a VR socket can display.
///
/// WHY THIS EXISTS:
///   The socket must NOT contain hardcoded category IDs, but a designer also
///   shouldn't have to type raw backend IDs like "sofas" into the Inspector.
///   This asset is the bridge: it is filled ENTIRELY from the live backend
///   (see <see cref="VRSubcategoryCatalogRefresher"/>) and then drives a clean
///   dropdown on every socket (see VRFurnitureSocketEditor).
///
///   • Nothing here is hand-authored or hardcoded — press Play once with a
///     refresher in the scene and it rebuilds from ros-categories.
///   • Each entry maps a human label ("Living Room › Sofas") to the product
///     category_id the socket queries with.
///
/// Keep exactly ONE of these in a Resources folder so both edit-mode tooling and
/// runtime code can load it by name.
/// </summary>
[CreateAssetMenu(
    fileName = "VRSubcategoryCatalog",
    menuName = "IBMROS/VR/Subcategory Catalog",
    order = 0)]
public class VRSubcategoryCatalog : ScriptableObject
{
    /// <summary>Resources path (no extension) used for the shared singleton asset.</summary>
    public const string ResourcesPath = "VRSubcategoryCatalog";

    [System.Serializable]
    public class Entry
    {
        [Tooltip("Room / department this subcategory belongs to (display only).")]
        public string roomName;

        [Tooltip("Display name shown in the dropdown, e.g. 'Sofas'.")]
        public string displayName;

        [Tooltip("The product category_id the socket queries with. Sourced from " +
                 "ros-categories — never typed by hand.")]
        public string categoryId;

        /// <summary>Label used in the Inspector dropdown: "Living Room › Sofas".</summary>
        public string DropdownLabel =>
            string.IsNullOrEmpty(roomName) ? displayName : $"{roomName} › {displayName}";
    }

    [Tooltip("Filled from the backend by VRSubcategoryCatalogRefresher. " +
             "Do not edit by hand — press Play with a refresher in the scene to rebuild.")]
    public List<Entry> entries = new();

    [Tooltip("When this catalog was last rebuilt from the backend (for sanity checks).")]
    public string lastRefreshedUtc;

    /// <summary>Load the shared catalog from Resources (may be null if never created).</summary>
    public static VRSubcategoryCatalog Load()
        => Resources.Load<VRSubcategoryCatalog>(ResourcesPath);

    /// <summary>Find the entry for a resolved category_id, or null.</summary>
    public Entry Find(string categoryId)
    {
        if (string.IsNullOrEmpty(categoryId)) return null;
        return entries.Find(e => e.categoryId == categoryId);
    }
}

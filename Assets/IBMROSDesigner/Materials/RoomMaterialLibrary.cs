using System.Collections.Generic;
using UnityEngine;

namespace IBMROS.Designer.Materials
{
    /// <summary>
    /// The application's material catalog for room surfaces.
    ///
    /// Deliberately data-driven the way the plugin already expects: a material is
    /// identified by NAME and resolved from Resources at runtime
    /// (Exoa's SpaceMaterialController does exactly this with
    /// HDSettings.FLOOR_MATERIALS_FOLDER etc., but those folders ship with the
    /// full HomeDesigner product and don't exist in the FloorMapDesigner module
    /// we have). We provide them here, so adding a new floor/wall/ceiling finish
    /// is dropping a .mat into the matching Resources folder — no code change.
    ///
    ///   Assets/IBMROSDesigner/Resources/Floor/*.mat
    ///   Assets/IBMROSDesigner/Resources/Wall/*.mat
    ///   Assets/IBMROSDesigner/Resources/Ceiling/*.mat
    ///
    /// Names stored in the document are exactly the asset file names, so a saved
    /// room re-resolves its look on load.
    /// </summary>
    public static class RoomMaterialLibrary
    {
        public enum Surface { Floor, Wall, Ceiling }

        public const string FLOOR_FOLDER = "Floor";
        public const string WALL_FOLDER = "Wall";
        public const string CEILING_FOLDER = "Ceiling";

        /// <summary>Fallbacks used when a room has no stored choice (fresh presets).</summary>
        public const string DEFAULT_FLOOR = "Floor_Laminate";
        public const string DEFAULT_WALL = "Wall_PlasterWhite";
        public const string DEFAULT_CEILING = "Ceiling_PlasterWhite";

        private static readonly Dictionary<string, Material> cache = new Dictionary<string, Material>();
        private static readonly Dictionary<Surface, List<string>> listCache =
            new Dictionary<Surface, List<string>>();

        public static string FolderOf(Surface surface)
        {
            switch (surface)
            {
                case Surface.Floor: return FLOOR_FOLDER;
                case Surface.Wall: return WALL_FOLDER;
                default: return CEILING_FOLDER;
            }
        }

        public static string DefaultNameOf(Surface surface)
        {
            switch (surface)
            {
                case Surface.Floor: return DEFAULT_FLOOR;
                case Surface.Wall: return DEFAULT_WALL;
                default: return DEFAULT_CEILING;
            }
        }

        /// <summary>Every material available for a surface (file names, sorted).</summary>
        public static IReadOnlyList<string> Names(Surface surface)
        {
            if (listCache.TryGetValue(surface, out List<string> cached))
                return cached;

            var names = new List<string>();
            foreach (Material m in Resources.LoadAll<Material>(FolderOf(surface)))
            {
                names.Add(m.name);
                cache[Key(surface, m.name)] = m;
            }
            names.Sort(string.CompareOrdinal);
            listCache[surface] = names;
            return names;
        }

        /// <summary>
        /// Resolves a stored name to a material. Falls back to the surface default
        /// so an unknown/renamed material never leaves a room untextured.
        /// Returns null only when the library itself is missing.
        /// </summary>
        public static Material Resolve(Surface surface, string name)
        {
            if (string.IsNullOrEmpty(name))
                name = DefaultNameOf(surface);

            string key = Key(surface, name);
            if (cache.TryGetValue(key, out Material hit) && hit != null)
                return hit;

            Material m = Resources.Load<Material>(FolderOf(surface) + "/" + name);
            if (m == null && name != DefaultNameOf(surface))
                m = Resources.Load<Material>(FolderOf(surface) + "/" + DefaultNameOf(surface));
            if (m != null)
                cache[key] = m;
            return m;
        }

        private static string Key(Surface surface, string name) => (int)surface + ":" + name;

        /// <summary>Drops caches (editor domain reloads / library edits at runtime).</summary>
        public static void Invalidate()
        {
            cache.Clear();
            listCache.Clear();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Invalidate();
    }
}

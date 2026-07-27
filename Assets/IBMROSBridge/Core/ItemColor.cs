using UnityEngine;

namespace IBMROS.Core
{
    /// <summary>
    /// Deterministic per-item palette color keyed on the item's stable identity (A2).
    ///
    /// Item color used to be chosen from a transient counter / sibling index, so it
    /// shifted whenever the scene rebuilt — on undo, on load, and (latent) whenever the
    /// A5 reconciler recreated an item. Deriving it from the persisted uniqueId makes a
    /// given item ALWAYS the same color: across rebuilds, undo/redo, reconcile-recreate,
    /// save/reload, and sessions. FNV-1a is used (not string.GetHashCode, which .NET
    /// randomizes per process) so the mapping is stable everywhere.
    /// </summary>
    public static class ItemColor
    {
        public static int IndexForId(string id, int paletteLength)
        {
            if (paletteLength <= 0)
                return 0;
            uint h = 2166136261u; // FNV-1a 32-bit
            if (!string.IsNullOrEmpty(id))
            {
                foreach (char c in id)
                {
                    h ^= c;
                    h *= 16777619u;
                }
            }
            return (int)(h % (uint)paletteLength);
        }

        public static Color ForId(string id, Color[] palette)
        {
            if (palette == null || palette.Length == 0)
                return Color.white;
            return palette[IndexForId(id, palette.Length)];
        }
    }
}

using System.Collections.Generic;

namespace IBMROS.Core
{
    /// <summary>
    /// A7 stage 2b — explicit opening→host-room binding (CAD "hosting").
    ///
    /// EXOA binds an opening to a wall purely by proximity (&lt;0.2 m), recomputed every
    /// rebuild, with no stored reference. That makes "which openings belong to this room"
    /// unanswerable except by a proximity guess — so reposition scoping needed a margin
    /// heuristic, and an opening on a wall dragged far in one step could be orphaned.
    ///
    /// This registry records the AUTHORITATIVE binding at the moment a room actually
    /// claims an opening (during that room's rebuild, using EXOA's own 0.2 m test),
    /// keyed by the stable A2 item ids. Reposition then targets an opening's remembered
    /// host exactly — it follows its wall regardless of how far the wall moved — with the
    /// proximity margin kept only as a safety net for newly-added / re-bound openings.
    /// Keyed by item id (not instance), so it survives reconciler recreation; stale
    /// entries for deleted openings never match a live opening and are harmless.
    /// </summary>
    public static class OpeningHostRegistry
    {
        private static readonly Dictionary<string, string> hostRoomByOpening = new Dictionary<string, string>();

        public static void SetHost(string openingItemId, string roomItemId)
        {
            if (string.IsNullOrEmpty(openingItemId) || string.IsNullOrEmpty(roomItemId))
                return;
            hostRoomByOpening[openingItemId] = roomItemId;
        }

        public static bool IsHostedBy(string openingItemId, string roomItemId)
        {
            if (string.IsNullOrEmpty(openingItemId) || string.IsNullOrEmpty(roomItemId))
                return false;
            return hostRoomByOpening.TryGetValue(openingItemId, out string r) && r == roomItemId;
        }

        public static string HostOf(string openingItemId)
        {
            if (!string.IsNullOrEmpty(openingItemId) && hostRoomByOpening.TryGetValue(openingItemId, out string r))
                return r;
            return null;
        }

        public static void Forget(string openingItemId)
        {
            if (!string.IsNullOrEmpty(openingItemId))
                hostRoomByOpening.Remove(openingItemId);
        }

        public static void Clear() => hostRoomByOpening.Clear();

        public static int Count => hostRoomByOpening.Count;
    }
}

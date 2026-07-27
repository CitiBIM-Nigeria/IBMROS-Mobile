using System.Collections.Generic;
using Exoa.Events;
using UnityEngine;

namespace IBMROS.Core
{
    /// <summary>
    /// Architecture track A7 (stage 1) — dependency-scoped rebuild invalidation.
    ///
    /// EXOA invalidates the whole world on any edit: an opening change fires
    /// OnRequestRebuildAllRooms, so every room regenerates even though a door only
    /// affects the wall it sits on. Professional editors (CAD parametric graphs,
    /// Unreal's transaction-scoped dirtying, reactive/virtual-DOM reconcilers) instead
    /// invalidate only the dependents of what changed. The dependency edge already
    /// exists implicitly here: an opening binds to a wall within ~0.2 m (EXOA's own
    /// snap test), so an opening depends exactly on the room(s) whose polygon edge it
    /// is near — and on the building shell (the exterior wall carries the hole too).
    ///
    /// This turns that implicit dependency into explicit scoped invalidation: an opening
    /// edit/add/remove rebuilds ONLY the room(s) it touches plus the (single) building,
    /// never every room and never other openings. Correctness is guaranteed by
    /// over-approximation — a generous proximity margin means we never miss an affected
    /// room; the scoped==full regression test proves the geometry is identical to a full
    /// rebuild. Room-geometry and settings invalidation remain on the global path for
    /// now (A7 stages 2–3).
    /// </summary>
    public static class ScopedRebuild
    {
        // EXOA binds openings to walls at &lt;0.2 m; use a margin so a room near a moved
        // opening is never missed (over-approximate = always correct, just less scoped).
        private const float INFLUENCE = 0.35f;

        /// <summary>Rebuilds only the rooms whose wall any of these opening points touches, plus the building.</summary>
        public static void ForOpeningPositions(List<Vector3> openingWorldPositions)
        {
#if FLOORMAP_MODULE
            if (openingWorldPositions == null || openingWorldPositions.Count == 0)
            {
                GameEditorEvents.OnRequestRebuildBuilding?.Invoke();
                return;
            }
            Exoa.Designer.SpaceController[] spaces =
                GameObject.FindObjectsByType<Exoa.Designer.SpaceController>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (Exoa.Designer.SpaceController sc in spaces)
            {
                if (sc == null || sc.Cpc == null)
                    continue;
                List<Vector3> poly = sc.Cpc.GetPointsWorldPositionList();
                if (poly == null || poly.Count < 2)
                    continue;
                if (AnyPointNearPolygon(openingWorldPositions, poly))
                    sc.RequestRebuild();
            }
            // The building shell/roof is a single controller and legitimately depends on
            // the whole contour set; rebuild it (deferred one frame so an opening's
            // end-of-frame destroy has completed first — one object, not a per-room storm).
            RebuildScheduler.RequestBuildingRebuild();
#else
            GameEditorEvents.OnRequestRebuildAllRooms?.Invoke();
            GameEditorEvents.OnRequestRebuildBuilding?.Invoke();
#endif
        }

        // Margin for RE-SNAPPING an opening to a moved/removed room's walls — wider than
        // INFLUENCE so an opening on a wall that shifted is still caught. (The robust
        // long-term fix is explicit opening→wall binding — logged as A7 stage 2b.)
        private const float REPOSITION_INFLUENCE = 0.6f;

        /// <summary>
        /// A7 stage 2 — re-snap only the openings near this room's walls (replaces the
        /// global OnRequestRepositionOpenings broadcast that re-snapped EVERY opening
        /// whenever any room's path changed).
        /// </summary>
        public static void RepositionOpeningsNear(Exoa.Designer.SpaceController room)
        {
#if FLOORMAP_MODULE
            if (room == null || room.Cpc == null)
                return;
            List<Vector3> poly = room.Cpc.GetPointsWorldPositionList();
            if (poly == null || poly.Count < 2)
                return;
            string roomId = room.UI != null ? room.UI.ItemUniqueId : null;
            Exoa.Designer.OpeningController[] openings =
                GameObject.FindObjectsByType<Exoa.Designer.OpeningController>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (Exoa.Designer.OpeningController o in openings)
            {
                if (o == null || o.Cpc == null)
                    continue;
                // A7 stage 2b: reposition an opening whose REMEMBERED host is this room
                // (exact — follows its wall through any move) OR that is currently within
                // the proximity margin (safety net for newly-added / re-bound openings).
                bool hosted = roomId != null && o.UI != null &&
                              OpeningHostRegistry.IsHostedBy(o.UI.ItemUniqueId, roomId);
                List<Vector3> pts = o.Cpc.GetPointsWorldPositionList();
                bool near = pts != null && AnyPointNearPolygon(pts, poly, REPOSITION_INFLUENCE);
                if (hosted || near)
                    o.RequestReposition();
            }
#endif
        }

        /// <summary>EXOA's exact opening-on-wall test (0.2 m) — used to record host bindings.</summary>
        public static bool IsOpeningOnPolygon(List<Vector3> openingPoints, List<Vector3> roomPolygon)
        {
            if (openingPoints == null || roomPolygon == null || roomPolygon.Count < 2)
                return false;
            return AnyPointNearPolygon(openingPoints, roomPolygon, 0.2f);
        }

        /// <summary>
        /// A7 stage 2 — scoped room/outside deletion: re-snap openings that were on the
        /// removed room, rebuild only rooms adjacent to its footprint (so shared-wall
        /// openings re-bind), and rebuild the single building — instead of the global
        /// OnRequestRebuildAllRooms broadcast.
        /// </summary>
        public static void ForRoomRemoved(List<Vector3> footprint)
        {
#if FLOORMAP_MODULE
            if (footprint == null || footprint.Count < 2)
            {
                GameEditorEvents.OnRequestRebuildAllRooms?.Invoke();
                GameEditorEvents.OnRequestRebuildBuilding?.Invoke();
                return;
            }
            RepositionOpeningsNearPolygon(footprint);
            Exoa.Designer.SpaceController[] spaces =
                GameObject.FindObjectsByType<Exoa.Designer.SpaceController>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (Exoa.Designer.SpaceController sc in spaces)
            {
                if (sc == null || sc.Cpc == null)
                    continue;
                List<Vector3> poly = sc.Cpc.GetPointsWorldPositionList();
                if (poly == null || poly.Count < 2)
                    continue;
                if (PolygonsNear(poly, footprint))
                    sc.RequestRebuild();
            }
            // Deferred: the removed room is destroyed at end of frame; rebuild the shell
            // next frame so it no longer includes the deleted room's contour.
            RebuildScheduler.RequestBuildingRebuild();
#else
            GameEditorEvents.OnRequestRebuildAllRooms?.Invoke();
            GameEditorEvents.OnRequestRebuildBuilding?.Invoke();
#endif
        }

#if FLOORMAP_MODULE
        private static void RepositionOpeningsNearPolygon(List<Vector3> poly)
        {
            Exoa.Designer.OpeningController[] openings =
                GameObject.FindObjectsByType<Exoa.Designer.OpeningController>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (Exoa.Designer.OpeningController o in openings)
            {
                if (o == null || o.Cpc == null)
                    continue;
                List<Vector3> pts = o.Cpc.GetPointsWorldPositionList();
                if (pts != null && AnyPointNearPolygon(pts, poly, REPOSITION_INFLUENCE))
                    o.RequestReposition();
            }
        }

        private static bool PolygonsNear(List<Vector3> a, List<Vector3> b)
        {
            return AnyPointNearPolygon(a, b, REPOSITION_INFLUENCE) ||
                   AnyPointNearPolygon(b, a, REPOSITION_INFLUENCE);
        }
#endif

        private static bool AnyPointNearPolygon(List<Vector3> points, List<Vector3> poly, float margin = INFLUENCE)
        {
            for (int p = 0; p < points.Count; p++)
            {
                Vector3 pt = points[p];
                for (int i = 0; i < poly.Count; i++)
                {
                    Vector3 a = poly[i];
                    Vector3 b = poly[(i + 1) % poly.Count]; // closed polygon
                    if (PointToSegmentXZ(pt, a, b) <= margin)
                        return true;
                }
            }
            return false;
        }

        private static float PointToSegmentXZ(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector2 pp = new Vector2(p.x, p.z), aa = new Vector2(a.x, a.z), bb = new Vector2(b.x, b.z);
            Vector2 ab = bb - aa;
            float len2 = ab.sqrMagnitude;
            float t = len2 > 1e-6f ? Mathf.Clamp01(Vector2.Dot(pp - aa, ab) / len2) : 0f;
            return Vector2.Distance(pp, aa + t * ab);
        }
    }
}

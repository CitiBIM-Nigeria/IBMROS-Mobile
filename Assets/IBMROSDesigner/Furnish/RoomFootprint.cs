using System.Collections.Generic;
using Exoa.Designer;
using UnityEngine;

namespace IBMROS.Designer.Furnish
{
    /// <summary>
    /// Keeps a piece of furniture's ORIENTED footprint inside the room it is in.
    ///
    /// Why not physics: the room's wall colliders are non-convex MeshColliders, which
    /// Unity does not support as the *query* shape for OverlapBox/ComputePenetration,
    /// and the existing BoxCast sweep can only cast an axis-aligned box
    /// (Physics.BoxCast takes an orientation, but the extents came from
    /// Renderer.bounds — a world AABB that has already lost the rotation). So a sofa
    /// turned 45 degrees was tested as its bounding square: too big along the wall,
    /// too small across it, and its real corners could sit inside the wall.
    ///
    /// The room polygon is already in the document, exactly, in metres — so the check
    /// is pure geometry against that polygon: no colliders, no allocation per frame,
    /// no dependence on how the walls happen to be meshed, and correct for L/T/Z rooms
    /// as well as rectangles. It runs as a clamp (slide along the wall) rather than a
    /// rejection, to match the collide-and-slide feel the drag already had.
    ///
    /// Degrades to a no-op in scenes with no floor-plan document (the legacy Room
    /// scene), where the caller's existing floor-bounds clamp remains the only rule.
    /// </summary>
    public static class RoomFootprint
    {
        /// <summary>Default inset from the wall line, metres. Matches ObjectDragHandler.wallMargin.</summary>
        public const float DEFAULT_MARGIN = 0.08f;

        /// <summary>Push-out passes. A corner near two walls needs the second pass to settle.</summary>
        private const int ITERATIONS = 3;

        /// <summary>An item's footprint: a rectangle in the item's own yaw frame.</summary>
        public struct Footprint
        {
            /// <summary>World XZ of the rectangle's centre.</summary>
            public Vector2 Centre;
            /// <summary>Half width / half depth, measured along the item's yawed axes.</summary>
            public Vector2 HalfExtents;
            /// <summary>The item's yaw, degrees.</summary>
            public float YawDeg;

            public bool IsValid => HalfExtents.x > 1e-4f && HalfExtents.y > 1e-4f;

            /// <summary>The four world-XZ corners, in order.</summary>
            public void Corners(Vector2[] into)
            {
                Quaternion yaw = Quaternion.Euler(0f, YawDeg, 0f);
                int k = 0;
                for (int sx = -1; sx <= 1; sx += 2)
                {
                    for (int sz = -1; sz <= 1; sz += 2)
                    {
                        Vector3 local = new Vector3(HalfExtents.x * sx, 0f, HalfExtents.y * sz);
                        Vector3 w = yaw * local;
                        into[k++] = Centre + new Vector2(w.x, w.z);
                    }
                }
            }
        }

        /// <summary>True when this scene has at least one document room to test against.</summary>
        public static bool HasRooms => Plan.PlanEditorUtil.AllSpaces().Count > 0;

        /// <summary>
        /// The item's oriented ground footprint, derived from mesh-local bounds so the
        /// rotation survives (Renderer.bounds is a world AABB and would not).
        /// Skips the blob shadow and particle renderers, as the drag handler does.
        /// </summary>
        public static Footprint Measure(Transform item)
        {
            var fp = new Footprint { YawDeg = item.eulerAngles.y, Centre = new Vector2(item.position.x, item.position.z) };
            Quaternion inv = Quaternion.Inverse(Quaternion.Euler(0f, fp.YawDeg, 0f));

            bool has = false;
            Vector2 min = Vector2.zero, max = Vector2.zero;
            foreach (Renderer r in item.GetComponentsInChildren<Renderer>())
            {
                if (r is ParticleSystemRenderer || r.gameObject.name == "DynamicBlobShadow")
                    continue;
                Bounds lb = r.localBounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = new Vector3(
                        (i & 1) == 0 ? lb.min.x : lb.max.x,
                        (i & 2) == 0 ? lb.min.y : lb.max.y,
                        (i & 4) == 0 ? lb.min.z : lb.max.z);
                    Vector3 world = r.transform.TransformPoint(corner);
                    Vector3 local = inv * (world - item.position);
                    Vector2 xz = new Vector2(local.x, local.z);
                    if (!has) { min = max = xz; has = true; }
                    else { min = Vector2.Min(min, xz); max = Vector2.Max(max, xz); }
                }
            }
            if (!has)
                return fp;

            // The mesh is rarely centred on the pivot; carry that offset into the centre.
            Vector2 offset = (min + max) * 0.5f;
            Vector3 worldOffset = Quaternion.Euler(0f, fp.YawDeg, 0f) * new Vector3(offset.x, 0f, offset.y);
            fp.Centre += new Vector2(worldOffset.x, worldOffset.z);
            fp.HalfExtents = (max - min) * 0.5f;
            return fp;
        }

        /// <summary>
        /// The world-XZ correction to apply to <paramref name="item"/> so its footprint
        /// sits inside its room. Vector2.zero when already inside, when the scene has no
        /// document rooms, or when the footprint cannot be measured.
        /// </summary>
        public static Vector2 Correction(Transform item, float margin = DEFAULT_MARGIN)
        {
            if (item == null)
                return Vector2.zero;
            Footprint fp = Measure(item);
            if (!fp.IsValid)
                return Vector2.zero;

            List<Vector2> room = RoomContaining(fp.Centre);
            if (room == null)
                return Vector2.zero;

            Vector2 start = fp.Centre;
            var corners = new Vector2[4];
            float inward = InwardSign(room);

            for (int pass = 0; pass < ITERATIONS; pass++)
            {
                fp.Corners(corners);
                Vector2 push = Vector2.zero;
                float radius = fp.HalfExtents.magnitude;

                for (int i = 0; i < room.Count; i++)
                {
                    Vector2 a = room[i], b = room[(i + 1) % room.Count];
                    Vector2 edge = b - a;
                    if (edge.sqrMagnitude < 1e-8f)
                        continue;

                    // Only walls the footprint can actually reach constrain it — an
                    // L-shaped room has edges whose infinite lines cut through the
                    // room itself, and those must not clamp anything.
                    if (Plan.PlanEditorUtil.DistToSegmentSq(fp.Centre, a, b, out _) > radius * radius)
                        continue;

                    Vector2 t = edge.normalized;
                    Vector2 n = new Vector2(-t.y, t.x) * inward;   // points into the room

                    float worst = float.MaxValue;
                    for (int c = 0; c < 4; c++)
                        worst = Mathf.Min(worst, Vector2.Dot(corners[c] - a, n));

                    if (worst < margin)
                        push += n * (margin - worst);
                }

                if (push.sqrMagnitude < 1e-10f)
                    break;
                fp.Centre += push;
            }

            return fp.Centre - start;
        }

        /// <summary>Applies <see cref="Correction"/> in place. Returns true if the item moved.</summary>
        public static bool ClampInside(Transform item, float margin = DEFAULT_MARGIN)
        {
            Vector2 fix = Correction(item, margin);
            if (fix.sqrMagnitude < 1e-10f)
                return false;
            item.position += new Vector3(fix.x, 0f, fix.y);
            return true;
        }

        /// <summary>The world XZ position <paramref name="candidate"/> corrected to stay in the room.</summary>
        public static Vector3 Clamped(Transform item, Vector3 candidate, float margin = DEFAULT_MARGIN)
        {
            if (item == null)
                return candidate;
            Vector3 restore = item.position;
            item.position = candidate;
            Vector2 fix = Correction(item, margin);
            item.position = restore;
            return candidate + new Vector3(fix.x, 0f, fix.y);
        }

        // ------------------------------------------------------------------ geometry

        /// <summary>
        /// The room polygon (metres, world XZ) that holds this point, else the nearest
        /// one. Null when the scene has no document rooms.
        /// </summary>
        private static List<Vector2> RoomContaining(Vector2 p)
        {
            List<Vector2> nearest = null;
            float nearestD = float.MaxValue;

            foreach (UIBaseItem ui in Plan.PlanEditorUtil.AllSpaces())
            {
                List<Vector3> world = Plan.PlanEditorUtil.WorldPoints(ui);
                if (world.Count < 3)
                    continue;
                var poly = new List<Vector2>(world.Count);
                foreach (Vector3 w in world)
                    poly.Add(Plan.PlanEditorUtil.WorldToMeters(w));

                if (Contains(poly, p))
                    return poly;

                for (int i = 0; i < poly.Count; i++)
                {
                    float d = Plan.PlanEditorUtil.DistToSegmentSq(p, poly[i], poly[(i + 1) % poly.Count], out _);
                    if (d < nearestD)
                    {
                        nearestD = d;
                        nearest = poly;
                    }
                }
            }
            return nearest;
        }

        /// <summary>+1 when the left normal points into the polygon, -1 otherwise.</summary>
        private static float InwardSign(List<Vector2> poly)
        {
            float area2 = 0f;
            for (int i = 0; i < poly.Count; i++)
            {
                Vector2 a = poly[i], b = poly[(i + 1) % poly.Count];
                area2 += a.x * b.y - b.x * a.y;
            }
            return area2 >= 0f ? 1f : -1f;
        }

        /// <summary>Even-odd point-in-polygon (metres, world XZ).</summary>
        public static bool Contains(List<Vector2> poly, Vector2 p)
        {
            bool inside = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            {
                Vector2 a = poly[i], b = poly[j];
                if (a.y > p.y != b.y > p.y &&
                    p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x)
                    inside = !inside;
            }
            return inside;
        }
    }
}

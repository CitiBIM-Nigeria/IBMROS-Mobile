using System.Collections.Generic;

namespace IBMROS.Core
{
    /// <summary>
    /// Pure 2D polygon-splitting math for room splitting (P3). No Unity dependency, so
    /// it is unit-testable outside the editor (like SnapshotHistory). A split line is
    /// treated as an infinite line; the polygon is cut where that line crosses exactly
    /// two edges, producing two simple sub-polygons. Handles convex rooms and the common
    /// concave cases where the line enters and exits once; returns null (caller keeps the
    /// original room) when the line does not cleanly divide the polygon into two.
    /// </summary>
    public static class PolygonSplit
    {
        public struct Pt
        {
            public float x, y;
            public Pt(float x, float y) { this.x = x; this.y = y; }
        }

        public struct Result
        {
            public bool ok;
            public List<Pt> a;
            public List<Pt> b;
        }

        private const float EPS = 1e-5f;

        /// <summary>
        /// Splits polygon by the line through (lx0,ly0)-(lx1,ly1). Returns two polygons,
        /// or ok=false if the line does not cross exactly two edges.
        /// </summary>
        public static Result Split(IList<Pt> poly, float lx0, float ly0, float lx1, float ly1)
        {
            Result r = new Result { ok = false };
            if (poly == null || poly.Count < 3)
                return r;

            float dx = lx1 - lx0, dy = ly1 - ly0;
            if (dx * dx + dy * dy < EPS)
                return r;

            // Walk edges; where an edge crosses the line, record the intersection and the
            // edge index. We build the two rings by splicing at the two crossing points.
            var crossings = new List<(int edge, Pt p, float t)>(); // t = param along edge
            int n = poly.Count;
            for (int i = 0; i < n; i++)
            {
                Pt p0 = poly[i];
                Pt p1 = poly[(i + 1) % n];
                float s0 = Side(p0, lx0, ly0, dx, dy);
                float s1 = Side(p1, lx0, ly0, dx, dy);
                // Crossing when the endpoints are on opposite sides (strictly).
                if ((s0 > EPS && s1 < -EPS) || (s0 < -EPS && s1 > EPS))
                {
                    float t = s0 / (s0 - s1); // fraction along p0->p1 where side==0
                    Pt ip = new Pt(p0.x + t * (p1.x - p0.x), p0.y + t * (p1.y - p0.y));
                    crossings.Add((i, ip, t));
                }
            }

            if (crossings.Count != 2)
                return r; // not a clean two-edge cut

            int e0 = crossings[0].edge, e1 = crossings[1].edge;
            Pt i0 = crossings[0].p, i1 = crossings[1].p;

            // Ring A: from just after crossing e0, around to crossing e1, closing i1->i0.
            var a = new List<Pt> { i0 };
            for (int k = (e0 + 1) % n; ; k = (k + 1) % n)
            {
                a.Add(poly[k]);
                if (k == e1) break;
            }
            a.Add(i1);

            // Ring B: from just after crossing e1, around to crossing e0, closing i0->i1.
            var b = new List<Pt> { i1 };
            for (int k = (e1 + 1) % n; ; k = (k + 1) % n)
            {
                b.Add(poly[k]);
                if (k == e0) break;
            }
            b.Add(i0);

            if (a.Count < 3 || b.Count < 3)
                return r;
            if (Area(a) < EPS || Area(b) < EPS)
                return r;

            r.ok = true;
            r.a = a;
            r.b = b;
            return r;
        }

        // Signed distance-ish of point p from the line (lx0,ly0)+t*(dx,dy). Sign = side.
        private static float Side(Pt p, float lx0, float ly0, float dx, float dy)
        {
            return (p.x - lx0) * dy - (p.y - ly0) * dx;
        }

        /// <summary>Absolute polygon area (shoelace).</summary>
        public static float Area(IList<Pt> poly)
        {
            float s = 0f;
            int n = poly.Count;
            for (int i = 0; i < n; i++)
            {
                Pt a = poly[i];
                Pt b = poly[(i + 1) % n];
                s += a.x * b.y - b.x * a.y;
            }
            return (s < 0 ? -s : s) * 0.5f;
        }
    }
}

using System.Collections.Generic;
using UnityEngine;
using OpenRoomPlan.Core;

namespace OpenRoomPlan.Reconstruction
{
    /// <summary>
    /// Level-B evaluation (spec §6): compare a reconstructed <see cref="RoomModel"/> against a ground-truth
    /// one (LiDAR/RoomPlan or tape-measured) and produce the <see cref="ReconstructionMetrics"/> that drive
    /// the Go/No-Go call (spec §2). Pure geometry over the parametric models — both models must be in the
    /// same world frame (guaranteed by the dual-path single-session design, spec §3).
    /// </summary>
    public static class RoomEval
    {
        /// <summary>Corner-match + wall-match tolerance used for the completeness proxy.</summary>
        const float CompletenessOffsetTolMeters = 0.10f; // τ = 10 cm (spec §6)
        const float CompletenessAngleTolDeg = 10f;

        public static ReconstructionMetrics Compare(in RoomModel candidate, in RoomModel gt)
        {
            var m = new ReconstructionMetrics
            {
                producedValidRoom = candidate.valid,
                medianCornerErrorMeters = float.NaN,
                meanCornerErrorMeters = float.NaN,
                meanWallAngleErrorDeg = float.NaN,
                meanWallOffsetMeters = float.NaN,
                floorplanIoU = float.NaN,
                dimensionErrorPct = float.NaN,
                wallCompletenessPct = float.NaN,
            };
            if (!candidate.valid || !gt.valid) return m;

            // --- Corner error (XZ, nearest-match candidate→GT) ---
            if (candidate.footprint != null && gt.footprint != null &&
                candidate.footprint.Length > 0 && gt.footprint.Length > 0)
            {
                var errs = new List<float>(candidate.footprint.Length);
                foreach (var c in candidate.footprint)
                {
                    float best = float.MaxValue;
                    foreach (var g in gt.footprint)
                        best = Mathf.Min(best, Vector2.Distance(c, g));
                    errs.Add(best);
                }
                errs.Sort();
                m.meanCornerErrorMeters = Mean(errs);
                m.medianCornerErrorMeters = errs.Count % 2 == 1
                    ? errs[errs.Count / 2]
                    : 0.5f * (errs[errs.Count / 2 - 1] + errs[errs.Count / 2]);
            }

            // --- Wall angle + offset (match each GT wall to the closest-normal candidate wall) ---
            if (candidate.walls != null && gt.walls != null && candidate.walls.Length > 0 && gt.walls.Length > 0)
            {
                float angleSum = 0f, offsetSum = 0f;
                int matched = 0, complete = 0;

                foreach (var g in gt.walls)
                {
                    float bestAngle = float.MaxValue;
                    float bestOffset = float.MaxValue;
                    foreach (var c in candidate.walls)
                    {
                        // Normals as undirected lines (solver sign is arbitrary): angle in [0, 90].
                        float cos = Mathf.Clamp(Mathf.Abs(Vector3.Dot(g.normal, c.normal)), 0f, 1f);
                        float ang = Mathf.Acos(cos) * Mathf.Rad2Deg;
                        if (ang < bestAngle)
                        {
                            bestAngle = ang;
                            // Perp distance between near-parallel planes, aligning the normal sign first.
                            float sign = Vector3.Dot(g.normal, c.normal) >= 0f ? 1f : -1f;
                            bestOffset = Mathf.Abs(g.offset - sign * c.offset);
                        }
                    }
                    angleSum += bestAngle;
                    offsetSum += bestOffset;
                    matched++;
                    if (bestAngle <= CompletenessAngleTolDeg && bestOffset <= CompletenessOffsetTolMeters)
                        complete++;
                }

                if (matched > 0)
                {
                    m.meanWallAngleErrorDeg = angleSum / matched;
                    m.meanWallOffsetMeters = offsetSum / matched;
                    // Proxy for spec's area completeness: % of GT walls with a candidate wall within τ/10°.
                    m.wallCompletenessPct = 100f * complete / matched;
                }
            }

            // --- Floorplan IoU (convex polygon clip; v1 footprints are rectangles) ---
            if (candidate.footprint != null && gt.footprint != null &&
                candidate.footprint.Length >= 3 && gt.footprint.Length >= 3)
            {
                float aC = Mathf.Abs(SignedArea(candidate.footprint));
                float aG = Mathf.Abs(SignedArea(gt.footprint));
                float inter = Mathf.Abs(SignedArea(ClipConvex(candidate.footprint, gt.footprint)));
                float union = aC + aG - inter;
                if (union > 1e-6f) m.floorplanIoU = inter / union;
            }

            // --- Dimension error (axes may swap; compare sorted horizontal extents, height directly) ---
            {
                Vector2 cHor = SortedXY(candidate.dimensions);
                Vector2 gHor = SortedXY(gt.dimensions);
                float e0 = RelErr(cHor.x, gHor.x);
                float e1 = RelErr(cHor.y, gHor.y);
                float e2 = RelErr(candidate.dimensions.z, gt.dimensions.z);
                m.dimensionErrorPct = 100f * (e0 + e1 + e2) / 3f;
            }

            return m;
        }

        /// <summary>Spec §2 tier for a single room's metrics. Judged on the decision metrics only.</summary>
        public static string Tier(in ReconstructionMetrics m)
        {
            if (!m.producedValidRoom) return "RED (no valid closed room)";
            bool green = m.medianCornerErrorMeters <= 0.08f && m.meanWallAngleErrorDeg <= 3f && m.floorplanIoU >= 0.90f;
            if (green) return "GREEN";
            bool yellow = m.medianCornerErrorMeters <= 0.15f && m.meanWallAngleErrorDeg <= 6f && m.floorplanIoU >= 0.80f;
            return yellow ? "YELLOW" : "RED";
        }

        // --- helpers ---

        static float Mean(List<float> v)
        {
            float s = 0f; foreach (var x in v) s += x; return v.Count > 0 ? s / v.Count : float.NaN;
        }

        static float RelErr(float est, float gt) => gt > 1e-4f ? Mathf.Abs(est - gt) / gt : 0f;

        static Vector2 SortedXY(Vector3 dims) =>
            dims.x >= dims.y ? new Vector2(dims.x, dims.y) : new Vector2(dims.y, dims.x);

        /// <summary>Shoelace; positive for CCW.</summary>
        static float SignedArea(IReadOnlyList<Vector2> poly)
        {
            if (poly == null || poly.Count < 3) return 0f;
            float a = 0f;
            for (int i = 0; i < poly.Count; i++)
            {
                Vector2 p = poly[i], q = poly[(i + 1) % poly.Count];
                a += p.x * q.y - q.x * p.y;
            }
            return 0.5f * a;
        }

        /// <summary>Sutherland–Hodgman: clip convex <paramref name="subject"/> by convex <paramref name="clip"/> (any winding).</summary>
        static List<Vector2> ClipConvex(IReadOnlyList<Vector2> subject, IReadOnlyList<Vector2> clip)
        {
            var output = new List<Vector2>(subject);
            // Ensure CCW clip polygon so "inside" is a consistent half-plane test.
            var clipCCW = new List<Vector2>(clip);
            if (SignedArea(clipCCW) < 0f) clipCCW.Reverse();

            for (int e = 0; e < clipCCW.Count && output.Count > 0; e++)
            {
                Vector2 a = clipCCW[e], b = clipCCW[(e + 1) % clipCCW.Count];
                var input = output;
                output = new List<Vector2>(input.Count + 2);

                for (int i = 0; i < input.Count; i++)
                {
                    Vector2 p = input[i], q = input[(i + 1) % input.Count];
                    bool pIn = Cross(a, b, p) >= 0f;
                    bool qIn = Cross(a, b, q) >= 0f;

                    if (pIn) output.Add(p);
                    if (pIn != qIn) output.Add(Intersect(a, b, p, q));
                }
            }
            return output;
        }

        static float Cross(Vector2 a, Vector2 b, Vector2 p) =>
            (b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x);

        static Vector2 Intersect(Vector2 a, Vector2 b, Vector2 p, Vector2 q)
        {
            Vector2 ab = b - a, pq = q - p;
            float denom = ab.x * pq.y - ab.y * pq.x;
            if (Mathf.Abs(denom) < 1e-9f) return p; // parallel — degenerate, caller's polygons are convex
            float t = ((a.x - p.x) * ab.y - (a.y - p.y) * ab.x) / -denom;
            return p + pq * Mathf.Clamp01(t);
        }
    }
}

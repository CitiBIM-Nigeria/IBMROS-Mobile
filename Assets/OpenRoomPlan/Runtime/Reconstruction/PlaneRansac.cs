using System.Collections.Generic;
using UnityEngine;

namespace OpenRoomPlan.Reconstruction
{
    /// <summary>A plane: dot(n, x) = d, with n unit-length.</summary>
    public struct Plane3
    {
        public Vector3 n;
        public float d;
    }

    public struct ExtractedPlane
    {
        public Plane3 plane;
        public List<int> inliers;
        public Vector3 centroid;
        public float yMin, yMax;
    }

    /// <summary>
    /// Sequential RANSAC plane extraction (dominant-plane removal): find the biggest plane, remove its
    /// inliers, repeat. Normals come from the RANSAC hypothesis; the offset is refit as the inlier mean
    /// (walls/floor/ceiling get orientation-regularized downstream by the Manhattan solver anyway).
    /// </summary>
    public static class PlaneRansac
    {
        public static List<ExtractedPlane> Extract(
            IReadOnlyList<Vector3> points,
            int maxPlanes = 10, float distThresh = 0.04f, int iterations = 300, int minInliers = 300)
        {
            var results = new List<ExtractedPlane>();
            int n = points.Count;
            if (n < minInliers) return results;

            var remaining = new List<int>(n);
            for (int i = 0; i < n; i++) remaining.Add(i);

            for (int p = 0; p < maxPlanes && remaining.Count >= minInliers; p++)
            {
                Plane3 best = default;
                int bestCount = 0;

                for (int it = 0; it < iterations; it++)
                {
                    int i0 = remaining[Random.Range(0, remaining.Count)];
                    int i1 = remaining[Random.Range(0, remaining.Count)];
                    int i2 = remaining[Random.Range(0, remaining.Count)];
                    if (i0 == i1 || i1 == i2 || i0 == i2) continue;

                    Vector3 a = points[i0], b = points[i1], c = points[i2];
                    Vector3 nrm = Vector3.Cross(b - a, c - a);
                    float mag = nrm.magnitude;
                    if (mag < 1e-6f) continue;
                    nrm /= mag;
                    float dd = Vector3.Dot(nrm, a);

                    int count = 0;
                    for (int r = 0; r < remaining.Count; r++)
                    {
                        float dist = Mathf.Abs(Vector3.Dot(nrm, points[remaining[r]]) - dd);
                        if (dist < distThresh) count++;
                    }
                    if (count > bestCount) { bestCount = count; best = new Plane3 { n = nrm, d = dd }; }
                }

                if (bestCount < minInliers) break;

                // Collect inliers, refit offset as their mean, gather stats.
                var inliers = new List<int>(bestCount);
                var still = new List<int>(remaining.Count - bestCount);
                Vector3 centroid = Vector3.zero;
                float yMin = float.MaxValue, yMax = float.MinValue;
                float offsetSum = 0f;

                foreach (int idx in remaining)
                {
                    float dist = Mathf.Abs(Vector3.Dot(best.n, points[idx]) - best.d);
                    if (dist < distThresh)
                    {
                        inliers.Add(idx);
                        Vector3 pt = points[idx];
                        centroid += pt;
                        offsetSum += Vector3.Dot(best.n, pt);
                        if (pt.y < yMin) yMin = pt.y;
                        if (pt.y > yMax) yMax = pt.y;
                    }
                    else still.Add(idx);
                }

                best.d = offsetSum / inliers.Count; // refit offset
                centroid /= inliers.Count;

                results.Add(new ExtractedPlane { plane = best, inliers = inliers, centroid = centroid, yMin = yMin, yMax = yMax });
                remaining = still;
            }

            return results;
        }
    }
}

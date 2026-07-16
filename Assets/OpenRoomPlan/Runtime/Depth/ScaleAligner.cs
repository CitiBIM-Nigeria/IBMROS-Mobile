using System.Collections.Generic;
using UnityEngine;
using OpenRoomPlan.Core;

namespace OpenRoomPlan.Depth
{
    /// <summary>
    /// Turns a relative / affine-invariant depth map into metric depth by least-squares fitting an affine
    /// transform against metric anchors. Two anchor sources, per spec §7:
    ///   • a reference metric depth map (platform ARCore/ARKit depth, or LiDAR GT) — the dense path, used
    ///     offline over recorded sessions where per-frame VIO points weren't logged;
    ///   • sparse world-space anchors (VIO feature points) projected into the frame — the on-device path.
    ///
    /// For a DISPARITY net the fit happens in disparity space (a·disp + b ≈ 1/metric) and depth is then
    /// 1/(a·disp+b); for a relative-DEPTH net it fits a·rel + b ≈ metric. The fitted scale factor's
    /// per-frame stability is the "scaleFactorStdDev" the eval report tracks (spec §6, Level A).
    /// </summary>
    public static class ScaleAligner
    {
        public struct Fit
        {
            public float scale;    // a
            public float shift;    // b
            public int anchorCount;
            public bool valid;
            public float scaleFactor => scale; // convenience alias for the stability metric
        }

        /// <summary>
        /// Align <paramref name="rel"/> (rw×rh) to metres using <paramref name="refMetric"/> (fw×fh) as
        /// dense anchors. The reference is resampled (nearest) onto the rel grid. Returns a new metric
        /// buffer (NaN where invalid); null if too few valid overlapping samples to fit.
        /// </summary>
        public static float[] AlignToReferenceDepth(
            float[] rel, int rw, int rh, bool relIsInverse,
            float[] refMetric, int fw, int fh,
            out Fit fit, float minRefMeters = 0.3f, float maxRefMeters = 8f, int minAnchors = 50)
        {
            fit = default;
            if (rel == null || refMetric == null || rw <= 0 || rh <= 0 || fw <= 0 || fh <= 0) return null;

            var xs = new List<float>();
            var ys = new List<float>();
            float sxToRef = fw / (float)rw, syToRef = fh / (float)rh;

            for (int y = 0; y < rh; y++)
            {
                int ry = Mathf.Clamp((int)(y * syToRef), 0, fh - 1);
                for (int x = 0; x < rw; x++)
                {
                    float rv = rel[y * rw + x];
                    if (!IsUsableRel(rv, relIsInverse)) continue;

                    int rx = Mathf.Clamp((int)(x * sxToRef), 0, fw - 1);
                    float mv = refMetric[ry * fw + rx];
                    if (float.IsNaN(mv) || mv < minRefMeters || mv > maxRefMeters) continue;

                    xs.Add(rv);
                    ys.Add(relIsInverse ? 1f / mv : mv); // target is disparity when the net is inverse
                }
            }

            if (xs.Count < minAnchors || !SolveAffine(xs, ys, out float a, out float b)) return null;
            fit = new Fit { scale = a, shift = b, anchorCount = xs.Count, valid = true };
            return Apply(rel, rw, rh, relIsInverse, a, b);
        }

        /// <summary>
        /// Align <paramref name="rel"/> using sparse world-space metric anchors projected into the frame
        /// with <paramref name="intr"/> (stated at intr.width/height) and camera <paramref name="pose"/>.
        /// </summary>
        public static float[] AlignToAnchors(
            float[] rel, int rw, int rh, bool relIsInverse,
            in CameraIntrinsics intr, in Pose pose, IReadOnlyList<Vector3> anchorsWorld,
            out Fit fit, int minAnchors = 8)
        {
            fit = default;
            if (rel == null || anchorsWorld == null || anchorsWorld.Count == 0 || !intr.IsValid) return null;

            // Scale intrinsics from their stated resolution to the rel-map resolution.
            float sx = rw / (float)intr.width, sy = rh / (float)intr.height;
            float fx = intr.fx * sx, fy = intr.fy * sy, cx = intr.cx * sx, cy = intr.cy * sy;
            if (fx <= 0f || fy <= 0f) return null;

            var invRot = Quaternion.Inverse(pose.rotation);
            var xs = new List<float>();
            var ys = new List<float>();

            foreach (var pWorld in anchorsWorld)
            {
                Vector3 local = invRot * (pWorld - pose.position);
                float z = local.z; // metres along camera forward
                if (z <= 0.1f) continue;

                // Inverse of DepthBackprojector's mapping (camY = -(v-cy)/fy*d).
                float u = fx * local.x / z + cx;
                float v = cy - fy * local.y / z;
                int iu = Mathf.RoundToInt(u), iv = Mathf.RoundToInt(v);
                if (iu < 0 || iu >= rw || iv < 0 || iv >= rh) continue;

                float rv = rel[iv * rw + iu];
                if (!IsUsableRel(rv, relIsInverse)) continue;

                xs.Add(rv);
                ys.Add(relIsInverse ? 1f / z : z);
            }

            if (xs.Count < minAnchors || !SolveAffine(xs, ys, out float a, out float b)) return null;
            fit = new Fit { scale = a, shift = b, anchorCount = xs.Count, valid = true };
            return Apply(rel, rw, rh, relIsInverse, a, b);
        }

        static bool IsUsableRel(float v, bool inverse)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) return false;
            return inverse ? v > 1e-6f : v > 0f;
        }

        static float[] Apply(float[] rel, int rw, int rh, bool inverse, float a, float b)
        {
            var outMetric = new float[rw * rh];
            for (int i = 0; i < outMetric.Length; i++)
            {
                float v = rel[i];
                if (!IsUsableRel(v, inverse)) { outMetric[i] = float.NaN; continue; }
                if (inverse)
                {
                    float disp = a * v + b;                 // fitted metric disparity
                    outMetric[i] = disp > 1e-4f ? 1f / disp : float.NaN;
                }
                else
                {
                    float d = a * v + b;
                    outMetric[i] = d > 0f ? d : float.NaN;
                }
            }
            return outMetric;
        }

        /// <summary>Ordinary least squares y = a·x + b. Returns false if x has ~no variance.</summary>
        static bool SolveAffine(List<float> xs, List<float> ys, out float a, out float b)
        {
            a = 1f; b = 0f;
            int n = xs.Count;
            if (n < 2) return false;
            double sx = 0, sy = 0, sxx = 0, sxy = 0;
            for (int i = 0; i < n; i++)
            {
                double x = xs[i], y = ys[i];
                sx += x; sy += y; sxx += x * x; sxy += x * y;
            }
            double denom = n * sxx - sx * sx;
            if (System.Math.Abs(denom) < 1e-9) return false;
            a = (float)((n * sxy - sx * sy) / denom);
            b = (float)((sy - a * sx) / n);
            return !(float.IsNaN(a) || float.IsNaN(b) || float.IsInfinity(a) || float.IsInfinity(b));
        }
    }
}

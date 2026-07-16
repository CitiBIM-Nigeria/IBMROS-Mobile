using UnityEngine;

namespace OpenRoomPlan.Depth
{
    /// <summary>
    /// Confidence-weighted merge of platform depth (ARCore/ARKit) with the scale-aligned net depth —
    /// the V2 "fused" candidate default from the depth strategy: trust the platform where it is confident,
    /// let the net fill the holes it cannot see (textureless walls, long range). Both inputs are assumed
    /// METRIC (run <see cref="ScaleAligner"/> on the net first). Output is on the platform grid when a
    /// platform map is supplied, else the net grid.
    /// </summary>
    public static class DepthFusion
    {
        /// <param name="platformConf">Optional 0..1 confidence for the platform map (same grid); null = trust all valid.</param>
        /// <param name="confThreshold">At/above this platform confidence, take platform outright.</param>
        public static float[] Fuse(
            float[] platform, float[] platformConf, int pw, int ph,
            float[] net, int nw, int nh,
            out int w, out int h,
            float minMeters = 0.3f, float maxMeters = 8f, float confThreshold = 0.5f)
        {
            // No platform map (e.g. non-LiDAR iOS with no env-depth API): the net is all we have.
            if (platform == null || pw <= 0 || ph <= 0)
            {
                w = nw; h = nh;
                return net == null ? null : Clamp(net, nw, nh, minMeters, maxMeters);
            }

            w = pw; h = ph;
            var outDepth = new float[pw * ph];
            float sx = nw / (float)pw, sy = nh / (float)ph;
            bool haveNet = net != null && nw > 0 && nh > 0;

            for (int y = 0; y < ph; y++)
            {
                int ny = haveNet ? Mathf.Clamp((int)(y * sy), 0, nh - 1) : 0;
                for (int x = 0; x < pw; x++)
                {
                    int idx = y * pw + x;
                    float pv = platform[idx];
                    bool pValid = Valid(pv, minMeters, maxMeters);
                    float conf = platformConf != null ? platformConf[idx] : (pValid ? 1f : 0f);

                    float nv = float.NaN;
                    if (haveNet)
                    {
                        int nx = Mathf.Clamp((int)(x * sx), 0, nw - 1);
                        nv = net[ny * nw + nx];
                    }
                    bool nValid = Valid(nv, minMeters, maxMeters);

                    if (pValid && conf >= confThreshold) outDepth[idx] = pv;           // trust platform
                    else if (pValid && nValid) outDepth[idx] = conf * pv + (1f - conf) * nv; // blend
                    else if (pValid) outDepth[idx] = pv;
                    else if (nValid) outDepth[idx] = nv;                                 // net fills the hole
                    else outDepth[idx] = float.NaN;
                }
            }
            return outDepth;
        }

        static bool Valid(float v, float lo, float hi) => !float.IsNaN(v) && v >= lo && v <= hi;

        static float[] Clamp(float[] src, int w, int h, float lo, float hi)
        {
            var o = new float[w * h];
            for (int i = 0; i < o.Length; i++) o[i] = Valid(src[i], lo, hi) ? src[i] : float.NaN;
            return o;
        }
    }
}

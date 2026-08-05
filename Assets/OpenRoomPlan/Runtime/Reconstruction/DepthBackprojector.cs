using UnityEngine;
using OpenRoomPlan.Core;

namespace OpenRoomPlan.Reconstruction
{
    /// <summary>
    /// Back-projects a metric depth map into world-space points using intrinsics + camera pose.
    ///
    /// AXIS CONVENTION (validate on device): assumes depth = distance along camera forward (+Z local),
    /// image origin top-left, Unity camera local axes (+X right, +Y up, +Z forward). If the first
    /// on-device clouds come out mirrored/flipped, flip <paramref name="flipV"/> or the camY/camX sign here.
    /// </summary>
    public static class DepthBackprojector
    {
        public static void Accumulate(
            PointCloud cloud, float[] depth, int dw, int dh,
            in CameraIntrinsics intr, in Pose pose,
            float minDepth = 0.3f, float maxDepth = 5.0f, int stride = 2, bool flipV = false)
        {
            if (depth == null || depth.Length < dw * dh || !intr.IsValid) return;

            // Intrinsics were stored at intr.width x intr.height (the RGB res); rescale to the
            // depth map res.
            //
            // WHY THIS IS NOT AN INDEPENDENT X/Y SCALE. It used to be sx=dw/W, sy=dh/H applied
            // separately, which is only right when depth and RGB share an aspect ratio. On a
            // Pixel 8 Pro they do not: ARCore reports 160x90 depth (16:9) while the recorded RGB
            // is 384x288 (4:3). Independent scaling then gives the depth camera NON-SQUARE
            // pixels — measured fy 87.5 where square pixels demand ~116.6, a 33% vertical error
            // that shears every back-projected point.
            //
            // Physical model: one sensor, square pixels, so fx and fy must scale by the SAME
            // factor. A differing aspect means the smaller-aspect image is a CENTRE CROP of the
            // other's field of view, which moves the principal point but not the focal length.
            // Corroborated by the data: under this model cy lands at 44.6 for a 90-px-tall depth
            // map, i.e. the image centre, which the independent scaling only reached by accident.
            //
            // Same-aspect providers (e.g. ARKit 256x192 depth against 4:3 RGB) get a zero crop
            // term, so their behaviour is unchanged.
            //
            // NOTE: this assumes the crop is centred. Worth confirming per provider by checking
            // that a depth edge lands on the matching RGB edge; a non-centred crop would show as
            // a constant offset between the two.
            float s = dw / (float)intr.width;
            float expectedH = intr.height * s;
            float cropY = (expectedH - dh) * 0.5f;
            if (cropY < 0f)
            {
                // Depth is TALLER in aspect than the RGB: it shares the vertical FOV instead and
                // is cropped horizontally. Re-derive from height so fx/fy stay equal-scaled.
                s = dh / (float)intr.height;
                cropY = 0f;
            }
            float cropX = (intr.width * s - dw) * 0.5f;

            float fx = intr.fx * s, fy = intr.fy * s;
            float cx = intr.cx * s - cropX;
            float cy = intr.cy * s - cropY;
            if (fx <= 0f || fy <= 0f) return;

            var rot = pose.rotation;
            var pos = pose.position;

            for (int v = 0; v < dh; v += stride)
            {
                int vv = flipV ? (dh - 1 - v) : v;
                for (int u = 0; u < dw; u += stride)
                {
                    float d = depth[v * dw + u];
                    if (float.IsNaN(d) || d < minDepth || d > maxDepth) continue;

                    float camX = (u - cx) / fx * d;
                    float camY = -(vv - cy) / fy * d; // image v grows down; Unity Y grows up
                    float camZ = d;

                    cloud.Add(pos + rot * new Vector3(camX, camY, camZ));
                }
            }
        }
    }
}

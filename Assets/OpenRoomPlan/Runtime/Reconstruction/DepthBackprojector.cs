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

            // Intrinsics were stored at intr.width x intr.height (RGB res); rescale to the depth map res.
            float sx = dw / (float)intr.width;
            float sy = dh / (float)intr.height;
            float fx = intr.fx * sx, fy = intr.fy * sy;
            float cx = intr.cx * sx, cy = intr.cy * sy;
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

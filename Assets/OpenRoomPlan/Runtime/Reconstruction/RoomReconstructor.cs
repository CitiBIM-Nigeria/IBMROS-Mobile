using System.Collections.Generic;
using UnityEngine;
using OpenRoomPlan.Core;

namespace OpenRoomPlan.Reconstruction
{
    /// <summary>
    /// End-to-end room solver: accumulate posed depth frames into a downsampled world cloud, extract
    /// planes (RANSAC), and regularize into a parametric <see cref="RoomModel"/> (Manhattan). Pure
    /// geometry — depends only on Core, so it runs identically on-device and in offline Editor eval.
    ///
    /// Feed it any depth source (platform, DA-V2-S, or a fused result) via AddFrame(); the source is
    /// just which float[] you pass, so the same solver scores every model in the benchmark.
    /// </summary>
    public sealed class RoomReconstructor
    {
        readonly PointCloud _cloud;
        public RoomReconstructor(float voxelSize = 0.03f) => _cloud = new PointCloud(voxelSize);

        public int PointCount => _cloud.Count;
        public IReadOnlyList<Vector3> Points => _cloud.Points;

        public void AddFrame(
            float[] depth, int width, int height,
            in CameraIntrinsics intr, in Pose pose,
            float minDepth = 0.3f, float maxDepth = 5f, int stride = 2, bool flipV = false)
        {
            DepthBackprojector.Accumulate(_cloud, depth, width, height, intr, pose, minDepth, maxDepth, stride, flipV);
        }

        public RoomModel Solve(ManhattanSolver.Config cfg = default)
        {
            var planes = PlaneRansac.Extract(_cloud.Points);
            return ManhattanSolver.BuildRoom(planes, Vector3.up, cfg);
        }
    }
}

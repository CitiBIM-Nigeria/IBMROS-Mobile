using System.Collections.Generic;
using UnityEngine;
using OpenRoomPlan.Core;

namespace OpenRoomPlan.Reconstruction
{
    /// <summary>How posed depth frames are accumulated into world geometry.</summary>
    public enum AccumulatorKind
    {
        RawPointCloud,   // one point per occupied voxel, first observation wins (fast, no denoising)
        Tsdf,            // weighted running-average TSDF: noise fuses out across frames (report Stage 4)
    }

    /// <summary>
    /// End-to-end room solver: accumulate posed depth frames into a world map, extract planes (RANSAC),
    /// and regularize into a parametric <see cref="RoomModel"/> (Manhattan). Pure geometry — depends only
    /// on Core, so it runs identically on-device and in offline Editor eval.
    ///
    /// Feed it any depth source (platform, DA-V2-S, or a fused result) via AddFrame(); the source is
    /// just which float[] you pass, so the same solver scores every model in the benchmark. The
    /// accumulator is selectable: the raw cloud preserves Phase-1 behaviour; the TSDF averages away
    /// per-frame depth noise before the solver sees it.
    /// </summary>
    public sealed class RoomReconstructor
    {
        readonly PointCloud _cloud;
        readonly TsdfVolume _tsdf;
        readonly float _surfaceMinWeight;

        public RoomReconstructor(float voxelSize = 0.03f,
                                 AccumulatorKind accumulator = AccumulatorKind.RawPointCloud,
                                 float tsdfSurfaceMinWeight = 2f)
        {
            _surfaceMinWeight = tsdfSurfaceMinWeight;
            if (accumulator == AccumulatorKind.Tsdf) _tsdf = new TsdfVolume(voxelSize);
            else _cloud = new PointCloud(voxelSize);
        }

        public AccumulatorKind Accumulator => _tsdf != null ? AccumulatorKind.Tsdf : AccumulatorKind.RawPointCloud;
        public TsdfVolume Tsdf => _tsdf; // null unless AccumulatorKind.Tsdf (semantic/BEV access)

        public int PointCount => Points.Count;
        public IReadOnlyList<Vector3> Points =>
            _tsdf != null ? _tsdf.SurfacePoints(_surfaceMinWeight) : _cloud.Points;

        public void AddFrame(
            float[] depth, int width, int height,
            in CameraIntrinsics intr, in Pose pose,
            float minDepth = 0.3f, float maxDepth = 5f, int stride = 2, bool flipV = false,
            byte[] semantics = null)
        {
            if (_tsdf != null)
                _tsdf.IntegrateFrame(depth, width, height, intr, pose, minDepth, maxDepth, stride, flipV, semantics);
            else
                DepthBackprojector.Accumulate(_cloud, depth, width, height, intr, pose, minDepth, maxDepth, stride, flipV);
        }

        public RoomModel Solve(ManhattanSolver.Config cfg = default)
        {
            var planes = PlaneRansac.Extract(Points);
            return ManhattanSolver.BuildRoom(planes, Vector3.up, cfg);
        }
    }
}

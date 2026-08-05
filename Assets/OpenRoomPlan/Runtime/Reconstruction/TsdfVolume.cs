using System.Collections.Generic;
using UnityEngine;
using OpenRoomPlan.Core;

namespace OpenRoomPlan.Reconstruction
{
    /// <summary>
    /// Voxel-hashed semantic TSDF (report Stage 4, the primary scene map): truncated signed distance
    /// per voxel, fused as a weighted running average across frames, so depth noise — inevitable from
    /// motion-stereo and a small monocular net — averages away instead of accumulating, which the raw
    /// <see cref="PointCloud"/> cannot do. Memory stays proportional to observed surface via hashing.
    ///
    /// Each voxel optionally carries a <see cref="SemanticClass"/> fused by majority vote, so per-pixel
    /// segmentation (Phase 2b) plugs in with no structural change. CPU implementation for the offline
    /// harness; the API is the contract a later GPU-compute port fills in behind.
    ///
    /// Axis convention matches <see cref="DepthBackprojector"/> exactly (camX=(u-cx)/fx·d,
    /// camY=−(v−cy)/fy·d, camZ=d, optional flipV) so TSDF and raw-cloud reconstructions are comparable.
    /// </summary>
    public sealed class TsdfVolume
    {
        struct Voxel
        {
            public float tsdf;        // truncated signed distance, meters (+ = free space in front)
            public float weight;      // accumulated observation weight, capped
            public byte classId;      // current majority SemanticClass
            public sbyte classVotes;  // Boyer-Moore style majority counter
        }

        readonly Dictionary<long, Voxel> _voxels = new();
        readonly float _voxelSize;
        readonly float _truncation;    // meters; band around the surface that gets updated
        readonly float _maxWeight;

        List<Vector3> _surfaceCache;
        float _surfaceCacheMinWeight = -1f;

        public TsdfVolume(float voxelSize = 0.03f, float truncationVoxels = 4f, float maxWeight = 64f)
        {
            _voxelSize = voxelSize;
            _truncation = voxelSize * truncationVoxels;
            _maxWeight = maxWeight;
        }

        public int VoxelCount => _voxels.Count;
        public float VoxelSize => _voxelSize;

        /// <summary>
        /// Fuse one posed metric depth frame. <paramref name="semantics"/> is an optional per-pixel
        /// <see cref="SemanticClass"/> map on the same grid as the depth (null = geometry only).
        /// </summary>
        public void IntegrateFrame(
            float[] depth, int dw, int dh,
            in CameraIntrinsics intr, in Pose pose,
            float minDepth = 0.3f, float maxDepth = 5f, int stride = 2, bool flipV = false,
            byte[] semantics = null, bool depthIsRadial = false)
        {
            if (depth == null || depth.Length < dw * dh || !intr.IsValid) return;

            // Single scale factor plus a centre-crop term — one sensor means square pixels, so
            // fx and fy cannot scale independently. This is the same correction as
            // DepthBackprojector; it was fixed there first and this path was still wrong,
            // which is why the TSDF accumulator looked no better than the raw cloud.
            float s_ = dw / (float)intr.width;
            float expectedH = intr.height * s_;
            float cropY = (expectedH - dh) * 0.5f;
            if (cropY < 0f) { s_ = dh / (float)intr.height; cropY = 0f; }
            float cropX = (intr.width * s_ - dw) * 0.5f;

            float fx = intr.fx * s_, fy = intr.fy * s_;
            float cx = intr.cx * s_ - cropX, cy = intr.cy * s_ - cropY;
            if (fx <= 0f || fy <= 0f) return;

            var rot = pose.rotation;
            var pos = pose.position;
            _surfaceCache = null; // invalidate

            for (int v = 0; v < dh; v += stride)
            {
                int vv = flipV ? (dh - 1 - v) : v;
                for (int u = 0; u < dw; u += stride)
                {
                    float d = depth[v * dw + u];
                    if (float.IsNaN(d) || d < minDepth || d > maxDepth) continue;

                    byte cls = semantics != null ? semantics[v * dw + u] : (byte)SemanticClass.Unknown;

                    // Camera-space ray for this pixel, parameterized by Z-depth s: p_cam(s) = dirCam · s.
                    Vector3 dirCam = new Vector3((u - cx) / fx, -(vv - cy) / fy, 1f);
                    Vector3 dirWorld = rot * dirCam;

                    // The band below is walked in z, so a radial range must become a z first
                    // (see DepthBackprojector.Accumulate's depthIsRadial note). |dirCam| is
                    // exactly the ray-length factor, and it is already to hand.
                    if (depthIsRadial) d /= dirCam.magnitude;

                    // Update the truncation band around the observed surface. Step < voxel size along
                    // the ray so no voxel in the band is skipped.
                    float s0 = Mathf.Max(minDepth * 0.5f, d - _truncation);
                    float s1 = d + _truncation;
                    float step = _voxelSize * 0.7f / Mathf.Max(1f, dirCam.magnitude);

                    for (float s = s0; s <= s1; s += step)
                    {
                        Vector3 p = pos + dirWorld * s;
                        long key = VoxelKey(p);
                        float sdf = Mathf.Clamp(d - s, -_truncation, _truncation);

                        _voxels.TryGetValue(key, out var vox);
                        float w = vox.weight;
                        vox.tsdf = (vox.tsdf * w + sdf) / (w + 1f);
                        vox.weight = Mathf.Min(w + 1f, _maxWeight);

                        // Semantics only vote near the surface (free-space samples say nothing about class).
                        if (cls != (byte)SemanticClass.Unknown && Mathf.Abs(sdf) <= _voxelSize)
                        {
                            if (vox.classVotes == 0) { vox.classId = cls; vox.classVotes = 1; }
                            else if (vox.classId == cls) vox.classVotes = (sbyte)Mathf.Min(vox.classVotes + 1, sbyte.MaxValue);
                            else vox.classVotes--;
                        }

                        _voxels[key] = vox;
                    }
                }
            }
        }

        /// <summary>
        /// Denoised surface points: centers of voxels near the zero crossing seen at least
        /// <paramref name="minWeight"/> times. Feeds the same RANSAC → Manhattan solver as the raw cloud.
        /// </summary>
        public IReadOnlyList<Vector3> SurfacePoints(float minWeight = 2f)
        {
            if (_surfaceCache != null && Mathf.Approximately(_surfaceCacheMinWeight, minWeight))
                return _surfaceCache;

            float band = _voxelSize * 0.5f;
            var pts = new List<Vector3>(_voxels.Count / 8);
            foreach (var kv in _voxels)
            {
                var vox = kv.Value;
                if (vox.weight >= minWeight && Mathf.Abs(vox.tsdf) <= band)
                    pts.Add(VoxelCenter(kv.Key));
            }
            _surfaceCache = pts;
            _surfaceCacheMinWeight = minWeight;
            return pts;
        }

        /// <summary>Surface points of one semantic class (for the Phase-2b BEV/opening/furniture stages).</summary>
        public List<Vector3> SurfacePoints(SemanticClass cls, float minWeight = 2f)
        {
            float band = _voxelSize * 0.5f;
            var pts = new List<Vector3>();
            foreach (var kv in _voxels)
            {
                var vox = kv.Value;
                if (vox.weight >= minWeight && Mathf.Abs(vox.tsdf) <= band &&
                    vox.classId == (byte)cls && vox.classVotes > 0)
                    pts.Add(VoxelCenter(kv.Key));
            }
            return pts;
        }

        /// <summary>
        /// RoomPlan-style BEV density pseudo-image (report Stage C input): per XZ cell, the count of
        /// surface voxels with Y in [yMin, yMax]. Returns row-major grid; <paramref name="origin"/> is
        /// the world XZ of cell (0,0)'s corner. Feeds the future wall line-net; useful now for debugging.
        /// </summary>
        public float[] RasterizeBevDensity(
            float yMin, float yMax, float cellSize, out Vector2 origin, out int gw, out int gh,
            float minWeight = 2f)
        {
            var pts = SurfacePoints(minWeight);
            origin = Vector2.zero; gw = gh = 0;
            if (pts.Count == 0) return null;

            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            foreach (var p in pts)
            {
                if (p.y < yMin || p.y > yMax) continue;
                if (p.x < minX) minX = p.x; if (p.x > maxX) maxX = p.x;
                if (p.z < minZ) minZ = p.z; if (p.z > maxZ) maxZ = p.z;
            }
            if (minX > maxX) return null;

            origin = new Vector2(minX, minZ);
            gw = Mathf.Max(1, Mathf.CeilToInt((maxX - minX) / cellSize) + 1);
            gh = Mathf.Max(1, Mathf.CeilToInt((maxZ - minZ) / cellSize) + 1);
            var grid = new float[gw * gh];

            foreach (var p in pts)
            {
                if (p.y < yMin || p.y > yMax) continue;
                int gx = Mathf.Clamp((int)((p.x - minX) / cellSize), 0, gw - 1);
                int gz = Mathf.Clamp((int)((p.z - minZ) / cellSize), 0, gh - 1);
                grid[gz * gw + gx] += 1f;
            }
            return grid;
        }

        // --- voxel hashing (same folding scheme as PointCloud) ---

        long VoxelKey(Vector3 p)
        {
            long x = Mathf.FloorToInt(p.x / _voxelSize) & 0x1FFFFF;
            long y = Mathf.FloorToInt(p.y / _voxelSize) & 0x1FFFFF;
            long z = Mathf.FloorToInt(p.z / _voxelSize) & 0x1FFFFF;
            return (x << 42) | (y << 21) | z;
        }

        Vector3 VoxelCenter(long key)
        {
            // Unfold 21-bit fields back to signed ints (sign-extend from bit 20).
            int x = SignExtend21((int)((key >> 42) & 0x1FFFFF));
            int y = SignExtend21((int)((key >> 21) & 0x1FFFFF));
            int z = SignExtend21((int)(key & 0x1FFFFF));
            return new Vector3((x + 0.5f) * _voxelSize, (y + 0.5f) * _voxelSize, (z + 0.5f) * _voxelSize);
        }

        static int SignExtend21(int v) => (v & 0x100000) != 0 ? v | ~0x1FFFFF : v;
    }
}

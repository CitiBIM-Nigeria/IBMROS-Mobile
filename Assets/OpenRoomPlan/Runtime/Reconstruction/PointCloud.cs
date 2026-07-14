using System.Collections.Generic;
using UnityEngine;

namespace OpenRoomPlan.Reconstruction
{
    /// <summary>
    /// Growable world-space point buffer with voxel-grid downsampling so memory stays proportional to
    /// observed surface, not to the number of frames integrated. One point per occupied voxel (proto of
    /// the Phase-2 voxel-hashed TSDF).
    /// </summary>
    public sealed class PointCloud
    {
        public readonly List<Vector3> Points = new();
        readonly HashSet<long> _occupied = new();
        readonly float _voxelSize;

        public PointCloud(float voxelSize = 0.03f) => _voxelSize = voxelSize;

        public int Count => Points.Count;

        public void Add(Vector3 p)
        {
            if (_occupied.Add(VoxelKey(p))) Points.Add(p);
        }

        long VoxelKey(Vector3 p)
        {
            long x = Mathf.FloorToInt(p.x / _voxelSize) & 0x1FFFFF; // 21 bits each (signed range folded)
            long y = Mathf.FloorToInt(p.y / _voxelSize) & 0x1FFFFF;
            long z = Mathf.FloorToInt(p.z / _voxelSize) & 0x1FFFFF;
            return (x << 42) | (y << 21) | z;
        }
    }
}

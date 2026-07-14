using System;
using UnityEngine;

namespace OpenRoomPlan.Core
{
    /// <summary>A vertical wall as a plane in world space: dot(normal, x) = offset. Normal is horizontal.</summary>
    [Serializable]
    public struct WallPlane
    {
        public Vector3 normal;   // unit, horizontal (y≈0), points into the room
        public float offset;     // plane constant: dot(normal, point) = offset
        public float yMin, yMax; // observed vertical extent (meters, world Y)
        public int inlierCount;
        public bool manhattanSnapped;
    }

    /// <summary>
    /// The parametric room — the system's output "contract" and what we score against ground truth.
    /// v1 footprint is an oriented rectangle in the Manhattan frame; a general polygon comes in Phase 2.
    /// </summary>
    [Serializable]
    public struct RoomModel
    {
        public bool valid;                 // produced a closed room?
        public Vector3 up;                 // gravity up used (world Y for AR)
        public float floorY, ceilingY;

        public WallPlane[] walls;
        public Vector2[] footprint;        // XZ corners, ordered CCW, at floorY
        public Vector3 dimensions;         // (length, width, height) meters
        public bool isRectilinearApprox;   // true while footprint is a rectangle

        // Manhattan frame (horizontal, orthonormal) the room was regularized to.
        public Vector3 axisRight;
        public Vector3 axisForward;

        public static RoomModel Invalid => new RoomModel { valid = false };
    }
}

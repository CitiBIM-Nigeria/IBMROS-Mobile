using UnityEngine;
using OpenRoomPlan.Core;

namespace OpenRoomPlan.Reconstruction
{
    /// <summary>
    /// Scene-view visualizer for eval: draws the accumulated point cloud and reconstructed room(s) as
    /// gizmos so you can orbit and eyeball the result (and catch a mirrored/flipped backprojection).
    /// Populated by the Eval Tool window; not meant to ship.
    /// </summary>
    public sealed class RoomPreview : MonoBehaviour
    {
        [HideInInspector] public Vector3[] points;
        public RoomModel candidate;
        public bool hasCandidate;
        public RoomModel groundTruth;
        public bool hasGroundTruth;

        public float pointSize = 0.02f;
        public int maxPointsDrawn = 6000;

        void OnDrawGizmos()
        {
            if (points != null && points.Length > 0)
            {
                Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.75f);
                int step = Mathf.Max(1, points.Length / Mathf.Max(1, maxPointsDrawn));
                for (int i = 0; i < points.Length; i += step)
                    Gizmos.DrawCube(points[i], Vector3.one * pointSize);
            }

            if (hasCandidate) DrawRoom(candidate, new Color(0.3f, 1f, 0.4f));       // green = candidate
            if (hasGroundTruth) DrawRoom(groundTruth, new Color(1f, 0.85f, 0.2f));  // yellow = LiDAR GT
        }

        static void DrawRoom(RoomModel m, Color c)
        {
            if (!m.valid || m.footprint == null || m.footprint.Length < 2) return;
            Gizmos.color = c;
            int n = m.footprint.Length;
            for (int i = 0; i < n; i++)
            {
                Vector2 a = m.footprint[i], b = m.footprint[(i + 1) % n];
                var af = new Vector3(a.x, m.floorY, a.y);
                var bf = new Vector3(b.x, m.floorY, b.y);
                var ac = new Vector3(a.x, m.ceilingY, a.y);
                Gizmos.DrawLine(af, bf);                                   // floor edge
                Gizmos.DrawLine(ac, new Vector3(b.x, m.ceilingY, b.y));    // ceiling edge
                Gizmos.DrawLine(af, ac);                                   // vertical edge
            }
        }
    }
}

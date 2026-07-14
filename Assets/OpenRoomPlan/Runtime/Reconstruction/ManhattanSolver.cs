using System.Collections.Generic;
using UnityEngine;
using OpenRoomPlan.Core;

namespace OpenRoomPlan.Reconstruction
{
    /// <summary>
    /// Turns RANSAC planes into a parametric room: classify floor/ceiling/walls by orientation vs gravity,
    /// regularize wall directions to a Manhattan grid, and build an oriented-rectangle footprint (v1).
    /// A general (non-rectangular) polygon is Phase 2; the adversarial angled-wall room is expected to
    /// fail here and is reported as such.
    /// </summary>
    public static class ManhattanSolver
    {
        public struct Config
        {
            public float horizontalCosThreshold; // |dot(n,up)| above => horizontal plane
            public float wallCosThreshold;        // |dot(n,up)| below => wall
            public float manhattanToleranceDeg;   // snap to grid if within this of a 90° multiple
            public float minRoomSpanMeters;       // reject degenerate footprints

            public static Config Default => new Config
            {
                horizontalCosThreshold = 0.85f,
                wallCosThreshold = 0.35f,
                manhattanToleranceDeg = 25f,
                minRoomSpanMeters = 0.5f,
            };
        }

        public static RoomModel BuildRoom(List<ExtractedPlane> planes, Vector3 up, Config cfg = default)
        {
            if (cfg.horizontalCosThreshold <= 0f) cfg = Config.Default;
            up = up.normalized;
            if (planes == null || planes.Count == 0) return RoomModel.Invalid;

            var horizontals = new List<ExtractedPlane>();
            var wallPlanes = new List<ExtractedPlane>();
            foreach (var pl in planes)
            {
                float cosUp = Mathf.Abs(Vector3.Dot(pl.plane.n, up));
                if (cosUp > cfg.horizontalCosThreshold) horizontals.Add(pl);
                else if (cosUp < cfg.wallCosThreshold) wallPlanes.Add(pl);
                // else: slanted surface, ignored in v1
            }
            if (wallPlanes.Count == 0) return RoomModel.Invalid;

            // Floor / ceiling from horizontals (lowest / highest centroid). Tables can fool this in v1.
            bool hasFloor = false;
            float floorY = 0f, ceilingY = float.NaN;
            if (horizontals.Count > 0)
            {
                float minY = float.MaxValue, maxY = float.MinValue;
                foreach (var h in horizontals)
                {
                    if (h.centroid.y < minY) { minY = h.centroid.y; }
                    if (h.centroid.y > maxY) { maxY = h.centroid.y; }
                }
                floorY = minY; ceilingY = maxY; hasFloor = true;
            }

            // Reference Manhattan direction = most-supported wall.
            ExtractedPlane refWall = wallPlanes[0];
            foreach (var w in wallPlanes) if (w.inliers.Count > refWall.inliers.Count) refWall = w;
            float ref0Deg = HorizontalAngleDeg(refWall.plane.n, up);

            Vector3 right = HorizontalDir(ref0Deg, up);
            Vector3 forward = HorizontalDir(ref0Deg + 90f, up);

            // Snap each wall; split into the two Manhattan axes by projected coordinate.
            var outWalls = new List<WallPlane>(wallPlanes.Count);
            var rightCoords = new List<float>();   // walls whose normal is along 'right'
            var forwardCoords = new List<float>();  // walls whose normal is along 'forward'
            float wallYMax = float.MinValue;

            foreach (var w in wallPlanes)
            {
                float angDeg = HorizontalAngleDeg(w.plane.n, up);
                float rel = Mathf.DeltaAngle(ref0Deg, angDeg);      // [-180,180]
                int k = Mathf.RoundToInt(rel / 90f);
                float residual = Mathf.Abs(rel - k * 90f);
                bool snapped = residual <= cfg.manhattanToleranceDeg;

                float snappedAngle = snapped ? ref0Deg + k * 90f : angDeg;
                Vector3 nrm = HorizontalDir(snappedAngle, up);
                float offset = Vector3.Dot(nrm, w.centroid);

                outWalls.Add(new WallPlane
                {
                    normal = nrm, offset = offset,
                    yMin = w.yMin, yMax = w.yMax,
                    inlierCount = w.inliers.Count, manhattanSnapped = snapped,
                });
                if (w.yMax > wallYMax) wallYMax = w.yMax;

                if (snapped)
                {
                    if ((Mathf.Abs(k) & 1) == 0) rightCoords.Add(Vector3.Dot(w.centroid, right));
                    else forwardCoords.Add(Vector3.Dot(w.centroid, forward));
                }
            }

            if (!hasFloor) { floorY = Mathf.Min(WallMinY(wallPlanes), 0f); }
            if (float.IsNaN(ceilingY)) ceilingY = wallYMax;

            var model = new RoomModel
            {
                up = up, floorY = floorY, ceilingY = ceilingY,
                walls = outWalls.ToArray(),
                axisRight = right, axisForward = forward,
                isRectilinearApprox = true,
            };

            // Oriented rectangle from the extreme walls in each axis.
            if (rightCoords.Count >= 1 && forwardCoords.Count >= 1)
            {
                float rMin = Min(rightCoords), rMax = Max(rightCoords);
                float fMin = Min(forwardCoords), fMax = Max(forwardCoords);
                float lenR = rMax - rMin, lenF = fMax - fMin;

                if (lenR >= cfg.minRoomSpanMeters && lenF >= cfg.minRoomSpanMeters)
                {
                    model.footprint = new[]
                    {
                        Corner(right, forward, rMin, fMin),
                        Corner(right, forward, rMax, fMin),
                        Corner(right, forward, rMax, fMax),
                        Corner(right, forward, rMin, fMax),
                    };
                    model.dimensions = new Vector3(lenR, lenF, Mathf.Max(0f, ceilingY - floorY));
                    model.valid = true;
                }
            }

            return model;
        }

        // --- helpers ---

        static float HorizontalAngleDeg(Vector3 n, Vector3 up)
        {
            Vector3 h = n - up * Vector3.Dot(n, up);
            if (h.sqrMagnitude < 1e-8f) return 0f;
            h.Normalize();
            return Mathf.Atan2(h.z, h.x) * Mathf.Rad2Deg;
        }

        static Vector3 HorizontalDir(float angleDeg, Vector3 up)
        {
            float r = angleDeg * Mathf.Deg2Rad;
            Vector3 d = new Vector3(Mathf.Cos(r), 0f, Mathf.Sin(r));
            // Re-orthogonalize against up in case up isn't exactly world-Y.
            d -= up * Vector3.Dot(d, up);
            return d.normalized;
        }

        static Vector2 Corner(Vector3 right, Vector3 forward, float r, float f)
        {
            Vector3 w = right * r + forward * f;
            return new Vector2(w.x, w.z);
        }

        static float WallMinY(List<ExtractedPlane> walls)
        {
            float m = float.MaxValue;
            foreach (var w in walls) if (w.yMin < m) m = w.yMin;
            return m == float.MaxValue ? 0f : m;
        }

        static float Min(List<float> v) { float m = float.MaxValue; foreach (var x in v) if (x < m) m = x; return m; }
        static float Max(List<float> v) { float m = float.MinValue; foreach (var x in v) if (x > m) m = x; return m; }
    }
}

using System.Collections.Generic;
using Exoa.Designer;
using UnityEngine;
using Grid = Exoa.Designer.Grid;

namespace IBMROS.Designer.Plan
{
    /// <summary>Which 2D tool is armed (owned by PlanTouchController).</summary>
    public enum PlanToolMode { Browse, AddDoor, AddWindow, DrawRect }

    /// <summary>
    /// Shared math/lookup helpers for the touch 2D editor. Everything is meters
    /// on the world XZ plane (INTEGRATION_MAP §1); screen conversions go through
    /// the live camera. No state lives here.
    /// </summary>
    public static class PlanEditorUtil
    {
        private static Camera cam;
        private static Grid grid;

        public static Camera Cam
        {
            get
            {
                if (cam == null) cam = Camera.main;
                return cam;
            }
        }

        public static Grid SceneGrid
        {
            get
            {
                if (grid == null) grid = Object.FindAnyObjectByType<Grid>();
                return grid;
            }
        }

        /// <summary>Ray↔ground-plane (y=0) intersection; math plane, no colliders.</summary>
        public static bool ScreenToGround(Vector2 screenPos, out Vector3 world)
        {
            world = default;
            Camera c = Cam;
            if (c == null) return false;
            Ray ray = c.ScreenPointToRay(screenPos);
            Plane ground = new Plane(Vector3.up, Vector3.zero);
            if (!ground.Raycast(ray, out float enter)) return false;
            world = ray.GetPoint(enter);
            return true;
        }

        public static Vector2 WorldToMeters(Vector3 world) => new Vector2(world.x, world.z);
        public static Vector3 MetersToWorld(Vector2 m) => new Vector3(m.x, 0f, m.y);

        public static Vector2 WorldToScreen(Vector3 world)
        {
            Vector3 s = Cam.WorldToScreenPoint(world);
            return new Vector2(s.x, s.y);
        }

        /// <summary>0.5 m grid snap via the scene grid (same snap the vendor uses).</summary>
        public static Vector3 Snap(Vector3 world)
        {
            Grid g = SceneGrid;
            return g != null ? g.GetNearestPointOnGrid(world) : world;
        }

        /// <summary>Resolve a document item id to its live UI item (same scan the gateway uses).</summary>
        public static UIBaseItem FindItem(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return null;
            foreach (UIBaseItem ui in Object.FindObjectsByType<UIBaseItem>(FindObjectsSortMode.None))
                if (ui.ItemUniqueId == itemId)
                    return ui;
            return null;
        }

        /// <summary>All items that have polygon geometry (rooms + outside areas).</summary>
        public static List<UIBaseItem> AllSpaces()
        {
            var list = new List<UIBaseItem>();
            foreach (UIBaseItem ui in Object.FindObjectsByType<UIBaseItem>(FindObjectsSortMode.None))
            {
                if (ui.sequencingItemType == DataModel.FloorMapItemType.Room ||
                    ui.sequencingItemType == DataModel.FloorMapItemType.Outside)
                    list.Add(ui);
            }
            return list;
        }

        public static bool IsOpening(UIBaseItem ui) =>
            ui != null &&
            (ui.sequencingItemType == DataModel.FloorMapItemType.Door ||
             ui.sequencingItemType == DataModel.FloorMapItemType.Window ||
             ui.sequencingItemType == DataModel.FloorMapItemType.Opening);

        /// <summary>Current world-space control points of an item (empty list when unavailable).</summary>
        public static List<Vector3> WorldPoints(UIBaseItem ui)
        {
            if (ui == null || ui.cpc == null) return new List<Vector3>();
            List<Vector3> pts = ui.cpc.GetPointsWorldPositionList();
            return pts ?? new List<Vector3>();
        }

        /// <summary>Squared distance from p to segment ab (2D meters).</summary>
        public static float DistToSegmentSq(Vector2 p, Vector2 a, Vector2 b, out Vector2 closest)
        {
            Vector2 ab = b - a;
            float len = ab.sqrMagnitude;
            float t = len > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / len) : 0f;
            closest = a + ab * t;
            return (p - closest).sqrMagnitude;
        }

        /// <summary>
        /// Nearest wall segment across all spaces. Returns false when none is
        /// within maxDistMeters. Outputs the item, segment index (i → i+1 wrap),
        /// the projected point and the wall tangent.
        /// </summary>
        public static bool NearestWall(Vector2 pMeters, float maxDistMeters,
            out UIBaseItem item, out int segIndex, out Vector2 pointOnWall, out Vector2 tangent)
        {
            item = null; segIndex = -1; pointOnWall = default; tangent = Vector2.right;
            float best = maxDistMeters * maxDistMeters;
            foreach (UIBaseItem ui in AllSpaces())
            {
                List<Vector3> pts = WorldPoints(ui);
                for (int i = 0; i < pts.Count; i++)
                {
                    Vector2 a = WorldToMeters(pts[i]);
                    Vector2 b = WorldToMeters(pts[(i + 1) % pts.Count]);
                    float d = DistToSegmentSq(pMeters, a, b, out Vector2 c);
                    if (d < best)
                    {
                        best = d;
                        item = ui; segIndex = i; pointOnWall = c;
                        tangent = (b - a).sqrMagnitude > 1e-8f ? (b - a).normalized : Vector2.right;
                    }
                }
            }
            return item != null;
        }

        /// <summary>Screen-space pixels per meter at the ground plane (ortho-safe).</summary>
        public static float PixelsPerMeter()
        {
            Camera c = Cam;
            if (c == null) return 100f;
            Vector2 s0 = WorldToScreen(Vector3.zero);
            Vector2 s1 = WorldToScreen(new Vector3(1f, 0f, 0f));
            float d = Vector2.Distance(s0, s1);
            return d > 0.01f ? d : 100f;
        }
    }
}

using System;
using System.Collections.Generic;
using Exoa.Designer;
using IBMROS.Bridge.UndoRedo;
using UnityEngine;
using static Exoa.Designer.DataModel;

namespace IBMROS.Core
{
    /// <summary>
    /// Architecture track A3 (v1) — the mutation gateway.
    ///
    /// One programmatic surface for editing the floor plan, used the same way by UI,
    /// AI and XR callers (DD §8.7/§11: route every mutation through one gateway so
    /// undo, labels, validation and events stay uniform). This is also the seed of
    /// P9's FloorPlanAPI: methods take METERS (world XZ), never grid coordinates —
    /// conversion happens internally via the scene grid.
    ///
    /// v1 scope: operations flow through the same proven creation paths the load code
    /// uses (UIFloorMapMenu item creation → controllers → rebuild events), each wrapped
    /// in a labelled undo action. A2 step 2 will retarget the write side at the
    /// document model directly; callers won't notice.
    /// </summary>
    public static class FloorPlanEditor
    {
        public static bool IsAvailable =>
            GameObject.FindAnyObjectByType<FloorMapSerializer>() != null &&
            GameObject.FindAnyObjectByType<UIFloorMapMenu>() != null &&
            GameObject.FindAnyObjectByType<Exoa.Designer.Grid>() != null;

        /// <summary>Creates a room from a polygon in meters (world XZ). Returns the item id, or null.</summary>
        public static string CreateRoom(IList<Vector2> pointsMeters, string name = null)
        {
            if (pointsMeters == null || pointsMeters.Count < 3)
                return null;
            FloorMapItem item = NewItem(FloorMapItemType.Room, name);
            item.normalizedPositions = ToNormalized(pointsMeters);
            if (item.normalizedPositions == null)
                return null;
            return Commit(item, "Create Room");
        }

        /// <summary>Minimum side length (meters) for a rectangular room created by two corners.</summary>
        public const float MIN_RECT_SIDE = 0.5f;

        /// <summary>
        /// Creates an axis-aligned rectangular room from two opposite corners in meters
        /// (world XZ). Returns null when the corners are closer than MIN_RECT_SIDE on
        /// either axis (degenerate / accidental tap). One undo step.
        /// </summary>
        public static string CreateRectRoomFromCorners(Vector2 cornerA, Vector2 cornerB, string name = null)
        {
            float w = Mathf.Abs(cornerB.x - cornerA.x);
            float l = Mathf.Abs(cornerB.y - cornerA.y);
            if (w < MIN_RECT_SIDE || l < MIN_RECT_SIDE)
                return null;
            Vector2 center = new Vector2((cornerA.x + cornerB.x) * 0.5f, (cornerA.y + cornerB.y) * 0.5f);
            return CreateRectRoom(w, l, center, name);
        }

        /// <summary>Creates an axis-aligned rectangular room (meters), centred on centerMeters.</summary>
        public static string CreateRectRoom(float widthMeters, float lengthMeters, Vector2 centerMeters, string name = null)
        {
            float hw = widthMeters * 0.5f, hl = lengthMeters * 0.5f;
            return CreateRoom(new List<Vector2>
            {
                new Vector2(centerMeters.x - hw, centerMeters.y - hl),
                new Vector2(centerMeters.x + hw, centerMeters.y - hl),
                new Vector2(centerMeters.x + hw, centerMeters.y + hl),
                new Vector2(centerMeters.x - hw, centerMeters.y + hl),
            }, name);
        }

        /// <summary>
        /// Splits a room (or outside area) with an infinite line through two points in
        /// meters (world XZ), replacing it with two rooms. Openings re-bind to whichever
        /// wall they end up nearest (proximity). Returns the two new ids, or (null,null)
        /// if the line does not cleanly divide the room. One undo step. P3 room-split.
        /// </summary>
        public static (string a, string b) SplitRoom(string roomId, Vector2 lineP0, Vector2 lineP1)
        {
            UIBaseItem ui = FindItem(roomId);
            if (ui == null || ui.cpc == null)
                return (null, null);
            if (ui.sequencingItemType != FloorMapItemType.Room && ui.sequencingItemType != FloorMapItemType.Outside)
                return (null, null);

            List<Vector3> world = ui.cpc.GetPointsWorldPositionList();
            if (world == null || world.Count < 3)
                return (null, null);
            List<PolygonSplit.Pt> poly = new List<PolygonSplit.Pt>(world.Count);
            foreach (Vector3 w in world)
                poly.Add(new PolygonSplit.Pt(w.x, w.z));

            PolygonSplit.Result res = PolygonSplit.Split(poly, lineP0.x, lineP0.y, lineP1.x, lineP1.y);
            if (!res.ok)
                return (null, null);

            FloorMapItemType type = ui.sequencingItemType;
            string name = ui.Name;
            string idA, idB;
            using (BeginAction("Split Room"))
            {
                ui.Delete();
                idA = CreatePolyOfType(type, res.a, name);
                idB = CreatePolyOfType(type, res.b, name);
            }
            return (idA, idB);
        }

        private static string CreatePolyOfType(FloorMapItemType type, List<PolygonSplit.Pt> pts, string name)
        {
            List<Vector2> meters = new List<Vector2>(pts.Count);
            foreach (PolygonSplit.Pt p in pts)
                meters.Add(new Vector2(p.x, p.y));
            return type == FloorMapItemType.Outside ? CreateOutside(meters, name) : CreateRoom(meters, name);
        }

        /// <summary>Creates an outside area (terrace) from a polygon in meters.</summary>
        public static string CreateOutside(IList<Vector2> pointsMeters, string name = null)
        {
            if (pointsMeters == null || pointsMeters.Count < 3)
                return null;
            FloorMapItem item = NewItem(FloorMapItemType.Outside, name);
            item.normalizedPositions = ToNormalized(pointsMeters);
            if (item.normalizedPositions == null)
                return null;
            return Commit(item, "Create Outside Area");
        }

        /// <summary>
        /// Places a door/window/opening at a point in meters on (or within 0.2 m of) a
        /// wall line. wallTangent is the direction the wall runs (unit vector, XZ).
        /// </summary>
        public static string AddOpening(FloorMapItemType type, Vector2 positionMeters, Vector2 wallTangent,
            float width, float height, float ypos = 0f, string name = null)
        {
            if (type != FloorMapItemType.Door && type != FloorMapItemType.Window && type != FloorMapItemType.Opening)
                return null;
            FloorMapItem item = NewItem(type, name);
            item.width = width;
            item.height = height;
            item.ypos = ypos;
            item.normalizedPositions = ToNormalized(new List<Vector2> { positionMeters });
            if (item.normalizedPositions == null)
                return null;
            Vector2 t = wallTangent.sqrMagnitude > 0f ? wallTangent.normalized : Vector2.right;
            item.directions = new List<Vector3> { new Vector3(t.x, 0f, t.y) };
            return Commit(item, "Place " + type);
        }

        /// <summary>
        /// Sets an item's scalar settings (any non-null argument is applied). Rooms use
        /// width/height only where meaningful; openings use width/height/ypos + window
        /// params. One undo step. Returns false when the item isn't found.
        /// </summary>
        public static bool SetItemSettings(string itemId,
            float? width = null, float? height = null, float? ypos = null,
            bool? hasWindow = null, float? windowFrameSize = null,
            float? windowSizeH = null, float? windowSizeV = null,
            int? windowSubDivH = null, int? windowSubDivV = null)
        {
            UIBaseItem ui = FindItem(itemId);
            if (ui == null)
                return false;
            using (BeginAction("Change Settings"))
            {
                // Setting a widget value fires its onValueChanged → BroadcastChange →
                // rebuild + DocumentEvents, so these writes flow through the exact path
                // a user drag uses. All within one undo scope.
                if (width.HasValue) ui.Width = width.Value;
                if (height.HasValue) ui.Height = height.Value;
                if (ypos.HasValue) ui.YPos = ypos.Value;
                if (hasWindow.HasValue) ui.HasWindow = hasWindow.Value;
                if (windowFrameSize.HasValue) ui.WindowFrameSize = windowFrameSize.Value;
                if (windowSizeH.HasValue) ui.WindowSizeH = windowSizeH.Value;
                if (windowSizeV.HasValue) ui.WindowSizeV = windowSizeV.Value;
                if (windowSubDivH.HasValue) ui.WindowSubDivH = windowSubDivH.Value;
                if (windowSubDivV.HasValue) ui.WindowSubDivV = windowSubDivV.Value;
            }
            return true;
        }

        /// <summary>
        /// Sets global building construction settings (any non-null argument applied),
        /// folding in the UIBuildingSettings mutation paths. One undo step.
        /// </summary>
        public static void SetBuildingSettings(
            float? wallsHeight = null, float? doorsHeight = null,
            float? interiorWallThickness = null, float? exteriorWallThickness = null,
            float? windowsThickness = null, float? doorsThickness = null)
        {
            AppController app = AppController.Instance;
            if (app == null)
                return;
            using (BeginAction("Building Settings"))
            {
                if (wallsHeight.HasValue) app.wallsHeight = wallsHeight.Value;
                if (doorsHeight.HasValue) app.doorsHeight = doorsHeight.Value;
                if (interiorWallThickness.HasValue) app.interiorWallThickness = interiorWallThickness.Value;
                if (exteriorWallThickness.HasValue) app.exteriorWallThickness = exteriorWallThickness.Value;
                if (windowsThickness.HasValue) app.windowsThickness = windowsThickness.Value;
                if (doorsThickness.HasValue) app.doorsThickness = doorsThickness.Value;
                // Superset of the per-field rebuilds UIBuildingSettings fires.
                Exoa.Events.GameEditorEvents.OnRequestRebuildAllOpenings?.Invoke();
                Exoa.Events.GameEditorEvents.OnRequestRebuildAllRooms?.Invoke();
                Exoa.Events.GameEditorEvents.OnRequestRebuildBuilding?.Invoke();
                DocumentEvents.RaiseChanged(DocumentChangeKind.BuildingSettings, "Building Settings");
            }
        }

        /// <summary>All space item ids in the current document (query surface for AI/UI/tests).</summary>
        public static List<string> GetItemIds()
        {
            FloorPlanDocument doc = SyncedDocument();
            return doc != null ? doc.GetAllItemIds() : new List<string>();
        }

        /// <summary>The current settings/state of one item as JSON, or null if not found.</summary>
        public static string GetItemJson(string itemId)
        {
            FloorPlanDocument doc = SyncedDocument();
            if (doc == null)
                return null;
            bool found;
            FloorMapItem item = doc.GetItemById(itemId, out found);
            if (!found)
                return null;
            return Exoa.Json.JsonConvert.SerializeObject(item, Exoa.Json.Formatting.Indented,
                new Exoa.Json.JsonSerializerSettings { ReferenceLoopHandling = Exoa.Json.ReferenceLoopHandling.Ignore });
        }

        /// <summary>
        /// Replaces an item's polygon/position with new points in meters (world XZ).
        /// The point count must match the item's current control points (in-place move,
        /// no GameObject churn); returns false otherwise. One undo step.
        /// </summary>
        public static bool MoveItemPoints(string itemId, IList<Vector2> pointsMeters)
        {
            UIBaseItem ui = FindItem(itemId);
            if (ui == null || ui.cpc == null || pointsMeters == null)
                return false;
            if (ui.cpc.GetPointsList().Count != pointsMeters.Count || pointsMeters.Count == 0)
                return false;
            List<Vector3> normalized = ToNormalized(pointsMeters);
            if (normalized == null)
                return false;
            FloorMapItem target = ui.GetData();
            target.normalizedPositions = normalized;
            bool isRoom = ui.sequencingItemType == FloorMapItemType.Room ||
                          ui.sequencingItemType == FloorMapItemType.Outside;

            // An opening is a HOLE in someone's wall, so moving one has to rebuild
            // the wall it is leaving as well as the wall it is arriving at —
            // otherwise the old hole stays punched and the new one is never cut.
            // Capture the departure position before the points move.
            List<Vector3> beforeWorld = null;
            if (!isRoom)
            {
                beforeWorld = new List<Vector3>(ui.cpc.GetPointsWorldPositionList());
            }

            using (BeginAction("Move Points"))
            {
                // Same in-place recipe the A5 reconciler uses for geometry restores.
                IBMROS.Bridge.UndoRedo.RestoreReconciler.MovePointsInPlace(ui, target);
                // A7 2b: moving a room's walls carries its hosted openings — re-snap them
                // (the host registry lets them follow even a large move).
                if (isRoom && ui.drawer is Exoa.Designer.SpaceController sc)
                    ScopedRebuild.RepositionOpeningsNear(sc);
                else if (!isRoom)
                {
                    // Old host first (close), then the new one (cut). Both go through
                    // the scoped rebuild so untouched rooms are left alone.
                    if (beforeWorld != null && beforeWorld.Count > 0)
                        ScopedRebuild.ForOpeningPositions(beforeWorld);
                    ScopedRebuild.ForOpeningPositions(ui.cpc.GetPointsWorldPositionList());
                }
                DocumentEvents.RaiseChanged(DocumentChangeKind.Geometry, "Move Points");
            }
            return true;
        }

        /// <summary>
        /// Translates a whole item (room, opening or outside area) by a delta in meters
        /// (world XZ) — moves all its points. One undo step. P3 per-item Move.
        /// </summary>
        public static bool MoveItemBy(string itemId, Vector2 deltaMeters)
        {
            UIBaseItem ui = FindItem(itemId);
            if (ui == null || ui.cpc == null)
                return false;
            List<Vector3> world = ui.cpc.GetPointsWorldPositionList();
            if (world == null || world.Count == 0)
                return false;
            List<Vector2> moved = new List<Vector2>(world.Count);
            foreach (Vector3 w in world)
                moved.Add(new Vector2(w.x + deltaMeters.x, w.z + deltaMeters.y));
            return MoveItemPoints(itemId, moved);
        }

        /// <summary>Renames the item with this id. Returns false when not found.</summary>
        public static bool RenameItem(string itemId, string newName)
        {
            UIBaseItem ui = FindItem(itemId);
            if (ui == null)
                return false;
            using (BeginAction("Rename"))
            {
                ui.Name = newName;
                // Name edits rebuild nothing; announce on the document pipe (A1).
                DocumentEvents.RaiseChanged(DocumentChangeKind.Rename, "Rename");
            }
            return true;
        }

        /// <summary>Duplicates the item with this id as a new item (fresh identity). Returns the new id, or null.</summary>
        public static string DuplicateItem(string itemId)
        {
            UIBaseItem ui = FindItem(itemId);
            UIFloorMapMenu menu = GameObject.FindAnyObjectByType<UIFloorMapMenu>();
            if (ui == null || menu == null)
                return null;
            FloorMapItem data = ui.GetData();
            data.uniqueId = null; // a duplicate is a new item, never a shared identity
            data.GenerateUniqueId();
            using (BeginAction("Duplicate"))
            {
                menu.CreateNewUIItem(data, data.type);
            }
            return data.uniqueId;
        }

        /// <summary>Deletes the item with this id (room, opening or outside area).</summary>
        public static bool DeleteItem(string itemId)
        {
            UIBaseItem ui = FindItem(itemId);
            if (ui == null)
                return false;
            using (BeginAction("Delete"))
            {
                ui.Delete();
            }
            return true;
        }

        /// <summary>Serializes the current document (through the model).</summary>
        public static string ToJson()
        {
            FloorMapSerializer ser = GameObject.FindAnyObjectByType<FloorMapSerializer>();
            return ser != null ? ser.SerializeScene() : null;
        }

        /// <summary>
        /// Replaces the whole document (the P9 proposal-UX building block: callers show
        /// a result, and the load is one undoable step back to the previous state).
        /// </summary>
        public static bool LoadJson(string json)
        {
            FloorMapSerializer ser = GameObject.FindAnyObjectByType<FloorMapSerializer>();
            if (ser == null || string.IsNullOrEmpty(json))
                return false;
            using (BeginAction("Load Plan"))
            {
                Exoa.Events.GameEditorEvents.OnRequestClearAll?.Invoke(true, true, true);
                ser.DeserializeToScene(json);
                Exoa.Events.GameEditorEvents.OnFileLoaded?.Invoke(Exoa.Events.GameEditorEvents.FileType.FloorMapFile);
            }
            return true;
        }

        // ------------------------------------------------------------------ internals

        /// <summary>
        /// Returns the document model refreshed from the current UI state. Read ops go
        /// through here so queries always see live truth (A2: document is the query
        /// model). SerializeScene is the existing gather-from-UI path — cheap, no side
        /// effects on disk — and it stamps stable ids onto every item as it runs.
        /// When A2 step 2c flips serialize-authority to the document, this becomes a
        /// no-op passthrough.
        /// </summary>
        private static FloorPlanDocument SyncedDocument()
        {
            FloorMapSerializer ser = GameObject.FindAnyObjectByType<FloorMapSerializer>();
            if (ser == null)
                return null;
            ser.SerializeScene(); // refresh document.Data from UI
            return ser.Document;
        }

        private static FloorMapItem NewItem(FloorMapItemType type, string name)
        {
            FloorMapItem item = new FloorMapItem();
            item.GenerateUniqueId();
            item.type = type.ToString();
            item.name = name ?? "";
            item.directions = new List<Vector3>();
            return item;
        }

        private static string Commit(FloorMapItem item, string label)
        {
            UIFloorMapMenu menu = GameObject.FindAnyObjectByType<UIFloorMapMenu>();
            if (menu == null)
                return null;
            using (BeginAction(label))
            {
                menu.CreateNewUIItem(item, item.type); // the same path deserialization uses
            }
            return item.uniqueId;
        }

        /// <summary>Meters (world XZ) → the grid-relative coords the schema stores (plan axes in x,y).</summary>
        private static List<Vector3> ToNormalized(IList<Vector2> pointsMeters)
        {
            Exoa.Designer.Grid grid = GameObject.FindAnyObjectByType<Exoa.Designer.Grid>();
            if (grid == null)
                return null;
            List<Vector3> list = new List<Vector3>(pointsMeters.Count);
            foreach (Vector2 p in pointsMeters)
            {
                Vector2 n = grid.GetNormalizedPosition(new Vector3(p.x, 0f, p.y));
                list.Add(new Vector3(n.x, n.y, 0f));
            }
            return list;
        }

        private static UIBaseItem FindItem(string itemId)
        {
            if (string.IsNullOrEmpty(itemId))
                return null;
            UIBaseItem[] items = GameObject.FindObjectsByType<UIBaseItem>();
            foreach (UIBaseItem ui in items)
            {
                if (ui.ItemUniqueId == itemId)
                    return ui;
            }
            return null;
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new NullScope();
            public void Dispose() { }
        }

        private static IDisposable BeginAction(string label)
        {
            UndoRedoService svc = UndoRedoService.Instance;
            if (svc == null)
                return NullScope.Instance;
            return svc.BeginAction(label);
        }
    }
}

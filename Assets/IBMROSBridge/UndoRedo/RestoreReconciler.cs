using System.Collections.Generic;
using Exoa.Designer;
using Exoa.Events;
using UnityEngine;
using static Exoa.Designer.DataModel;

namespace IBMROS.Bridge.UndoRedo
{
    /// <summary>
    /// A5 v2 — identity-keyed diff-and-reconcile restore.
    ///
    /// The architecture professional editors use for structural undo (Unreal's
    /// transaction buffer restores only the objects in the transaction; Figma-style
    /// editors diff the document and reconcile the scene like a virtual DOM; Blender
    /// reuses unchanged datablocks across global-state undo). Ours: diff the target
    /// document against the live document BY ITEM ID (A2 identity) and touch only what
    /// differs —
    ///   · scalar/name deltas   → write the values onto the live UI item (no rebuild
    ///                            for names; localized rebuild for dimensions)
    ///   · geometry deltas      → move the item's existing control points in place and
    ///                            fire its own change event (that item + dependents
    ///                            rebuild meshes synchronously; no GameObject churn)
    ///   · removed items        → delete just those items
    ///   · added items          → create just those items (the load path per item)
    /// Unchanged items are never touched, so nothing blinks.
    ///
    /// Refuses (returns false, scene untouched → caller does the always-correct full
    /// clear+deserialize): multi-floor documents, floor-id changes, building-settings
    /// changes, items without identity, or any live/document divergence.
    /// </summary>
    internal static class RestoreReconciler
    {
        /// <summary>Applies targetJson to the live scene incrementally. False = not handled.</summary>
        public static bool TryReconcile(string targetJson, string currentJson)
        {
            if (string.IsNullOrEmpty(targetJson) || string.IsNullOrEmpty(currentJson))
                return false;

            FloorMapV2 target = DeserializeFloorMapJsonFile(targetJson);
            FloorMapV2 current = DeserializeFloorMapJsonFile(currentJson);

            if (target.floors == null || current.floors == null) return Bail("null floors");
            if (target.floors.Count != 1 || current.floors.Count != 1)
                return Bail("multi-floor (t=" + target.floors.Count + " c=" + current.floors.Count + ")");
            if (target.floors[0].uniqueId != current.floors[0].uniqueId) return Bail("floor id mismatch");
            if (!SettingsEqual(target.settings, current.settings)) return Bail("settings changed");

            List<FloorMapItem> tItems = target.floors[0].spaces ?? new List<FloorMapItem>();
            List<FloorMapItem> cItems = current.floors[0].spaces ?? new List<FloorMapItem>();

            var targetById = new Dictionary<string, FloorMapItem>();
            foreach (FloorMapItem t in tItems)
            {
                if (string.IsNullOrEmpty(t.uniqueId) || targetById.ContainsKey(t.uniqueId))
                    return Bail("target identity-less/dup (" + t.type + " '" + t.name + "')");
                targetById[t.uniqueId] = t;
            }
            var currentById = new Dictionary<string, FloorMapItem>();
            foreach (FloorMapItem c in cItems)
            {
                if (string.IsNullOrEmpty(c.uniqueId) || currentById.ContainsKey(c.uniqueId))
                    return Bail("current identity-less/dup (" + c.type + " '" + c.name + "')");
                currentById[c.uniqueId] = c;
            }

            UIFloorMapMenu menu = GameObject.FindAnyObjectByType<UIFloorMapMenu>();
            if (menu == null)
                return Bail("no menu");

            // Live scene must agree with the current document before we mutate it.
            var liveById = new Dictionary<string, UIBaseItem>();
            foreach (UIBaseItem ui in GameObject.FindObjectsByType<UIBaseItem>())
            {
                if (liveById.ContainsKey(ui.ItemUniqueId))
                    return Bail("live dup id");
                liveById[ui.ItemUniqueId] = ui;
            }
            foreach (string id in currentById.Keys)
            {
                if (!liveById.ContainsKey(id))
                    return Bail("live/document divergence (doc id not live)");
            }

            // ---- plan the diff (validate everything BEFORE mutating anything) ----
            var toRemove = new List<UIBaseItem>();
            foreach (var kv in currentById)
            {
                if (!targetById.ContainsKey(kv.Key))
                    toRemove.Add(liveById[kv.Key]);
            }
            var toAdd = new List<FloorMapItem>();
            var toMoveGeometry = new List<(UIBaseItem ui, FloorMapItem t)>();
            var toApplyScalars = new List<(UIBaseItem ui, FloorMapItem t)>();
            foreach (var kv in targetById)
            {
                FloorMapItem t = kv.Value;
                FloorMapItem c;
                if (!currentById.TryGetValue(kv.Key, out c))
                {
                    toAdd.Add(t);
                    continue;
                }
                if (c.type != t.type)
                {
                    // Type morph (e.g. Door↔Window): recreate just this item with the new
                    // type — scoped, no full-scene rebuild.
                    toRemove.Add(liveById[kv.Key]);
                    toAdd.Add(t);
                    continue;
                }
                if (!PositionsEqual(c, t))
                {
                    // ROOMS: reconcile control points in place (proven safe; the room is a
                    // single mesh). OPENINGS: recreate the changed item (delete+add) — the
                    // correct load path, scoped to just THIS opening item + its room. The
                    // in-place reconcile of multi-point opening items was reverted: it
                    // desynced the parametric wall holes from the door visuals and could
                    // corrupt state (2026-07-18 report). Correctness over the item-blink.
                    bool isOpening = t.type == FloorMapItemType.Door.ToString() ||
                                     t.type == FloorMapItemType.Window.ToString() ||
                                     t.type == FloorMapItemType.Opening.ToString();
                    if (isOpening)
                    {
                        toRemove.Add(liveById[kv.Key]);
                        toAdd.Add(t);
                        continue;
                    }
                    toMoveGeometry.Add((liveById[kv.Key], t));
                }
                if (!ScalarsEqual(c, t))
                    toApplyScalars.Add((liveById[kv.Key], t));
            }

            // Re-snap all openings ONLY when room/outside walls actually changed — that
            // is the only case where openings must re-derive their direction/position
            // from moved walls. Adding/removing/editing an opening must NOT trigger it,
            // or every other opening would rebuild (the reported "all windows flicker on
            // undo"). Openings recreated by the add path already carry their stored
            // direction (applied on control-point creation), so no global re-snap needed.
            bool wallsChanged = false;
            foreach (UIBaseItem ui in toRemove)
                if (IsWallItem(ui.sequencingItemType)) wallsChanged = true;
            foreach (FloorMapItem t in toAdd)
                if (IsWallItemType(t.type)) wallsChanged = true;
            foreach (var (ui, t) in toMoveGeometry)
                if (IsWallItemType(t.type)) wallsChanged = true;

            // ---- execute ----
            foreach (UIBaseItem ui in toRemove)
                ui.Delete(); // scoped: destroys this item only; fires a rebuild event

            foreach (FloorMapItem t in toAdd)
                menu.CreateNewUIItem(t, t.type); // the same path deserialization uses

            foreach (var (ui, t) in toMoveGeometry)
                MovePointsInPlace(ui, t);

            foreach (var (ui, t) in toApplyScalars)
                ApplyScalars(ui, t);

            if (wallsChanged)
                GameEditorEvents.OnRequestRepositionOpenings?.Invoke();

            LastOutcome = "scoped: remove=" + toRemove.Count + " add=" + toAdd.Count +
                          " move=" + toMoveGeometry.Count + " scalar=" + toApplyScalars.Count;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log("[IBMROS Reconcile] " + LastOutcome);
#endif
            return true;
        }

        /// <summary>Last reconcile outcome (why it fell back, or scoped counts) — for diagnosis.</summary>
        public static string LastOutcome { get; private set; }

        private static bool Bail(string reason)
        {
            LastOutcome = "full-restore: " + reason;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.LogWarning("[IBMROS Reconcile] falling back to FULL restore — " + reason +
                " (whole scene rebuilds; if this fires on a normal opening undo it is the blink cause)");
#endif
            return false;
        }

        private static bool IsWallItem(FloorMapItemType t) =>
            t == FloorMapItemType.Room || t == FloorMapItemType.Outside;

        private static bool IsWallItemType(string t) =>
            t == FloorMapItemType.Room.ToString() || t == FloorMapItemType.Outside.ToString();

        /// <summary>
        /// Reconciles an item's control points to the target's IN PLACE — updates shared
        /// points, appends missing ones, removes extras — WITHOUT destroying the item's
        /// GameObjects. Crucial for a multi-opening item: reducing its point count keeps
        /// the surviving opening instances alive (no blink), because the OpeningController
        /// reuses its children rather than being recreated. Handles point-count changes,
        /// so it replaces the old "recreate the whole opening item" path.
        /// </summary>
        internal static void SetItemPointsInPlace(UIBaseItem ui, FloorMapItem t)
        {
            ControlPointsController cpc = ui.cpc;
            Exoa.Designer.Grid grid = cpc.GetGrid();
            List<ControlPoint> points = cpc.GetPointsList();
            int target = t.normalizedPositions != null ? t.normalizedPositions.Count : 0;

            for (int i = 0; i < target; i++)
            {
                Vector3 np = t.normalizedPositions[i];
                Vector3 dir = (t.directions != null && i < t.directions.Count) ? t.directions[i] : Vector3.zero;
                if (i < points.Count)
                {
                    points[i].transform.position = grid.GetWorldPosition(np);
                    points[i].SetNormalizedPosition(new Vector2(np.x, np.y));
                    points[i].dir = dir;
                }
                else
                {
                    ControlPoint cp = cpc.CreateControlPointBasedOnNormalizedPosition(np, false, (uint)i, false);
                    if (cp != null)
                        cp.dir = dir;
                }
            }
            for (int i = points.Count - 1; i >= target; i--)
                cpc.RemoveControlPoint(points[i]);

            cpc.CreatePathVisualization();
            cpc.OnControlPointsChanged?.Invoke();
        }

        // Back-compat alias for the same-count case (FloorPlanEditor.MoveItemPoints).
        internal static void MovePointsInPlace(UIBaseItem ui, FloorMapItem t) => SetItemPointsInPlace(ui, t);

        private static void ApplyScalars(UIBaseItem ui, FloorMapItem t)
        {
            // Widget setters fire onValueChanged only on actual change: unchanged fields
            // cost nothing, changed ones trigger that item's localized rebuild. Name is
            // input text only — undoing a rename rebuilds nothing.
            ui.Name = t.name;
            ui.Width = t.width;
            ui.Height = t.height;
            ui.YPos = t.ypos;
            ui.HasWindow = t.hasWindow;
            ui.WindowFrameSize = t.windowFrameSize;
            ui.WindowSizeH = t.windowSizeH;
            ui.WindowSizeV = t.windowSizeV;
            ui.WindowSubDivH = t.windowSubDivH;
            ui.WindowSubDivV = t.windowSubDivV;
        }

        private static bool ScalarsEqual(FloorMapItem a, FloorMapItem b)
        {
            return a.name == b.name && a.width == b.width && a.height == b.height &&
                   a.ypos == b.ypos && a.hasWindow == b.hasWindow &&
                   a.windowFrameSize == b.windowFrameSize &&
                   a.windowSizeH == b.windowSizeH && a.windowSizeV == b.windowSizeV &&
                   a.windowSubDivH == b.windowSubDivH && a.windowSubDivV == b.windowSubDivV;
        }

        private static bool SettingsEqual(BuildingSettings a, BuildingSettings b)
        {
            return a.wallsHeight == b.wallsHeight && a.doorsHeight == b.doorsHeight &&
                   a.interiorWallThickness == b.interiorWallThickness &&
                   a.exteriorWallThickness == b.exteriorWallThickness &&
                   a.windowsThickness == b.windowsThickness && a.doorsThickness == b.doorsThickness &&
                   a.roofThickness == b.roofThickness && a.roofOverhang == b.roofOverhang &&
                   a.roofType == b.roofType;
        }

        // Compares POSITIONS only. Directions are derived (re-snapped from walls), not
        // primary edits — comparing them would spuriously flag openings as "changed"
        // whenever re-snap timing differed between two snapshots, which was a source of
        // the phantom-rotation and history-instability bugs.
        private static bool PositionsEqual(FloorMapItem a, FloorMapItem b)
        {
            const float eps = 1e-4f;
            return ListEqual(a.normalizedPositions, b.normalizedPositions, eps);
        }

        private static bool ListEqual(List<Vector3> a, List<Vector3> b, float eps)
        {
            int ca = a?.Count ?? 0, cb = b?.Count ?? 0;
            if (ca != cb) return false;
            for (int i = 0; i < ca; i++)
                if ((a[i] - b[i]).sqrMagnitude > eps * eps) return false;
            return true;
        }
    }
}

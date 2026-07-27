using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Exoa.Designer;
using IBMROS.Bridge.UndoRedo;
using IBMROS.Core;
using UnityEngine;

namespace IBMROS.Bridge.Stability
{
    /// <summary>
    /// Headless behavioral test of the A3 FloorPlanEditor gateway (and, transitively,
    /// the undo pipeline): builds a plan programmatically in the real editor scene and
    /// asserts on the generated meshes. Run exactly like the golden runner but with
    /// IBMROS_GOLDEN_MODE=apitest — the process exits 0 (pass) / 1 (fail).
    /// </summary>
    public class ApiSmokeTest : MonoBehaviour
    {
        private int failures;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void BatchBootstrap()
        {
            string mode = System.Environment.GetEnvironmentVariable("IBMROS_GOLDEN_MODE");
            if (mode == null || mode.Trim().ToLowerInvariant() != "apitest")
                return;
            if (GameObject.FindAnyObjectByType<FloorMapSerializer>() == null)
                return;
            GameObject go = new GameObject("IBMROS_ApiSmokeTest");
            ApiSmokeTest test = go.AddComponent<ApiSmokeTest>();
            test.StartCoroutine(test.RunAndExit());
        }

        private void Check(bool cond, string name)
        {
            Debug.Log("[IBMROS ApiTest] " + (cond ? "PASS " : "FAIL ") + name);
            if (!cond) failures++;
        }

        private static List<string> Meshes()
        {
            return GoldenRegressionRunner.CaptureMeshStats();
        }

        // Instance id of the (first) room's controller GameObject — 0 if none. Used to
        // detect whether a scoped restore kept the object alive (A5) vs recreated it.
        private static int RoomInstanceId()
        {
            RoomController rc = GameObject.FindAnyObjectByType<RoomController>();
            return rc != null ? rc.gameObject.GetInstanceID() : 0;
        }

        private static UIBaseItem FindUi(string id)
        {
            foreach (UIBaseItem ui in GameObject.FindObjectsByType<UIBaseItem>())
                if (ui.ItemUniqueId == id) return ui;
            return null;
        }

        private static Color ItemColorById(string id)
        {
            UIBaseItem ui = FindUi(id);
            return ui != null ? ui.colorDisplayed : Color.clear;
        }

        private static Color[] PaletteOf(string id)
        {
            UIBaseItem ui = FindUi(id);
            return ui != null && ui.cpc != null ? ui.cpc.randomColors : new Color[0];
        }

        // Instance id of a room's Floor mesh (regenerated on rebuild), or 0. Detects
        // whether a room was rebuilt: an untouched room keeps the same mesh instance.
        private static int RoomFloorMeshInstanceId(string id)
        {
            UIBaseItem ui = FindUi(id);
            if (ui == null || ui.drawer == null || ui.drawer.GO == null)
                return 0;
            foreach (MeshFilter mf in ui.drawer.GO.GetComponentsInChildren<MeshFilter>(true))
                if (mf.gameObject.name == "Floor" && mf.sharedMesh != null)
                    return mf.sharedMesh.GetInstanceID();
            return 0;
        }

        // World-space X of a room's Floor mesh center (for verifying a Move). NaN if none.
        private static float RoomFloorCenterX(string id)
        {
            UIBaseItem ui = FindUi(id);
            if (ui == null || ui.drawer == null || ui.drawer.GO == null)
                return float.NaN;
            foreach (MeshRenderer mr in ui.drawer.GO.GetComponentsInChildren<MeshRenderer>(true))
                if (mr.gameObject.name == "Floor")
                    return mr.bounds.center.x;
            return float.NaN;
        }

        // Instance id of the opening-visual GameObject for a given item, or 0. Used to
        // detect whether a scoped undo left an unrelated opening untouched (vs rebuilt).
        private static int OpeningInstanceIdFor(string id)
        {
            UIBaseItem ui = FindUi(id);
            if (ui == null || ui.drawer == null || ui.drawer.GO == null)
                return 0;
            ProceduralOpening po = ui.drawer.GO.GetComponentInChildren<ProceduralOpening>();
            return po != null ? po.gameObject.GetInstanceID() : 0;
        }

        // True when the (first) opening sits at the bare Euler(0,90,0) base rotation,
        // i.e. its direction was never applied (yaw ≈ 90). A correctly-directed opening
        // resolves elsewhere. Returns false when no opening exists.
        private static bool OpeningAtBaseRotation()
        {
            ProceduralOpening po = GameObject.FindAnyObjectByType<ProceduralOpening>();
            if (po == null)
                return false;
            float yaw = po.transform.rotation.eulerAngles.y;
            float d = Mathf.Abs(Mathf.DeltaAngle(yaw, 90f));
            return d < 5f;
        }

        private static int RoomWallCount(List<string> lines) =>
            lines.Count(l => l.Contains("RoomController_Prefab(Clone)/Walls/Wall") && !l.Contains("|v:0|"));

        private static bool RoomFloorBuilt(List<string> lines) =>
            lines.Any(l => l.Contains("RoomController_Prefab(Clone)/Floor|") && !l.Contains("|v:0|"));

        private static bool HasOpeningVisual(List<string> lines) =>
            lines.Any(l => l.Contains("DoorWindowMesh") && !l.Contains("|v:0|"));

        private IEnumerator RunAndExit()
        {
            yield return new WaitForSeconds(1.5f); // let the scene boot

            FloorMapSerializer ser = GameObject.FindAnyObjectByType<FloorMapSerializer>();
            Check(ser != null, "scene has serializer");
            Check(FloorPlanEditor.IsAvailable, "FloorPlanEditor available");

            // Fresh empty project.
            Exoa.Events.GameEditorEvents.OnRequestClearAll?.Invoke(true, true, true);
            ser.DeserializeToScene(ser.SerializeEmpty(null));
            yield return new WaitForSeconds(0.5f);

            // 1 — create a rectangular room through the gateway.
            string roomId = FloorPlanEditor.CreateRectRoom(6f, 5f, Vector2.zero, "ApiRoom");
            Check(!string.IsNullOrEmpty(roomId), "CreateRectRoom returns id");
            yield return new WaitForSeconds(1f);
            List<string> afterRoom = Meshes();
            Check(RoomFloorBuilt(afterRoom), "room floor generated");
            Check(RoomWallCount(afterRoom) >= 4, "room has 4+ wall meshes");
            Color roomColor0 = ItemColorById(roomId);
            Check(roomColor0 == IBMROS.Core.ItemColor.ForId(roomId, PaletteOf(roomId)),
                "room color derives from stable identity");

            // 2 — place a door on the bottom wall (z = -2.5, running along X).
            string doorId = FloorPlanEditor.AddOpening(DataModel.FloorMapItemType.Door,
                new Vector2(0f, -2.5f), Vector2.right, 0.9f, 2.1f, 0f, "ApiDoor");
            Check(!string.IsNullOrEmpty(doorId), "AddOpening returns id");
            yield return new WaitForSeconds(1f);
            List<string> afterDoor = Meshes();
            Check(HasOpeningVisual(afterDoor), "door visual generated");
            // The wall the door cuts through gains jamb/reveal geometry (> plain 4 verts).
            Check(afterDoor.Any(l => l.Contains("/Walls/Wall") && ExtractVerts(l) > 4),
                "a wall carries the door hole (jamb verts)");
            // ROTATION regression: a door with dir=zero renders at the bare Euler(0,90,0)
            // base (the reported "everything rotated 90°"). A correctly-directed door on
            // a wall running along X resolves to yaw 0 or 180 — never ~90.
            Check(!OpeningAtBaseRotation(), "door direction applied (not stuck at 90° base)");

            // 2b — SCOPED opening undo (reported bug): add a second opening, then undo it;
            // the FIRST opening must NOT be rebuilt (no flicker). We prove it by instance id.
            string windowId = FloorPlanEditor.AddOpening(DataModel.FloorMapItemType.Window,
                new Vector2(-3f, 0f), new Vector2(0f, 1f), 1.2f, 1.1f, 0.9f, "ApiWindow");
            Check(!string.IsNullOrEmpty(windowId), "second opening (window) created");
            yield return new WaitForSeconds(1.2f);
            int doorOpeningInst = OpeningInstanceIdFor(doorId);
            Check(doorOpeningInst != 0, "door opening instance captured");
            UndoRedoService undoEarly = UndoRedoService.Instance;
            undoEarly.Undo(); // remove the window
            yield return new WaitForSeconds(1.4f);
            Check(!FloorPlanEditor.GetItemIds().Contains(windowId), "undo removed the window");
            Check(OpeningInstanceIdFor(doorId) == doorOpeningInst,
                "SCOPED: undoing one opening did NOT rebuild the other (no flicker)");

            // 3 — rename through the gateway; state carries the name.
            Check(FloorPlanEditor.RenameItem(roomId, "Renamed Room"), "RenameItem finds item");
            yield return new WaitForSeconds(1f);
            string json = FloorPlanEditor.ToJson();
            Check(json != null && json.Contains("Renamed Room"), "rename persisted in document");
            Check(json != null && json.Contains(roomId), "item identity persisted in document");

            // 4 — undo removes the last step; redo brings it back.
            // A5 scoped restore: undoing a RENAME must NOT tear down the scene. We prove
            // that by capturing the room's RoomController instance id before undo and
            // asserting the SAME instance survives afterward (a full rebuild would have
            // destroyed and recreated it, changing the id).
            int roomInstanceBefore = RoomInstanceId();
            UndoRedoService undo = UndoRedoService.Instance;
            Check(undo != null && undo.CanUndo, "undo available after edits");
            if (undo != null && undo.CanUndo)
            {
                undo.Undo(); // revert rename
                yield return new WaitForSeconds(1.2f);
                string reverted = FloorPlanEditor.ToJson();
                Check(reverted != null && !reverted.Contains("Renamed Room"), "undo reverted the rename");
                Check(RoomInstanceId() == roomInstanceBefore && roomInstanceBefore != 0,
                    "A5: rename-undo kept the room in place (no teardown)");
                undo.Redo();
                yield return new WaitForSeconds(1.2f);
                string redone = FloorPlanEditor.ToJson();
                Check(redone != null && redone.Contains("Renamed Room"), "redo re-applied the rename");
                Check(RoomInstanceId() == roomInstanceBefore,
                    "A5: rename-redo also in place (same room instance)");
            }

            // 4b — query surface: document sees both items with stable ids.
            List<string> ids = FloorPlanEditor.GetItemIds();
            Check(ids.Contains(roomId) && ids.Contains(doorId), "GetItemIds returns room + door");
            string doorJson = FloorPlanEditor.GetItemJson(doorId);
            Check(doorJson != null && doorJson.Contains("\"Door\""), "GetItemJson returns door state");

            // 4c — SetItemSettings changes the door width; document reflects it.
            Check(FloorPlanEditor.SetItemSettings(doorId, width: 1.5f), "SetItemSettings on door");
            yield return new WaitForSeconds(1f);
            string doorJson2 = FloorPlanEditor.GetItemJson(doorId);
            Check(doorJson2 != null && doorJson2.Contains("1.5"), "door width change persisted");

            // 4d — SetBuildingSettings: taller walls rebuild geometry.
            List<string> beforeWalls = Meshes();
            float beforeH = beforeWalls.Where(l => l.Contains("/Walls/Wall0|")).Select(ExtractSizeY).FirstOrDefault();
            FloorPlanEditor.SetBuildingSettings(wallsHeight: 4f);
            yield return new WaitForSeconds(1.2f);
            float afterH = Meshes().Where(l => l.Contains("/Walls/Wall0|")).Select(ExtractSizeY).FirstOrDefault();
            Check(afterH > beforeH + 0.5f, "SetBuildingSettings raised wall height (" + beforeH + "->" + afterH + ")");

            // 5 — delete the door through the gateway.
            Check(FloorPlanEditor.DeleteItem(doorId), "DeleteItem finds door");
            yield return new WaitForSeconds(1.2f);
            Check(!HasOpeningVisual(Meshes()), "door visual removed after delete");

            // 6 — undo the delete: reconcile re-adds ONLY the door; the room GameObject
            // must survive untouched (v1 full-restored here and recreated everything).
            int roomInstBeforeDoorUndo = RoomInstanceId();
            undo.Undo();
            yield return new WaitForSeconds(1.6f);
            Check(HasOpeningVisual(Meshes()), "undo restored the deleted door");
            Check(RoomInstanceId() == roomInstBeforeDoorUndo && roomInstBeforeDoorUndo != 0,
                "A5v2: door-restore kept the room in place");
            // The restored door must re-derive its direction (not the 90° base) — this is
            // the exact "openings rotate 90° after undo" the owner reported.
            Check(!OpeningAtBaseRotation(), "restored door direction correct (not 90° base)");
            // And serialize must be STABLE afterward — an unstable read here is what spawned
            // the phantom step that killed redo. Compare across the leak-sweep window.
            string stab0 = FloorPlanEditor.ToJson();
            int stepsStab = undo.StepCount;
            yield return new WaitForSeconds(6.2f);
            string stab1 = FloorPlanEditor.ToJson();
            Check(stab0 == stab1, "serialize stable after opening restore (no drift)");
            Check(undo.StepCount == stepsStab, "no phantom step after opening restore");

            // 7 — THE reported bug regression: redo must survive idling well past the
            // leak-sweep period after an undo (phantom pushes used to truncate it).
            int stepsBeforeIdle;
            undo.Undo(); // undo the wallsHeight change (full-restore path: settings differ)
            yield return new WaitForSeconds(1.6f);
            Check(undo.CanRedo, "redo available right after undo");
            stepsBeforeIdle = undo.StepCount;
            yield return new WaitForSeconds(6.5f); // crosses the 5s dev sweep
            Check(undo.CanRedo, "REGRESSION: redo survives idle after undo (no phantom push)");
            Check(undo.StepCount == stepsBeforeIdle, "REGRESSION: history stable while idle after undo");
            undo.Redo();
            yield return new WaitForSeconds(1.6f);
            Check(Meshes().Where(l => l.Contains("/Walls/Wall0|")).Select(ExtractSizeY).FirstOrDefault() > 3.5f,
                "redo after idle re-applied the wall height");

            // 8 — geometry undo via reconcile: move the room's points, undo, and the
            // room GameObject must survive (in-place control-point restore).
            Check(FloorPlanEditor.MoveItemPoints(roomId, new List<Vector2>
            {
                new Vector2(-3.5f, -2.5f), new Vector2(3.5f, -2.5f),
                new Vector2(3.5f, 2.5f), new Vector2(-3.5f, 2.5f),
            }), "MoveItemPoints via gateway");
            yield return new WaitForSeconds(1.4f);
            Check(Meshes().Where(l => l.Contains("/Walls/Wall0|")).Select(ExtractSizeX).FirstOrDefault() > 6.5f,
                "MoveItemPoints widened the room");
            int roomInstBeforeGeoUndo = RoomInstanceId();
            undo.Undo();
            yield return new WaitForSeconds(1.6f);
            Check(Meshes().Where(l => l.Contains("/Walls/Wall0|")).Select(ExtractSizeX).FirstOrDefault() < 6.5f,
                "geometry-undo reverted the points");
            Check(RoomInstanceId() == roomInstBeforeGeoUndo && roomInstBeforeGeoUndo != 0,
                "A5v2: geometry-undo in place (no teardown)");

            // 9 — the full chain (the other reported bug): undo all the way back to an
            // empty scene, then redo all the way forward — every step must keep working.
            int guard = 0;
            while (undo.CanUndo && guard++ < 30)
            {
                undo.Undo();
                yield return new WaitForSeconds(1.3f);
            }
            Check(guard < 30, "undo chain terminates");
            Check(!RoomFloorBuilt(Meshes()), "undo chain walked back to an empty scene");
            guard = 0;
            while (undo.CanRedo && guard++ < 30)
            {
                undo.Redo();
                yield return new WaitForSeconds(1.3f);
            }
            Check(guard < 30, "redo chain terminates");
            List<string> final = Meshes();
            Check(RoomFloorBuilt(final), "redo chain rebuilt the room");
            Check(HasOpeningVisual(final), "redo chain rebuilt the door");
            string finalJson = FloorPlanEditor.ToJson();
            Check(finalJson != null && finalJson.Contains("Renamed Room"), "redo chain restored the rename");
            // Color stability: the room survived a full teardown (undo to empty) and
            // rebuild (redo forward). Its color must be identical — not re-rolled.
            Check(ItemColorById(roomId) == roomColor0, "room color stable across full rebuild");

            // 10 — P2.6 selection: a downward raycast onto the room resolves to its id;
            // SelectById tracks it; the reconciler-backed delete clears the selection.
            IBMROS.Bridge.Interaction.SelectionService sel = IBMROS.Bridge.Interaction.SelectionService.Instance;
            Check(sel != null, "SelectionService present");
            if (sel != null)
            {
                // (Raycast-resolve is tested in the clean isolated scene at step 12, not
                // here where the scene has been through many edits/undo/redo.)
                sel.SelectById(roomId);
                Check(sel.SelectedId == roomId && sel.HasSelection, "SelectById selects the room");

                // Duplicate via gateway then delete the duplicate — leaves the scene as it was.
                string dupId = FloorPlanEditor.DuplicateItem(roomId);
                yield return new WaitForSeconds(1.2f);
                Check(!string.IsNullOrEmpty(dupId) && dupId != roomId, "DuplicateItem makes a new item");
                Check(FloorPlanEditor.GetItemIds().Contains(dupId), "duplicate present in document");
                FloorPlanEditor.DeleteItem(dupId);
                yield return new WaitForSeconds(1.2f);
                Check(!FloorPlanEditor.GetItemIds().Contains(dupId), "duplicate deleted");

                sel.Deselect();
                Check(!sel.HasSelection, "Deselect clears selection");
            }

            // 11 — P3.1 rectangular-room tool: corner-to-corner creates a correct room,
            // and a degenerate (too-small) pair is rejected.
            string rectId = FloorPlanEditor.CreateRectRoomFromCorners(
                new Vector2(5f, 5f), new Vector2(11f, 9f), "RectTool");
            yield return new WaitForSeconds(1.2f);
            Check(!string.IsNullOrEmpty(rectId), "CreateRectRoomFromCorners makes a room");
            // 6 x 4 m room -> a wall spanning ~6 m along X exists.
            Check(Meshes().Any(l => l.Contains("RoomController_Prefab(Clone)/Walls/Wall") &&
                Mathf.Abs(ExtractSizeX(l) - 6f) < 0.3f), "rect room has a ~6 m wall (correct dimensions)");
            Check(FloorPlanEditor.CreateRectRoomFromCorners(new Vector2(2f, 2f), new Vector2(2.2f, 2.2f)) == null,
                "degenerate rectangle rejected (min-size guard)");
            if (!string.IsNullOrEmpty(rectId))
            {
                FloorPlanEditor.DeleteItem(rectId);
                yield return new WaitForSeconds(1f);
            }

            // 12 — A7 scoped invalidation: an opening change rebuilds ONLY the room it
            // touches. Two rooms far apart; a door on room A must not rebuild room B, and
            // the scoped result must be geometrically identical to a full rebuild.
            Exoa.Events.GameEditorEvents.OnRequestClearAll?.Invoke(true, true, true);
            ser.DeserializeToScene(ser.SerializeEmpty(null));
            yield return new WaitForSeconds(0.5f);
            string roomA = FloorPlanEditor.CreateRectRoom(6f, 5f, new Vector2(0f, 0f), "A7_RoomA");
            string roomB = FloorPlanEditor.CreateRectRoom(6f, 5f, new Vector2(40f, 0f), "A7_RoomB");
            yield return new WaitForSeconds(1.4f);
            int roomBMesh = RoomFloorMeshInstanceId(roomB);
            Check(roomBMesh != 0, "room B floor mesh captured");

            string a7Door = FloorPlanEditor.AddOpening(DataModel.FloorMapItemType.Door,
                new Vector2(0f, -2.5f), Vector2.right, 0.9f, 2.1f, 0f, "A7_Door");
            yield return new WaitForSeconds(1.4f);
            Check(RoomFloorMeshInstanceId(roomB) == roomBMesh,
                "SCOPED: opening on room A did NOT rebuild distant room B (mesh untouched)");

            // P2.6 selection — test the RESOLUTION LOGIC deterministically (a raycast's
            // physical pick depends on Exoa's collider setup: coplanar ground grid,
            // one-sided ceiling colliders — that belongs to interactive verification with
            // the real camera). ResolveId must map any GameObject in a room's hierarchy
            // back to that room's id via IObjectDrawer.UI.ItemUniqueId.
            UIBaseItem uiA = FindUi(roomA);
            Check(uiA != null && uiA.drawer != null, "room A UI + drawer found");
            if (uiA != null && uiA.drawer != null)
            {
                Check(IBMROS.Bridge.Interaction.SelectionService.ResolveId(uiA.drawer.GO) == roomA,
                    "ResolveId maps a room's root GameObject to its id");
                MeshFilter childMesh = uiA.drawer.GO.GetComponentInChildren<MeshFilter>();
                Check(childMesh != null &&
                      IBMROS.Bridge.Interaction.SelectionService.ResolveId(childMesh.gameObject) == roomA,
                    "ResolveId maps a room's child mesh (wall/floor) to the room id");
            }

            // A7 2b: the door's explicit host binding was recorded as room A.
            Check(IBMROS.Core.OpeningHostRegistry.IsHostedBy(a7Door, roomA),
                "A7 2b: door registered as hosted by room A");

            // scoped == full: force a global rebuild; geometry must be identical.
            List<string> scopedMeshes = Meshes();
            Exoa.Events.GameEditorEvents.OnRequestRebuildAllRooms?.Invoke();
            Exoa.Events.GameEditorEvents.OnRequestRebuildBuilding?.Invoke();
            yield return new WaitForSeconds(1.4f);
            List<string> fullMeshes = Meshes();
            Check(scopedMeshes.Count == fullMeshes.Count && scopedMeshes.SequenceEqual(fullMeshes),
                "scoped rebuild geometry == full rebuild geometry (correctness)");

            // 13 — A7 stage 2: reposition-on-create is scoped. Creating a distant room
            // must NOT re-snap/rebuild room A's door (was a global reposition broadcast).
            int a7DoorInst = OpeningInstanceIdFor(a7Door);
            Check(a7DoorInst != 0, "room A door opening instance captured");
            FloorPlanEditor.CreateRectRoom(6f, 5f, new Vector2(80f, 0f), "A7_RoomC");
            yield return new WaitForSeconds(1.4f);
            Check(OpeningInstanceIdFor(a7Door) == a7DoorInst,
                "SCOPED: creating a distant room did NOT re-snap room A's door");

            // 14 — A7 stage 2: room delete is scoped. Deleting distant room B must NOT
            // rebuild room A (was OnRequestRebuildAllRooms broadcast).
            int roomAMesh = RoomFloorMeshInstanceId(roomA);
            FloorPlanEditor.DeleteItem(roomB);
            yield return new WaitForSeconds(1.4f);
            Check(RoomFloorMeshInstanceId(roomA) == roomAMesh,
                "SCOPED: deleting distant room B did NOT rebuild room A");
            List<string> scoped2 = Meshes();
            Exoa.Events.GameEditorEvents.OnRequestRebuildAllRooms?.Invoke();
            Exoa.Events.GameEditorEvents.OnRequestRebuildBuilding?.Invoke();
            yield return new WaitForSeconds(1.4f);
            Check(scoped2.SequenceEqual(Meshes()), "scoped room-delete geometry == full rebuild (correctness)");

            // 15 — THE reported blink: a multi-opening ITEM (several doors as points in
            // one OpeningController). Reducing its point count must keep the surviving
            // openings' GameObjects alive (no blink) — the bug was ClearChildren() nuking
            // all of them. Build a 3-point door on room A's bottom wall, then drop to 2.
            UIBaseItem doorUi = FindUi(a7Door);
            Exoa.Designer.Grid grid = GameObject.FindAnyObjectByType<Exoa.Designer.Grid>();
            if (doorUi != null && doorUi.cpc != null && grid != null && doorUi.GetData().normalizedPositions.Count >= 1)
            {
                Vector2 nA = grid.GetNormalizedPosition(new Vector3(-1.5f, 0f, -2.5f));
                Vector2 nB = grid.GetNormalizedPosition(new Vector3(1.5f, 0f, -2.5f));
                DataModel.FloorMapItem d3 = doorUi.GetData();
                Vector3 p0 = d3.normalizedPositions[0];
                d3.normalizedPositions = new List<Vector3> { p0, new Vector3(nA.x, nA.y, 0f), new Vector3(nB.x, nB.y, 0f) };
                d3.directions = new List<Vector3> { Vector3.right, Vector3.right, Vector3.right };
                IBMROS.Bridge.UndoRedo.RestoreReconciler.SetItemPointsInPlace(doorUi, d3);
                yield return new WaitForSeconds(1.2f);
                ProceduralOpening[] op3 = doorUi.drawer.GO.GetComponentsInChildren<ProceduralOpening>(true);
                Check(op3.Length == 3, "multi-opening item built (3 door instances, got " + op3.Length + ")");
                if (op3.Length == 3)
                {
                    int keep0 = op3[0].gameObject.GetInstanceID();
                    int keep1 = op3[1].gameObject.GetInstanceID();
                    DataModel.FloorMapItem d2 = doorUi.GetData();
                    d2.normalizedPositions = new List<Vector3> { d2.normalizedPositions[0], d2.normalizedPositions[1] };
                    d2.directions = new List<Vector3> { Vector3.right, Vector3.right };
                    IBMROS.Bridge.UndoRedo.RestoreReconciler.SetItemPointsInPlace(doorUi, d2);
                    yield return new WaitForSeconds(1.2f);
                    ProceduralOpening[] op2 = doorUi.drawer.GO.GetComponentsInChildren<ProceduralOpening>(true);
                    Check(op2.Length == 2, "reduced to 2 opening instances (got " + op2.Length + ")");
                    bool kept = op2.Any(o => o.gameObject.GetInstanceID() == keep0) &&
                                op2.Any(o => o.gameObject.GetInstanceID() == keep1);
                    Check(kept, "BLINK FIX: surviving openings kept their GameObject instances when point count dropped");
                }
            }

            // 15c — P3 per-item Move: MoveItemBy translates the whole room.
            Exoa.Events.GameEditorEvents.OnRequestClearAll?.Invoke(true, true, true);
            ser.DeserializeToScene(ser.SerializeEmpty(null));
            yield return new WaitForSeconds(0.5f);
            string moveRoom = FloorPlanEditor.CreateRectRoom(6f, 5f, Vector2.zero, "MoveMe");
            yield return new WaitForSeconds(1.2f);
            float cx0 = RoomFloorCenterX(moveRoom);
            Check(!float.IsNaN(cx0) && Mathf.Abs(cx0) < 0.6f, "room starts near origin (x=" + cx0 + ")");
            Check(FloorPlanEditor.MoveItemBy(moveRoom, new Vector2(3f, 0f)), "MoveItemBy returns true");
            yield return new WaitForSeconds(1.2f);
            float cx1 = RoomFloorCenterX(moveRoom);
            Check(!float.IsNaN(cx1) && Mathf.Abs(cx1 - (cx0 + 3f)) < 0.5f,
                "MoveItemBy shifted the room +3m (x " + cx0 + "->" + cx1 + ")");
            // Move is undoable/redoable.
            UndoRedoService.Instance.Undo();
            yield return new WaitForSeconds(1.4f);
            Check(Mathf.Abs(RoomFloorCenterX(moveRoom) - cx0) < 0.5f, "undo Move returns room to origin");
            UndoRedoService.Instance.Redo();
            yield return new WaitForSeconds(1.4f);
            Check(Mathf.Abs(RoomFloorCenterX(moveRoom) - (cx0 + 3f)) < 0.5f, "redo Move re-applies the shift");

            // 16 — P3 room split: split one room into two with a line; original is
            // replaced by two rooms, both built.
            Exoa.Events.GameEditorEvents.OnRequestClearAll?.Invoke(true, true, true);
            ser.DeserializeToScene(ser.SerializeEmpty(null));
            yield return new WaitForSeconds(0.5f);
            string splitRoom = FloorPlanEditor.CreateRectRoom(8f, 6f, Vector2.zero, "SplitMe");
            yield return new WaitForSeconds(1.2f);
            int roomsBefore = GameObject.FindObjectsByType<RoomController>().Length;
            var split = FloorPlanEditor.SplitRoom(splitRoom, new Vector2(0f, -10f), new Vector2(0f, 10f));
            Check(!string.IsNullOrEmpty(split.a) && !string.IsNullOrEmpty(split.b) && split.a != split.b,
                "SplitRoom returns two new room ids");
            yield return new WaitForSeconds(1.6f);
            List<string> afterSplit = FloorPlanEditor.GetItemIds();
            Check(!afterSplit.Contains(splitRoom), "original room replaced by the split");
            Check(afterSplit.Contains(split.a) && afterSplit.Contains(split.b), "both split halves present");
            Check(GameObject.FindObjectsByType<RoomController>().Length == roomsBefore + 1,
                "one room became two (net +1 room controller)");
            // Split is undoable: undo restores the single original room.
            UndoRedoService.Instance.Undo();
            yield return new WaitForSeconds(1.6f);
            Check(GameObject.FindObjectsByType<RoomController>().Length == roomsBefore,
                "undo Split restores the single room");
            Check(FloorPlanEditor.GetItemIds().Contains(splitRoom), "undo Split brings back the original room id");
            UndoRedoService.Instance.Redo();
            yield return new WaitForSeconds(1.6f);
            Check(GameObject.FindObjectsByType<RoomController>().Length == roomsBefore + 1,
                "redo Split splits again into two");

            Debug.Log("[IBMROS ApiTest] " + (failures == 0 ? "ALL PASSED" : failures + " FAILURES"));
#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(failures == 0 ? 0 : 1);
#else
            Application.Quit(failures == 0 ? 0 : 1);
#endif
        }

        private static int ExtractVerts(string line)
        {
            int i = line.IndexOf("|v:", System.StringComparison.Ordinal);
            if (i < 0) return 0;
            int end = line.IndexOf('|', i + 3);
            int v;
            return int.TryParse(line.Substring(i + 3, end - i - 3), out v) ? v : 0;
        }

        private static float ExtractSizeX(string line)
        {
            int i = line.IndexOf("|s:(", System.StringComparison.Ordinal);
            if (i < 0) return 0f;
            int start = i + 4;
            int end = line.IndexOf(')', start);
            string[] parts = line.Substring(start, end - start).Split(',');
            float x;
            return parts.Length >= 1 && float.TryParse(parts[0], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out x) ? x : 0f;
        }

        // Size token looks like: ...|s:(11.9,3,0.05)|... — return the Y component.
        private static float ExtractSizeY(string line)
        {
            int i = line.IndexOf("|s:(", System.StringComparison.Ordinal);
            if (i < 0) return 0f;
            int start = i + 4;
            int end = line.IndexOf(')', start);
            string[] parts = line.Substring(start, end - start).Split(',');
            float y;
            return parts.Length >= 2 && float.TryParse(parts[1], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out y) ? y : 0f;
        }
    }
}

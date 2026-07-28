using System;
using System.Collections.Generic;
using Exoa.Designer;
using Exoa.Events;
using IBMROS.Bridge.Interaction;
using IBMROS.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using Grid = Exoa.Designer.Grid;

namespace IBMROS.Designer.Plan
{
    /// <summary>
    /// Touch-first interaction layer for the 2D floor-plan editor (roadmap lane A6).
    ///
    /// Owns single-pointer editing in Plan2D mode:
    ///   • corner drag        — selected room/outside corner handles
    ///   • wall (edge) drag   — selected room edge, constrained to the wall normal
    ///   • whole-room drag    — press inside the selected polygon + slop
    ///   • opening drag       — any door/window point, projected onto walls
    ///   • AddDoor/AddWindow  — one tap places an opening on the nearest wall
    ///   • DrawRect           — delegates to the bridge RectRoomTool
    ///
    /// Contract (INTEGRATION_MAP §5): previews mutate only ControlPoint visuals
    /// (SetNormalizedPosition + position + CreatePathVisualization — never raises
    /// document events), commits go through FloorPlanEditor (one undo step each),
    /// and final geometry is always computed from the points captured at drag
    /// start, never from live preview state. Camera gestures are disabled during
    /// drags via CameraEvents (same pattern the vendor CPC used).
    ///
    /// Tap-to-select stays with SelectionService (collider raycast, 12 px/0.35 s);
    /// this class only ever starts drags on the already-selected item, openings,
    /// or armed tools, so the two input layers cannot fight.
    /// </summary>
    public sealed class PlanTouchController : MonoBehaviour
    {
        public static PlanTouchController Instance { get; private set; }

        /// <summary>Raised when the armed tool changes.</summary>
        public static event Action<PlanToolMode> OnToolChanged;

        /// <summary>Raised every preview frame and after commits — visuals refresh hook.</summary>
        public static event Action OnPlanVisualsDirty;

        public PlanToolMode Tool { get; private set; } = PlanToolMode.Browse;
        public bool IsDragging => drag != DragKind.None && dragItem != null;
        public string DragItemId => IsDragging ? dragItem.ItemUniqueId : null;

        // Screen-space grab radii (px) and drag behavior.
        private const float HANDLE_RADIUS_PX = 30f;
        private const float EDGE_RADIUS_PX = 22f;
        private const float ROOM_DRAG_SLOP_PX = 8f;
        private const float FINE_SNAP_M = 0.1f;          // drag snap increment
        private const float AXIS_ASSIST_M = 0.15f;       // align-with-neighbours assist
        private const float OPENING_WALL_SEARCH_M = 1.5f; // wall search radius while dragging an opening
        private const float PLACE_WALL_SEARCH_M = 0.6f;  // wall search radius for tap-to-place
        private const float MIN_COMMIT_MOVE_M = 0.005f;

        private enum DragKind { None, Corner, Edge, Room, Opening }

        private DragKind drag = DragKind.None;
        private UIBaseItem dragItem;
        private int dragIndex;                 // corner index or edge start index
        private List<Vector2> startMeters;     // polygon at drag start (meters)
        private Vector2 grabMeters;            // ground point at drag start
        private Vector2 edgeNormal;            // unit normal for edge drags
        private bool moved;
        private bool pressedInsideRoom;        // waiting for slop before Room drag
        private Vector2 pressScreenPos;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void OnEnable()
        {
            DocumentEvents.OnDocumentChanged += HandleDocChanged;
            DesignerModeController.OnModeChanged += HandleModeChanged;
        }

        private void OnDisable()
        {
            DocumentEvents.OnDocumentChanged -= HandleDocChanged;
            DesignerModeController.OnModeChanged -= HandleModeChanged;
        }

        // ------------------------------------------------------------------ tools

        public void SetTool(PlanToolMode mode)
        {
            if (Tool == mode)
                mode = PlanToolMode.Browse; // tapping the armed tool disarms it

            CancelDrag();
            Tool = mode;

            RectRoomTool rect = RectRoomTool.Instance;
            if (rect != null)
                rect.SetActive(mode == PlanToolMode.DrawRect);

            OnToolChanged?.Invoke(Tool);
        }

        private void HandleModeChanged(DesignerMode mode)
        {
            if (mode != DesignerMode.Plan2D)
            {
                CancelDrag();
                if (Tool != PlanToolMode.Browse)
                    SetTool(PlanToolMode.Browse);
            }
        }

        private void HandleDocChanged(DocumentChange change)
        {
            // Vendor ControlPointsController.Update is the legacy mouse editing loop
            // (hover ghosts, click-adds, right-click popups). It must never run under
            // the touch editor; new items arrive with it enabled, so sweep on every
            // document change (INTEGRATION_MAP §5 'neutralize per item').
            DisableVendorCpcs();
            OnPlanVisualsDirty?.Invoke();
        }

        private void Start()
        {
            DisableVendorCpcs();
        }

        private static void DisableVendorCpcs()
        {
            foreach (ControlPointsController cpc in
                     UnityEngine.Object.FindObjectsByType<ControlPointsController>(FindObjectsSortMode.None))
            {
                if (cpc.enabled)
                    cpc.enabled = false;
            }
        }

        // ------------------------------------------------------------------ input

        private void Update()
        {
            // DrawRect is fully owned by the bridge tool; watch for it finishing.
            if (Tool == PlanToolMode.DrawRect)
            {
                RectRoomTool rect = RectRoomTool.Instance;
                if (rect == null || !rect.IsActive)
                    SetTool(PlanToolMode.Browse);
                return;
            }

            if (DesignerModeController.Instance == null ||
                DesignerModeController.Instance.Mode != DesignerMode.Plan2D)
                return;

            if (PointerDown())
                OnPointerDown(PointerPos());
            else if (IsDragging || pressedInsideRoom)
            {
                if (PointerHeld())
                    OnPointerMove(PointerPos());
                if (PointerUp())
                    OnPointerUp();
            }
        }

        // Single-pointer abstraction: first touch on device, left mouse in editor.
        // Multi-touch is camera territory (pinch/two-finger pan) — bail out and
        // cancel any drag so a second finger never smears geometry.
        private bool MultiTouch => Input.touchCount > 1;

        private bool PointerDown()
        {
            if (MultiTouch) return false;
            if (Input.touchCount == 1) return Input.GetTouch(0).phase == TouchPhase.Began && !IsPointerOverUi(Input.GetTouch(0).fingerId);
            return Input.GetMouseButtonDown(0) && !IsPointerOverUi(null);
        }

        private bool PointerHeld()
        {
            if (MultiTouch) { CancelDrag(); return false; }
            if (Input.touchCount == 1)
            {
                TouchPhase p = Input.GetTouch(0).phase;
                return p == TouchPhase.Moved || p == TouchPhase.Stationary;
            }
            return Input.GetMouseButton(0);
        }

        private bool PointerUp()
        {
            if (Input.touchCount == 1)
            {
                TouchPhase p = Input.GetTouch(0).phase;
                return p == TouchPhase.Ended || p == TouchPhase.Canceled;
            }
            return Input.touchCount == 0 && Input.GetMouseButtonUp(0);
        }

        private Vector2 PointerPos()
        {
            if (Input.touchCount >= 1) return Input.GetTouch(0).position;
            return Input.mousePosition;
        }

        private static bool IsPointerOverUi(int? fingerId)
        {
            EventSystem es = EventSystem.current;
            if (es == null) return false;
            return fingerId.HasValue ? es.IsPointerOverGameObject(fingerId.Value)
                                     : es.IsPointerOverGameObject();
        }

        // ------------------------------------------------------------------ press

        private void OnPointerDown(Vector2 screenPos)
        {
            if (!PlanEditorUtil.ScreenToGround(screenPos, out Vector3 groundWorld))
                return;
            Vector2 ground = PlanEditorUtil.WorldToMeters(groundWorld);
            pressScreenPos = screenPos;

            // Armed placement tools: one tap = one opening.
            if (Tool == PlanToolMode.AddDoor || Tool == PlanToolMode.AddWindow)
            {
                PlaceOpening(Tool == PlanToolMode.AddDoor, ground);
                SetTool(PlanToolMode.Browse);
                return;
            }

            float pxPerM = PlanEditorUtil.PixelsPerMeter();
            float handleRadM = HANDLE_RADIUS_PX / pxPerM;
            float edgeRadM = EDGE_RADIUS_PX / pxPerM;

            // 1) opening grab — any opening, nearest point within handle radius
            UIBaseItem bestOpening = null;
            float bestOpeningD = handleRadM * handleRadM;
            foreach (UIBaseItem ui in UnityEngine.Object.FindObjectsByType<UIBaseItem>(FindObjectsSortMode.None))
            {
                if (!PlanEditorUtil.IsOpening(ui) || ui.cpc == null) continue;
                foreach (Vector3 wp in PlanEditorUtil.WorldPoints(ui))
                {
                    float d = (PlanEditorUtil.WorldToMeters(wp) - ground).sqrMagnitude;
                    if (d < bestOpeningD) { bestOpeningD = d; bestOpening = ui; }
                }
            }
            if (bestOpening != null)
            {
                BeginDrag(DragKind.Opening, bestOpening, 0, ground);
                SelectionService.Instance?.SelectById(bestOpening.ItemUniqueId);
                return;
            }

            // Everything else operates on the SELECTED space item.
            UIBaseItem sel = PlanEditorUtil.FindItem(SelectionService.Instance?.SelectedId);
            if (sel == null || PlanEditorUtil.IsOpening(sel) || sel.cpc == null)
                return;

            List<Vector3> world = PlanEditorUtil.WorldPoints(sel);
            if (world.Count < 3)
                return;

            // 2) corner handle
            int corner = -1;
            float bestCorner = handleRadM * handleRadM;
            for (int i = 0; i < world.Count; i++)
            {
                float d = (PlanEditorUtil.WorldToMeters(world[i]) - ground).sqrMagnitude;
                if (d < bestCorner) { bestCorner = d; corner = i; }
            }
            if (corner >= 0)
            {
                BeginDrag(DragKind.Corner, sel, corner, ground);
                return;
            }

            // 3) wall edge
            int edge = -1;
            float bestEdge = edgeRadM * edgeRadM;
            Vector2 normal = default;
            for (int i = 0; i < world.Count; i++)
            {
                Vector2 a = PlanEditorUtil.WorldToMeters(world[i]);
                Vector2 b = PlanEditorUtil.WorldToMeters(world[(i + 1) % world.Count]);
                float d = PlanEditorUtil.DistToSegmentSq(ground, a, b, out _);
                if (d < bestEdge)
                {
                    bestEdge = d; edge = i;
                    Vector2 t = (b - a).normalized;
                    normal = new Vector2(-t.y, t.x);
                }
            }
            if (edge >= 0)
            {
                edgeNormal = normal;
                BeginDrag(DragKind.Edge, sel, edge, ground);
                return;
            }

            // 4) inside the polygon → whole-room drag after slop
            if (PointInPolygon(ground, world))
            {
                pressedInsideRoom = true;
                dragItem = sel;
                grabMeters = ground;
                startMeters = MetersOf(world);
            }
        }

        private void BeginDrag(DragKind kind, UIBaseItem item, int index, Vector2 ground)
        {
            drag = kind;
            dragItem = item;
            dragIndex = index;
            grabMeters = ground;
            moved = false;
            startMeters = MetersOf(PlanEditorUtil.WorldPoints(item));
            CameraEvents.OnRequestButtonAction?.Invoke(CameraEvents.Action.DisableCameraMoves, true);
        }

        // ------------------------------------------------------------------ move

        private void OnPointerMove(Vector2 screenPos)
        {
            // Promote an inside-press to a room drag once past slop.
            if (pressedInsideRoom && drag == DragKind.None)
            {
                if ((screenPos - pressScreenPos).magnitude < ROOM_DRAG_SLOP_PX)
                    return;
                drag = DragKind.Room;
                moved = false;
                CameraEvents.OnRequestButtonAction?.Invoke(CameraEvents.Action.DisableCameraMoves, true);
            }

            if (!IsDragging)
                return;
            if (!PlanEditorUtil.ScreenToGround(screenPos, out Vector3 groundWorld))
                return;
            Vector2 ground = PlanEditorUtil.WorldToMeters(groundWorld);
            Vector2 delta = ground - grabMeters;
            if (delta.sqrMagnitude > 1e-10f)
                moved = true;

            List<Vector2> preview = ComputePreview(ground, delta);
            if (preview != null)
                ApplyPreview(dragItem, preview, drag == DragKind.Opening);

            OnPlanVisualsDirty?.Invoke();
        }

        private List<Vector2> ComputePreview(Vector2 ground, Vector2 delta)
        {
            var pts = new List<Vector2>(startMeters);
            switch (drag)
            {
                case DragKind.Corner:
                {
                    Vector2 p = SnapFine(ground);
                    // axis assist: align with the two neighbours when close
                    Vector2 prev = pts[(dragIndex - 1 + pts.Count) % pts.Count];
                    Vector2 next = pts[(dragIndex + 1) % pts.Count];
                    if (Mathf.Abs(p.x - prev.x) < AXIS_ASSIST_M) p.x = prev.x;
                    else if (Mathf.Abs(p.x - next.x) < AXIS_ASSIST_M) p.x = next.x;
                    if (Mathf.Abs(p.y - prev.y) < AXIS_ASSIST_M) p.y = prev.y;
                    else if (Mathf.Abs(p.y - next.y) < AXIS_ASSIST_M) p.y = next.y;
                    pts[dragIndex] = p;
                    return pts;
                }
                case DragKind.Edge:
                {
                    float along = Vector2.Dot(delta, edgeNormal);
                    along = Mathf.Round(along / FINE_SNAP_M) * FINE_SNAP_M;
                    Vector2 offset = edgeNormal * along;
                    int i2 = (dragIndex + 1) % pts.Count;
                    pts[dragIndex] = startMeters[dragIndex] + offset;
                    pts[i2] = startMeters[i2] + offset;
                    return pts;
                }
                case DragKind.Room:
                {
                    Vector2 d = new Vector2(
                        Mathf.Round(delta.x / FINE_SNAP_M) * FINE_SNAP_M,
                        Mathf.Round(delta.y / FINE_SNAP_M) * FINE_SNAP_M);
                    for (int i = 0; i < pts.Count; i++)
                        pts[i] = startMeters[i] + d;
                    return pts;
                }
                case DragKind.Opening:
                {
                    if (!PlanEditorUtil.NearestWall(ground, OPENING_WALL_SEARCH_M,
                            out _, out _, out Vector2 onWall, out Vector2 tangent))
                        return null;
                    openingTangent = tangent;
                    return new List<Vector2> { onWall };
                }
            }
            return null;
        }

        private Vector2 openingTangent = Vector2.right;

        private static Vector2 SnapFine(Vector2 m) => new Vector2(
            Mathf.Round(m.x / FINE_SNAP_M) * FINE_SNAP_M,
            Mathf.Round(m.y / FINE_SNAP_M) * FINE_SNAP_M);

        /// <summary>Preview = ControlPoint visuals only; never raises events (no undo spam).</summary>
        private void ApplyPreview(UIBaseItem item, List<Vector2> meters, bool applyTangent)
        {
            Grid grid = PlanEditorUtil.SceneGrid;
            if (item == null || item.cpc == null || grid == null) return;
            List<ControlPoint> cps = item.cpc.GetPointsList();
            if (cps == null || cps.Count != meters.Count) return;

            for (int i = 0; i < cps.Count; i++)
            {
                Vector3 world = PlanEditorUtil.MetersToWorld(meters[i]);
                cps[i].SetNormalizedPosition(grid.GetNormalizedPosition(world));
                if (applyTangent)
                    cps[i].dir = new Vector3(openingTangent.x, 0f, openingTangent.y);
                cps[i].transform.position = world;
            }
            item.cpc.CreatePathVisualization();
        }

        // ------------------------------------------------------------------ release

        private void OnPointerUp()
        {
            bool wasDragging = IsDragging;
            UIBaseItem item = dragItem;
            DragKind kind = drag;

            if (wasDragging && moved && item != null)
            {
                Vector2 last = PointerPos();
                if (PlanEditorUtil.ScreenToGround(last, out Vector3 gw))
                {
                    Vector2 ground = PlanEditorUtil.WorldToMeters(gw);
                    List<Vector2> final = ComputePreview(ground, ground - grabMeters);
                    if (final != null && Distinct(final, startMeters))
                    {
                        bool ok = FloorPlanEditor.MoveItemPoints(item.ItemUniqueId, final);
                        if (!ok)
                            Debug.LogWarning("[PlanTouchController] MoveItemPoints rejected; restoring.");
                        if (!ok)
                            ApplyPreview(item, startMeters, kind == DragKind.Opening);
                    }
                    else
                    {
                        ApplyPreview(item, startMeters, kind == DragKind.Opening); // no-op drag → restore
                    }
                }
            }
            else if (wasDragging && !moved && item != null)
            {
                ApplyPreview(item, startMeters, kind == DragKind.Opening);
            }

            ClearDragState();
            OnPlanVisualsDirty?.Invoke();
        }

        private static bool Distinct(List<Vector2> a, List<Vector2> b)
        {
            if (a.Count != b.Count) return true;
            for (int i = 0; i < a.Count; i++)
                if ((a[i] - b[i]).sqrMagnitude > MIN_COMMIT_MOVE_M * MIN_COMMIT_MOVE_M)
                    return true;
            return false;
        }

        private void CancelDrag()
        {
            if (IsDragging && dragItem != null && startMeters != null)
                ApplyPreview(dragItem, startMeters, drag == DragKind.Opening);
            ClearDragState();
        }

        private void ClearDragState()
        {
            if (drag != DragKind.None)
                CameraEvents.OnRequestButtonAction?.Invoke(CameraEvents.Action.DisableCameraMoves, false);
            drag = DragKind.None;
            dragItem = null;
            startMeters = null;
            pressedInsideRoom = false;
            moved = false;
        }

        // ------------------------------------------------------------------ placement

        private void PlaceOpening(bool door, Vector2 ground)
        {
            if (!PlanEditorUtil.NearestWall(ground, PLACE_WALL_SEARCH_M,
                    out _, out _, out Vector2 onWall, out Vector2 tangent))
            {
                Debug.Log("[PlanTouchController] No wall near tap — opening not placed.");
                return;
            }
            string id = door
                ? FloorPlanEditor.AddOpening(DataModel.FloorMapItemType.Door, onWall, tangent, 0.9f, 2.1f)
                : FloorPlanEditor.AddOpening(DataModel.FloorMapItemType.Window, onWall, tangent, 1.2f, 1.2f, 0.9f);
            if (!string.IsNullOrEmpty(id))
                SelectionService.Instance?.SelectById(id);
        }

        // ------------------------------------------------------------------ misc

        private static List<Vector2> MetersOf(List<Vector3> world)
        {
            var list = new List<Vector2>(world.Count);
            foreach (Vector3 w in world)
                list.Add(PlanEditorUtil.WorldToMeters(w));
            return list;
        }

        private static bool PointInPolygon(Vector2 p, List<Vector3> polyWorld)
        {
            bool inside = false;
            int n = polyWorld.Count;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                Vector2 a = PlanEditorUtil.WorldToMeters(polyWorld[i]);
                Vector2 b = PlanEditorUtil.WorldToMeters(polyWorld[j]);
                if (a.y > p.y != b.y > p.y &&
                    p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x)
                    inside = !inside;
            }
            return inside;
        }
    }
}

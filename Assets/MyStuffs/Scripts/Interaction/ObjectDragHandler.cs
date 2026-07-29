using UnityEngine;
using System;

public class ObjectDragHandler : MonoBehaviour
{
    [Header("Dependencies")]
    [SerializeField] private SelectionManager selectionManager;

    [Header("Config")]
    [SerializeField] private LayerMask floorLayer;
    [SerializeField] private float     wallMargin = 0.08f;  // inset from wall (m)

    // A press is "on the item" only if the item is the nearest INTERACTABLE thing under
    // the finger. Testing the nearest collider of ANY layer (the old rule) let ordinary
    // room geometry veto the drag: from the bird's-eye camera the ray crosses the room's
    // ceiling collider on its way down, so every press on a sofa was refused and the
    // gesture fell through to the camera — the plan panned instead of the sofa moving.
    private const string INTERACTABLE_LAYER = "Interactable";

    // The bird's-eye camera sits ~36 m above the floor, so a 100 m budget is not a
    // matter of taste — it is the difference between reaching the room and not.
    private const float PICK_DISTANCE = 5000f;

    public event Action OnDragStart;
    public event Action OnDragEnd;

    private Transform _selectedObject;
    private Camera _mainCamera;
    private bool _isDragging = false;
    private bool _blocked = false;
    private Vector3 _dragStartPosition;
    private Vector3 _grabOffset;

    public bool IsDragging => _isDragging;
    
    private LayerMask _wallMask = 0;
    private LayerMask _interactableMask = 0;
    private FurniturePlacer _placer;

    // Floor extents, captured when a drag starts — the item's footprint is
    // clamped to this so it stays in the room (stops at walls, slides along them).
    private Bounds _roomBounds;
    private bool   _hasRoomBounds;
    private float  _floorY;   // floor height captured at drag start (for the plane)

    void Awake()
    {
        _mainCamera = Camera.main;
        // Only WALLS block dragging. Including "Default" made the cast hit the
        // floor/ceiling/props (and the model itself) → drag felt stuck/jumpy.
        _wallMask = LayerMask.GetMask("Wall");
        _interactableMask = LayerMask.GetMask(INTERACTABLE_LAYER);

        // The sweep can only stop the item if the walls actually have colliders
        // ON the "Wall" layer. If this fires, the item will only be held in by the
        // floor-bounds fallback (the thing that let it clip through).
        if (_wallMask.value == 0)
            Debug.LogWarning("[ObjectDragHandler] No 'Wall' layer found — wall " +
                "collision is disabled. Put your wall colliders on a layer named " +
                "'Wall' so dragged items stop at them.");
    }

    void OnEnable()
    {
        if (selectionManager != null)
        {
            selectionManager.onObjectSelected += HandleObjectSelected;
            selectionManager.onObjectDeselected += HandleObjectDeselected;
        }
    }

    void OnDisable()
    {
        if (selectionManager != null)
        {
            selectionManager.onObjectSelected -= HandleObjectSelected;
            selectionManager.onObjectDeselected -= HandleObjectDeselected;
        }
    }

    public void SetBlocked(bool blocked)
    {
        _blocked = blocked;

        if (blocked && _isDragging)
            CancelDrag();
    }

    // Called by ObjectManipulator in priority order
    public bool TryBeginDrag(Vector2 screenPosition)
    {
        if (_blocked)
            return false;

        if (_mainCamera == null)
            _mainCamera = Camera.main;
        if (_mainCamera == null)
            return false;

        Ray ray = _mainCamera.ScreenPointToRay(screenPosition);

        // DIRECT MANIPULATION: a press on any furniture drags it, selecting it on the
        // way in. This used to require the item to be selected ALREADY, which made the
        // first press-and-drag a dead gesture — and it did not even select, because
        // SelectionManager listens to OnPointerClick and the movement suppresses the
        // click. So an item the user had not tapped first simply felt unmovable.
        if (!PressedOnSelected(ray))
        {
            Transform grabbed = InteractableUnder(ray);
            if (grabbed == null)
                return false;
            // A ghost mid-placement is on the Interactable layer with live colliders;
            // that gesture belongs to FurniturePlacer, not to a drag.
            if (IsPlacing())
                return false;
            if (selectionManager != null)
                selectionManager.SelectObject(grabbed); // fires onObjectSelected → _selectedObject
            _selectedObject = grabbed;                  // also correct with no manager wired
        }

        _isDragging        = true;
        _dragStartPosition = _selectedObject.position;

        // The drag plane. Prefer the floor under the finger; fall back to the floor the
        // item is already standing on, so a missing/misconfigured floor collider costs
        // the room clamp but never the drag itself.
        if (Physics.Raycast(ray, out RaycastHit floorHit, PICK_DISTANCE, floorLayer))
        {
            _roomBounds    = floorHit.collider.bounds;   // room footprint to clamp to
            _floorY        = floorHit.point.y;           // plane height for UpdateDrag
            _hasRoomBounds = true;
        }
        else
        {
            _floorY        = BaseY(_selectedObject);
            _hasRoomBounds = false;
        }

        // XZ grab offset — keeps the item under the finger instead of snapping its
        // pivot there. Derived from the drag plane so it matches UpdateDrag exactly.
        Plane dragPlane = new Plane(Vector3.up, new Vector3(0f, _floorY, 0f));
        if (dragPlane.Raycast(ray, out float enter))
        {
            Vector3 grabPoint = ray.GetPoint(enter);
            _grabOffset = new Vector3(
                _selectedObject.position.x - grabPoint.x,
                0f,  // Y handled separately in UpdateDrag
                _selectedObject.position.z - grabPoint.z
            );
        }
        else
        {
            _grabOffset = Vector3.zero;
        }

        OnDragStart?.Invoke();
        return true;
    }

    /// <summary>
    /// True when the selected item is the nearest interactable thing under the pointer.
    /// Masked to Interactable on purpose: room geometry (ceiling, roof, floor) and
    /// screen-space rig helpers must not be able to shadow the item, but another piece
    /// of furniture in front of it still wins — pressing the sofa behind a table should
    /// not move the sofa.
    /// </summary>
    private bool PressedOnSelected(Ray ray)
    {
        // Nothing selected is a legitimate state now that TryBeginDrag can grab an
        // unselected item — IsChildOf(null) throws.
        if (_selectedObject == null)
            return false;

        if (_interactableMask.value == 0)
            _interactableMask = LayerMask.GetMask(INTERACTABLE_LAYER);

        if (!Physics.Raycast(ray, out RaycastHit hit, PICK_DISTANCE, _interactableMask,
                             QueryTriggerInteraction.Ignore))
            return false;

        return hit.transform == _selectedObject
               || hit.transform.IsChildOf(_selectedObject);
    }

    /// <summary>
    /// The furniture item under the pointer, resolved to the transform that owns it.
    /// Colliders live on the model's mesh children, so a raw hit.transform is usually
    /// a child — dragging that would move one mesh out of the item while
    /// FurnitureItem (and therefore everything that saves the layout) stays put.
    /// </summary>
    private Transform InteractableUnder(Ray ray)
    {
        if (_interactableMask.value == 0)
            _interactableMask = LayerMask.GetMask(INTERACTABLE_LAYER);

        if (!Physics.Raycast(ray, out RaycastHit hit, PICK_DISTANCE, _interactableMask,
                             QueryTriggerInteraction.Ignore))
            return null;

        FurnitureItem item = hit.transform.GetComponentInParent<FurnitureItem>();
        if (item != null)
            return item.transform;

        // A door is not a sofa. Openings sit on the Interactable layer so they can be
        // selected by the same framework, but they have no free position — they own a
        // slot in a wall — so this handler must never take one. OpeningInteraction slides
        // them along their host wall instead.
        if (!string.IsNullOrEmpty(
                IBMROS.Designer.Openings.OpeningInteraction.OpeningIdOf(hit.transform)))
            return null;

        return hit.transform;
    }

    private bool IsPlacing()
    {
        if (_placer == null)
            _placer = FindAnyObjectByType<FurniturePlacer>(FindObjectsInactive.Include);
        return _placer != null && _placer.IsPlacing;
    }

    /// <summary>World Y of the bottom of an item's visible bounds (its standing height).</summary>
    private static float BaseY(Transform t)
    {
        Renderer[] renderers = t.GetComponentsInChildren<Renderer>();
        bool has = false;
        Bounds combined = default;
        foreach (var r in renderers)
        {
            if (r is ParticleSystemRenderer || r.gameObject.name == "DynamicBlobShadow")
                continue;
            if (!has) { combined = r.bounds; has = true; }
            else combined.Encapsulate(r.bounds);
        }
        return has ? combined.min.y : t.position.y;
    }

    public void UpdateDrag(Vector2 screenPosition)
    {
        if (!_isDragging || _selectedObject == null) return;

        Ray ray = _mainCamera.ScreenPointToRay(screenPosition);

        // Raycast a MATH PLANE at floor height — NOT the floor collider. The
        // plane always returns a point, so the item follows the cursor smoothly
        // everywhere (even when the cursor is over a wall), instead of freezing
        // when a collider raycast misses. That was the real cause of the
        // stickiness. The room-bounds clamp below keeps it off the walls.
        Plane floorPlane = new Plane(Vector3.up, new Vector3(0f, _floorY, 0f));
        if (!floorPlane.Raycast(ray, out float enter)) return;
        Vector3 hitPoint = ray.GetPoint(enter);

        // Object footprint (for floor-snap + clamping).
        Bounds combined    = default;
        bool   hasBounds   = false;
        float  pivotToBase = 0f;
        Renderer[] renderers = _selectedObject.GetComponentsInChildren<Renderer>();
        if (renderers.Length > 0)
        {
            foreach (var r in renderers)
            {
                if (r is ParticleSystemRenderer || r.gameObject.name == "DynamicBlobShadow")
                    continue;

                if (!hasBounds)
                {
                    combined = r.bounds;
                    hasBounds = true;
                }
                else
                {
                    combined.Encapsulate(r.bounds);
                }
            }
            if (hasBounds)
            {
                pivotToBase = _selectedObject.position.y - combined.min.y;
            }
        }

        Vector3 targetPosition = new Vector3(
            hitPoint.x + _grabOffset.x,
            _floorY + pivotToBase,
            hitPoint.z + _grabOffset.z
        );

        // ── WALL BLOCKING (the real fix) ─────────────────────────────────────
        // The old code clamped to the FLOOR collider's bounding box, which only
        // matches the walls when the floor edge sits exactly on the inner wall
        // face. After the walls were rebuilt as boxes that no longer lines up, so
        // an item could slide a wall-thickness into / behind the wall. Instead we
        // SWEEP the item's footprint against the ACTUAL wall colliders and stop at
        // the contact. Works for any room shape and wall thickness, and slides
        // along the wall (rounds corners) because the sweep is axis-separated.
        if (hasBounds && _wallMask.value != 0)
        {
            Vector3 centerOffset = combined.center - _selectedObject.position; // pivot → footprint centre
            Vector3 fromCenter   = combined.center;                            // current footprint centre
            Vector3 toCenter     = targetPosition + centerOffset;              // desired footprint centre
            toCenter.y           = fromCenter.y;                               // sweep horizontally only

            Vector3 solvedCenter = SweepAlongWalls(fromCenter, toCenter, combined.extents);

            targetPosition.x = solvedCenter.x - centerOffset.x;
            targetPosition.z = solvedCenter.z - centerOffset.z;
        }

        // Outer safety: never leave the floor footprint even if a wall is missing
        // or not on the Wall layer (keeps the item in the room as a fallback).
        if (_hasRoomBounds && hasBounds)
        {
            float minX = _roomBounds.min.x + combined.extents.x;
            float maxX = _roomBounds.max.x - combined.extents.x;
            float minZ = _roomBounds.min.z + combined.extents.z;
            float maxZ = _roomBounds.max.z - combined.extents.z;
            if (minX <= maxX) targetPosition.x = Mathf.Clamp(targetPosition.x, minX, maxX);
            if (minZ <= maxZ) targetPosition.z = Mathf.Clamp(targetPosition.z, minZ, maxZ);
        }

        _selectedObject.position = targetPosition;

        // ORIENTED footprint containment (the sweep above casts an axis-aligned box, so
        // a rotated item was tested as its bounding square and its real corners could
        // end up inside a wall). Exact against the room polygon from the document; a
        // no-op in scenes without one, where the clamps above remain the only rule.
        IBMROS.Designer.Furnish.RoomFootprint.ClampInside(_selectedObject, wallMargin);
    }

    // Collide-and-slide: move the footprint from 'from' toward 'to', stopping at
    // the first wall on each axis. Axis-separated (X then Z) so a blocked axis
    // doesn't freeze the other — the item slides along the wall and turns corners.
    private Vector3 SweepAlongWalls(Vector3 from, Vector3 to, Vector3 halfExtents)
    {
        Vector3 pos = from;
        pos = SweepAxis(pos, new Vector3(to.x, pos.y, pos.z), halfExtents); // X
        pos = SweepAxis(pos, new Vector3(pos.x, pos.y, to.z), halfExtents); // Z
        return pos;
    }

    private Vector3 SweepAxis(Vector3 from, Vector3 to, Vector3 halfExtents)
    {
        Vector3 delta = to - from;
        float   dist  = delta.magnitude;
        if (dist < 1e-4f) return to;
        Vector3 dir = delta / dist;

        // Shrink the cast box a hair so a face already flush to a wall doesn't
        // register a zero-distance hit and lock the axis.
        Vector3 castExtents = Vector3.Max(halfExtents - Vector3.one * 0.005f,
                                          Vector3.one * 0.001f);

        if (Physics.BoxCast(from, castExtents, dir, out RaycastHit hit,
                            Quaternion.identity, dist, _wallMask,
                            QueryTriggerInteraction.Ignore))
        {
            // Stop wallMargin short of the wall so the mesh doesn't visually touch.
            float allowed = Mathf.Max(0f, hit.distance - wallMargin);
            return from + dir * allowed;
        }
        return to;
    }

 
    /// <summary>Below this (1 mm) a "drag" was really a tap — no undo step for it.</summary>
    private const float MIN_UNDO_MOVE_M = 0.001f;

    public void EndDrag()
    {
        if (!_isDragging)
            return;

        _isDragging = false;
        OnDragEnd?.Invoke();

        if (_selectedObject == null)
            return;

        // Record only real moves. Without this every press-and-release on a selected
        // item pushed an identity step, so Undo spent taps doing nothing visible.
        Vector3 end = _selectedObject.position;
        if ((end - _dragStartPosition).sqrMagnitude < MIN_UNDO_MOVE_M * MIN_UNDO_MOVE_M)
            return;

        UndoRedoManager.Instance?.Record(
            new MoveAction(_selectedObject, _dragStartPosition, end)
        );
    }

    private void CancelDrag()
    {
        if (!_isDragging)
            return;

        _isDragging = false;
        OnDragEnd?.Invoke();
    }

    private void HandleObjectSelected(Transform target)
    {
        _selectedObject = target;
    }

    private void HandleObjectDeselected()
    {
        _selectedObject = null;
        CancelDrag();
    }
}
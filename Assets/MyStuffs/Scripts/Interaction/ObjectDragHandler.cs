using UnityEngine;
using System;

public class ObjectDragHandler : MonoBehaviour
{
    [Header("Dependencies")]
    [SerializeField] private SelectionManager selectionManager;

    [Header("Config")]
    [SerializeField] private LayerMask floorLayer;
    [SerializeField] private float     wallMargin = 0.08f;  // inset from wall (m)

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
        if (_blocked || _selectedObject == null)
            return false;

        Ray ray = _mainCamera.ScreenPointToRay(screenPosition);

        if (!Physics.Raycast(ray, out RaycastHit hit, 100f))
            return false;

        bool hitSelected = hit.transform == _selectedObject
                           || hit.transform.IsChildOf(_selectedObject);

        if (!hitSelected)
            return false;

        _isDragging        = true;
        _dragStartPosition = _selectedObject.position;

        // Only store XZ grab offset — Y is always calculated from floor
        if (Physics.Raycast(ray, out RaycastHit floorHit, 100f, floorLayer))
        {
            // XZ offset only — keeps object under finger horizontally
            _grabOffset = new Vector3(
                _selectedObject.position.x - floorHit.point.x,
                0f,  // Y handled separately in UpdateDrag
                _selectedObject.position.z - floorHit.point.z
            );
            _roomBounds    = floorHit.collider.bounds;   // room footprint to clamp to
            _floorY        = floorHit.point.y;           // plane height for UpdateDrag
            _hasRoomBounds = true;
        }
        else
        {
            _grabOffset    = Vector3.zero;
            _hasRoomBounds = false;
        }

        OnDragStart?.Invoke();
        return true;
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
            combined = renderers[0].bounds;
            foreach (var r in renderers)
                combined.Encapsulate(r.bounds);
            pivotToBase = _selectedObject.position.y - combined.min.y;
            hasBounds   = true;
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

 
    public void EndDrag()
    {
        if (!_isDragging)
            return;

        _isDragging = false;
        OnDragEnd?.Invoke();

        UndoRedoManager.Instance?.Record(
            new MoveAction(_selectedObject, _dragStartPosition, _selectedObject.position)
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
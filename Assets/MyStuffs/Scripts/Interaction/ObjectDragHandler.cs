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

        // Clamp the item's footprint to the room so it stops at a wall but still
        // slides ALONG it (clamping X leaves Z free, and vice-versa). Pure math,
        // no physics — smooth, and it can't pass through walls.
        if (_hasRoomBounds && hasBounds)
        {
            // wallMargin pulls the stop point IN from the floor edge so an item
            // halts just before the wall instead of creeping into its thickness
            // (the re-grab-at-wall bug). Tune to ~your wall thickness in the
            // Inspector if needed.
            float minX = _roomBounds.min.x + combined.extents.x + wallMargin;
            float maxX = _roomBounds.max.x - combined.extents.x - wallMargin;
            float minZ = _roomBounds.min.z + combined.extents.z + wallMargin;
            float maxZ = _roomBounds.max.z - combined.extents.z - wallMargin;
            if (minX <= maxX) targetPosition.x = Mathf.Clamp(targetPosition.x, minX, maxX);
            if (minZ <= maxZ) targetPosition.z = Mathf.Clamp(targetPosition.z, minZ, maxZ);
        }

        _selectedObject.position = targetPosition;
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
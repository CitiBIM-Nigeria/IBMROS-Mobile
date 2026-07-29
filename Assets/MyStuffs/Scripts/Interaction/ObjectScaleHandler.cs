using UnityEngine;
using System;

public class ObjectScaleHandler : MonoBehaviour
{
    [Header("Dependencies")]
    [SerializeField] private SelectionManager selectionManager;
    [SerializeField] private ScaleRigUI scaleRigUI;

    [Header("Config")]
    [SerializeField] private float minScale = 0.2f;
    [SerializeField] private float maxScale = 5.0f;

    public event Action OnScaleStart;
    public event Action OnScaleEnd;

    private Transform _selectedObject;
    private bool _isScaling = false;
    private bool _blocked = false;
    private ScaleHandleUI _activeHandle;
    private Vector3 _initialScale;
    private Vector2 _initialPointerPosition;
    private Vector3 _initialObjectPosition;

    public bool IsScaling => _isScaling;

    void OnEnable()
    {
        if (selectionManager != null)
        {
            selectionManager.onObjectSelected += HandleObjectSelected;
            selectionManager.onObjectDeselected += HandleObjectDeselected;
        }

        if (scaleRigUI != null)
        {
            scaleRigUI.OnHandleDown += HandleDown;
            scaleRigUI.OnHandleDrag += HandleDrag;
            scaleRigUI.OnHandleUp += HandleUp;
        }
    }

    void OnDisable()
    {
        if (selectionManager != null)
        {
            selectionManager.onObjectSelected -= HandleObjectSelected;
            selectionManager.onObjectDeselected -= HandleObjectDeselected;
        }

        if (scaleRigUI != null)
        {
            scaleRigUI.OnHandleDown -= HandleDown;
            scaleRigUI.OnHandleDrag -= HandleDrag;
            scaleRigUI.OnHandleUp -= HandleUp;
        }
    }

    public void SetBlocked(bool blocked)
    {
        _blocked = blocked;
        if (blocked && _isScaling)
            CancelScale();
    }

    private void HandleObjectSelected(Transform target)
    {
        _selectedObject = target;
    }

    private void HandleObjectDeselected()
    {
        _selectedObject = null;
        CancelScale();
    }

    private void HandleDown(ScaleHandleUI handle, Vector2 screenPosition)
    {
        if (_blocked || _selectedObject == null)
            return;

        _isScaling = true;
        _activeHandle = handle;
        _initialScale = _selectedObject.localScale;
        _initialObjectPosition = _selectedObject.position;
        _initialPointerPosition = screenPosition;

        scaleRigUI?.SetHandleHighlight(handle);
        OnScaleStart?.Invoke();
    }
    private void HandleDrag(ScaleHandleUI handle, Vector2 screenPosition)
    {
        if (!_isScaling || _selectedObject == null || _activeHandle == null)
            return;

        PerformScaling(screenPosition);
    }

    /// <summary>Below this a "scale" was a tap on a handle — no undo step for it.</summary>
    private const float MIN_UNDO_SCALE_DELTA = 0.0005f;

    private void HandleUp(ScaleHandleUI handle, Vector2 screenPosition)
    {
        if (!_isScaling) return;
        _isScaling = false;
        _activeHandle = null;
        scaleRigUI?.ClearHighlights();
        SnapToFloor(); // ← move it here, fire once on release

        OnScaleEnd?.Invoke();

        if (_selectedObject == null)
            return;

        // Record only real scales, and record the POSITION with them — scaling
        // anchors the opposite side and re-snaps to the floor, so scale alone is
        // not enough to put the item back.
        Vector3 endScale = _selectedObject.localScale;
        if ((endScale - _initialScale).sqrMagnitude < MIN_UNDO_SCALE_DELTA * MIN_UNDO_SCALE_DELTA)
            return;

        UndoRedoManager.Instance?.Record(
            new ScaleAction(_selectedObject, _initialScale, endScale,
                            _initialObjectPosition, _selectedObject.position)
        );
    }

    private void PerformScaling(Vector2 currentScreenPos)
    {
        if (_activeHandle == null || _selectedObject == null)
            return;

        Camera cam = Camera.main;
        Vector3 handleWorldPos = _selectedObject.position
            + _selectedObject.rotation * _activeHandle.direction;
        Vector3 handleScreenPos = cam.WorldToScreenPoint(handleWorldPos);
        // Use the CACHED position, not the live one
        Vector3 centerScreenPos = cam.WorldToScreenPoint(_initialObjectPosition);

        Vector2 screenDir = new Vector2(
            handleScreenPos.x - centerScreenPos.x,
            handleScreenPos.y - centerScreenPos.y
        ).normalized;

        Vector2 initialOffset = _initialPointerPosition
            - new Vector2(centerScreenPos.x, centerScreenPos.y);
        Vector2 currentOffset = currentScreenPos
            - new Vector2(centerScreenPos.x, centerScreenPos.y);

        float initialProjection = Vector2.Dot(initialOffset, screenDir);
        float currentProjection = Vector2.Dot(currentOffset, screenDir);

        if (Mathf.Abs(initialProjection) < 0.01f)
            return;

        float scaleFactor = Mathf.Clamp(
            currentProjection / initialProjection, 0.1f, 10f);

        Vector3 newScale = _initialScale;

        switch (_activeHandle.type)
        {
            case HandleType.Corner:
                // Corner scales both X and Y (height grows with width)
                newScale.x *= scaleFactor;
                newScale.y *= scaleFactor;
                break;
            case HandleType.AxisX:
                // Side X handle scales only X
                newScale.x *= scaleFactor;
                break;
            case HandleType.AxisZ:
                // Side Z handle scales only Z
                newScale.y *= scaleFactor;
                break;
        }

        newScale.x = Mathf.Clamp(newScale.x, minScale, maxScale);
        newScale.y = Mathf.Clamp(newScale.y, minScale, maxScale);
        newScale.z = Mathf.Clamp(newScale.z, minScale, maxScale);

        _selectedObject.localScale = newScale;
        
        // Shift object so the opposite side stays anchored
        Vector3 worldDelta = _selectedObject.rotation
                             * new Vector3(
                                 _activeHandle.direction.x * (newScale.x - _initialScale.x) * 0.5f,
                                 0,
                                 _activeHandle.direction.z * (newScale.z - _initialScale.z) * 0.5f
                             );

    // Note: this needs the initial position cached at scale start
        _selectedObject.position = _initialObjectPosition + worldDelta;

        // Keep object on floor after scaling
        SnapToFloor();

        // Growing an item pushes its footprint outward, so scaling up against a wall
        // used to drive the mesh into it — the only limits here were min/maxScale.
        IBMROS.Designer.Furnish.RoomFootprint.ClampInside(_selectedObject);
    }

    private void SnapToFloor()
    {
        Renderer renderer = _selectedObject.GetComponentInChildren<Renderer>();
        if (renderer == null)
            return;

        float bottomY = renderer.bounds.min.y;
        float offset = -bottomY;

        Vector3 pos = _selectedObject.position;
        pos.y += offset;
        _selectedObject.position = pos;
    }

    private void CancelScale()
    {
        if (!_isScaling)
            return;

        _isScaling = false;
        _activeHandle = null;
        scaleRigUI?.ClearHighlights();
        OnScaleEnd?.Invoke();
    }

    // Called by ObjectManipulator coordinator
    public bool TryBeginScale(Vector2 screenPosition)
    {
        // Screen space handles handle their own input via UGUI events
        // This method is no longer used but kept for interface compatibility
        return false;
    }
    

    public void UpdateScale(Vector2 screenPosition) { }

    public void EndScale() { }
}
using UnityEngine;
using System;

public class ObjectDragHandler : MonoBehaviour
{
    [Header("Dependencies")]
    [SerializeField] private SelectionManager selectionManager;

    [Header("Config")]
    [SerializeField] private LayerMask floorLayer;

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

    void Awake()
    {
        _mainCamera = Camera.main;
        _wallMask = LayerMask.GetMask("Wall", "Default");
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
        }
        else
        {
            _grabOffset = Vector3.zero;
        }

        OnDragStart?.Invoke();
        return true;
    }

    public void UpdateDrag(Vector2 screenPosition)
    {
        if (!_isDragging || _selectedObject == null) return;

        Ray ray = _mainCamera.ScreenPointToRay(screenPosition);

        if (Physics.Raycast(ray, out RaycastHit hit, 100f, floorLayer))
        {
            // XZ from floor hit + grab offset
            // Y always from floor + pivot-to-bottom so chair never sinks
            float pivotToBase = 0f;
            Renderer[] renderers = _selectedObject.GetComponentsInChildren<Renderer>();
            if (renderers.Length > 0)
            {
                Bounds combined = renderers[0].bounds;
                foreach (var r in renderers)
                    combined.Encapsulate(r.bounds);
                pivotToBase = _selectedObject.position.y - combined.min.y;
            }
            
            Vector3 targetPosition = new Vector3(
                hit.point.x + _grabOffset.x,
                hit.point.y + pivotToBase,
                hit.point.z + _grabOffset.z
            );

            Vector3 direction = targetPosition - _selectedObject.position;
            float   distance  = direction.magnitude;

            if (distance > 0.001f)
            {
                var box = _selectedObject.GetComponent<BoxCollider>();
                if (box != null)
                {
                    if (!Physics.BoxCast(
                            _selectedObject.position,
                            box.size * 0.45f,
                            direction.normalized,
                            out _,
                            _selectedObject.rotation,
                            distance,
                            _wallMask))
                    {
                        _selectedObject.position = targetPosition;
                    }
                }
                else
                {
                    _selectedObject.position = targetPosition;
                }
            }
        }
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
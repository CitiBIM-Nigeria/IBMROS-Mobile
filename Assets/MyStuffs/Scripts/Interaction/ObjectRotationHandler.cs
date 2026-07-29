using UnityEngine;
using UnityEngine.EventSystems;
using System;

public class ObjectRotationHandler : MonoBehaviour
{
    [Header("Dependencies")]
    [SerializeField] private SelectionManager selectionManager;
    [SerializeField] private UIDragHandle rotationHandle;

    [Header("Config")]
    [SerializeField] private float rotationSpeed = 0.5f;

    public event Action OnRotationStart;
    public event Action OnRotationEnd;

    private Transform _selectedObject;
    private bool _isRotating = false;
    private bool _blocked = false;
    private Quaternion _rotationStart;

    public bool IsRotating => _isRotating;

    void OnEnable()
    {
        if (selectionManager != null)
        {
            selectionManager.onObjectSelected += HandleObjectSelected;
            selectionManager.onObjectDeselected += HandleObjectDeselected;
        }

        if (rotationHandle != null)
        {
            rotationHandle.onDragStart += HandleDragStart;
            rotationHandle.onDrag += HandleDrag;
            rotationHandle.onDragEnd += HandleDragEnd;
        }
    }

    void OnDisable()
    {
        if (selectionManager != null)
        {
            selectionManager.onObjectSelected -= HandleObjectSelected;
            selectionManager.onObjectDeselected -= HandleObjectDeselected;
        }

        if (rotationHandle != null)
        {
            rotationHandle.onDragStart -= HandleDragStart;
            rotationHandle.onDrag -= HandleDrag;
            rotationHandle.onDragEnd -= HandleDragEnd;
        }
    }

    public void SetBlocked(bool blocked)
    {
        _blocked = blocked;

        if (blocked && _isRotating)
            CancelRotation();
    }

    private void HandleObjectSelected(Transform target)
    {
        _selectedObject = target;
    }

    private void HandleObjectDeselected()
    {
        _selectedObject = null;
        CancelRotation();
    }

    private void HandleDragStart(PointerEventData data)
    {
        if (_blocked || _selectedObject == null)
            return;

        _rotationStart = _selectedObject.rotation;
        _isRotating = true;
        OnRotationStart?.Invoke();
    }

    private void HandleDrag(PointerEventData data)
    {
        if (!_isRotating || _selectedObject == null)
            return;

        _selectedObject.Rotate(
            Vector3.up,
            -data.delta.x * rotationSpeed,
            Space.World
        );

        // Turning an item in place sweeps its corners outward, so a piece that fitted
        // against a wall can rotate straight through it. Nothing validated that before.
        IBMROS.Designer.Furnish.RoomFootprint.ClampInside(_selectedObject);
    }

    /// <summary>Below this a "rotation" was a tap on the handle — no undo step for it.</summary>
    private const float MIN_UNDO_ANGLE_DEG = 0.25f;

    private void HandleDragEnd(PointerEventData data)
    {
        if (!_isRotating)
            return;

        _isRotating = false;
        OnRotationEnd?.Invoke();

        if (_selectedObject == null)
            return;

        // Record only real rotations, so Undo never spends a tap on an identity step.
        Quaternion end = _selectedObject.rotation;
        if (Quaternion.Angle(_rotationStart, end) < MIN_UNDO_ANGLE_DEG)
            return;

        UndoRedoManager.Instance?.Record(
            new RotateAction(_selectedObject, _rotationStart, end)
        );
    }

    private void CancelRotation()
    {
        if (!_isRotating)
            return;

        _isRotating = false;
        OnRotationEnd?.Invoke();
    }
}
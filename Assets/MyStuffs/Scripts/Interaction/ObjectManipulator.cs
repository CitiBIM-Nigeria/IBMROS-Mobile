using UnityEngine;
using System;

/// <summary>
/// Coordinates all input-driven interactions in the Room scene.
///
/// Priority order for pointer input:
///   1. Scale handle tap/drag  (highest)
///   2. Drag selected object
///   3. Camera look rotation   (single finger, no object)
///
/// Two-finger input is routed directly to CameraController
/// and never interferes with object manipulation.
/// </summary>
public class ObjectManipulator : MonoBehaviour
{
    // ---------------------------------------------------------------
    // DEPENDENCIES
    // ---------------------------------------------------------------

    [Header("Dependencies")]
    [SerializeField] private InputManager          inputManager;
    [SerializeField] private SelectionManager      selectionManager;
    [SerializeField] private ObjectDragHandler     dragHandler;
    [SerializeField] private ObjectRotationHandler rotationHandler;
    [SerializeField] private ObjectScaleHandler    scaleHandler;
    [SerializeField] private CameraController      cameraController;

    // ---------------------------------------------------------------
    // EVENTS
    // ---------------------------------------------------------------

    public event Action OnManipulationStart;
    public event Action OnManipulationEnd;
    public event Action OnCameraRotationStart;
    public event Action OnCameraRotationEnd;

    // ---------------------------------------------------------------
    // PRIVATE STATE
    // ---------------------------------------------------------------

    private bool      _isRotatingCamera = false;
    private Vector2   _lastScreenPos    = Vector2.zero;
    private Transform _selectedObject   = null;

    // ---------------------------------------------------------------
    // UNITY LIFECYCLE
    // ---------------------------------------------------------------

    void OnEnable()
    {
        if (inputManager != null)
        {
            inputManager.OnPointerDown       += HandlePointerDown;
            inputManager.OnPointerMove       += HandlePointerMove;
            inputManager.OnPointerUp         += HandlePointerUp;
            inputManager.OnTwoFingerPanDelta += HandleTwoFingerPan;
        }

        if (selectionManager != null)
        {
            selectionManager.onObjectSelected   += HandleObjectSelected;
            selectionManager.onObjectDeselected += HandleObjectDeselected;
        }
    }

    void OnDisable()
    {
        if (inputManager != null)
        {
            inputManager.OnPointerDown       -= HandlePointerDown;
            inputManager.OnPointerMove       -= HandlePointerMove;
            inputManager.OnPointerUp         -= HandlePointerUp;
            inputManager.OnTwoFingerPanDelta -= HandleTwoFingerPan;
        }

        if (selectionManager != null)
        {
            selectionManager.onObjectSelected   -= HandleObjectSelected;
            selectionManager.onObjectDeselected -= HandleObjectDeselected;
        }
    }

    // ---------------------------------------------------------------
    // SELECTION CALLBACKS
    // ---------------------------------------------------------------

    private void HandleObjectSelected(Transform target)
    {
        _selectedObject = target;
    }

    private void HandleObjectDeselected()
    {
        _selectedObject = null;
        CancelCameraRotation();
    }

    // ---------------------------------------------------------------
    // SINGLE FINGER INPUT
    // ---------------------------------------------------------------

    private void HandlePointerDown(Vector2 screenPosition)
    {
        // Priority 1 — scale handle (handled via UGUI events in ScaleRigUI)
        if (scaleHandler.TryBeginScale(screenPosition))
        {
            OnManipulationStart?.Invoke();
            return;
        }

        // Priority 2 — drag selected object
        if (dragHandler.TryBeginDrag(screenPosition))
        {
            OnManipulationStart?.Invoke();
            return;
        }

        // Priority 2b — slide a selected door/window along its wall. Architectural
        // elements are constrained, so they get their own handler rather than the free
        // drag above; it comes after furniture so a chair in front of a door still wins.
        var openings = IBMROS.Designer.Openings.OpeningInteraction.Instance;
        if (openings != null && openings.TryBeginDrag(screenPosition))
        {
            OnManipulationStart?.Invoke();
            return;
        }

        // Priority 3 — camera look rotation (finger on empty space)
        _isRotatingCamera = true;
        _lastScreenPos    = screenPosition;
        dragHandler.SetBlocked(true);
        scaleHandler.SetBlocked(true);
        rotationHandler.SetBlocked(true);
        OnCameraRotationStart?.Invoke();
    }

    private void HandlePointerMove(Vector2 screenPosition)
    {
        if (scaleHandler.IsScaling)
        {
            scaleHandler.UpdateScale(screenPosition);
            return;
        }

        if (dragHandler.IsDragging)
        {
            dragHandler.UpdateDrag(screenPosition);
            return;
        }

        var openingsMove = IBMROS.Designer.Openings.OpeningInteraction.Instance;
        if (openingsMove != null && openingsMove.OwnsInput)
        {
            openingsMove.UpdateDrag(screenPosition);
            return;
        }

        if (_isRotatingCamera)
        {
            Vector2 delta  = screenPosition - _lastScreenPos;
            _lastScreenPos = screenPosition;
            cameraController?.RotateCamera(delta);
        }
    }

    private void HandlePointerUp(Vector2 screenPosition)
    {
        if (scaleHandler.IsScaling)
        {
            scaleHandler.EndScale();
            OnManipulationEnd?.Invoke();
            return;
        }

        if (dragHandler.IsDragging)
        {
            dragHandler.EndDrag();
            OnManipulationEnd?.Invoke();
            return;
        }

        var openingsUp = IBMROS.Designer.Openings.OpeningInteraction.Instance;
        if (openingsUp != null && openingsUp.OwnsInput)
        {
            openingsUp.EndDrag();
            OnManipulationEnd?.Invoke();
            return;
        }

        if (_isRotatingCamera)
            CancelCameraRotation();
    }

    private void CancelCameraRotation()
    {
        if (!_isRotatingCamera) return;

        _isRotatingCamera = false;
        dragHandler.SetBlocked(false);
        scaleHandler.SetBlocked(false);
        rotationHandler.SetBlocked(false);
        OnCameraRotationEnd?.Invoke();
    }

    // ---------------------------------------------------------------
    // TWO FINGER PAN — routes directly to camera, never touches objects
    // ---------------------------------------------------------------

    private void HandleTwoFingerPan(Vector2 screenDelta)
    {
        cameraController?.HandleTwoFingerPan(screenDelta);
    }

    // ---------------------------------------------------------------
    // OBJECT ACTIONS (called by UI buttons)
    // ---------------------------------------------------------------

    public void DeleteSelectedObject()
    {
        // An opening is deleted THROUGH the document, not by hiding a GameObject: the
        // wall's mesh is generated from the room's opening list, so removing the item is
        // what closes the hole — and it updates the 2D plan and undo in the same step.
        // Hiding the visual here would leave a hole in the wall with nothing in it.
        var openings = IBMROS.Designer.Openings.OpeningInteraction.Instance;
        if (openings != null && openings.HasSelection)
        {
            selectionManager.DeselectObject();
            openings.Delete();
            return;
        }

        if (_selectedObject == null) return;

        GameObject go = _selectedObject.gameObject;
        selectionManager.DeselectObject();

        UndoRedoManager.Instance?.Record(new DeleteAction(go));

        // Deactivate instead of Destroy so Undo can restore it
        go.SetActive(false);

        Debug.Log($"[ObjectManipulator] Deleted: {go.name}");
    }

    public void DuplicateSelectedObject()
    {
        // Instantiating an opening's visual would clone a mesh with no wall behind it.
        // Duplicating the ITEM gives a real second opening, cut into the same wall and
        // slid clear of the original.
        var openings = IBMROS.Designer.Openings.OpeningInteraction.Instance;
        if (openings != null && openings.HasSelection)
        {
            openings.Duplicate();
            return;
        }

        if (_selectedObject == null) return;

        Vector3 offset = new Vector3(0.5f, 0f, 0.5f);

        GameObject clone = Instantiate(
            _selectedObject.gameObject,
            _selectedObject.position + offset,
            _selectedObject.rotation
        );

        clone.name = _selectedObject.name
            .Replace("(Clone)", "").Trim();

        UndoRedoManager.Instance?.Record(
            new DuplicateAction(clone, _selectedObject.name, selectionManager));

        selectionManager.SelectObject(clone.transform);

        Debug.Log($"[ObjectManipulator] Duplicated: {clone.name}");
    }
}
using UnityEngine;
using UnityEngine.EventSystems;
using System;

public class SelectionManager : MonoBehaviour
{
    [Header("Dependencies")]
    public InputManager inputManager;
    
    [Header("Config")]
    public LayerMask interactableLayer;

    // Events
    public event Action<Transform> onObjectSelected;
    public event Action onObjectDeselected;
    public event Action<Transform> onObjectReSelected;
    
    private Transform _selectedObject;
    private Camera _mainCamera;
    private FurniturePlacer _placer;

    void Awake()
    {
        _mainCamera = Camera.main;

        if (_mainCamera == null)
            Debug.LogError("[SelectionManager] Main Camera not found. " +
                           "Make sure your camera is tagged MainCamera.");

        if (inputManager != null)
            inputManager.OnPointerClick += HandlePointerClick;
    }

    void OnDestroy()
    {
        if (inputManager != null)
            inputManager.OnPointerClick -= HandlePointerClick; // capital O
    }

    private void HandlePointerClick(Vector2 screenPosition)
    {
        if (EventSystem.current == null)
            return;

        if (EventSystem.current.IsPointerOverGameObject())
            return;

        // While a ghost is being placed the confirming tap belongs to
        // FurniturePlacer. The ghost sits under the finger on the Interactable
        // layer with live colliders, so without this the same tap selected the
        // ghost and the placement was lost.
        if (_placer == null)
            _placer = FindAnyObjectByType<FurniturePlacer>(FindObjectsInactive.Include);
        if (_placer != null && _placer.IsPlacing)
            return;

        Ray ray = _mainCamera.ScreenPointToRay(screenPosition);
        RaycastHit hit;

        if (Physics.Raycast(ray, out hit, 100f, interactableLayer))
        {
            if (_selectedObject == hit.transform)
            {
                onObjectReSelected?.Invoke(hit.transform);
                return;
            }

            SelectObject(hit.transform);
        }
        else
        {
            DeselectObject();
        }
    }

    // --- PUBLIC METHODS (Called by ObjectManipulator) ---

    public void SelectObject(Transform newObject)
    {
        _selectedObject = newObject;
        onObjectSelected?.Invoke(_selectedObject);
    }

    public void DeselectObject()
    {
        if (_selectedObject != null)
        {
            _selectedObject = null;
            onObjectDeselected?.Invoke(); // This tells ContextualMenuController to hide panels
        }
    }
}
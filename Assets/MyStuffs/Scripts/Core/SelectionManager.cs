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
            Transform target = ResolveSelectable(hit.transform);

            if (_selectedObject == target)
            {
                onObjectReSelected?.Invoke(target);
                return;
            }

            SelectObject(target);
        }
        else
        {
            DeselectObject();
        }
    }

    /// <summary>
    /// Walks a collider hit up to the transform that OWNS the furniture item.
    ///
    /// FurnitureSpawnManager puts a MeshCollider on every mesh child, so a raw hit is
    /// usually a child of the model while FurnitureItem sits on the root. Selecting the
    /// child made every downstream operation act on one mesh instead of the item: drag
    /// and rotate moved part of the model, Delete hid a single mesh, Duplicate cloned a
    /// fragment, and FurniturePersistence — which saves FurnitureItem.transform — wrote
    /// the untouched root, so a saved room did not match what the user had arranged.
    /// Anything without a FurnitureItem (scene props, handles) selects as-is.
    /// </summary>
    private static Transform ResolveSelectable(Transform hit)
    {
        FurnitureItem item = hit.GetComponentInParent<FurnitureItem>();
        return item != null ? item.transform : hit;
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
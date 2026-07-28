using UnityEngine;
using System;

public class FurniturePlacer : MonoBehaviour
{
    [Header("Dependencies")]
    [SerializeField] private InputManager inputManager;
    [SerializeField] private SelectionManager selectionManager;
    [SerializeField] private FurnitureRegistry furnitureRegistry;

    [Header("Config")]
    [SerializeField] private LayerMask floorLayer;
    [SerializeField] private float placementHeightOffset = 0f;

    [Header("Initial Snap")]
    [Tooltip("How far in front of the camera to spawn the object (XZ plane).")]
    [SerializeField] private float spawnDistanceForward = 2.5f;

    [Tooltip("How high above the spawn point to start the downward raycast. " +
             "Increase this if your floor is below world Y=0.")]
    [SerializeField] private float snapRaycastStartHeight = 5f;

    public event Action<FurnitureItem> OnFurniturePlaced;
    public event Action OnPlacementCancelled;

    private GameObject _previewObject;
    private FurnitureItem _previewItem;
    private bool _isPlacing = false;
    private Camera _mainCamera;

    private Vector3 _smoothedPosition;
    private bool _hasInitialPosition = false;
    public float POSITION_SMOOTH_SPEED = 2f;

    private float _pivotToBottomOffset = 0f;

    private System.Collections.Generic.Dictionary<Renderer, Material[]> _originalMaterials = new System.Collections.Generic.Dictionary<Renderer, Material[]>();
    private Material _ghostMaterial;

    void Awake()
    {
        _mainCamera = Camera.main;
        
        // Load from Resources to prevent the shader from being stripped in Android/iOS builds!
        Shader ghostShader = Resources.Load<Shader>("Shaders/GhostPreview");
        
        if (ghostShader != null) 
        {
            _ghostMaterial = new Material(ghostShader);
        }
        else
        {
            Debug.LogError("[FurniturePlacer] Could not find GhostPreview shader in Resources!");
        }
    }

    void OnEnable()
    {
        if (inputManager != null)
        {
            inputManager.OnPointerMove += HandlePointerMove;
            inputManager.OnPointerClick += HandlePointerClick;
        }
    }

    void OnDisable()
    {
        if (inputManager != null)
        {
            inputManager.OnPointerMove -= HandlePointerMove;
            inputManager.OnPointerClick -= HandlePointerClick;
        }
    }

    public void CancelPlacement()
    {
        if (!_isPlacing) return;

        if (_previewObject != null)
            Destroy(_previewObject);

        _previewObject      = null;
        _previewItem        = null;
        _isPlacing          = false;
        _hasInitialPosition = false;
        _pivotToBottomOffset = 0f;
        _originalMaterials.Clear();

        OnPlacementCancelled?.Invoke();
        Debug.Log("[FurniturePlacer] Placement cancelled.");
    }

    public bool IsPlacing => _isPlacing;

    // ---------------------------------------------------------------
    // INPUT HANDLERS
    // ---------------------------------------------------------------

    private void HandlePointerMove(Vector2 screenPosition)
    {
        if (!_isPlacing || _previewObject == null) return;

        if (_mainCamera == null) _mainCamera = Camera.main;
        if (_mainCamera == null) return;

        Ray ray = _mainCamera.ScreenPointToRay(screenPosition);

        if (!Physics.Raycast(ray, out RaycastHit hit, 100f, floorLayer))
            return;

        Vector3 targetPosition = GetLiftedPosition(hit.point);

        if (!_hasInitialPosition)
        {
            _smoothedPosition   = targetPosition;
            _hasInitialPosition = true;
        }

        _smoothedPosition = Vector3.Lerp(
            _smoothedPosition,
            targetPosition,
            Time.deltaTime * POSITION_SMOOTH_SPEED
        );

        _previewObject.transform.position = _smoothedPosition;
    }

    private void HandlePointerClick(Vector2 screenPosition)
    {
        if (!_isPlacing || _previewObject == null)
            return;

        if (_mainCamera == null) _mainCamera = Camera.main;
        if (_mainCamera == null) return;

        Ray ray = _mainCamera.ScreenPointToRay(screenPosition);

        if (!Physics.Raycast(ray, out RaycastHit hit, 100f, floorLayer))
            return;

        _previewObject.transform.position = hit.point;
        ApplyFloorOffset(hit.point);

        SetPreviewMaterial(false);
        _previewItem.SetPlaced(true);

        furnitureRegistry?.Register(_previewItem);

        UndoRedoManager.Instance?.Record(
            new PlaceAction(_previewObject, selectionManager)
        );

        FurnitureItem placedItem = _previewItem;

        _previewObject = null;
        _previewItem   = null;
        _isPlacing     = false;

        selectionManager?.SelectObject(placedItem.transform);
        OnFurniturePlaced?.Invoke(placedItem);
    }

    // ---------------------------------------------------------------
    // BEGIN PLACEMENT
    // ---------------------------------------------------------------

    public void BeginPlacement(GameObject furniturePrefab)
    {
        if (furniturePrefab == null) return;
        if (_isPlacing) CancelPlacement();

        _previewObject = Instantiate(furniturePrefab);
        _previewItem   = _previewObject.GetComponent<FurnitureItem>()
                         ?? _previewObject.AddComponent<FurnitureItem>();

        SetPreviewMaterial(true);
        _isPlacing = true;

        selectionManager?.DeselectObject();
        SnapToFloorInFrontOfCamera();

        Debug.Log($"[FurniturePlacer] Placement started for {furniturePrefab.name}");
    }

    public void BeginPlacementFromInstance(GameObject sceneInstance)
    {
        if (sceneInstance == null) return;
        if (_isPlacing) CancelPlacement();

        _previewObject = sceneInstance;
        _previewItem   = _previewObject.GetComponent<FurnitureItem>()
                         ?? _previewObject.AddComponent<FurnitureItem>();

        SetPreviewMaterial(true);
        _isPlacing = true;

        selectionManager?.DeselectObject();
        SnapToFloorInFrontOfCamera();

        Debug.Log($"[FurniturePlacer] Placement started (from instance) for {sceneInstance.name}");
    }

    // ---------------------------------------------------------------
    // SNAP TO FLOOR — fixed
    //
    // Old approach: ray from camera position going camera.forward
    //   Problem: at eye height the ray hits a wall or flies over the
    //   floor entirely, leaving the ghost floating in the air.
    //
    // New approach:
    //   1. Pick a point on the XZ plane directly in front of the camera
    //      (ignoring camera pitch so it's always on the floor plane).
    //   2. Cast a ray straight DOWN from high above that point.
    //   3. That ray always hits the floor regardless of camera angle.
    // ---------------------------------------------------------------

    /// <summary>
    /// Optional spawn override, in world space. Set by the designer for the
    /// bird's-eye view, where "in front of the camera" is meaningless (the
    /// camera looks straight down, so flatForward collapses and the ghost landed
    /// off to the side — often outside the room). Returning null falls back to
    /// the in-front-of-camera behaviour used inside the room.
    /// </summary>
    public static System.Func<Vector3?> SpawnPointProvider;

    private void SnapToFloorInFrontOfCamera()
    {
        if (_mainCamera == null)
            _mainCamera = Camera.main;

        if (_mainCamera == null) return;

        if (SpawnPointProvider != null)
        {
            Vector3? preferred = SpawnPointProvider();
            if (preferred.HasValue)
            {
                Vector3 origin = new Vector3(preferred.Value.x,
                                             preferred.Value.y + snapRaycastStartHeight,
                                             preferred.Value.z);
                if (Physics.Raycast(new Ray(origin, Vector3.down), out RaycastHit floorHit,
                                    snapRaycastStartHeight + 20f, floorLayer))
                    ApplyFloorOffset(floorHit.point);
                else
                    ApplyFloorOffset(new Vector3(preferred.Value.x, 0f, preferred.Value.z));
                return;
            }
        }

        // Step 1 — find a point in front of the camera on the XZ plane.
        // We use the camera's yaw (Y rotation) only, ignoring pitch,
        // so the target point is always at floor level distance.
        Vector3 flatForward = _mainCamera.transform.forward;
        flatForward.y = 0f;

        // If camera is looking straight up/down flatForward can be zero
        if (flatForward.sqrMagnitude < 0.001f)
            flatForward = _mainCamera.transform.right; // fallback

        flatForward.Normalize();

        Vector3 targetXZ = _mainCamera.transform.position
                           + flatForward * spawnDistanceForward;

        // Step 2 — cast straight down from above that point to find the floor.
        Vector3 rayOrigin = new Vector3(targetXZ.x,
                                        targetXZ.y + snapRaycastStartHeight,
                                        targetXZ.z);

        Ray downRay = new Ray(rayOrigin, Vector3.down);

        if (Physics.Raycast(downRay, out RaycastHit hit,
                            snapRaycastStartHeight + 20f, floorLayer))
        {
            Debug.Log($"[FurniturePlacer] Floor hit at {hit.point}");
            ApplyFloorOffset(hit.point);
        }
        else
        {
            // Fallback — floor collider not found on floorLayer.
            // Place at the XZ target position at Y=0 as a last resort.
            // This usually means floorLayer isn't assigned correctly.
            Debug.LogWarning("[FurniturePlacer] Floor not found via downward raycast. " +
                             "Check that floorLayer is assigned and your floor has a collider " +
                             "on that layer. Falling back to Y=0.");

            Vector3 fallback = targetXZ;
            fallback.y = 0f;
            ApplyFloorOffset(fallback);
        }

        // Initialise smoothed position to avoid lerp-from-zero on first move
        if (_previewObject != null)
        {
            _smoothedPosition   = _previewObject.transform.position;
            _hasInitialPosition = true;
        }
    }

    // ---------------------------------------------------------------
    // FLOOR OFFSET — lifts pivot so bottom of mesh sits on the floor
    // ---------------------------------------------------------------

    private void ApplyFloorOffset(Vector3 hitPoint)
    {
        if (_previewObject == null) return;

        // Place at hit point first so bounds are calculated in world space
        _previewObject.transform.position = hitPoint;

        Renderer[] renderers = _previewObject.GetComponentsInChildren<Renderer>();

        if (renderers.Length == 0)
        {
            _pivotToBottomOffset = 0f;
            _previewObject.transform.position = hitPoint
                + Vector3.up * placementHeightOffset;
            return;
        }

        Bounds combined = renderers[0].bounds;
        foreach (var r in renderers)
            combined.Encapsulate(r.bounds);

        // Distance from pivot (current Y) to the bottom of the mesh
        _pivotToBottomOffset = _previewObject.transform.position.y
                               - combined.min.y;

        Vector3 finalPosition = hitPoint;
        finalPosition.y += _pivotToBottomOffset + placementHeightOffset;
        _previewObject.transform.position = finalPosition;
    }

    private Vector3 GetLiftedPosition(Vector3 hitPoint)
    {
        return new Vector3(
            hitPoint.x,
            hitPoint.y + _pivotToBottomOffset + placementHeightOffset,
            hitPoint.z
        );
    }

    // ---------------------------------------------------------------
    // PREVIEW MATERIAL — semi-transparent ghost
    // ---------------------------------------------------------------

    private void SetPreviewMaterial(bool isPreview)
    {
        if (_previewObject == null) return;

        Renderer[] renderers = _previewObject.GetComponentsInChildren<Renderer>();

        if (isPreview)
        {
            _originalMaterials.Clear();
            foreach (var renderer in renderers)
            {
                _originalMaterials[renderer] = renderer.materials;
                
                if (_ghostMaterial != null)
                {
                    Material[] ghostMats = new Material[renderer.materials.Length];
                    for (int i = 0; i < ghostMats.Length; i++) ghostMats[i] = _ghostMaterial;
                    renderer.materials = ghostMats;
                }
            }
            
            // Add blob shadow
            if (_previewObject.GetComponent<BlobShadow>() == null)
            {
                _previewObject.AddComponent<BlobShadow>();
            }
        }
        else
        {
            // Restore original materials
            foreach (var renderer in renderers)
            {
                if (_originalMaterials.TryGetValue(renderer, out var origMats))
                {
                    renderer.materials = origMats;
                }
            }
            _originalMaterials.Clear();
            
            // Keep the blob shadow instead of destroying it
            var blob = _previewObject.GetComponent<BlobShadow>();
            if (blob != null) 
            {
                // We keep it permanently
            }
        }
    }
}
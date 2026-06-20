using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Bridge between the furniture UI panel and the placement pipeline.
/// 1. Looks up the GLB filename from FurnitureRegistry
/// 2. Loads the model via FurnitureService
/// 3. Hands the loaded model to FurniturePlacer for ghost placement
/// </summary>
public class FurnitureSpawnManager : MonoBehaviour
{
    [Header("Dependencies")]
    [SerializeField] private FurnitureRegistry registry;
    [SerializeField] private FurniturePlacer   furniturePlacer;
    
    // ---------------------------------------------------------------
    // CALLED BY RoomUIManager when Add to Room is tapped
    // ---------------------------------------------------------------

    public async void SpawnItem(string s3ModelUrl)
    {
        if (furniturePlacer == null)
        {
            Debug.LogError("[FurnitureSpawnManager] FurniturePlacer not assigned.");
            return;
        }

        if (string.IsNullOrEmpty(s3ModelUrl))
        {
            Debug.LogWarning("[FurnitureSpawnManager] No model URL. Using placeholder.");
            var placeholder = CreatePlaceholderCube("Unknown", isLoading: false);
            InitializeFurnitureItem(placeholder, "Unknown", null);
            furniturePlacer.BeginPlacement(placeholder);
            return;
        }

        Debug.Log($"[FurnitureSpawnManager] Loading GLB: {s3ModelUrl}");

        // Show loading cube immediately so user gets instant feedback
        var loadingCube = CreatePlaceholderCube(s3ModelUrl, isLoading: true);
        InitializeFurnitureItem(loadingCube, s3ModelUrl, null);
        furniturePlacer.BeginPlacementFromInstance(loadingCube);

        GameObject loadedModel = null;

        void OnLoaded(string fileName, GameObject model)
        {
            if (fileName != s3ModelUrl) return;
            FurnitureService.OnModelLoaded -= OnLoaded;
            loadedModel = model;
        }

        void OnFailed(string fileName, string error)
        {
            if (fileName != s3ModelUrl) return;
            FurnitureService.OnModelLoadFailed -= OnFailed;
            Debug.LogError($"[FurnitureSpawnManager] Load failed: {error}");
        }

        FurnitureService.OnModelLoaded     += OnLoaded;
        FurnitureService.OnModelLoadFailed += OnFailed;

        await FurnitureService.Instance.LoadModel(s3ModelUrl);

        FurnitureService.OnModelLoaded     -= OnLoaded;
        FurnitureService.OnModelLoadFailed -= OnFailed;

        if (loadedModel != null)
        {
            // Grab position and rotation from the loading cube before destroying it
            Vector3    cubePosition = loadingCube.transform.position;
            Quaternion cubeRotation = loadingCube.transform.rotation;

            // Cancel placement of the loading cube and destroy it
            furniturePlacer.CancelPlacement();
            Destroy(loadingCube);

            // Place the real model at the same position
            loadedModel.transform.position = cubePosition;
            loadedModel.transform.rotation = cubeRotation;

            InitializeFurnitureItem(loadedModel, s3ModelUrl, null);
            furniturePlacer.BeginPlacementFromInstance(loadedModel);
            return;
        }

        // Model failed to load — loading cube stays as placeholder
        Debug.LogWarning($"[FurnitureSpawnManager] Model failed, keeping placeholder cube.");
        if (loadingCube != null)
            loadingCube.name = $"[Placeholder] {s3ModelUrl}";
    }
    
    private GameObject CreatePlaceholderCube(string itemKey, bool isLoading = false)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = isLoading ? "LoadingPlaceholder" : $"[Placeholder] {itemKey}";
        go.transform.localScale = new Vector3(0.8f, 0.8f, 0.8f);

        var renderer = go.GetComponent<Renderer>();
        if (renderer != null)
        {
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            // Grey while loading, blue as permanent placeholder
            mat.color = isLoading
                ? new Color(0.75f, 0.75f, 0.75f, 0.6f)
                : new Color(0.4f, 0.6f, 1f, 0.5f);
            renderer.material = mat;
        }

        // Remove default collider — InitializeFurnitureItem adds its own
        var col = go.GetComponent<Collider>();
        if (col != null) Destroy(col);

        return go;
    }

    
    // ---------------------------------------------------------------
    // HELPERS
    // ---------------------------------------------------------------

    private void InitializeFurnitureItem(
        GameObject go, string itemKey, FurnitureRegistry.CatalogEntry entry)
    {
        var item = go.GetComponent<FurnitureItem>()
                   ?? go.AddComponent<FurnitureItem>();

        item.Initialize(
            id:            itemKey,
            furnitureName: itemKey,
            category:      "Uncategorized",
            realWorldSize: Vector3.one
        );

        int layer = LayerMask.NameToLayer("Interactable");
        if (layer >= 0)
        {
            go.layer = layer;
            foreach (Transform child in go.GetComponentsInChildren<Transform>())
                child.gameObject.layer = layer;
        }

        foreach (var col in go.GetComponentsInChildren<Collider>())
            Destroy(col);

        Renderer[] renderers = go.GetComponentsInChildren<Renderer>();

        if (renderers.Length > 0)
        {
            Bounds combined = renderers[0].bounds;
            foreach (var r in renderers)
                combined.Encapsulate(r.bounds);

            var box    = go.AddComponent<BoxCollider>();
            box.center = go.transform.InverseTransformPoint(combined.center);
            box.size   = combined.size;
        }
        else
        {
            go.AddComponent<BoxCollider>();
        }

        // Add Rigidbody so Unity physics engine respects colliders
        // Kinematic = we control position manually via drag
        // FreezeAll = no physics rotation or movement
        // ContinuousSpeculative = detects collisions with static walls correctly
        // Remove any existing Rigidbodies from children first
        foreach (var existingRb in go.GetComponentsInChildren<Rigidbody>())
            Destroy(existingRb);

        // Add fresh Rigidbody to root only
        var furnitureRb = go.AddComponent<Rigidbody>();
        furnitureRb.isKinematic            = true;
        furnitureRb.useGravity             = false;
        furnitureRb.constraints            = RigidbodyConstraints.FreezeAll;
        furnitureRb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

        Debug.Log($"[FurnitureSpawnManager] Initialized: {itemKey} " +
                  $"| Collider: {go.GetComponent<BoxCollider>() != null} " +
                  $"| Rigidbody: {go.GetComponent<Rigidbody>() != null}");
    }
    
    
    
}
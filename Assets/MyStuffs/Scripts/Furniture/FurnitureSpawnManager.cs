using System.Diagnostics;
using System.Threading.Tasks;
using UnityEngine;
using Debug = UnityEngine.Debug;

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

    void Awake()
    {
        // Self-heal missing Inspector wiring (the cause of the
        // "FurniturePlacer not assigned" error) by finding them in the scene.
        if (furniturePlacer == null)
            furniturePlacer = FindObjectOfType<FurniturePlacer>(true);
        if (registry == null)
            registry = FindObjectOfType<FurnitureRegistry>(true);
    }

    // ---------------------------------------------------------------
    // CALLED BY RoomUIManager when Add to Room is tapped
    // ---------------------------------------------------------------

    public async void SpawnItem(string s3ModelUrl)
    {
        if (furniturePlacer == null)
            furniturePlacer = FindObjectOfType<FurniturePlacer>(true);

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

        var spawnSw = Stopwatch.StartNew();
        string shortName = System.IO.Path.GetFileName(s3ModelUrl);

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
            Debug.LogError($"[Spawn] {shortName} | load FAILED: {error}");
        }

        FurnitureService.OnModelLoaded     += OnLoaded;
        FurnitureService.OnModelLoadFailed += OnFailed;

        await FurnitureService.Instance.LoadModel(s3ModelUrl);
        long loadMs = spawnSw.ElapsedMilliseconds;

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

            var initSw = Stopwatch.StartNew();
            InitializeFurnitureItem(loadedModel, s3ModelUrl, null);
            initSw.Stop();

            furniturePlacer.BeginPlacementFromInstance(loadedModel);

            spawnSw.Stop();
            Debug.Log($"[Spawn] {shortName}" +
                      $" | load {loadMs}ms" +
                      $" | init {initSw.ElapsedMilliseconds}ms" +
                      $" | END-TO-END {spawnSw.ElapsedMilliseconds}ms");
            return;
        }

        // Model failed to load — loading cube stays as placeholder
        spawnSw.Stop();
        Debug.LogWarning($"[Spawn] {shortName} | FAILED | kept placeholder | {spawnSw.ElapsedMilliseconds}ms");
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
        var sw = Stopwatch.StartNew();

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

        // FPS: stop placed furniture from CASTING real-time shadows. The shadow
        // pass re-draws every mesh each frame — a big per-frame cost on mobile,
        // and the low-res mobile shadow map is what made shadows look bad. They
        // still RECEIVE shadows, so they're still lit by the room.
        foreach (var r in go.GetComponentsInChildren<Renderer>())
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        long preColliders = sw.ElapsedMilliseconds;

        foreach (var col in go.GetComponentsInChildren<Collider>())
            Destroy(col);

        // TWO collider roles:
        //  • a ROOT BoxCollider, DISABLED — ObjectDragHandler reads its .size to
        //    BoxCast against walls and stop the item (it passed through walls
        //    when this box was missing). Disabled so it doesn't grab selection
        //    raycasts; .size is still readable while disabled.
        //  • non-convex MeshColliders per mesh, ENABLED — precise tap selection.
        //    Non-convex has NO 256-poly limit (that's convex-only, the source of
        //    the "Couldn't create a Convex Mesh" error) and works for raycasts;
        //    movement is transform-based so it never needs to physically collide.
        Renderer[] renderers = go.GetComponentsInChildren<Renderer>();
        if (renderers.Length > 0)
        {
            Bounds combined = default;
            bool hasBounds = false;
            
            foreach (var r in renderers)
            {
                if (r is ParticleSystemRenderer || r.gameObject.name == "DynamicBlobShadow")
                    continue;

                if (!hasBounds)
                {
                    combined = r.bounds;
                    hasBounds = true;
                }
                else
                {
                    combined.Encapsulate(r.bounds);
                }
            }
            
            if (hasBounds)
            {
                var box     = go.AddComponent<BoxCollider>();
                box.center  = go.transform.InverseTransformPoint(combined.center);
                box.size    = combined.size;
                box.enabled = false;   // size-only, for the wall BoxCast
            }
        }

        int meshColliders = 0;
        foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
        {
            if (mf.sharedMesh == null) continue;
            var mc        = mf.gameObject.AddComponent<MeshCollider>();
            mc.sharedMesh = mf.sharedMesh;
            mc.convex     = false;
            meshColliders++;
        }

        // No meshes at all → re-enable the box so selection still works.
        if (meshColliders == 0)
        {
            var box = go.GetComponent<BoxCollider>();
            if (box != null) box.enabled = true;
            else go.AddComponent<BoxCollider>();
        }

        long colliderMs = sw.ElapsedMilliseconds - preColliders;

        // Add Rigidbody so Unity physics engine respects colliders
        // Kinematic = we control position manually via drag
        // FreezeAll = no physics rotation or movement
        // ContinuousSpeculative = detects collisions with static walls correctly
        // Remove any existing Rigidbodies from children first
        long preRb = sw.ElapsedMilliseconds;
        foreach (var existingRb in go.GetComponentsInChildren<Rigidbody>())
            Destroy(existingRb);

        // Add fresh Rigidbody to root only
        var furnitureRb = go.AddComponent<Rigidbody>();
        furnitureRb.isKinematic            = true;
        furnitureRb.useGravity             = false;
        furnitureRb.constraints            = RigidbodyConstraints.FreezeAll;
        furnitureRb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        long rbMs = sw.ElapsedMilliseconds - preRb;

        sw.Stop();
        int totalColliders = go.GetComponentsInChildren<Collider>().Length;
        string shortName = System.IO.Path.GetFileName(itemKey);
        Debug.Log($"[Spawn] {shortName}" +
                  $" | colliders {colliderMs}ms ({meshColliders} mesh + 1 box)" +
                  $" | rigidbody {rbMs}ms" +
                  $" | TOTAL-INIT {sw.ElapsedMilliseconds}ms");
    }
    
    
    
}
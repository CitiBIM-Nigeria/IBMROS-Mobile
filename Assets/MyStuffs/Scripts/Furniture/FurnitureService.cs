using System;
using System.Threading.Tasks;
using UnityEngine;

public class FurnitureService : MonoBehaviour
{
    public static FurnitureService Instance { get; private set; }

    // ============================================
    // EVENTS
    // Both UI Toolkit and UGUI controllers
    // subscribe to these instead of calling
    // FurnitureModelLoader directly
    // ============================================

    // Fired when a model loads successfully
    // string = fileName, GameObject = loaded model
    public static event Action<string, GameObject> OnModelLoaded;

    // Fired when a model fails to load
    // string = fileName, string = error message
    public static event Action<string, string> OnModelLoadFailed;

    // Fired during download to show progress
    // string = fileName, float = 0.0 to 1.0
    public static event Action<string, float> OnModelDownloadProgress;

    // Fired when a model starts downloading
    public static event Action<string> OnModelDownloadStarted;

    // Fired when a model finishes downloading
    public static event Action<string> OnModelDownloadComplete;

    // Loading state for UI
    public static event Action<bool, string> OnLoadingChanged;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    // ============================================
    // LOAD MODEL
    // Main method both teams call to get a
    // furniture model loaded into the scene
    // ============================================

    public async Task LoadModel(string fileName)
    {
        if (string.IsNullOrEmpty(fileName))
        {
            Debug.LogError("[FurnitureService] fileName cannot be empty.");
            OnModelLoadFailed?.Invoke(fileName, "Invalid file name.");
            return;
        }

        try
        {
            bool isCached = FurnitureModelLoader.Instance.IsCached(fileName);

            if (!isCached)
            {
                OnModelDownloadStarted?.Invoke(fileName);
                OnLoadingChanged?.Invoke(true, $"Downloading {fileName}...");
            }
            else
            {
                OnLoadingChanged?.Invoke(true, "Loading model...");
            }

            GameObject model = await FurnitureModelLoader.Instance.LoadModel(fileName);

            OnLoadingChanged?.Invoke(false, string.Empty);

            if (model != null)
            {
                if (!isCached)
                    OnModelDownloadComplete?.Invoke(fileName);

                OnModelLoaded?.Invoke(fileName, model);
            }
            else
            {
                Debug.LogError($"[FurnitureService] Failed to load model: {fileName}");
                OnModelLoadFailed?.Invoke(fileName, "Failed to load model. Please try again.");
            }
        }
        catch (Exception e)
        {
            OnLoadingChanged?.Invoke(false, string.Empty);
            Debug.LogError($"[FurnitureService] LoadModel error: {e.Message}");
            OnModelLoadFailed?.Invoke(fileName, "Something went wrong. Please try again.");
        }
    }

    // ============================================
    // PRELOAD MODEL
    // Downloads and caches a model without
    // instantiating it yet. Call this while
    // user is browsing to speed up AR placement
    // ============================================

    public async Task PreloadModel(string fileName)
    {
        if (string.IsNullOrEmpty(fileName))
            return;

        if (FurnitureModelLoader.Instance.IsCached(fileName))
            return;

        try
        {
            OnModelDownloadStarted?.Invoke(fileName);

            GameObject model = await FurnitureModelLoader.Instance.LoadModel(fileName);

            if (model != null)
            {
                OnModelDownloadComplete?.Invoke(fileName);

                // Destroy the instantiated object since we only wanted to cache
                Destroy(model);
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[FurnitureService] Preload error: {e.Message}");
        }
    }

    // ============================================
    // CACHE MANAGEMENT
    // ============================================

    public bool IsModelCached(string fileName)
    {
        return FurnitureModelLoader.Instance.IsCached(fileName);
    }

    public void ClearModelCache()
    {
        FurnitureModelLoader.Instance.ClearCache();
    }

    public void RemoveModelFromCache(string fileName)
    {
        FurnitureModelLoader.Instance.DeleteFromCache(fileName);
    }
}
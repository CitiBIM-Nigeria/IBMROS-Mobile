using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using GLTFast; // GLBs are now WebP-free, so glTFast loads them with correct
               // sRGB/Linear colour spaces + URP materials (no manual fixups).
using UnityEngine;
using UnityEngine.Networking;

public class FurnitureModelLoader : MonoBehaviour
{
    public static FurnitureModelLoader Instance { get; private set; }

    // Local cache folder inside the app's persistent data path
    private string CachePath => Path.Combine(Application.persistentDataPath, "FurnitureCache");

    // Bump this string whenever the pipeline re-uploads models at the SAME S3
    // paths (e.g. after a re-scrape). On startup, if the cached version differs,
    // the whole model cache is wiped so old bytes can't be served. This is what
    // stops a stale, texture-stripped mesh.glb from loading after a re-scrape.
    private const string CacheVersion = "2026-06-23-per-colour-clean";

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        if (!Directory.Exists(CachePath))
            Directory.CreateDirectory(CachePath);

        // Auto-bust a stale cache when the version marker doesn't match.
        string marker = Path.Combine(CachePath, "cache_version.txt");
        string current = File.Exists(marker) ? File.ReadAllText(marker) : "";
        if (current != CacheVersion)
        {
            ClearCache();   // deletes + recreates the cache folder
            try { File.WriteAllText(Path.Combine(CachePath, "cache_version.txt"),
                                    CacheVersion); } catch { }
            Debug.Log($"[FurnitureModelLoader] Model cache cleared (version "
                      + $"{CacheVersion}) — will re-download fresh models.");
        }
    }
    
    // Main method to load a furniture model by filename
    // Returns the loaded GameObject or null if it failed
    public async Task<GameObject> LoadModel(string fileName)
    {
        try
        {
            string localPath = Path.Combine(CachePath, fileName);

            // Check if model is already cached locally
            if (File.Exists(localPath))
            {
                Debug.Log($"[FurnitureModelLoader] Loading {fileName} from cache.");
                return await LoadFromFile(localPath, fileName);
            }

            // Not cached, download from S3
            Debug.Log($"[FurnitureModelLoader] Downloading {fileName} from S3.");
            bool downloaded = await DownloadFromCloudFront(fileName, localPath);

            if (!downloaded)
            {
                Debug.LogError($"[FurnitureModelLoader] Failed to download {fileName}.");
                return null;
            }

            return await LoadFromFile(localPath, fileName);
        }
        catch (Exception e)
        {
            Debug.LogError($"[FurnitureModelLoader] LoadModel error: {e.Message}");
            return null;
        }
    }

    // Downloads a file from S3 and saves it to the local cache
    private async Task<bool> DownloadFromCloudFront(string fileName, string localPath)
    {
        try
        {
            // EscapeUriString encodes spaces but leaves slashes intact
            // EscapeDataString would encode slashes as %2F which breaks CloudFront paths
            string encodedFileName = Uri.EscapeUriString(fileName);
            string url = $"{AwsConfig.CloudFrontDomain}/{encodedFileName}";

            Debug.Log($"[FurnitureModelLoader] Requesting: {url}");

            // Create the local subdirectory if it does not exist
            string localDir = Path.GetDirectoryName(localPath);
            if (!string.IsNullOrEmpty(localDir) && !Directory.Exists(localDir))
            {
                Directory.CreateDirectory(localDir);
                Debug.Log($"[FurnitureModelLoader] Created cache directory: {localDir}");
            }

            using var request = UnityWebRequest.Get(url);

            // Stream straight to disk instead of buffering the whole GLB in RAM and
            // then writing it again. Halves the memory cost and removes the extra
            // byte[]→file copy — the slow part on big meshes / low-memory phones.
            // Write to a ".part" temp file and move it into place only on success,
            // so a failed/cancelled download can never leave a half-written model
            // in the cache (which would later load as a broken mesh).
            string tmpPath = localPath + ".part";
            request.downloadHandler = new DownloadHandlerFile(tmpPath)
            {
                removeFileOnAbort = true
            };

            var operation = request.SendWebRequest();
            while (!operation.isDone)
                await Task.Yield();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[FurnitureModelLoader] CloudFront error: {request.error}");
                try { if (File.Exists(tmpPath)) File.Delete(tmpPath); } catch { }
                return false;
            }

            if (File.Exists(localPath)) File.Delete(localPath);
            File.Move(tmpPath, localPath);
            Debug.Log($"[FurnitureModelLoader] Downloaded and cached: {fileName}");
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"[FurnitureModelLoader] CloudFront download error: {e.Message}");
            return false;
        }
    }
    
    // Loads a GLB file from a local path using glTFast
    private async Task<GameObject> LoadFromFile(string localPath, string fileName)
    {
        try
        {
            // glTFast loads the self-contained GLB (PNG/JPEG textures, no
            // EXT_texture_webp) and builds URP materials with correct colour
            // spaces — no shader-swap or texture re-application needed.
            byte[] data = await File.ReadAllBytesAsync(localPath);

            var gltf   = new GltfImport();
            bool loaded = await gltf.LoadGltfBinary(data);
            if (!loaded)
            {
                Debug.LogError($"[FurnitureModelLoader] glTFast failed to load {fileName}.");
                return null;
            }

            var root = new GameObject(fileName);
            bool ok  = await gltf.InstantiateMainSceneAsync(root.transform);
            if (!ok)
            {
                Debug.LogError($"[FurnitureModelLoader] glTFast failed to instantiate {fileName}.");
                Destroy(root);
                return null;
            }

            Debug.Log($"[FurnitureModelLoader] Loaded (glTFast): {fileName}");
            return root;
        }
        catch (Exception e)
        {
            Debug.LogError($"[FurnitureModelLoader] LoadFromFile error: {e.Message}");
            return null;
        }
    }

    // ---------------------------------------------------------------
    // COLOUR SWAP (texture-swap workflow)
    // ---------------------------------------------------------------

    // baseColor textures live on spawned models, so they must NOT go through the
    // shared LRU cache (which can Destroy() them). We hold them here instead, keyed
    // by URL, so repeat swaps are instant. Call ClearBaseColorCache when leaving a
    // product to free them.
    private readonly Dictionary<string, Texture2D> _baseColorCache = new();

    private async Task<Texture2D> GetBaseColorTexture(string url)
    {
        if (string.IsNullOrEmpty(url)) return null;
        if (_baseColorCache.TryGetValue(url, out var cached) && cached != null)
            return cached;
        var tex = await ImageCache.LoadTextureUncached(url);
        if (tex != null) _baseColorCache[url] = tex;
        return tex;
    }

    // Recolour an already-loaded model by swapping the baseColor (fabric) texture
    // on its material. The shared mesh.glb already embeds the PRIMARY colour, so
    // this is only needed to switch to a different colour.
    public async Task ApplyBaseColor(GameObject model, string baseColorUrl)
    {
        if (model == null || string.IsNullOrEmpty(baseColorUrl))
            return;

        var tex = await GetBaseColorTexture(baseColorUrl);
        if (tex == null)
        {
            Debug.LogWarning($"[FurnitureModelLoader] baseColor load failed: {baseColorUrl}");
            return;
        }

        int applied = 0;
        foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
        {
            foreach (var mat in renderer.materials)
            {
                if (mat == null) continue;
                if (mat.HasProperty("_BaseMap"))
                {
                    mat.SetTexture("_BaseMap", tex);
                    if (mat.HasProperty("_BaseColor"))
                        mat.SetColor("_BaseColor", Color.white);
                    applied++;
                }
                else if (mat.HasProperty("baseColorTexture"))
                {
                    mat.SetTexture("baseColorTexture", tex);
                    applied++;
                }
            }
        }
        Debug.Log($"[FurnitureModelLoader] Recoloured {applied} material(s) on {model.name}.");
    }

    // Pre-download the other colours' textures when a product opens so a later
    // swap is instant. Fire-and-forget.
    public async Task PrefetchBaseColor(string baseColorUrl)
    {
        await GetBaseColorTexture(baseColorUrl);
    }

    // Free the held baseColor textures (call when leaving a product / room).
    public void ClearBaseColorCache()
    {
        foreach (var tex in _baseColorCache.Values)
            if (tex != null) Destroy(tex);
        _baseColorCache.Clear();
    }

    // Clears the entire local model cache
    public void ClearCache()
    {
        try
        {
            if (Directory.Exists(CachePath))
            {
                Directory.Delete(CachePath, true);
                Directory.CreateDirectory(CachePath);
                Debug.Log("[FurnitureModelLoader] Cache cleared.");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[FurnitureModelLoader] ClearCache error: {e.Message}");
        }
    }

    // Deletes a single model from cache
    public void DeleteFromCache(string fileName)
    {
        string localPath = Path.Combine(CachePath, fileName);

        if (File.Exists(localPath))
        {
            File.Delete(localPath);
            Debug.Log($"[FurnitureModelLoader] Deleted {fileName} from cache.");
        }
    }

    // Checks if a model is already cached
    public bool IsCached(string fileName)
    {
        return File.Exists(Path.Combine(CachePath, fileName));
    }
}
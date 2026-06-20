using System;
using System.IO;
using System.Threading.Tasks;
using UnityGLTF; // Swapped from GLTFast
using UnityEngine;
using UnityEngine.Networking;

public class FurnitureModelLoader : MonoBehaviour
{
    public static FurnitureModelLoader Instance { get; private set; }

    // Local cache folder inside the app's persistent data path
    private string CachePath => Path.Combine(Application.persistentDataPath, "FurnitureCache");

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        // Create cache folder if it does not exist
        if (!Directory.Exists(CachePath))
            Directory.CreateDirectory(CachePath);
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

            using var request = UnityWebRequest.Get(url);
            request.downloadHandler = new DownloadHandlerBuffer();

            var operation = request.SendWebRequest();
            while (!operation.isDone)
                await Task.Yield();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[FurnitureModelLoader] CloudFront error: {request.error}");
                return false;
            }

            // Create the local subdirectory if it does not exist
            string localDir = Path.GetDirectoryName(localPath);
            if (!string.IsNullOrEmpty(localDir) && !Directory.Exists(localDir))
            {
                Directory.CreateDirectory(localDir);
                Debug.Log($"[FurnitureModelLoader] Created cache directory: {localDir}");
            }

            await File.WriteAllBytesAsync(localPath, request.downloadHandler.data);
            Debug.Log($"[FurnitureModelLoader] Downloaded and cached: {fileName}");
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"[FurnitureModelLoader] CloudFront download error: {e.Message}");
            return false;
        }
    }
    
    // Fix URP materials after GLB import
    private void FixMaterials(GameObject model)
    {
        var renderers = model.GetComponentsInChildren<Renderer>();
        foreach (var renderer in renderers)
        {
            foreach (var mat in renderer.materials)
            {
                // Replace Standard with URP Lit
                if (mat.shader.name.Contains("Standard") || 
                    mat.shader.name.Contains("GLTF"))
                {
                    mat.shader = Shader.Find("Universal Render Pipeline/Lit");
                }
            }
        }
    }
    
    // Loads a GLB file from local path using UnityGLTF
    private async Task<GameObject> LoadFromFile(string localPath, string fileName)
    {
        try
        {
            var options  = new ImportOptions();
            var importer = new GLTFSceneImporter(localPath, options);

            await importer.LoadSceneAsync();

            if (importer.CreatedObject == null)
            {
                Debug.LogError($"[FurnitureModelLoader] Failed to instantiate {fileName}.");
                return null;
            }

            importer.CreatedObject.name = fileName;

            // Fix materials for URP — Standard shader appears white in URP
            FixUrpMaterials(importer.CreatedObject);

            Debug.Log($"[FurnitureModelLoader] Loaded: {fileName}");
            return importer.CreatedObject;
        }
        catch (Exception e)
        {
            Debug.LogError($"[FurnitureModelLoader] LoadFromFile error: {e.Message}");
            return null;
        }
    }

    private void FixUrpMaterials(GameObject model)
    {
        if (model == null) return;

        var urpLit = Shader.Find("Universal Render Pipeline/Lit");
        if (urpLit == null)
        {
            Debug.LogWarning("[FurnitureModelLoader] URP Lit shader not found.");
            return;
        }

        var renderers  = model.GetComponentsInChildren<Renderer>(true);
        int fixedCount = 0;

        foreach (var renderer in renderers)
        {
            var materials = renderer.materials;

            foreach (var mat in materials)
            {
                if (mat == null) continue;
                if (mat.shader.name.Contains("Universal Render Pipeline")) continue;

                // Grab the single base texture and color before shader swap
                var mainTex   = mat.mainTexture;
                var mainColor = mat.color;

                // Swap to URP Lit
                mat.shader = urpLit;

                // Restore base texture and color using URP property names
                mat.SetColor("_BaseColor", mainColor);

                if (mainTex != null)
                    mat.SetTexture("_BaseMap", mainTex);

                // Reasonable defaults for furniture
                mat.SetFloat("_Metallic",   0f);
                mat.SetFloat("_Smoothness", 0.3f);

                fixedCount++;
            }

            renderer.materials = materials;
        }

        Debug.Log($"[FurnitureModelLoader] Fixed {fixedCount} materials on {model.name}");
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
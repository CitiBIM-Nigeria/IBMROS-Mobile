using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using GLTFast; // GLBs are now WebP-free, so glTFast loads them with correct
               // sRGB/Linear colour spaces + URP materials (no manual fixups).
using UnityEngine;
using UnityEngine.Networking;
using Debug = UnityEngine.Debug;

public class FurnitureModelLoader : MonoBehaviour
{
    public static FurnitureModelLoader Instance { get; private set; }

    // Local cache folder inside the app's persistent data path
    private string CachePath => Path.Combine(Application.persistentDataPath, "FurnitureCache");

    // Bump this string whenever the pipeline re-uploads models at the SAME S3
    // paths (e.g. after a re-scrape). On startup, if the cached version differs,
    // the whole model cache is wiped so old bytes can't be served. This is what
    // stops a stale, texture-stripped model.glb from loading after a re-scrape.
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
        var total = Stopwatch.StartNew();
        try
        {
            string localPath = Path.Combine(CachePath, fileName);
            bool cacheHit = File.Exists(localPath);
            string shortName = Path.GetFileName(fileName);

            long downloadMs = 0;
            long fileSizeBytes = 0;
            double speedMBps = 0;
            long firstByteMs = 0;

            if (cacheHit)
            {
                fileSizeBytes = new FileInfo(localPath).Length;
            }
            else
            {
                // Not cached, download from CloudFront
                var dlResult = await DownloadFromCloudFront(fileName, localPath);

                if (!dlResult.Success)
                {
                    total.Stop();
                    Debug.LogError($"[ModelLoad] {shortName} | DOWNLOAD FAILED | " +
                                   $"{total.ElapsedMilliseconds}ms | key: {fileName}");
                    return null;
                }

                downloadMs    = dlResult.ElapsedMs;
                fileSizeBytes = dlResult.FileSizeBytes;
                speedMBps     = dlResult.SpeedMBps;
                firstByteMs   = dlResult.FirstByteMs;
            }

            // Load from file (disk read + parse + instantiate)
            var loadResult = await LoadFromFile(localPath, fileName);

            total.Stop();

            if (loadResult.Model == null)
            {
                Debug.LogError($"[ModelLoad] {shortName} | PARSE FAILED | {total.ElapsedMilliseconds}ms");
                return null;
            }

            // One summary log line for the entire pipeline
            string sizeStr = FormatSize(fileSizeBytes);
            if (cacheHit)
            {
                Debug.Log($"[ModelLoad] {shortName}" +
                          $" | CACHE HIT | file {sizeStr}" +
                          $" | disk-read {loadResult.DiskReadMs}ms" +
                          $" | parse {loadResult.ParseMs}ms" +
                          $" | instantiate {loadResult.InstantiateMs}ms" +
                          $" | TOTAL {total.ElapsedMilliseconds}ms");
            }
            else
            {
                Debug.Log($"[ModelLoad] {shortName}" +
                          $" | CACHE MISS" +
                          $" | download {downloadMs}ms ({sizeStr}, {speedMBps:F1}MB/s, first-byte {firstByteMs}ms)" +
                          $" | disk-read {loadResult.DiskReadMs}ms" +
                          $" | parse {loadResult.ParseMs}ms" +
                          $" | instantiate {loadResult.InstantiateMs}ms" +
                          $" | TOTAL {total.ElapsedMilliseconds}ms");
            }

            return loadResult.Model;
        }
        catch (Exception e)
        {
            total.Stop();
            Debug.LogError($"[ModelLoad] LoadModel error ({total.ElapsedMilliseconds}ms): {e.Message}");
            return null;
        }
    }

    private static string FormatSize(long bytes)
    {
        if (bytes >= 1_048_576) return $"{bytes / 1_048_576.0:F1}MB";
        if (bytes >= 1024)      return $"{bytes / 1024.0:F0}KB";
        return $"{bytes}B";
    }

    // Result struct for download metrics
    private struct DownloadResult
    {
        public bool   Success;
        public long   ElapsedMs;
        public long   FileSizeBytes;
        public double SpeedMBps;
        public long   FirstByteMs;
    }

    // Downloads a file from CloudFront and saves it to the local cache
    private async Task<DownloadResult> DownloadFromCloudFront(string fileName, string localPath)
    {
        var result = new DownloadResult();
        try
        {
            // EscapeUriString encodes spaces but leaves slashes intact
            // EscapeDataString would encode slashes as %2F which breaks CloudFront paths
            string encodedFileName = Uri.EscapeUriString(fileName);
            string url = $"{AwsConfig.CloudFrontDomain}/{encodedFileName}";

            // Create the local subdirectory if it does not exist
            string localDir = Path.GetDirectoryName(localPath);
            if (!string.IsNullOrEmpty(localDir) && !Directory.Exists(localDir))
                Directory.CreateDirectory(localDir);

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

            var sw = Stopwatch.StartNew();
            bool firstByteRecorded = false;

            var operation = request.SendWebRequest();
            while (!operation.isDone)
            {
                if (!firstByteRecorded && request.downloadProgress > 0)
                {
                    result.FirstByteMs = sw.ElapsedMilliseconds;
                    firstByteRecorded = true;
                }
                await Task.Yield();
            }
            sw.Stop();

            if (!firstByteRecorded)
                result.FirstByteMs = sw.ElapsedMilliseconds;

            result.ElapsedMs = sw.ElapsedMilliseconds;

            if (request.result != UnityWebRequest.Result.Success)
            {
                // 403 from CloudFront almost always means the S3 object does
                // not exist (stale/old-layout key) — log WHICH key so it's
                // diagnosable instead of a bare status code.
                Debug.LogError($"[ModelLoad] CloudFront error | {request.responseCode} | " +
                               $"{request.error} | {url}");
                try { if (File.Exists(tmpPath)) File.Delete(tmpPath); } catch { }
                return result;   // Success remains false
            }

            if (File.Exists(localPath)) File.Delete(localPath);
            File.Move(tmpPath, localPath);

            result.Success       = true;
            result.FileSizeBytes = (long)request.downloadedBytes;
            result.SpeedMBps     = sw.ElapsedMilliseconds > 0
                ? (result.FileSizeBytes / 1_048_576.0) / (sw.ElapsedMilliseconds / 1000.0)
                : 0;

            return result;
        }
        catch (Exception e)
        {
            Debug.LogError($"[ModelLoad] CloudFront download error: {e.Message}");
            return result;   // Success remains false
        }
    }

    // Result struct for load-from-file metrics
    private struct LoadFromFileResult
    {
        public GameObject Model;
        public long       DiskReadMs;
        public long       ParseMs;
        public long       InstantiateMs;
    }
    
    // Loads a GLB file from a local path using glTFast
    private async Task<LoadFromFileResult> LoadFromFile(string localPath, string fileName)
    {
        var result = new LoadFromFileResult();
        try
        {
            // glTFast loads the self-contained GLB (PNG/JPEG textures, no
            // EXT_texture_webp) and builds URP materials with correct colour
            // spaces — no shader-swap or texture re-application needed.
            var sw = Stopwatch.StartNew();
            byte[] data = await File.ReadAllBytesAsync(localPath);
            result.DiskReadMs = sw.ElapsedMilliseconds;

            long preParse = sw.ElapsedMilliseconds;

            GltfImport gltf;
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                // In Edit Mode, we cannot use the default GameObjectDeferAgent because it 
                // calls DontDestroyOnLoad, which is strictly forbidden outside Play Mode.
                gltf = new GltfImport(null, new UninterruptedDeferAgent());
            }
            else
#endif
            {
                gltf = new GltfImport();
            }

            bool loaded = await gltf.LoadGltfBinary(data);
            result.ParseMs = sw.ElapsedMilliseconds - preParse;

            if (!loaded)
            {
                Debug.LogError($"[ModelLoad] glTFast parse failed: {fileName}");
                return result;
            }

            long preInst = sw.ElapsedMilliseconds;
            var root = new GameObject(fileName);
            bool ok  = await gltf.InstantiateMainSceneAsync(root.transform);
            result.InstantiateMs = sw.ElapsedMilliseconds - preInst;

            if (!ok)
            {
                Debug.LogError($"[ModelLoad] glTFast instantiate failed: {fileName}");
                Destroy(root);
                return result;
            }

            result.Model = root;
            return result;
        }
        catch (Exception e)
        {
            Debug.LogError($"[ModelLoad] LoadFromFile error: {e.Message}");
            return result;
        }
    }

    // COLOUR SWAP: removed 2026-07-29. This was a texture-swap workflow that fetched a
    // per-variant `base_color.webp` and re-assigned _BaseMap on a loaded model. Nothing
    // called any of it — ApplyBaseColor, PrefetchBaseColor, ClearBaseColorCache,
    // GetBaseColorTexture and _baseColorCache all had zero references — because colour
    // variants moved to one self-contained GLB PER VARIANT
    // (models/variants/{variant}/model.glb, selected via ProductVariant.ModelUrl), so
    // there is nothing left to re-texture. The pipeline stopped writing base_color at
    // the same time; its header comment claiming otherwise was simply stale.
    //
    // Do not reintroduce this to "support WebP model textures": model textures are
    // embedded in the GLB as PNG/JPEG and glTFast handles them. The WebP decoder exists
    // for catalogue DISPLAY images only.

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
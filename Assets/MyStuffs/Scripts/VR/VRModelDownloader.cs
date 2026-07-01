using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GLTFast;                 // com.unity.cloud.gltfast (already in this project)
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// GENERIC runtime GLB downloader + loader for the VR (UGUI) team.
///
/// This is the VR-side equivalent of the mobile <see cref="FurnitureModelLoader"/>,
/// but deliberately self-contained and UI-agnostic so the VR team can drop it into
/// any scene and call it from any UGUI button / interactable without depending on
/// the mobile UI stack.
///
/// WHAT IT DOES
///   • Downloads any .glb from any HTTPS URL (or an S3/CloudFront key) ...
///   • ... STREAMING straight to disk (low memory — important on standalone VR
///     headsets like Quest) with real progress callbacks for a UGUI loading bar.
///   • Caches every model on disk so the second request is instant + offline.
///   • Instantiates it with glTFast (correct URP materials / colour spaces).
///   • Runs a "pink-material safety net" so a stripped shader degrades to a
///     visible URP/Lit material instead of magenta. (See PINK NOTE below.)
///
/// HOW THE VR TEAM USES IT  (typical UGUI flow)
/// <code>
///   // 1. Put one VRModelDownloader in the scene (e.g. on a "Managers" object).
///   //    Set BaseUrl in the Inspector to your CloudFront domain, or leave blank
///   //    and pass full URLs.
///
///   // 2. From a button / grab interaction:
///   public VRModelDownloader downloader;
///   public Transform spawnAnchor;
///
///   async void OnPlaceSofaClicked()
///   {
///       var go = await downloader.LoadModel(
///           "models/ikea/sofa_ektorp/mesh.glb",          // key OR full https url
///           progress: p => loadingBar.fillAmount = p);    // 0..1 for UGUI bar
///       if (go != null)
///       {
///           go.transform.SetPositionAndRotation(
///               spawnAnchor.position, spawnAnchor.rotation);
///       }
///   }
/// </code>
///
/// To download ANY other catalogue, the VR team only changes the URL/key they
/// pass in — nothing else. To point at a different bucket/CDN, change BaseUrl.
///
/// ──────────────────────────────────────────────────────────────────────────
/// PINK NOTE (read this — it is the #1 gotcha):
/// glTFast builds materials at RUNTIME from ShaderGraph shaders
/// (glTF-pbrMetallicRoughness / glTF-unlit / glTF-pbrSpecularGlossiness).
/// Unity STRIPS shaders that no built scene references, so on-device those
/// runtime materials have no shader => magenta/pink. The real fix is a PROJECT
/// setting (Graphics ▸ Always Included Shaders) — see the analysis notes.
/// The EnsureNoMagenta() pass below is only a *safety net*, not a substitute.
/// ──────────────────────────────────────────────────────────────────────────
/// </summary>
public class VRModelDownloader : MonoBehaviour
{
    [Header("Source")]
    [Tooltip("CloudFront / S3 base URL. Leave blank to pass full https URLs to " +
             "LoadModel(). With a value set you can pass just the object key.")]
    public string BaseUrl = "https://d3lz5hvxtvmgbq.cloudfront.net";

    [Header("Cache")]
    [Tooltip("Sub-folder of Application.persistentDataPath used for the disk cache.")]
    public string CacheFolderName = "VRModelCache";

    [Tooltip("Bump this when the SAME urls get re-uploaded with new bytes so old " +
             "cached files are wiped on next launch.")]
    public string CacheVersion = "v1";

    [Header("Network")]
    [Tooltip("Per-request timeout in seconds (0 = no timeout).")]
    public int TimeoutSeconds = 60;

    [Tooltip("Max models downloaded at the same time. Keep small on mobile/VR.")]
    public int MaxConcurrentDownloads = 3;

    [Header("Safety")]
    [Tooltip("After load, replace any missing/error (magenta) shader with a " +
             "fallback URP shader so the model is at least visible.")]
    public bool ReplaceMagentaWithFallback = true;

    // ---------------------------------------------------------------

    private string CacheRoot => Path.Combine(Application.persistentDataPath, CacheFolderName);

    // De-duplicate concurrent requests for the SAME file (two UGUI buttons, etc.)
    private readonly Dictionary<string, Task<GameObject>> _inFlight = new();

    // Cheap concurrency limiter so 20 grabs don't open 20 sockets at once.
    private SemaphoreSlim _gate;

    void Awake()
    {
        _gate = new SemaphoreSlim(Mathf.Max(1, MaxConcurrentDownloads));
        Directory.CreateDirectory(CacheRoot);
        BustCacheIfVersionChanged();
    }

    // ====================================================================
    // PUBLIC API
    // ====================================================================

    /// <summary>
    /// Download (if needed), cache, and instantiate a GLB. Returns the spawned
    /// root GameObject, or null on failure. Safe to call from UGUI handlers.
    /// </summary>
    /// <param name="urlOrKey">Full "https://..." URL, or an object key appended to BaseUrl.</param>
    /// <param name="progress">0..1 download progress (great for a UGUI bar). Null is fine.</param>
    /// <param name="ct">Optional cancellation (e.g. user closed the menu).</param>
    public async Task<GameObject> LoadModel(
        string urlOrKey,
        Action<float> progress = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(urlOrKey))
        {
            Debug.LogError("[VRModelDownloader] urlOrKey is empty.");
            return null;
        }

        string url       = ResolveUrl(urlOrKey);
        string localPath  = CachePathFor(url);

        // Ensure the bytes are on disk (download once, dedup concurrent callers).
        bool ready = await EnsureDownloaded(url, localPath, progress, ct);
        if (!ready) return null;

        // Instantiate fresh every call so two placements are independent objects.
        return await Instantiate(localPath, Path.GetFileName(localPath));
    }

    /// <summary>
    /// Download + cache WITHOUT instantiating — call while the user is browsing so
    /// the actual placement later is instant. Fire-and-forget friendly.
    /// </summary>
    public async Task Prefetch(string urlOrKey, CancellationToken ct = default)
    {
        string url = ResolveUrl(urlOrKey);
        await EnsureDownloaded(url, CachePathFor(url), null, ct);
    }

    public bool IsCached(string urlOrKey) => File.Exists(CachePathFor(ResolveUrl(urlOrKey)));

    public void ClearCache()
    {
        try
        {
            if (Directory.Exists(CacheRoot)) Directory.Delete(CacheRoot, true);
            Directory.CreateDirectory(CacheRoot);
        }
        catch (Exception e) { Debug.LogError($"[VRModelDownloader] ClearCache: {e.Message}"); }
    }

    // ====================================================================
    // DOWNLOAD (streamed to disk)
    // ====================================================================

    private Task<bool> EnsureDownloaded(
        string url, string localPath, Action<float> progress, CancellationToken ct)
    {
        if (File.Exists(localPath))
        {
            progress?.Invoke(1f);
            return Task.FromResult(true);
        }
        return DownloadToDisk(url, localPath, progress, ct);
    }

    private async Task<bool> DownloadToDisk(
        string url, string localPath, Action<float> progress, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(localPath));

            // Download to a temp file then atomically move, so a crash/cancel
            // mid-download can never leave a half-written "valid looking" cache file.
            string tmp = localPath + ".part";

            using var req = UnityWebRequest.Get(url);
            if (TimeoutSeconds > 0) req.timeout = TimeoutSeconds;

            // DownloadHandlerFile streams bytes to disk instead of buffering the
            // whole GLB in RAM — far better for big meshes on memory-limited VR HMDs.
            req.downloadHandler = new DownloadHandlerFile(tmp) { removeFileOnAbort = true };

            var op = req.SendWebRequest();
            while (!op.isDone)
            {
                if (ct.IsCancellationRequested) { req.Abort(); return false; }
                progress?.Invoke(req.downloadProgress);
                await Task.Yield();
            }

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[VRModelDownloader] Download failed ({req.responseCode}): {req.error}\n{url}");
                SafeDelete(tmp);
                return false;
            }

            if (File.Exists(localPath)) File.Delete(localPath);
            File.Move(tmp, localPath);
            progress?.Invoke(1f);
            Debug.Log($"[VRModelDownloader] Cached: {localPath}");
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"[VRModelDownloader] Download error: {e.Message}\n{url}");
            return false;
        }
        finally
        {
            _gate.Release();
        }
    }

    // ====================================================================
    // LOAD (glTFast)
    // ====================================================================

    private Task<GameObject> Instantiate(string localPath, string name)
    {
        // Dedup: if the same file is already being instantiated, reuse that task.
        if (_inFlight.TryGetValue(localPath, out var pending)) return pending;

        var task = InstantiateInternal(localPath, name);
        _inFlight[localPath] = task;
        _ = task.ContinueWith(_ => _inFlight.Remove(localPath),
                              TaskScheduler.FromCurrentSynchronizationContext());
        return task;
    }

    private async Task<GameObject> InstantiateInternal(string localPath, string name)
    {
        try
        {
            byte[] data = await File.ReadAllBytesAsync(localPath);

            var gltf = new GltfImport();
            if (!await gltf.LoadGltfBinary(data))
            {
                Debug.LogError($"[VRModelDownloader] glTFast parse failed: {name}");
                return null;
            }

            var root = new GameObject(name);
            if (!await gltf.InstantiateMainSceneAsync(root.transform))
            {
                Debug.LogError($"[VRModelDownloader] glTFast instantiate failed: {name}");
                Destroy(root);
                return null;
            }

            if (ReplaceMagentaWithFallback) EnsureNoMagenta(root);
            return root;
        }
        catch (Exception e)
        {
            Debug.LogError($"[VRModelDownloader] Instantiate error: {e.Message}");
            return null;
        }
    }

    // ====================================================================
    // PINK SAFETY NET
    // Detects materials whose shader was stripped from the build (Unity assigns
    // the magenta "Hidden/InternalErrorShader") and reassigns a real URP shader so
    // the mesh is at least visible. NOTE: the proper fix is Always-Included-Shaders.
    // ====================================================================

    private Shader _fallbackShader;

    private void EnsureNoMagenta(GameObject root)
    {
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                var m = mats[i];
                if (m == null || m.shader == null ||
                    m.shader.name == "Hidden/InternalErrorShader" ||
                    !m.shader.isSupported)
                {
                    _fallbackShader ??= Shader.Find("Universal Render Pipeline/Lit");
                    if (_fallbackShader != null)
                    {
                        Debug.LogWarning(
                            $"[VRModelDownloader] Stripped shader on '{r.name}' — " +
                            "falling back to URP/Lit. Add glTFast shaders to " +
                            "Graphics ▸ Always Included Shaders to fix properly.");
                        if (m == null) m = new Material(_fallbackShader);
                        else m.shader = _fallbackShader;
                        mats[i] = m;
                    }
                }
            }
            r.sharedMaterials = mats;
        }
    }

    // ====================================================================
    // HELPERS
    // ====================================================================

    private string ResolveUrl(string urlOrKey)
    {
        if (urlOrKey.StartsWith("http://") || urlOrKey.StartsWith("https://"))
            return urlOrKey;
        string baseUrl = (BaseUrl ?? "").TrimEnd('/');
        // Uri.EscapeDataString would break path slashes; only spaces etc. need escaping.
        string key = Uri.EscapeUriString(urlOrKey.TrimStart('/'));
        return $"{baseUrl}/{key}";
    }

    // Map a url to a flat, safe cache filename (keeps the .glb extension).
    private string CachePathFor(string url)
    {
        string ext  = Path.GetExtension(url);
        if (string.IsNullOrEmpty(ext)) ext = ".glb";
        string hash = Hash(url);
        return Path.Combine(CacheRoot, hash + ext);
    }

    private static string Hash(string s)
    {
        // Stable, collision-resistant enough for cache keys; avoids unsafe path chars.
        unchecked
        {
            using var md5 = System.Security.Cryptography.MD5.Create();
            var bytes = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(s));
            return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
        }
    }

    private void BustCacheIfVersionChanged()
    {
        try
        {
            string marker = Path.Combine(CacheRoot, "cache_version.txt");
            string current = File.Exists(marker) ? File.ReadAllText(marker) : "";
            if (current != CacheVersion)
            {
                ClearCache();
                File.WriteAllText(Path.Combine(CacheRoot, "cache_version.txt"), CacheVersion);
                Debug.Log($"[VRModelDownloader] Cache reset to version {CacheVersion}.");
            }
        }
        catch (Exception e) { Debug.LogWarning($"[VRModelDownloader] cache version: {e.Message}"); }
    }

    private static void SafeDelete(string p)
    {
        try { if (File.Exists(p)) File.Delete(p); } catch { /* ignore */ }
    }
}

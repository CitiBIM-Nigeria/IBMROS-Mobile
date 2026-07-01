using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
//using WebP;

public static class ImageCache
{
    private const int MAX_TEXTURES = 50;

    private static readonly Dictionary<string, Texture2D> _cache       = new();
    private static readonly LinkedList<string>            _accessOrder = new();
    private static readonly HashSet<string>               _inProgress  = new();

    public static async Task<Texture2D> GetTexture(string url)
    {
        if (string.IsNullOrEmpty(url)) return null;

        // Return cached texture immediately
        if (_cache.TryGetValue(url, out var cached))
        {
            _accessOrder.Remove(url);
            _accessOrder.AddFirst(url);
            return cached;
        }

        // Disk cache — instant, no network. (Saved on first download.)
        var fromDisk = LoadFromDisk(url);
        if (fromDisk != null)
        {
            AddToMemory(url, fromDisk);
            return fromDisk;
        }

        // Wait if same URL is already downloading
        if (_inProgress.Contains(url))
        {
            float waited = 0f;
            while (_inProgress.Contains(url) && waited < 10f)
            {
                await Task.Delay(100);
                waited += 0.1f;
            }
            return _cache.TryGetValue(url, out var late) ? late : null;
        }

        _inProgress.Add(url);

        try
        {
            var texture = await DownloadTexture(url);

            // If WebP failed fall back to PNG
            if (texture == null && url.EndsWith(".webp"))
            {
                string pngUrl = url.Replace(".webp", ".png");
                Debug.Log($"[ImageCache] WebP failed, trying PNG: {pngUrl}");
                texture = await DownloadTexture(pngUrl);
            }

            if (texture != null)
                AddToMemory(url, texture);

            return texture;
        }
        finally
        {
            _inProgress.Remove(url);
        }
    }

    // Add a texture to the in-memory LRU cache, evicting the oldest if at limit.
    private static void AddToMemory(string url, Texture2D texture)
    {
        if (_cache.Count >= MAX_TEXTURES && _accessOrder.Last != null)
        {
            string oldest = _accessOrder.Last.Value;
            _accessOrder.RemoveLast();
            if (_cache.TryGetValue(oldest, out var evicted))
            {
                UnityEngine.Object.Destroy(evicted);
                _cache.Remove(oldest);
            }
        }
        _cache[url] = texture;
        _accessOrder.AddFirst(url);
    }

    /// <summary>
    /// Download + decode a texture WITHOUT putting it in the shared LRU cache.
    /// Use for long-lived textures (e.g. textures applied to spawned 3D models)
    /// that must not be Destroy()'d by cache eviction. Handles webp/png/jpg, with
    /// the same .webp→.png fallback. Caller owns the returned Texture2D.
    /// </summary>
    public static async Task<Texture2D> LoadTextureUncached(string url)
    {
        if (string.IsNullOrEmpty(url)) return null;
        var fromDisk = LoadFromDisk(url);
        if (fromDisk != null) return fromDisk;
        var texture = await DownloadTexture(url);
        if (texture == null && url.EndsWith(".webp"))
            texture = await DownloadTexture(url.Replace(".webp", ".png"));
        return texture;
    }

    private static async Task<Texture2D> DownloadTexture(string url)
    {
        try
        {
            // Download raw bytes — works for any format
            using var request = UnityWebRequest.Get(url);
            request.SetRequestHeader("User-Agent",
                "Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) " +
                "AppleWebKit/605.1.15 (KHTML, like Gecko) " +
                "Version/17.0 Mobile/15E148 Safari/604.1");

            var op = request.SendWebRequest();
            while (!op.isDone)
                await Task.Yield();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[ImageCache] Download failed: {request.error} | {url}");
                return null;
            }

            byte[] data = request.downloadHandler.data;

            if (data == null || data.Length == 0)
            {
                Debug.LogWarning($"[ImageCache] Empty response: {url}");
                return null;
            }

            SaveToDisk(url, data);          // persist for instant load next launch
            return DecodeBytes(data, url);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[ImageCache] Exception: {e.Message} | {url}");
            return null;
        }
    }

    // ── Decode raw image bytes (webp / png / jpg) into a Texture2D ──────────────
    private static Texture2D DecodeBytes(byte[] data, string url)
    {
        if (data == null || data.Length == 0) return null;

        if (url.EndsWith(".webp"))
            return DecodeWebP(data, url);

        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (tex.LoadImage(data))          // handles PNG and JPG
            return tex;
        UnityEngine.Object.Destroy(tex);
        Debug.LogWarning($"[ImageCache] decode failed: {url}");
        return null;
    }

    // ── Disk cache: images persist across launches (no re-download) ─────────────
    private static string DiskDir =>
        System.IO.Path.Combine(Application.persistentDataPath, "ImageCache");

    private static string DiskPath(string url)
    {
        string ext = url.EndsWith(".webp") ? ".webp"
                   : url.EndsWith(".png")  ? ".png" : ".jpg";
        uint h = 2166136261u;                              // FNV-1a hash of the URL
        foreach (char c in url) { h ^= c; h *= 16777619u; }
        return System.IO.Path.Combine(DiskDir, h.ToString("x8") + ext);
    }

    private static void SaveToDisk(string url, byte[] data)
    {
        try
        {
            System.IO.Directory.CreateDirectory(DiskDir);
            System.IO.File.WriteAllBytes(DiskPath(url), data);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[ImageCache] disk write failed: {e.Message}");
        }
    }

    private static Texture2D LoadFromDisk(string url)
    {
        try
        {
            string p = DiskPath(url);
            if (!System.IO.File.Exists(p)) return null;
            return DecodeBytes(System.IO.File.ReadAllBytes(p), url);
        }
        catch
        {
            return null;
        }
    }

    private static Texture2D DecodeWebP(byte[] data, string url)
    {
        /*try
        {
            Error error = Error.Success;
            Texture2D tex = Texture2DExt.CreateTexture2DFromWebP(
                data,
                lMipmaps: false,
                lLinear: false,
                lError: out error
            );

            if (error != Error.Success)
            {
                Debug.LogWarning($"[ImageCache] WebP decode error: {error} | {url}");
                if (tex != null) UnityEngine.Object.Destroy(tex);
                return null;
            }

            if (tex == null)
            {
                Debug.LogWarning($"[ImageCache] WebP returned null texture: {url}");
                return null;
            }

            Debug.Log($"[ImageCache] WebP decoded: {tex.width}x{tex.height} | {url}");
            return tex;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[ImageCache] WebP exception: {e.Message} | {url}");
            return null;
        }*/
        return null;
    }

    public static void Clear()
    {
        foreach (var tex in _cache.Values)
            if (tex != null)
                UnityEngine.Object.Destroy(tex);
        _cache.Clear();
        _accessOrder.Clear();
    }
}
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Scan History — an AUTOMATIC, local-first record of every successful QR scan.
/// Deliberately separate from SavedItemsService: history is what the user DID
/// (auto-recorded), Saved Items is what the user CHOSE to keep (intentional).
///
/// Every successful scan (in-app scanner or external deep link) appends an entry:
/// canonical id, product id, name, thumbnail, timestamp, and whether the product
/// was actually added to a room. Newest first, capped so the file can't grow
/// unbounded. Local JSON in persistentDataPath — works for guests, offline, no
/// backend. The entry fields are chosen so a future account/cloud sync can
/// upload them as-is without changing the UX.
/// </summary>
public class ScanHistoryService : MonoBehaviour
{
    public static ScanHistoryService Instance { get; private set; }

    /// <summary>Raised whenever the history changes.</summary>
    public static event Action OnChanged;

    // Oldest entries fall off past this — plenty for review, bounded on disk.
    private const int MaxEntries = 200;

    [Serializable]
    public class ScanEntry
    {
        public string canonicalId;
        public string productId;
        public string name;
        public string thumbUrl;
        public string scannedAtIso;
        public bool   addedToRoom;
    }

    [Serializable]
    private class ScanList { public List<ScanEntry> items = new List<ScanEntry>(); }

    private ScanList _data = new ScanList();
    private string FilePath => Path.Combine(Application.persistentDataPath, "scan_history.json");

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        Load();
    }

    /// <summary>All scans, newest first.</summary>
    public IReadOnlyList<ScanEntry> All() => _data.items;

    public int Count => _data.items.Count;

    /// <summary>Record a successful scan. Called by the scanner / deep-link flow
    /// after the product resolved (and after the add-to-room attempt, so the
    /// outcome flag is accurate).</summary>
    public void Record(ProductModel p, bool addedToRoom)
    {
        if (p == null || string.IsNullOrEmpty(p.ProductId)) return;
        _data.items.Insert(0, new ScanEntry
        {
            canonicalId  = p.CanonicalProductId,
            productId    = p.ProductId,
            name         = p.Name,
            thumbUrl     = p.BestThumbnailUrl,
            scannedAtIso = DateTime.UtcNow.ToString("o"),
            addedToRoom  = addedToRoom,
        });
        if (_data.items.Count > MaxEntries)
            _data.items.RemoveRange(MaxEntries, _data.items.Count - MaxEntries);
        Save();
    }

    public void Clear()
    {
        if (_data.items.Count == 0) return;
        _data.items.Clear();
        Save();
    }

    // ---------------------------------------------------------------
    // PERSISTENCE
    // ---------------------------------------------------------------

    private void Load()
    {
        try
        {
            if (File.Exists(FilePath))
                _data = JsonUtility.FromJson<ScanList>(File.ReadAllText(FilePath)) ?? new ScanList();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[ScanHistory] load failed: {e.Message}");
            _data = new ScanList();
        }
    }

    private void Save()
    {
        try
        {
            File.WriteAllText(FilePath, JsonUtility.ToJson(_data));
            OnChanged?.Invoke();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[ScanHistory] save failed: {e.Message}");
        }
    }
}

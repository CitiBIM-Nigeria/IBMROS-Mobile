using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// "Save for Later" — a lightweight, LOCAL-FIRST collection of products the user
/// wants to revisit. Deliberately device-local (JSON in persistentDataPath) so
/// it works for GUEST users, offline, with no new backend. This is the natural
/// primary action for an in-store QR scan (the user is in the shop, not their
/// room, so they can't place yet).
///
/// A future cross-device sync (backend table keyed on the Cognito user) can layer
/// on top without changing this API — the stored fields (product/canonical id +
/// timestamp) migrate cleanly. See docs/QR_IMPLEMENTATION_NOTES.md.
/// </summary>
public class SavedItemsService : MonoBehaviour
{
    public static SavedItemsService Instance { get; private set; }

    /// <summary>Raised whenever the saved set changes (add/remove).</summary>
    public static event Action OnChanged;

    [Serializable]
    public class SavedItem
    {
        public string productId;
        public string canonicalId;
        public string name;
        public string thumbUrl;
        public string priceText;
        public string savedAtIso;
    }

    [Serializable]
    private class SavedList { public List<SavedItem> items = new List<SavedItem>(); }

    private SavedList _data = new SavedList();
    private string FilePath => Path.Combine(Application.persistentDataPath, "saved_items.json");

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        Load();
    }

    public IReadOnlyList<SavedItem> All() => _data.items;

    public int Count => _data.items.Count;

    public bool IsSaved(string productId) =>
        !string.IsNullOrEmpty(productId) && _data.items.Exists(i => i.productId == productId);

    /// <summary>Add if absent, remove if present. Returns the NEW saved state.</summary>
    public bool Toggle(ProductModel p)
    {
        if (p == null || string.IsNullOrEmpty(p.ProductId)) return false;
        if (IsSaved(p.ProductId)) { Remove(p.ProductId); return false; }
        Add(p);
        return true;
    }

    public void Add(ProductModel p)
    {
        if (p == null || string.IsNullOrEmpty(p.ProductId) || IsSaved(p.ProductId)) return;
        _data.items.Add(new SavedItem
        {
            productId   = p.ProductId,
            canonicalId = p.CanonicalProductId,
            name        = p.Name,
            thumbUrl    = p.BestThumbnailUrl,
            priceText   = p.FormattedPrice,
            savedAtIso  = DateTime.UtcNow.ToString("o"),
        });
        Save();
    }

    public void Remove(string productId)
    {
        if (_data.items.RemoveAll(i => i.productId == productId) > 0)
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
                _data = JsonUtility.FromJson<SavedList>(File.ReadAllText(FilePath)) ?? new SavedList();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[SavedItems] load failed: {e.Message}");
            _data = new SavedList();
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
            Debug.LogWarning($"[SavedItems] save failed: {e.Message}");
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using Exoa.Designer;
using Exoa.Events;
using UnityEngine;

namespace IBMROS.Designer.Furnish
{
    /// <summary>
    /// Persists furniture placements alongside the floor plan.
    ///
    /// Format: a sidecar file "FloorMaps/{plan}.furniture.json" (kept out of the
    /// FloorMapV2 schema for now — the settled end-state folds furniture into
    /// the document additively; the sidecar keys by the same plan name so that
    /// migration is a straight import).
    ///
    /// Save: every plan save (GameEditorEvents.OnFileSaved) snapshots all placed
    /// FurnitureItems (model key + pose). Load: after a plan load, existing
    /// placed items are cleared and the sidecar respawned through
    /// FurnitureSpawnManager.SpawnSavedItem.
    /// </summary>
    public sealed class FurniturePersistence : MonoBehaviour
    {
        [Serializable]
        private class Entry
        {
            public string modelKey;
            public Vector3 position;
            public Vector3 eulerAngles;
            public Vector3 localScale;
        }

        [Serializable]
        private class SidecarFile
        {
            public int version = 1;
            public List<Entry> items = new List<Entry>();
        }

        private void OnEnable()
        {
            GameEditorEvents.OnFileSaved += HandleSaved;
            GameEditorEvents.OnFileLoaded += HandleLoaded;
        }

        private void OnDisable()
        {
            GameEditorEvents.OnFileSaved -= HandleSaved;
            GameEditorEvents.OnFileLoaded -= HandleLoaded;
        }

        private static string SidecarPath(string planName) =>
            Path.Combine(Application.persistentDataPath, HDSettings.EXT_FLOORMAP_FOLDER,
                planName + ".furniture.json");

        // ------------------------------------------------------------------ save

        private void HandleSaved(string name, GameEditorEvents.FileType fileType)
        {
            if (fileType != GameEditorEvents.FileType.FloorMapFile || string.IsNullOrEmpty(name))
                return;
            // SaveSystem's save callback reports the file name WITH extension
            // (its load API takes it without — known API asymmetry). Normalize.
            if (name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                name = name.Substring(0, name.Length - 5);
            try
            {
                var file = new SidecarFile();
                foreach (FurnitureItem item in FindObjectsByType<FurnitureItem>(FindObjectsSortMode.None))
                {
                    if (!item.IsPlaced)
                        continue;
                    file.items.Add(new Entry
                    {
                        modelKey = item.FurnitureId,
                        position = item.transform.position,
                        eulerAngles = item.transform.eulerAngles,
                        localScale = item.transform.localScale,
                    });
                }
                Directory.CreateDirectory(Path.GetDirectoryName(SidecarPath(name)));
                File.WriteAllText(SidecarPath(name), JsonUtility.ToJson(file, true));
                Debug.Log($"[FurniturePersistence] Saved {file.items.Count} item(s) for '{name}'.");
            }
            catch (Exception e)
            {
                Debug.LogError($"[FurniturePersistence] Save failed for '{name}': {e.Message}");
            }
        }

        // ------------------------------------------------------------------ load

        private void HandleLoaded(GameEditorEvents.FileType fileType)
        {
            if (fileType != GameEditorEvents.FileType.FloorMapFile)
                return;
            string name = UISaving.instance != null ? UISaving.instance.CurrentFileName : null;
            if (string.IsNullOrEmpty(name))
                return;
            Restore(name);
        }

        private async void Restore(string planName)
        {
            string path = SidecarPath(planName);

            // A load replaces the whole world — clear items from the previous plan.
            foreach (FurnitureItem item in FindObjectsByType<FurnitureItem>(FindObjectsSortMode.None))
                Destroy(item.gameObject);

            if (!File.Exists(path))
                return;

            SidecarFile file;
            try
            {
                file = JsonUtility.FromJson<SidecarFile>(File.ReadAllText(path));
            }
            catch (Exception e)
            {
                Debug.LogError($"[FurniturePersistence] Sidecar unreadable for '{planName}': {e.Message}");
                return;
            }
            if (file == null || file.items == null || file.items.Count == 0)
                return;

            FurnitureSpawnManager spawner = FindAnyObjectByType<FurnitureSpawnManager>(FindObjectsInactive.Include);
            if (spawner == null)
            {
                Debug.LogWarning("[FurniturePersistence] No FurnitureSpawnManager — cannot restore items.");
                return;
            }

            int restored = 0;
            foreach (Entry e in file.items)
            {
                if (this == null) // scene tore down mid-restore
                    return;
                GameObject go = await spawner.SpawnSavedItem(
                    e.modelKey, e.position, Quaternion.Euler(e.eulerAngles), e.localScale);
                if (go != null)
                    restored++;
            }
            Debug.Log($"[FurniturePersistence] Restored {restored}/{file.items.Count} item(s) for '{planName}'.");
        }
    }
}

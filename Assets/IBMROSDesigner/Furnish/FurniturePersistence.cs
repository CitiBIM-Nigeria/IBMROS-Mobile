using System.Collections.Generic;
using System.IO;
using Exoa.Designer;
using UnityEngine;
using static Exoa.Designer.DataModel;

namespace IBMROS.Designer.Furnish
{
    /// <summary>
    /// Makes furniture part of the room document.
    ///
    /// Furniture used to live in a side file ({plan}.furniture.json) written on
    /// save and read on load. That kept plan and furnishing as two artefacts that
    /// could drift, and left furniture outside the document that everything else
    /// (save, load, undo, future sync) treats as the source of truth. It is now
    /// gathered into FloorMapLevel.furniture by the serializer and recreated from
    /// there on load, through FurnitureDocumentBridge.
    ///
    /// Legacy sidecars are imported once per plan and then removed, so existing
    /// saved rooms keep their furniture.
    /// </summary>
    public sealed class FurniturePersistence : MonoBehaviour
    {
        private void OnEnable()
        {
            FurnitureDocumentBridge.Provider = Gather;
            FurnitureDocumentBridge.Restorer = Restore;
        }

        private void OnDisable()
        {
            if (FurnitureDocumentBridge.Provider == Gather)
                FurnitureDocumentBridge.Provider = null;
            if (FurnitureDocumentBridge.Restorer == Restore)
                FurnitureDocumentBridge.Restorer = null;
        }

        // ------------------------------------------------------------------ save

        /// <summary>Snapshot of every placed item, in document form.</summary>
        private List<FurnitureRecord> Gather()
        {
            var list = new List<FurnitureRecord>();
            foreach (FurnitureItem item in FindObjectsByType<FurnitureItem>(FindObjectsSortMode.None))
            {
                // Ghosts mid-placement are not part of the room yet, and a deleted
                // item is kept inactive for undo — neither should be saved.
                if (!item.IsPlaced || !item.gameObject.activeSelf)
                    continue;
                Transform t = item.transform;
                list.Add(new FurnitureRecord
                {
                    uniqueId = item.GetInstanceID().ToString(),
                    modelKey = item.FurnitureId,
                    displayName = item.FurnitureName,
                    position = t.position,
                    eulerAngles = t.eulerAngles,
                    scale = t.localScale,
                });
            }
            return list;
        }

        // ------------------------------------------------------------------ load

        private void Restore(List<FurnitureRecord> records)
        {
            // A document restore during undo replays the load path. That is a plan
            // rebuild, not a new document: re-spawning here would duplicate live
            // furniture and strand the undo history on dead GameObjects.
            if (IBMROS.Bridge.UndoRedo.UndoRedoService.RestoreInProgress)
                return;
            StartCoroutine(RestoreRoutine(records));
        }

        private System.Collections.IEnumerator RestoreRoutine(List<FurnitureRecord> records)
        {
            // Clear what the previous plan left behind.
            foreach (FurnitureItem item in FindObjectsByType<FurnitureItem>(FindObjectsSortMode.None))
                Destroy(item.gameObject);
            yield return null; // let the destroys land before respawning

            List<FurnitureRecord> toSpawn = records;
            if (toSpawn == null || toSpawn.Count == 0)
                toSpawn = ImportLegacySidecar();
            if (toSpawn == null || toSpawn.Count == 0)
                yield break;

            FurnitureSpawnManager spawner =
                FindAnyObjectByType<FurnitureSpawnManager>(FindObjectsInactive.Include);
            if (spawner == null)
            {
                Debug.LogWarning("[FurniturePersistence] No FurnitureSpawnManager — cannot restore.");
                yield break;
            }

            int restored = 0;
            foreach (FurnitureRecord r in toSpawn)
            {
                if (this == null)
                    yield break;
                var task = spawner.SpawnSavedItem(r.modelKey, r.position,
                    Quaternion.Euler(r.eulerAngles),
                    r.scale == Vector3.zero ? Vector3.one : r.scale);
                while (!task.IsCompleted)
                    yield return null;
                if (task.Result != null)
                    restored++;
            }
            Debug.Log($"[FurniturePersistence] Restored {restored}/{toSpawn.Count} item(s) from the document.");
        }

        // ------------------------------------------------------------------ migration

        /// <summary>
        /// Reads a pre-document sidecar once, then retires it. Keeps rooms saved
        /// before furniture moved into the document.
        /// </summary>
        private static List<FurnitureRecord> ImportLegacySidecar()
        {
            string plan = UISaving.instance != null ? UISaving.instance.CurrentFileName : null;
            if (string.IsNullOrEmpty(plan))
                return null;
            string path = Path.Combine(Application.persistentDataPath,
                HDSettings.EXT_FLOORMAP_FOLDER, plan + ".furniture.json");
            if (!File.Exists(path))
                return null;

            try
            {
                var file = JsonUtility.FromJson<LegacySidecar>(File.ReadAllText(path));
                var list = new List<FurnitureRecord>();
                if (file?.items != null)
                {
                    foreach (LegacyEntry e in file.items)
                    {
                        list.Add(new FurnitureRecord
                        {
                            modelKey = e.modelKey,
                            position = e.position,
                            eulerAngles = e.eulerAngles,
                            scale = e.localScale == Vector3.zero ? Vector3.one : e.localScale,
                        });
                    }
                }
                File.Move(path, path + ".migrated");
                Debug.Log($"[FurniturePersistence] Imported {list.Count} item(s) from the legacy sidecar for '{plan}'.");
                return list;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[FurniturePersistence] Legacy sidecar import failed for '{plan}': {e.Message}");
                return null;
            }
        }

        [System.Serializable] private class LegacySidecar { public int version; public List<LegacyEntry> items; }
        [System.Serializable] private class LegacyEntry
        {
            public string modelKey;
            public Vector3 position;
            public Vector3 eulerAngles;
            public Vector3 localScale;
        }
    }
}

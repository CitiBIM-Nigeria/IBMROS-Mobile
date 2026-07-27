using System;
using System.IO;
using Exoa.Designer;
using Exoa.Events;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace IBMROS.Bridge.Stability
{
    /// <summary>
    /// IBMROS P1 Task 3 — Autosave (extension-only, no Exoa code touched).
    ///
    /// Self-bootstraps in any scene containing a FloorMapSerializer (i.e. the floor map
    /// editor), dirty-tracks the plan via the global rebuild events, and every 60 s writes
    /// a snapshot to the separate "FloorMapsAutosave" folder so the file-list UI stays
    /// unpolluted. When a file is opened and a newer autosave exists, offers recovery.
    /// </summary>
    public class AutosaveService : MonoBehaviour
    {
        public const string AUTOSAVE_FOLDER = "FloorMapsAutosave";
        public const float AUTOSAVE_INTERVAL = 60f;
        private const string UNTITLED_NAME = "Untitled";

        private static AutosaveService instance;

        private FloorMapSerializer serializer;
        private bool dirty;
        private float lastAutosaveTime;
        private float suppressUntil;
        private bool restoring;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            TryInstall();
            SceneManager.sceneLoaded += (scene, mode) => TryInstall();
        }

        private static void TryInstall()
        {
            if (instance != null)
                return;
            FloorMapSerializer ser = GameObject.FindAnyObjectByType<FloorMapSerializer>();
            if (ser == null)
                return;
            AppController app = GameObject.FindAnyObjectByType<AppController>();
            if (app == null || app.currentState == AppController.States.PlayMode)
                return;
            GameObject go = new GameObject("IBMROS_AutosaveService");
            go.AddComponent<AutosaveService>();
        }

        private void Awake()
        {
            instance = this;
            serializer = GameObject.FindAnyObjectByType<FloorMapSerializer>();
            // A load or scene setup is likely in progress right now; don't count its
            // rebuild storm as user edits.
            suppressUntil = Time.time + 2f;
        }

        private void OnEnable()
        {
            // Primary mutation signal (A1) — covers paths rebuild events miss (renames).
            IBMROS.Core.DocumentEvents.OnDocumentChanged += OnDocumentChanged;
            GameEditorEvents.OnRequestRebuildBuilding += MarkDirty;
            GameEditorEvents.OnRequestRebuildAllRooms += MarkDirty;
            GameEditorEvents.OnRequestRebuildAllOpenings += MarkDirty;
            GameEditorEvents.OnRequestFloorAction += OnFloorAction;
            GameEditorEvents.OnRequestClearAll += OnClearAll;
            GameEditorEvents.OnFileLoaded += OnFileLoaded;
            GameEditorEvents.OnFileSaved += OnFileSaved;
        }

        private void OnDisable()
        {
            IBMROS.Core.DocumentEvents.OnDocumentChanged -= OnDocumentChanged;
            GameEditorEvents.OnRequestRebuildBuilding -= MarkDirty;
            GameEditorEvents.OnRequestRebuildAllRooms -= MarkDirty;
            GameEditorEvents.OnRequestRebuildAllOpenings -= MarkDirty;
            GameEditorEvents.OnRequestFloorAction -= OnFloorAction;
            GameEditorEvents.OnRequestClearAll -= OnClearAll;
            GameEditorEvents.OnFileLoaded -= OnFileLoaded;
            GameEditorEvents.OnFileSaved -= OnFileSaved;
            if (instance == this)
                instance = null;
        }

        private void OnDocumentChanged(IBMROS.Core.DocumentChange change)
        {
            MarkDirty();
        }

        private void Update()
        {
            if (!dirty || serializer == null)
                return;
            if (Time.time < suppressUntil)
                return;
            if (Time.time - lastAutosaveTime < AUTOSAVE_INTERVAL)
                return;
            AutosaveNow();
        }

        private void MarkDirty()
        {
            if (restoring || Time.time < suppressUntil)
                return;
            if (!dirty)
            {
                dirty = true;
                // Start the interval from the first change, not from scene start,
                // so a burst of edits right after load still gets ~60s of coalescing.
                if (lastAutosaveTime <= 0f)
                    lastAutosaveTime = Time.time;
            }
        }

        private void OnFloorAction(GameEditorEvents.FloorAction action, string floorId)
        {
            if (action != GameEditorEvents.FloorAction.Select &&
                action != GameEditorEvents.FloorAction.PreviewBuilding)
                MarkDirty();
        }

        private void OnClearAll(bool clearFloorsUI, bool clearFloorMapUI, bool clearScene)
        {
            // A project load or "new project" follows; its rebuilds are not user edits.
            suppressUntil = Time.time + 1.5f;
            if (clearFloorsUI)
                dirty = false;
        }

        private void OnFileSaved(string fileName, GameEditorEvents.FileType fileType)
        {
            if (fileType != GameEditorEvents.FileType.FloorMapFile)
                return;
            // The real file is now newer than any autosave; nothing left to protect.
            dirty = false;
        }

        private void OnFileLoaded(GameEditorEvents.FileType fileType)
        {
            if (restoring || fileType != GameEditorEvents.FileType.FloorMapFile)
                return;
            suppressUntil = Time.time + 1.5f;
            dirty = false;
            OfferAutosaveRecovery();
        }

        private void AutosaveNow()
        {
            string json = null;
            try
            {
                if (serializer.IsSceneEmpty())
                    return;
                json = serializer.SerializeScene();
            }
            catch (Exception e)
            {
                HDLogger.LogError("[IBMROS Autosave] Serialize failed: " + e.Message, HDLogger.LogCategory.FileSystem);
                return;
            }
            string name = CurrentFileName();
            SaveSystem.Create(SaveSystem.Mode.FILE_SYSTEM).SaveFileItem(name + ".json", AUTOSAVE_FOLDER, json);
            dirty = false;
            lastAutosaveTime = Time.time;
            HDLogger.Log("[IBMROS Autosave] Saved " + AUTOSAVE_FOLDER + "/" + name + ".json", HDLogger.LogCategory.FileSystem);
        }

        private string CurrentFileName()
        {
            string name = UISaving.instance != null ? UISaving.instance.CurrentFileName : null;
            return string.IsNullOrEmpty(name) ? UNTITLED_NAME : name;
        }

        /// <summary>
        /// If an autosave newer than the file just opened exists, offer to restore it.
        /// The restore goes through the exact load code path (clear + deserialize), and the
        /// autosave is only promoted to the real file when the user saves manually.
        /// </summary>
        private void OfferAutosaveRecovery()
        {
            string name = CurrentFileName();
            if (name == UNTITLED_NAME)
                return;

            SaveSystem ss = SaveSystem.Create(SaveSystem.Mode.FILE_SYSTEM);
            string mainPath = ss.GetBasePath(HDSettings.EXT_FLOORMAP_FOLDER) + name + ".json";
            string autoPath = ss.GetBasePath(AUTOSAVE_FOLDER) + name + ".json";
            if (!File.Exists(autoPath) || !File.Exists(mainPath))
                return;

            DateTime autoTime = File.GetLastWriteTimeUtc(autoPath);
            if (autoTime <= File.GetLastWriteTimeUtc(mainPath).AddSeconds(2))
                return;

            AlertPopup popup = AlertPopup.ShowAlert("ibmrosAutosave", "Recover Unsaved Changes?",
                "A newer autosave of '" + name + "' exists (" + autoTime.ToLocalTime() + "). " +
                "Restore it? Your saved file is untouched until you save again.",
                true, "Keep Saved Version");
            if (popup == null)
                return;
            popup.OnClickOKEvent.AddListener(() => RestoreAutosave(autoPath));
        }

        private void RestoreAutosave(string autoPath)
        {
            string json;
            try
            {
                json = File.ReadAllText(autoPath);
            }
            catch (Exception e)
            {
                AlertPopup.ShowAlert("ibmrosAutosaveFail", "Autosave Unreadable",
                    "The autosave could not be read: " + e.Message);
                return;
            }
            restoring = true;
            try
            {
                GameEditorEvents.OnRequestClearAll?.Invoke(clearFloorsUI: true, clearFloorMapUI: true, clearScene: true);
                serializer.DeserializeToScene(json);
                GameEditorEvents.OnFileLoaded?.Invoke(GameEditorEvents.FileType.FloorMapFile);
                // The scene now differs from the file on disk: keep it protected.
                dirty = true;
                lastAutosaveTime = Time.time;
            }
            catch (Exception e)
            {
                AlertPopup.ShowAlert("ibmrosAutosaveFail", "Autosave Corrupt",
                    "The autosave could not be loaded: " + e.Message);
            }
            finally
            {
                restoring = false;
                suppressUntil = Time.time + 1.5f;
            }
        }
    }
}

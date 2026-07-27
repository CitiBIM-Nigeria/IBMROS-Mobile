using System;
using System.Collections.Generic;
using Exoa.Designer;
using Exoa.Events;
using IBMROS.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace IBMROS.Bridge.UndoRedo
{
    /// <summary>
    /// IBMROS P2 — the application's undo/redo foundation.
    ///
    /// Architecture: transaction-scoped state mementos behind a command-gateway API.
    /// Every step is a full FloorMapV2 document state captured through the existing
    /// FloorMapSerializer and stored in SnapshotHistory (byte-budgeted, gzip-compressed).
    /// Restoring replays the exact code path a file load already exercises
    /// (OnRequestClearAll → DeserializeToScene → OnFileLoaded), so undo can never
    /// desynchronise from what load produces — the property golden tests certify.
    ///
    /// Why state mementos and not command inverses: verified in source, Exoa does not
    /// announce every mutation (OutsideController rebuilds fire no global event; renames
    /// fire nothing at all), so a command layer could never be coverage-complete without
    /// invasively rewriting the vendor interaction code. State capture is correct by
    /// construction; the gateway API below still gives future features (furniture, AI
    /// proposals, XR grabs) semantic, atomic, labelled steps:
    ///
    ///   using (UndoRedoService.Instance.BeginAction("Place Sofa")) { ...mutate... }
    ///   UndoRedoService.Instance.NotifyMutation("Rename Room");   // fire-and-forget
    ///
    /// Capture pipeline (A1 observable document): every mutation announces itself on
    /// IBMROS.Core.DocumentEvents — raised at the mutation sources, with semantic labels.
    /// A signal starts a settle window (rebuild throttle drained); capture happens only
    /// with no finger on the screen (drag coalescing) and pushes if the state differs.
    /// Rebuild events are kept as a redundant secondary signal during the transition.
    /// In editor/development builds only, a slow sweep acts as a MUTATION LEAK DETECTOR:
    /// if it finds a change no event announced, that is an A1 invariant violation and is
    /// logged as an error to be fixed at the source — release builds never poll.
    ///
    /// Primary interaction is touch (UndoRedoHud buttons + 3-finger-tap undo); the
    /// keyboard shortcuts are compiled only into the editor for development testing.
    /// </summary>
    public sealed class UndoRedoService : MonoBehaviour
    {
        public const int MAX_STEPS = 50;
        public const long MAX_HISTORY_BYTES = 4L * 1024 * 1024;
        private const float SETTLE_SECONDS = 0.4f;       // > 0.1s rebuild throttle + queued pass
        private const float LEAK_SWEEP_SECONDS = 5f;     // dev-only silent-mutation detector
        private const float SUPPRESS_SECONDS = 1f;       // ignore rebuild storm after load/restore
        private const string DEFAULT_LABEL = "Edit";

        public static UndoRedoService Instance { get; private set; }

        /// <summary>Raised whenever the history or cursor changes; drives UI button state.</summary>
        public static event Action OnHistoryChanged;

        private readonly SnapshotHistory history = new SnapshotHistory(MAX_STEPS, MAX_HISTORY_BYTES);
        private FloorMapSerializer serializer;
        private bool restoring;
        private bool resyncing;
        private float suppressUntil;
        private bool changeSignalSeen;
        private float lastChangeSignal;
        private float lastGestureTime;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private float lastLeakSweep;
        private bool signalSinceLastSweep;
#endif
        private string pendingLabel;
        private int actionScopeDepth;
        private string actionScopeLabel;

        public bool CanUndo => history.CanUndo;
        public bool CanRedo => history.CanRedo;

        /// <summary>Label of the action Undo would revert (for toasts/tooltips), or null.</summary>
        public string UndoLabel => history.UndoLabel;
        public string RedoLabel => history.RedoLabel;

        // ------------------------------------------------------------------ bootstrap

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            TryInstall();
            SceneManager.sceneLoaded += (scene, mode) => TryInstall();
        }

        private static void TryInstall()
        {
            if (Instance != null)
                return;
            if (GameObject.FindAnyObjectByType<FloorMapSerializer>() == null)
                return;
            AppController app = GameObject.FindAnyObjectByType<AppController>();
            if (app == null || app.currentState == AppController.States.PlayMode)
                return;
            new GameObject("IBMROS_UndoRedoService").AddComponent<UndoRedoService>();
        }

        private void Awake()
        {
            Instance = this;
            serializer = GameObject.FindAnyObjectByType<FloorMapSerializer>();
            suppressUntil = Time.time + SUPPRESS_SECONDS;
        }

        private void OnEnable()
        {
            // Primary mutation signal (A1): every edit announces itself here, labelled.
            DocumentEvents.OnDocumentChanged += OnDocumentChanged;
            // Secondary signals kept through the A-track transition; they also cover
            // paths not yet routed through DocumentEvents (e.g. UIBuildingSettings).
            GameEditorEvents.OnRequestRebuildBuilding += OnChangeSignal;
            GameEditorEvents.OnRequestRebuildAllRooms += OnChangeSignal;
            GameEditorEvents.OnRequestRebuildAllOpenings += OnChangeSignal;
            GameEditorEvents.OnRequestClearAll += OnClearAll;
            GameEditorEvents.OnFileLoaded += OnFileLoaded;
        }

        private void OnDisable()
        {
            DocumentEvents.OnDocumentChanged -= OnDocumentChanged;
            GameEditorEvents.OnRequestRebuildBuilding -= OnChangeSignal;
            GameEditorEvents.OnRequestRebuildAllRooms -= OnChangeSignal;
            GameEditorEvents.OnRequestRebuildAllOpenings -= OnChangeSignal;
            GameEditorEvents.OnRequestClearAll -= OnClearAll;
            GameEditorEvents.OnFileLoaded -= OnFileLoaded;
            if (Instance == this)
                Instance = null;
        }

        // ------------------------------------------------------------------ public API

        public void Undo()
        {
            if (restoring)
                return;
            // Commit an in-flight edit first (so Redo can return to it) — but ONLY when
            // a change signal actually arrived. An unconditional serialize-compare here
            // pushed phantom steps from round-trip drift on rapid consecutive undos,
            // truncating redo (part of the "undo only works once" report).
            if (changeSignalSeen)
            {
                changeSignalSeen = false;
                CaptureIfChanged(ConsumePendingLabel());
            }
            string state = history.Undo();
            if (state != null)
                Restore(state);
        }

        public void Redo()
        {
            if (restoring)
                return;
            string state = history.Redo();
            if (state != null)
                Restore(state);
        }

        /// <summary>
        /// Opens a labelled, atomic undo step: pending edits are committed first, automatic
        /// capture pauses, and everything mutated inside the scope becomes one step when it
        /// is disposed. Nested scopes fold into the outermost one. Safe to use from UI, AI,
        /// and XR code alike — this is the mutation gateway future features route through.
        /// </summary>
        public ActionScope BeginAction(string label)
        {
            if (actionScopeDepth == 0)
            {
                changeSignalSeen = false;
                CaptureIfChanged(ConsumePendingLabel());
                actionScopeLabel = label;
            }
            actionScopeDepth++;
            return new ActionScope(this);
        }

        /// <summary>
        /// Tells the service the document may have changed (with an optional semantic
        /// label). For future features that don't need a full BeginAction scope. Routed
        /// through DocumentEvents so every consumer (autosave, telemetry) hears it too.
        /// </summary>
        public void NotifyMutation(string label = null)
        {
            if (restoring)
                return;
            DocumentEvents.RaiseChanged(DocumentChangeKind.External, label);
        }

        public readonly struct ActionScope : IDisposable
        {
            private readonly UndoRedoService owner;
            internal ActionScope(UndoRedoService owner) { this.owner = owner; }
            public void Dispose() => owner.EndAction();
        }

        private void EndAction()
        {
            if (actionScopeDepth <= 0)
                return;
            actionScopeDepth--;
            if (actionScopeDepth > 0)
                return;
            string label = actionScopeLabel ?? DEFAULT_LABEL;
            actionScopeLabel = null;
            changeSignalSeen = false;
            CaptureIfChanged(label);
        }

        // ------------------------------------------------------------------ capture

        private void Update()
        {
#if UNITY_EDITOR
            HandleKeyboardShortcuts();
#endif
            HandleUndoGesture();

            if (restoring || resyncing || serializer == null || actionScopeDepth > 0 || Time.time < suppressUntil)
                return;
            // Coalesce gestures: a drag becomes exactly one step, captured on release.
            if (Input.GetMouseButton(0) || Input.touchCount > 0)
                return;

            // Baseline for scenes that start without a file load (blank editor / New).
            if (history.Count == 0)
            {
                CaptureIfChanged("Baseline");
                return;
            }

            if (changeSignalSeen && Time.time - lastChangeSignal > SETTLE_SECONDS)
            {
                changeSignalSeen = false;
                CaptureIfChanged(ConsumePendingLabel());
                return;
            }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // Mutation leak detector (dev only — release builds never poll): a change
            // found here means some edit path violated the A1 observable-document
            // invariant. Capture it anyway (never lose user work), and log the debt.
            if (Time.time - lastLeakSweep > LEAK_SWEEP_SECONDS)
            {
                lastLeakSweep = Time.time;
                bool announced = signalSinceLastSweep;
                signalSinceLastSweep = false;
                string state = SerializeSafe();
                if (state != null && history.Push(state, DEFAULT_LABEL))
                {
                    OnHistoryChanged?.Invoke();
                    if (!announced)
                        HDLogger.LogError(
                            "[IBMROS Undo] MUTATION LEAK: the document changed but no " +
                            "DocumentEvents/rebuild signal announced it. Find the edit path " +
                            "and raise DocumentEvents.RaiseChanged at its source (A1 invariant).",
                            HDLogger.LogCategory.General);
                }
            }
#endif
        }

        private string ConsumePendingLabel()
        {
            string label = pendingLabel ?? DEFAULT_LABEL;
            pendingLabel = null;
            return label;
        }

        private void CaptureIfChanged(string label)
        {
            string state = SerializeSafe();
            if (state != null && history.Push(state, label))
                OnHistoryChanged?.Invoke();
        }

        private string SerializeSafe()
        {
            try
            {
                return serializer.SerializeScene();
            }
            catch (Exception e)
            {
                HDLogger.LogError("[IBMROS Undo] Serialize failed: " + e.Message, HDLogger.LogCategory.General);
                return null;
            }
        }

        // ------------------------------------------------------------------ restore

        private void Restore(string state)
        {
            restoring = true;
            try
            {
                // A5 v2: identity-keyed diff-and-reconcile — only the items that differ
                // between the live scene and the target are touched (scalars in place,
                // geometry via control-point moves, add/remove per item). Multi-floor,
                // settings changes, identity-less items or any divergence fall back to
                // the always-correct full clear+deserialize.
                if (!RestoreReconciler.TryReconcile(state, SerializeSafe()))
                {
                    AppController.SuppressNextFocusOnLoad = true; // full path fires OnFileLoaded
                    FullRestore(state);
                }
            }
            catch (Exception e)
            {
                HDLogger.LogError("[IBMROS Undo] Reconcile restore failed, doing full restore: " + e.Message,
                    HDLogger.LogCategory.General);
                try
                {
                    AppController.SuppressNextFocusOnLoad = true;
                    FullRestore(state);
                }
                catch (Exception e2)
                {
                    HDLogger.LogError("[IBMROS Undo] Restore failed: " + e2.Message, HDLogger.LogCategory.General);
                }
            }
            finally
            {
                restoring = false;
                suppressUntil = Time.time + SUPPRESS_SECONDS;
            }
            StartCoroutine(ResyncBaselineAfterRestore());
            OnHistoryChanged?.Invoke();
        }

        /// <summary>
        /// THE undo-lifecycle bug fix: serialize(deserialize(S)) is not byte-identical
        /// to S (openings' directions are only recomputed by re-snap after load; floats
        /// drift), so after a restore the scene re-serializes differently than the
        /// snapshot we restored. Without this resync the next capture saw a phantom
        /// "change", pushed a bogus step and TRUNCATED REDO — the reported "undo only
        /// works once / redo dies after a few seconds". Once rebuilds settle we adopt
        /// the scene's own serialization as the canonical current entry.
        /// </summary>
        private System.Collections.IEnumerator ResyncBaselineAfterRestore()
        {
            int cursorAtSchedule = history.Cursor;
            int countAtSchedule = history.Count;
            // Block the capture pipeline from firing a phantom step while the scene is
            // still settling (rebuild throttle + opening re-snap can outlast the normal
            // suppress window).
            resyncing = true;
            try
            {
                yield return new WaitForSeconds(SUPPRESS_SECONDS);
                // Poll until serialization stops changing (two equal consecutive reads),
                // then adopt it as the canonical current entry. This makes the history
                // self-stabilizing regardless of how long re-snap/rebuild takes.
                string prev = null;
                for (int i = 0; i < 10; i++)
                {
                    if (restoring || actionScopeDepth > 0)
                        yield break;
                    if (history.Cursor != cursorAtSchedule || history.Count != countAtSchedule)
                        yield break; // user moved on; don't rewrite history
                    string cur = SerializeSafe();
                    if (cur != null && cur == prev)
                    {
                        history.ReplaceCurrent(cur);
                        changeSignalSeen = false;
                        yield break;
                    }
                    prev = cur;
                    yield return new WaitForSeconds(0.25f);
                }
                // Didn't fully converge in budget: adopt the last read anyway so no
                // phantom step can be pushed later.
                if (!restoring && actionScopeDepth == 0 &&
                    history.Cursor == cursorAtSchedule && history.Count == countAtSchedule && prev != null)
                {
                    history.ReplaceCurrent(prev);
                    changeSignalSeen = false;
                }
            }
            finally
            {
                resyncing = false;
                suppressUntil = Time.time + 0.2f; // small tail so the very next frame isn't a capture
            }
        }

        private void FullRestore(string state)
        {
            // Exactly the UISaving.LoadInternal code path, minus disk IO.
            GameEditorEvents.OnRequestClearAll?.Invoke(clearFloorsUI: true, clearFloorMapUI: true, clearScene: true);
            serializer.DeserializeToScene(state);
            GameEditorEvents.OnFileLoaded?.Invoke(GameEditorEvents.FileType.FloorMapFile);
        }

        // A5 v1's inline scoped-restore (scalars only) was superseded by the
        // identity-keyed diff-and-reconcile in RestoreReconciler (A5 v2), which also
        // handles geometry deltas and per-item add/remove.

        /// <summary>Diagnostics for tests/tools: number of steps currently in history.</summary>
        public int StepCount => history.Count;

        /// <summary>Diagnostics for tests/tools: current cursor index within history.</summary>
        public int CursorIndex => history.Cursor;

        // ------------------------------------------------------------------ editor events

        private void OnDocumentChanged(DocumentChange change)
        {
            if (restoring)
                return;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            signalSinceLastSweep = true;
#endif
            if (actionScopeDepth > 0 || Time.time < suppressUntil)
                return;
            // First label between captures names the step; repeats of a drag agree anyway.
            if (pendingLabel == null && !string.IsNullOrEmpty(change.Label))
                pendingLabel = change.Label;
            changeSignalSeen = true;
            lastChangeSignal = Time.time;
        }

        private void OnChangeSignal()
        {
            if (restoring)
                return;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            signalSinceLastSweep = true;
#endif
            if (actionScopeDepth > 0 || Time.time < suppressUntil)
                return;
            changeSignalSeen = true;
            lastChangeSignal = Time.time;
        }

        private void OnClearAll(bool clearFloorsUI, bool clearFloorMapUI, bool clearScene)
        {
            if (restoring)
                return;
            // A file load or a new project follows; undo never crosses documents.
            if (clearFloorsUI)
                ResetHistory();
            suppressUntil = Time.time + SUPPRESS_SECONDS;
        }

        private void OnFileLoaded(GameEditorEvents.FileType fileType)
        {
            if (restoring || fileType != GameEditorEvents.FileType.FloorMapFile)
                return;
            ResetHistory();
            suppressUntil = Time.time + SUPPRESS_SECONDS;
            // Baseline immediately: the document data is complete as soon as the load
            // deserialized, even while meshes are still building behind the throttle.
            CaptureIfChanged("Open");
        }

        private void ResetHistory()
        {
            history.Clear();
            changeSignalSeen = false;
            pendingLabel = null;
            OnHistoryChanged?.Invoke();
        }

        // ------------------------------------------------------------------ input

        private void HandleUndoGesture()
        {
            // 3-finger tap = undo (touch idiom, debounced to one step per tap).
            if (Input.touchCount == 3 && Input.GetTouch(2).phase == TouchPhase.Began &&
                Time.time - lastGestureTime > 0.5f)
            {
                lastGestureTime = Time.time;
                Undo();
            }
        }

#if UNITY_EDITOR
        // Development conveniences only — compiled out of device builds (mobile-first:
        // the UndoRedoHud touch buttons are the product interaction).
        private void HandleKeyboardShortcuts()
        {
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl) ||
                        Input.GetKey(KeyCode.LeftCommand) || Input.GetKey(KeyCode.RightCommand);
            if (!ctrl)
                return;
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (Input.GetKeyDown(KeyCode.Z))
            {
                if (shift) Redo(); else Undo();
            }
            else if (Input.GetKeyDown(KeyCode.Y))
            {
                Redo();
            }
        }
#endif
    }
}

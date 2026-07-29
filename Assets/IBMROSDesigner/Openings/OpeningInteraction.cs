using System;
using Exoa.Designer;
using IBMROS.Core;
using IBMROS.Designer.Plan;
using UnityEngine;

namespace IBMROS.Designer.Openings
{
    /// <summary>
    /// Makes doors and windows selectable and manipulable, reusing the furniture
    /// selection framework rather than growing a second one.
    ///
    /// Three jobs:
    ///
    /// 1. PICKABILITY. Until now an opening could not be tapped at all: the vendor
    ///    OpeningPrefab shipped with addMeshColliders off and on layer 0, so the door
    ///    visuals had no colliders and the pick ray went straight through the hole they
    ///    sit in. The instances are re-created on every rebuild, so the layer has to be
    ///    re-stamped rather than set once.
    ///
    /// 2. SELECTION BRIDGE. Openings live on the same Interactable layer furniture uses,
    ///    so the existing SelectionManager finds them and the existing contextual
    ///    toolbar frames them, with no parallel UI. What must NOT happen is the furniture
    ///    stack treating a door as a sofa — a door has no free position — so
    ///    ObjectDragHandler refuses opening hits and this class owns their movement.
    ///
    /// 3. CONSTRAINED DRAG. A drag becomes "slide along the host wall": the pointer is
    ///    projected onto the wall segment and clamped to the span that keeps the whole
    ///    opening inside it (OpeningAnchor.MoveTo). The same call is what the 2D plan
    ///    uses, so dragging in either view is the same operation on the same document —
    ///    which is what makes 2D and 3D agree and undo work, rather than two code paths
    ///    that have to be kept in step.
    /// </summary>
    [DefaultExecutionOrder(ORDER)]
    public sealed class OpeningInteraction : MonoBehaviour
    {
        /// <summary>Between the camera gate and the plan editor, ahead of the cameras.</summary>
        public const int ORDER = -350;

        public static OpeningInteraction Instance { get; private set; }

        /// <summary>Raised when the selected opening changes (null = none).</summary>
        public static event Action<string> OnSelectedOpeningChanged;

        /// <summary>Raised while dragging/resizing, with a display string for the HUD.</summary>
        public static event Action<string> OnDimensionsChanged;

        private const float REASSERT_INTERVAL_S = 0.3f;
        private const float DRAG_SLOP_PX = 6f;

        private float nextReassert;
        private int interactableLayer = -1;

        private string selectedId;
        private bool dragging;
        private Vector2 pressScreen;
        private Camera cam;
        private SelectionManager selectionManager;
        private Transform handedVisual;   // what we last gave SelectionManager to frame

        /// <summary>The selected door/window's item id, or null when the selection is not one.</summary>
        public string SelectedId => selectedId;
        public bool HasSelection => !string.IsNullOrEmpty(selectedId);

        /// <summary>True while a drag owns the pointer, so other layers stand down.</summary>
        public bool OwnsInput => dragging;

        /// <summary>
        /// Self-installs into any scene that has a floor plan, the same way the bridge
        /// services do — so this needs no scene authoring and cannot be missed when a
        /// scene is duplicated.
        ///
        /// The AfterSceneLoad hook alone was NOT enough, and it failed silently: at that
        /// moment the serializer was not findable, the guard below returned, and because
        /// RoomDesigner is the scene play mode STARTS in, sceneLoaded never fired again —
        /// so Instance stayed null for the whole session and every opening interaction
        /// was dead. Measured: Instance=false at frame 3821 while a manual Ensure() call
        /// installed it immediately. PlanTouchController now also calls Ensure from its
        /// OnEnable, which is a real scene component and therefore always runs.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            Ensure();
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += (s, m) => Ensure();
        }

        public static void Ensure()
        {
            if (Instance != null)
                return;
            // Include inactive: the vendor hierarchy can have the serializer parked under
            // a disabled parent during boot, which is what made the guard miss it.
            if (UnityEngine.Object.FindAnyObjectByType<FloorMapSerializer>(
                    FindObjectsInactive.Include) == null)
                return;
            Instance = FindAnyObjectByType<OpeningInteraction>(FindObjectsInactive.Include);
            if (Instance == null)
                Instance = new GameObject("IBMROS_OpeningInteraction")
                    .AddComponent<OpeningInteraction>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
            interactableLayer = LayerMask.NameToLayer("Interactable");
        }

        private void OnDestroy() { if (Instance == this) Instance = null; }

        private void OnEnable()
        {
            DocumentEvents.OnDocumentChanged += OnDocChanged;
            nextReassert = 0f;
            // Selection is ONE model shared with furniture: when the user taps empty
            // space and SelectionManager deselects, the opening deselects with it —
            // otherwise the toolbar is gone but Delete/Rotate still have a target.
            if (selectionManager == null)
                selectionManager = FindAnyObjectByType<SelectionManager>(FindObjectsInactive.Include);
            if (selectionManager != null)
                selectionManager.onObjectDeselected += OnManagerDeselected;
        }

        private void OnDisable()
        {
            DocumentEvents.OnDocumentChanged -= OnDocChanged;
            if (selectionManager != null)
                selectionManager.onObjectDeselected -= OnManagerDeselected;
            EndDrag();
        }

        private void OnManagerDeselected() => Select(null);

        private void OnDocChanged(DocumentChange change)
        {
            nextReassert = 0f;
            // The selected opening may have been deleted or undone away.
            if (HasSelection && OpeningAnchor.Find(selectedId) == null)
                Select(null);
        }

        // ------------------------------------------------------------------ pickability

        private void Update()
        {
            if (Time.unscaledTime >= nextReassert)
            {
                nextReassert = Time.unscaledTime + REASSERT_INTERVAL_S;
                StampPickable();
                RefreshHandedVisual();
            }
            UpdateDrag();
        }

        /// <summary>
        /// An undo/reconcile can recreate the selected opening's visual instance while
        /// the selection (an item id) survives. The toolbar and rig hold the OLD
        /// transform, which is now destroyed — re-hand them the live one so the UI
        /// keeps following the item rather than a corpse.
        /// </summary>
        private void RefreshHandedVisual()
        {
            if (!HasSelection || dragging || handedVisual != null)
                return;
            Transform visual = VisualOf(selectedId);
            if (visual == null || selectionManager == null)
                return;
            handedVisual = visual;
            selectionManager.SelectObject(visual);
        }

        /// <summary>
        /// Puts every opening instance on the Interactable layer. Cheap, and it has to be
        /// periodic: OpeningController re-instantiates the visuals whenever the item
        /// changes, and a fresh instance carries the prefab's layer 0.
        /// </summary>
        private void StampPickable()
        {
            if (interactableLayer < 0)
                return;
            foreach (ProceduralOpening po in
                     UnityEngine.Object.FindObjectsByType<ProceduralOpening>(FindObjectsSortMode.None))
            {
                if (po.gameObject.layer != interactableLayer)
                    po.gameObject.layer = interactableLayer;
                foreach (Transform t in po.GetComponentsInChildren<Transform>(true))
                    if (t.gameObject.layer != interactableLayer)
                        t.gameObject.layer = interactableLayer;
            }
        }

        // ------------------------------------------------------------------ selection

        /// <summary>
        /// The opening item a collider belongs to, or null. This is the seam the
        /// furniture stack uses to keep its hands off architectural elements.
        /// </summary>
        public static string OpeningIdOf(Transform hit)
        {
            if (hit == null)
                return null;
            ProceduralOpening po = hit.GetComponentInParent<ProceduralOpening>();
            if (po == null)
                return null;
            OpeningController oc = po.GetComponentInParent<OpeningController>();
            UIBaseItem ui = oc != null ? oc.UI : null;
            return ui != null ? ui.ItemUniqueId : null;
        }

        /// <summary>The transform a toolbar should frame for an opening (its whole visual).</summary>
        public static Transform VisualOf(string itemId)
        {
            UIBaseItem ui = OpeningAnchor.Find(itemId);
            if (ui == null || ui.drawer == null || ui.drawer.GO == null)
                return null;
            ProceduralOpening po = ui.drawer.GO.GetComponentInChildren<ProceduralOpening>(true);
            return po != null ? po.transform : ui.drawer.GO.transform;
        }

        public void Select(string itemId)
        {
            string next = OpeningAnchor.Find(itemId) != null ? itemId : null;
            if (next == selectedId)
                return;
            selectedId = next;
            handedVisual = next != null ? VisualOf(next) : null;
            EndDrag();
            OnSelectedOpeningChanged?.Invoke(selectedId);
            PublishDimensions();
        }

        /// <summary>Selects from a raycast hit; returns true when the hit was an opening.</summary>
        public bool SelectFromHit(Transform hit)
        {
            string id = OpeningIdOf(hit);
            if (string.IsNullOrEmpty(id))
                return false;
            Select(id);
            return true;
        }

        // ------------------------------------------------------------------ drag

        /// <summary>
        /// Begins a slide when the press lands on ANY opening, selecting it on the way
        /// in — the same first-touch direct manipulation furniture has, so a window the
        /// user has not tapped first is still draggable. Returns false when the press is
        /// not on an opening, so the caller falls through to its own handling.
        /// </summary>
        public bool TryBeginDrag(Vector2 screenPos)
        {
            if (cam == null) cam = Camera.main;
            if (cam == null) return false;

            if (!Physics.Raycast(cam.ScreenPointToRay(screenPos), out RaycastHit hit, 5000f,
                                 1 << interactableLayer, QueryTriggerInteraction.Ignore))
                return false;
            string id = OpeningIdOf(hit.transform);
            if (string.IsNullOrEmpty(id))
                return false;

            if (id != selectedId)
            {
                Select(id);
                // Keep the shared selection model in step so the contextual toolbar
                // frames this opening on release, exactly like a furniture grab.
                if (selectionManager != null && handedVisual != null)
                    selectionManager.SelectObject(handedVisual);
            }

            dragging = true;
            pressScreen = screenPos;
            PlanCameraGate.Hold(PlanCameraGate.Reason.Furniture);
            return true;
        }

        public void UpdateDrag(Vector2 screenPos)
        {
            if (!dragging || !HasSelection)
                return;
            if ((screenPos - pressScreen).magnitude < DRAG_SLOP_PX)
                return;
            if (!PlanEditorUtil.ScreenToGround(screenPos, out Vector3 ground))
                return;

            // One call, shared with the 2D plan: project onto the host wall and clamp to
            // the span that keeps the opening inside it.
            OpeningAnchor.MoveTo(selectedId, PlanEditorUtil.WorldToMeters(ground));
            PublishDimensions();
        }

        public void EndDrag()
        {
            if (!dragging)
                return;
            dragging = false;
            PlanCameraGate.Release(PlanCameraGate.Reason.Furniture);
        }

        /// <summary>Drives the drag from the pointer when this class owns it directly.</summary>
        private void UpdateDrag()
        {
            if (!dragging)
                return;
            bool held = Input.touchCount == 1
                ? Input.GetTouch(0).phase == TouchPhase.Moved || Input.GetTouch(0).phase == TouchPhase.Stationary
                : Input.GetMouseButton(0);
            bool up = Input.touchCount == 1
                ? Input.GetTouch(0).phase == TouchPhase.Ended || Input.GetTouch(0).phase == TouchPhase.Canceled
                : Input.GetMouseButtonUp(0);

            if (held)
                UpdateDrag(PointerPos());
            if (up || (Input.touchCount == 0 && !Input.GetMouseButton(0)))
                EndDrag();
        }

        private static Vector2 PointerPos() =>
            Input.touchCount >= 1 ? Input.GetTouch(0).position : (Vector2)Input.mousePosition;

        // ------------------------------------------------------------------ dimensions

        /// <summary>
        /// Publishes the live W x H of the selection, so the HUD readout updates while the
        /// user drags — the same feedback furniture resizing gives.
        /// </summary>
        public void PublishDimensions()
        {
            UIBaseItem ui = OpeningAnchor.Find(selectedId);
            OnDimensionsChanged?.Invoke(ui == null
                ? string.Empty
                : ui.Width.ToString("0.00") + " m  x  " + ui.Height.ToString("0.00") + " m");
        }

        // ------------------------------------------------------------------ toolbar ops

        public bool Resize(float? width, float? height, float? ypos)
        {
            bool ok = OpeningAnchor.Resize(selectedId, width, height, ypos);
            PublishDimensions();
            return ok;
        }

        private IDisposable resizeScope;

        /// <summary>Width/height at the moment a resize gesture started.</summary>
        public Vector2 ResizeStartSize { get; private set; }

        /// <summary>
        /// Opens ONE undo step for a whole resize drag. Each frame's Resize opens its own
        /// scope, and the service folds nested scopes into the outermost — so without this
        /// a single drag would push a step per frame and Undo would crawl back through
        /// dozens of intermediate widths.
        /// </summary>
        public void BeginResize()
        {
            UIBaseItem ui = OpeningAnchor.Find(selectedId);
            if (ui == null)
                return;
            ResizeStartSize = new Vector2(ui.Width, ui.Height);
            var svc = IBMROS.Bridge.UndoRedo.UndoRedoService.Instance;
            resizeScope = svc != null
                ? (IDisposable)svc.BeginAction("Resize " + ui.sequencingItemType) : null;
        }

        public void EndResize()
        {
            resizeScope?.Dispose();
            resizeScope = null;
            PublishDimensions();
        }

        /// <summary>
        /// Applies a resize drag from one rig handle.
        ///
        /// For openings the rig is laid out in the WALL PLANE with a fixed convention
        /// (ScaleRigUI.UpdateOpeningRig): AxisX pills sit on the vertical edges, AxisZ
        /// pills on the horizontal ones. So the handle type maps 1:1 to a dimension on
        /// a wall of any orientation — AxisX = width (along the wall), AxisZ = height,
        /// corners = both — with no world-direction guessing.
        /// </summary>
        public bool ResizeFromHandle(HandleType type, float scaleFactor)
        {
            Vector2 start = ResizeStartSize;
            switch (type)
            {
                case HandleType.Corner:
                    return Resize(start.x * scaleFactor, start.y * scaleFactor, null);
                case HandleType.AxisX:
                    return Resize(start.x * scaleFactor, null, null);
                default: // AxisZ (and the unused AxisY) = the vertical dimension
                    return Resize(null, start.y * scaleFactor, null);
            }
        }

        /// <summary>Largest width/height the selection may take where it sits.</summary>
        public Vector2 ResizeLimits()
        {
            UIBaseItem ui = OpeningAnchor.Find(selectedId);
            return ui == null
                ? new Vector2(OpeningAnchor.MIN_WIDTH, OpeningAnchor.MIN_HEIGHT)
                : new Vector2(OpeningAnchor.MaxWidthOf(ui), OpeningAnchor.MaxHeightOf(ui));
        }

        public bool Flip() => OpeningAnchor.Flip(selectedId);

        public bool Delete()
        {
            string id = selectedId;
            Select(null);
            return OpeningAnchor.Delete(id);
        }

        public string Duplicate()
        {
            string copy = OpeningAnchor.Duplicate(selectedId);
            if (!string.IsNullOrEmpty(copy))
                Select(copy);
            return copy;
        }

        public bool Replace(string modelId) =>
            Materials.OpeningStyler.SetModel(selectedId, modelId);

        public bool SetPartMaterial(Materials.OpeningStyler.Part part, string materialName) =>
            Materials.OpeningStyler.SetPartMaterial(selectedId, part, materialName);
    }
}

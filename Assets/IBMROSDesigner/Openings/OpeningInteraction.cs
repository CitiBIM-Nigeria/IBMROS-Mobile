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

        /// <summary>The selected door/window's item id, or null when the selection is not one.</summary>
        public string SelectedId => selectedId;
        public bool HasSelection => !string.IsNullOrEmpty(selectedId);

        /// <summary>True while a drag owns the pointer, so other layers stand down.</summary>
        public bool OwnsInput => dragging;

        /// <summary>
        /// Self-installs into any scene that has a floor plan, the same way the bridge
        /// services do — so this needs no scene authoring and cannot be missed when a
        /// scene is duplicated.
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
            if (UnityEngine.Object.FindAnyObjectByType<FloorMapSerializer>() == null)
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
        }

        private void OnDisable()
        {
            DocumentEvents.OnDocumentChanged -= OnDocChanged;
            EndDrag();
        }

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
            }
            UpdateDrag();
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
        /// Begins a slide when the press lands on the selected opening. Returns false so
        /// the caller can fall through to its own handling when it does not.
        /// </summary>
        public bool TryBeginDrag(Vector2 screenPos)
        {
            if (!HasSelection)
                return false;
            if (cam == null) cam = Camera.main;
            if (cam == null) return false;

            if (!Physics.Raycast(cam.ScreenPointToRay(screenPos), out RaycastHit hit, 5000f,
                                 1 << interactableLayer, QueryTriggerInteraction.Ignore))
                return false;
            if (OpeningIdOf(hit.transform) != selectedId)
                return false;

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
        /// The rig's 8 handles all lie in a HORIZONTAL ring around the target — it was
        /// built for furniture footprints, where both axes are on the ground. A door's
        /// two dimensions are width (along its wall) and height (vertical), so which
        /// handle means which cannot be read off HandleType: AxisX runs along the wall
        /// for a wall facing one way and across it for a wall facing another. Deciding
        /// from the handle's WORLD direction against the wall tangent is what makes the
        /// gesture behave the same on every wall orientation.
        ///
        /// Corner handles change both, as they do for furniture.
        /// </summary>
        public bool ResizeFromHandle(Vector3 handleWorldDir, bool corner, float scaleFactor)
        {
            UIBaseItem ui = OpeningAnchor.Find(selectedId);
            if (ui == null)
                return false;

            Vector2 start = ResizeStartSize;
            if (corner)
                return Resize(start.x * scaleFactor, start.y * scaleFactor, null);

            OpeningAnchor.WallSlot slot = OpeningAnchor.SlotOf(ui);
            Vector2 dir = new Vector2(handleWorldDir.x, handleWorldDir.z);
            bool alongWall = true;
            if (slot.Valid && dir.sqrMagnitude > 1e-6f)
                alongWall = Mathf.Abs(Vector2.Dot(dir.normalized, slot.Tangent)) > 0.707f;

            return alongWall
                ? Resize(start.x * scaleFactor, null, null)
                : Resize(null, start.y * scaleFactor, null);
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

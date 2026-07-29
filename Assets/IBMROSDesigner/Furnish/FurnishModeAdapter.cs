using Exoa.Events;
using UnityEngine;
using UnityEngine.UIElements;

namespace IBMROS.Designer.Furnish
{
    /// <summary>
    /// Bridges the transplanted Room-scene furniture stack into the designer.
    ///
    /// Responsibilities:
    ///   • Furnish mode UI: opening the catalog hides ALL normal room chrome
    ///     (designer HUD, joystick, selection bar) and keeps it hidden through
    ///     ghost placement — exactly the SetRoomUIVisible behaviour the old Room
    ///     scene had. It returns when the panel closes AND placement finishes.
    ///   • Input arbitration: while furniture is being dragged/placed, the Exoa
    ///     camera rig stands down — via a PlanCameraGate reason, never by writing
    ///     the vendor flag — so one drag can never move furniture and pan the room
    ///     at the same time, and releasing our reason cannot cancel anyone else's.
    ///   • Mode gating: the furniture interaction stack only runs where it can
    ///     do something useful, and the catalog NEVER changes the current view
    ///     (2D stays 2D, 3D stays 3D).
    /// </summary>
    public sealed class FurnishModeAdapter : MonoBehaviour
    {
        public static FurnishModeAdapter Instance { get; private set; }

        [SerializeField] private RoomUIManager roomUi;
        [SerializeField] private FurniturePanelController furniturePanel;
        [SerializeField] private GameObject interactionManagers;

        private Hud.DesignerHudController hud;
        private ItemDetailSheetController detailSheet;
        private SelectionManager selection;
        private ActionMenuController actionMenu;
        private ObjectManipulator manipulator;
        private FurniturePlacer placer;
        private ThreeD.WalkthroughController walkthrough;
        private bool wasPlacing;

        /// <summary>True from catalog-open until placement resolves.</summary>
        public bool FurnishUiActive { get; private set; }

        /// <summary>True while a furniture drag or ghost placement owns the pointer.</summary>
        public bool FurnitureOwnsInput { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            if (roomUi == null) roomUi = FindAnyObjectByType<RoomUIManager>(FindObjectsInactive.Include);
            if (furniturePanel == null) furniturePanel = FindAnyObjectByType<FurniturePanelController>(FindObjectsInactive.Include);
            if (interactionManagers == null)
            {
                var im = FindAnyObjectByType<InputManager>(FindObjectsInactive.Include);
                if (im != null) interactionManagers = im.gameObject;
            }
            hud = FindAnyObjectByType<Hud.DesignerHudController>(FindObjectsInactive.Include);
            detailSheet = FindAnyObjectByType<ItemDetailSheetController>(FindObjectsInactive.Include);
            selection = FindAnyObjectByType<SelectionManager>(FindObjectsInactive.Include);
            actionMenu = FindAnyObjectByType<ActionMenuController>(FindObjectsInactive.Include);
            manipulator = FindAnyObjectByType<ObjectManipulator>(FindObjectsInactive.Include);
            placer = FindAnyObjectByType<FurniturePlacer>(FindObjectsInactive.Include);
            walkthrough = FindAnyObjectByType<ThreeD.WalkthroughController>(FindObjectsInactive.Include);
        }

        private void OnEnable()
        {
            DesignerModeController.OnModeChanged += ApplyMode;
            if (furniturePanel != null)
                furniturePanel.OnPanelClosed += OnPanelClosed;
            if (detailSheet != null)
                detailSheet.OnSheetClosed += TryRestore;
            if (manipulator != null)
            {
                manipulator.OnManipulationStart += OnFurnitureGrabbed;
                manipulator.OnManipulationEnd += OnFurnitureReleased;
            }
            if (placer != null)
                placer.OnFurniturePlaced += OnPlacementResolved;
        }

        /// <summary>Centroid of the biggest room polygon, in world space.</summary>
        private static Vector3? LargestRoomCentre()
        {
            float bestArea = -1f;
            Vector2 best = Vector2.zero;
            foreach (var ui in Plan.PlanEditorUtil.AllSpaces())
            {
                var pts = Plan.PlanEditorUtil.WorldPoints(ui);
                if (pts.Count < 3)
                    continue;
                float area2 = 0f;
                var centroid = Vector2.zero;
                for (int i = 0; i < pts.Count; i++)
                {
                    Vector2 a = Plan.PlanEditorUtil.WorldToMeters(pts[i]);
                    Vector2 b = Plan.PlanEditorUtil.WorldToMeters(pts[(i + 1) % pts.Count]);
                    area2 += a.x * b.y - b.x * a.y;
                    centroid += a;
                }
                float area = Mathf.Abs(area2) * 0.5f;
                if (area > bestArea)
                {
                    bestArea = area;
                    best = centroid / pts.Count;
                }
            }
            return bestArea > 0f ? new Vector3(best.x, 0f, best.y) : (Vector3?)null;
        }

        private void OnDisable()
        {
            DesignerModeController.OnModeChanged -= ApplyMode;
            FurniturePlacer.SpawnPointProvider = null;
            if (furniturePanel != null)
                furniturePanel.OnPanelClosed -= OnPanelClosed;
            if (detailSheet != null)
                detailSheet.OnSheetClosed -= TryRestore;
            if (manipulator != null)
            {
                manipulator.OnManipulationStart -= OnFurnitureGrabbed;
                manipulator.OnManipulationEnd -= OnFurnitureReleased;
            }
            if (placer != null)
                placer.OnFurniturePlaced -= OnPlacementResolved;
        }

        private void Update()
        {
            // OnPlacementCancelled is unreliable, so detect the placing→idle
            // edge (confirm OR cancel) and restore the chrome from there.
            bool placing = placer != null && placer.IsPlacing;
            if (wasPlacing && !placing)
                TryRestore();
            wasPlacing = placing;
        }

        private void Start()
        {
            // Bird's-eye spawns land at the room centre; inside-the-room spawns
            // keep the existing in-front-of-camera behaviour.
            FurniturePlacer.SpawnPointProvider = () =>
            {
                if (DesignerModeController.Instance == null ||
                    DesignerModeController.Instance.Mode != DesignerMode.Plan2D)
                    return null;
                return LargestRoomCentre();
            };

            HideRoomUiChrome();
            ApplyMode(DesignerModeController.Instance != null
                ? DesignerModeController.Instance.Mode : DesignerMode.Plan2D);
        }

        // ------------------------------------------------------------------ catalog

        /// <summary>
        /// Add Furniture. Stays in the CURRENT view — a 2D bird's-eye user places
        /// furniture from 2D, a 3D user from inside the room (previously this
        /// force-switched to 3D). All normal room UI hides while furnishing.
        /// </summary>
        public void OpenCatalog()
        {
            // The furniture interaction stack must run for placement raycasts,
            // even in 2D where it is otherwise gated off.
            SetInteractionActive(true);
            SetFurnishUiActive(true);
            SetDismissOverlayVisible(true);
            furniturePanel?.Open();
        }

        private void OnPanelClosed()
        {
            SetDismissOverlayVisible(false);
            TryRestore();
        }

        private void OnPlacementResolved(FurnitureItem item) => TryRestore();

        /// <summary>
        /// Bring the room chrome back — but only once NOTHING furnish-related is
        /// still up. Ghost placement continues after the panel closes, so the UI
        /// must stay hidden until the item actually lands.
        /// </summary>
        private void TryRestore()
        {
            if (!FurnishUiActive)
                return;
            if (furniturePanel != null && furniturePanel.IsOpen)
                return;
            if (detailSheet != null && detailSheet.IsOpen)
                return;
            if (placer != null && placer.IsPlacing)
                return;
            SetFurnishUiActive(false);
        }

        /// <summary>Hides/restores every piece of normal room chrome at once.</summary>
        private void SetFurnishUiActive(bool furnishing)
        {
            if (FurnishUiActive == furnishing)
                return;
            FurnishUiActive = furnishing;

            hud?.SetHudVisible(!furnishing);
            walkthrough?.SetJoystickVisible(!furnishing);
            if (furnishing)
            {
                // Mirrors the old Room scene's open sequence.
                selection?.DeselectObject();
                actionMenu?.HidePanels();
            }

            // Ghost placement steers with the pointer — the camera must not.
            Plan.PlanCameraGate.Set(Plan.PlanCameraGate.Reason.FurnishUi, furnishing);
        }

        // ------------------------------------------------------------------ input arbitration

        private void OnFurnitureGrabbed()
        {
            FurnitureOwnsInput = true;
            Plan.PlanCameraGate.Hold(Plan.PlanCameraGate.Reason.Furniture);
        }

        /// <summary>
        /// Clears only OUR reason. This used to push the vendor flag straight to
        /// "moves allowed" whenever the catalog was closed, which silently cancelled
        /// the 2D plan's own hold and left Plan2D freely pannable at rest. The gate
        /// composes the reasons instead, so releasing this one cannot re-enable
        /// panning while the plan still wants the rig held.
        /// </summary>
        private void OnFurnitureReleased()
        {
            FurnitureOwnsInput = false;
            Plan.PlanCameraGate.Release(Plan.PlanCameraGate.Reason.Furniture);
        }

        // ------------------------------------------------------------------ mode gating

        private void ApplyMode(DesignerMode mode)
        {
            // The furniture stack stays live in EVERY mode. Disabling it in
            // Plan2D (as this used to) killed furniture dragging AND rotation in
            // the bird's-eye view, because ObjectDragHandler /
            // ObjectRotationHandler only subscribe while enabled. Priority is
            // arbitrated per-gesture instead (PlanTouchController defers when the
            // press lands on furniture).
            SetInteractionActive(true);
        }

        private void SetInteractionActive(bool active)
        {
            if (interactionManagers != null && interactionManagers.activeSelf != active)
                interactionManagers.SetActive(active);
        }

        // ------------------------------------------------------------------ RoomUI plumbing

        private void HideRoomUiChrome()
        {
            var doc = roomUi != null ? roomUi.GetComponent<UIDocument>() : null;
            if (doc == null)
                return;
            VisualElement root = doc.rootVisualElement;
            SetHidden(root.Q<VisualElement>("TopBar"));
            SetHidden(root.Q<VisualElement>("BottomBar"));
        }

        private static void SetHidden(VisualElement e)
        {
            if (e != null)
                e.style.display = DisplayStyle.None;
        }

        private void SetDismissOverlayVisible(bool visible)
        {
            var doc = roomUi != null ? roomUi.GetComponent<UIDocument>() : null;
            VisualElement overlay = doc != null
                ? doc.rootVisualElement.Q<VisualElement>("FurnitureDismissOverlay")
                : null;
            if (overlay != null)
                overlay.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}

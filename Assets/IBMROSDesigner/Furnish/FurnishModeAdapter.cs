using UnityEngine;
using UnityEngine.UIElements;

namespace IBMROS.Designer.Furnish
{
    /// <summary>
    /// Adapts the transplanted Room-scene furniture stack to the designer's
    /// mode model.
    ///
    ///   • Hides RoomUI.uxml's own chrome (TopBar/BottomBar) — DesignerHud owns
    ///     navigation; only the furniture panel, item-detail sheet and QR
    ///     overlays from that document stay usable.
    ///   • Gates the furniture interaction stack (InputManager, SelectionManager,
    ///     ObjectManipulator…) to 3D modes so 2D plan gestures never fight
    ///     furniture selection, and PlanTouchController never sees 3D taps
    ///     (it already self-gates to Plan2D).
    ///   • Exposes OpenCatalog() for DesignerHud's Furnish button.
    /// </summary>
    public sealed class FurnishModeAdapter : MonoBehaviour
    {
        public static FurnishModeAdapter Instance { get; private set; }

        [SerializeField] private RoomUIManager roomUi;
        [SerializeField] private FurniturePanelController furniturePanel;
        [SerializeField] private GameObject interactionManagers;

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
        }

        private void OnEnable()
        {
            DesignerModeController.OnModeChanged += ApplyMode;
            if (furniturePanel != null)
                furniturePanel.OnPanelClosed += OnPanelClosed;
        }

        private void OnDisable()
        {
            DesignerModeController.OnModeChanged -= ApplyMode;
            if (furniturePanel != null)
                furniturePanel.OnPanelClosed -= OnPanelClosed;
        }

        private void Start()
        {
            HideRoomUiChrome();
            ApplyMode(DesignerModeController.Instance != null
                ? DesignerModeController.Instance.Mode : DesignerMode.Plan2D);
        }

        /// <summary>
        /// DesignerHud's Furnish/Add buttons. From the 2D plan this first drops
        /// the user inside the room (placement needs the 3D interaction stack),
        /// then opens the catalog with the dismiss overlay armed so tapping
        /// outside the panel closes it (RoomUIManager only arms that overlay on
        /// its own — hidden — bottom-bar path).
        /// </summary>
        public void OpenCatalog()
        {
            if (DesignerModeController.Instance != null &&
                DesignerModeController.Instance.Mode == DesignerMode.Plan2D)
                DesignerModeController.Instance.Set3D();
            SetDismissOverlayVisible(true);
            furniturePanel?.Open();
        }

        private void OnPanelClosed() => SetDismissOverlayVisible(false);

        private void SetDismissOverlayVisible(bool visible)
        {
            var doc = roomUi != null ? roomUi.GetComponent<UIDocument>() : null;
            VisualElement overlay = doc != null
                ? doc.rootVisualElement.Q<VisualElement>("FurnitureDismissOverlay")
                : null;
            if (overlay != null)
                overlay.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

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

        private void ApplyMode(DesignerMode mode)
        {
            bool furnish = mode != DesignerMode.Plan2D;
            if (interactionManagers != null && interactionManagers.activeSelf != furnish)
                interactionManagers.SetActive(furnish);
            if (!furnish)
            {
                // Programmatic Close() doesn't raise OnPanelClosed — drop the
                // dismiss overlay ourselves or it lingers over the 2D plan.
                furniturePanel?.Close();
                SetDismissOverlayVisible(false);
            }
        }
    }
}

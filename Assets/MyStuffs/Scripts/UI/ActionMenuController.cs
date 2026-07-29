using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;

public class ActionMenuController : MonoBehaviour
{
    [Header("Dependencies")]
    [SerializeField] private SelectionManager selectionManager;
    [SerializeField] private ObjectManipulator objectManipulator;
    [SerializeField] private ScaleRigUI scaleRigUI;

    [Header("Panel Above")]
    [SerializeField] private RectTransform panelAbove;
    [SerializeField] private Button colorButton;
    [SerializeField] private Button scaleButton;
    [SerializeField] private Button duplicateButton;
    [SerializeField] private Button deleteButton;
    [SerializeField] private Button moveButton;
    [SerializeField] private Button moreOptionsButton;

    [Header("Panel Below")]
    [SerializeField] private RectTransform panelBelow;
    [SerializeField] private UIDragHandle rotationHandle;
    private Vector2 _panelBelowInitialAnchoredPos;
    private bool _panelBelowInitialCached = false;

    [Header("Settings")]
    [SerializeField] private float verticalPadding = 20f;
    [SerializeField] private float minVerticalSpacing = 20f;

    public event Action OnScaleModeRequested;
    public event Action OnColorModeRequested;

    private Camera _mainCamera;
    private Transform _targetObject;
    private Renderer _targetRenderer;
    private bool _isVisible = false;
    private bool _isScalingMode = false;
    private Vector3[] _corners = new Vector3[4];
    private bool _isRotatingPanel = false;
    private Bounds _targetCombinedBounds;
    private bool   _hasCombinedBounds = false;

    void Awake()
    {
        _mainCamera = Camera.main;
    }

    void OnEnable()
    {
        if (selectionManager != null)
        {
            selectionManager.onObjectSelected += HandleObjectSelected;
            selectionManager.onObjectDeselected += HandleObjectDeselected;
            selectionManager.onObjectReSelected += HandleObjectSelected;
        }

        if (objectManipulator != null)
        {
            objectManipulator.OnManipulationStart += HandleManipulationStart;
            objectManipulator.OnManipulationEnd += HandleManipulationEnd;
            objectManipulator.OnCameraRotationStart += HandleCameraRotationStart;
            objectManipulator.OnCameraRotationEnd += HandleCameraRotationEnd;
        }

        if (rotationHandle != null)
        {
            rotationHandle.onDragStart += HandleRotationDragStart;
            rotationHandle.onDrag += HandleRotationDrag;
            rotationHandle.onDragEnd += HandleRotationDragEnd;
        }

        WireButtons();
        HidePanels();
    }

    void OnDisable()
    {
        if (selectionManager != null)
        {
            selectionManager.onObjectSelected -= HandleObjectSelected;
            selectionManager.onObjectDeselected -= HandleObjectDeselected;
            selectionManager.onObjectReSelected -= HandleObjectSelected;
        }

        if (objectManipulator != null)
        {
            objectManipulator.OnManipulationStart -= HandleManipulationStart;
            objectManipulator.OnManipulationEnd -= HandleManipulationEnd;
            objectManipulator.OnCameraRotationStart -= HandleCameraRotationStart;
            objectManipulator.OnCameraRotationEnd -= HandleCameraRotationEnd;
        }

        if (rotationHandle != null)
        {
            rotationHandle.onDragStart -= HandleRotationDragStart;
            rotationHandle.onDrag -= HandleRotationDrag;
            rotationHandle.onDragEnd -= HandleRotationDragEnd;
        }

        UnwireButtons();
    }

    void Update()
    {
        if (!_isVisible || _targetRenderer == null)
            return;

        // Do not update positions while user is dragging rotation handle
        if (_isRotatingPanel)
            return;

        UpdatePanelPositions();

        if (_openingPicker != null && _openingPicker.gameObject.activeSelf)
            PositionOpeningPicker();
    }

    private void WireButtons()
    {
        if (colorButton != null)
            colorButton.onClick.AddListener(OnColorClicked);

        if (scaleButton != null)
            scaleButton.onClick.AddListener(OnScaleClicked);

        if (duplicateButton != null)
            duplicateButton.onClick.AddListener(OnDuplicateClicked);

        if (deleteButton != null)
            deleteButton.onClick.AddListener(OnDeleteClicked);

        if (moveButton != null)
            moveButton.onClick.AddListener(OnMoveClicked);

        if (moreOptionsButton != null)
            moreOptionsButton.onClick.AddListener(OnMoreOptionsClicked);
    }

    private void UnwireButtons()
    {
        if (colorButton != null)
            colorButton.onClick.RemoveListener(OnColorClicked);

        if (scaleButton != null)
            scaleButton.onClick.RemoveListener(OnScaleClicked);

        if (duplicateButton != null)
            duplicateButton.onClick.RemoveListener(OnDuplicateClicked);

        if (deleteButton != null)
            deleteButton.onClick.RemoveListener(OnDeleteClicked);

        if (moveButton != null)
            moveButton.onClick.RemoveListener(OnMoveClicked);

        if (moreOptionsButton != null)
            moreOptionsButton.onClick.RemoveListener(OnMoreOptionsClicked);
    }

    // BUTTON HANDLERS

    private void OnColorClicked()
    {
        // A door/window's appearance lives in the floor-plan document (per-part
        // materials resolved by OpeningStyleLibrary), so Color opens a picker over that
        // catalog. Furniture keeps its existing event path.
        if (OpeningSelected)
        {
            ToggleOpeningPicker(materials: true);
            return;
        }
        OnColorModeRequested?.Invoke();
    }

    private void OnScaleClicked()
    {
        _isScalingMode = true;
        HidePanels();

        scaleRigUI?.ShowRig();
        OnScaleModeRequested?.Invoke();
    }

    private void OnDuplicateClicked()
    {
        objectManipulator?.DuplicateSelectedObject();
    }

    private void OnDeleteClicked()
    {
        objectManipulator?.DeleteSelectedObject();
    }

    private void OnMoveClicked()
    {
        Debug.Log("[ActionMenuController] Move tapped. Coming soon.");
    }

    private void OnMoreOptionsClicked()
    {
        Debug.Log("[ActionMenuController] More options tapped. Coming soon.");
    }

    // ---------------------------------------------------------------- openings
    //
    // A selected door/window uses THIS toolbar — one contextual toolbar for
    // everything, as in the reference — but its operations differ from furniture:
    // Rotate means "flip within the wall" (not free yaw, so the floating rotation
    // handle is hidden), and Replace/Color pick from the opening catalog. The extra
    // buttons are cloned from an existing one at runtime because the toolbar is a
    // scene-authored uGUI panel with no serialized slots for them.

    private bool OpeningSelected =>
        IBMROS.Designer.Openings.OpeningInteraction.Instance != null &&
        IBMROS.Designer.Openings.OpeningInteraction.Instance.HasSelection;

    private Button _rotateOpeningButton;
    private Button _replaceOpeningButton;
    private RectTransform _openingPicker;
    private bool _pickerShowsMaterials;

    private void EnsureOpeningButtons()
    {
        if (_rotateOpeningButton != null || duplicateButton == null)
            return;
        _rotateOpeningButton = CloneToolbarButton(duplicateButton, "Rotate",
            () => IBMROS.Designer.Openings.OpeningInteraction.Instance?.Flip());
        _replaceOpeningButton = CloneToolbarButton(duplicateButton, "Replace",
            () => ToggleOpeningPicker(materials: false));
    }

    private Button CloneToolbarButton(Button template, string label, UnityEngine.Events.UnityAction onClick)
    {
        GameObject go = Instantiate(template.gameObject, template.transform.parent);
        go.name = "Btn" + label + "_Opening";
        go.transform.SetSiblingIndex(template.transform.GetSiblingIndex() + 1);
        Button btn = go.GetComponent<Button>();
        btn.onClick = new Button.ButtonClickedEvent();   // never inherit the template's action
        btn.onClick.AddListener(onClick);
        SetButtonLabel(go, label);
        go.SetActive(false);
        return btn;
    }

    private static void SetButtonLabel(GameObject go, string label)
    {
        TMP_Text tmp = go.GetComponentInChildren<TMP_Text>(true);
        if (tmp != null) { tmp.text = label; return; }
        Text legacy = go.GetComponentInChildren<Text>(true);
        if (legacy != null) legacy.text = label;
    }

    private void SetOpeningButtonsVisible(bool visible)
    {
        EnsureOpeningButtons();
        if (_rotateOpeningButton != null)
            _rotateOpeningButton.gameObject.SetActive(visible);
        if (_replaceOpeningButton != null)
            _replaceOpeningButton.gameObject.SetActive(visible);
    }

    /// <summary>
    /// The Replace / Color list for the selected opening: model ids from
    /// Resources/OpeningModels (plus Standard = procedural) or the material names from
    /// Resources/Opening. Built at runtime on the toolbar canvas; one tap applies —
    /// each pick is already a single labelled undo step inside OpeningStyler.
    /// </summary>
    private void ToggleOpeningPicker(bool materials)
    {
        if (_openingPicker != null && _openingPicker.gameObject.activeSelf &&
            _pickerShowsMaterials == materials)
        {
            _openingPicker.gameObject.SetActive(false);
            return;
        }
        _pickerShowsMaterials = materials;
        BuildOpeningPicker(materials);
    }

    private void CloseOpeningPicker()
    {
        if (_openingPicker != null)
            _openingPicker.gameObject.SetActive(false);
    }

    private void BuildOpeningPicker(bool materials)
    {
        if (_openingPicker != null)
            Destroy(_openingPicker.gameObject);

        var canvas = panelAbove != null ? panelAbove.GetComponentInParent<Canvas>() : null;
        if (canvas == null)
            return;

        var root = new GameObject(materials ? "OpeningColorPicker" : "OpeningReplacePicker",
            typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup),
            typeof(ContentSizeFitter));
        _openingPicker = (RectTransform)root.transform;
        _openingPicker.SetParent(canvas.transform, false);
        var bg = root.GetComponent<Image>();
        bg.color = new Color(1f, 1f, 1f, 0.97f);
        var layout = root.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(10, 10, 10, 10);
        layout.spacing = 6;
        layout.childForceExpandHeight = false;
        var fitter = root.GetComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

        var openings = IBMROS.Designer.Openings.OpeningInteraction.Instance;
        if (materials)
        {
            foreach (string name in IBMROS.Designer.Materials.OpeningStyleLibrary.MaterialNames())
            {
                string picked = name;
                Material m = IBMROS.Designer.Materials.OpeningStyleLibrary.ResolveMaterial(picked);
                Color swatch = m != null && m.HasProperty("_BaseColor")
                    ? m.GetColor("_BaseColor") : Color.white;
                AddPickerRow(picked.Replace("Opening_", ""), swatch, () =>
                {
                    openings?.SetPartMaterial(
                        IBMROS.Designer.Materials.OpeningStyler.Part.Frame, picked);
                    CloseOpeningPicker();
                });
            }
        }
        else
        {
            AddPickerRow("Standard", Color.white, () =>
            {
                openings?.Replace(null);
                CloseOpeningPicker();
            });
            foreach (string id in IBMROS.Designer.Materials.OpeningStyleLibrary.ModelIds())
            {
                string picked = id;
                AddPickerRow(picked.Replace("Door_", "").Replace("Window_", ""),
                    Color.white, () =>
                {
                    openings?.Replace(picked);
                    CloseOpeningPicker();
                });
            }
        }

        PositionOpeningPicker();
    }

    private void AddPickerRow(string label, Color swatch, UnityEngine.Events.UnityAction onClick)
    {
        var row = new GameObject("Row_" + label, typeof(RectTransform), typeof(Image),
            typeof(Button), typeof(LayoutElement));
        row.transform.SetParent(_openingPicker, false);
        row.GetComponent<LayoutElement>().preferredWidth = 260f;
        row.GetComponent<LayoutElement>().preferredHeight = 64f;
        var img = row.GetComponent<Image>();
        img.color = new Color(swatch.r, swatch.g, swatch.b, 0.25f);
        var btn = row.GetComponent<Button>();
        btn.onClick.AddListener(onClick);

        var textGo = new GameObject("Label", typeof(RectTransform));
        textGo.transform.SetParent(row.transform, false);
        var rt = (RectTransform)textGo.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.sizeDelta = Vector2.zero;
        var tmp = textGo.AddComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.fontSize = 26f;
        tmp.color = new Color(0.12f, 0.12f, 0.14f, 1f);
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.raycastTarget = false;
    }

    private void PositionOpeningPicker()
    {
        if (_openingPicker == null || panelAbove == null)
            return;
        float drop = (panelAbove.rect.height * panelAbove.lossyScale.y) * 0.5f
                   + (_openingPicker.rect.height * _openingPicker.lossyScale.y) * 0.5f + 16f;
        _openingPicker.position = panelAbove.position - new Vector3(0f, drop, 0f);
    }

    // SELECTION HANDLERS

    private void HandleObjectSelected(Transform target)
    {
        _isScalingMode = false;
        scaleRigUI?.HideRig();
        CloseOpeningPicker();
        SetOpeningButtonsVisible(OpeningSelected);

        _targetObject   = target;

        // Get combined bounds of ALL renderers not just first child
        Renderer[] renderers = target.GetComponentsInChildren<Renderer>();
        if (renderers.Length > 0)
        {
            Bounds combined = renderers[0].bounds;
            foreach (var r in renderers)
                combined.Encapsulate(r.bounds);

            // Create a temporary renderer reference is not enough
            // Store the combined bounds directly for positioning
            _targetRenderer = renderers[0]; // keep reference for null check
            _targetCombinedBounds = combined;
            _hasCombinedBounds = true;
        }
        else
        {
            _targetRenderer = null;
            _hasCombinedBounds = false;
        }

        ShowPanels();
    }

    private void HandleObjectDeselected()
    {
        _isScalingMode = false;
        scaleRigUI?.HideRig();
        CloseOpeningPicker();

        _targetObject = null;
        _targetRenderer = null;
        HidePanels();
    }

    private void HandleManipulationStart()
    {
        HidePanels();
    }

    private void HandleManipulationEnd()
    {
        if (_targetObject != null && !_isScalingMode)
            ShowPanels();
    }

    private void HandleCameraRotationStart()
    {
        HidePanels();
    }

    private void HandleCameraRotationEnd()
    {
        // User must tap object again to reopen
    }

    private void HandleRotationDragStart(UnityEngine.EventSystems.PointerEventData data)
    {
        _isRotatingPanel = true;

        if (panelAbove != null)
            panelAbove.gameObject.SetActive(false);
    }

    private void HandleRotationDrag(UnityEngine.EventSystems.PointerEventData data)
    {
        if (panelBelow == null)
            return;

        Canvas canvas = panelBelow.GetComponentInParent<Canvas>();

        if (canvas == null)
            return;

        Vector2 localPoint;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            canvas.transform as RectTransform,
            data.position,
            canvas.worldCamera,
            out localPoint
        );

        panelBelow.localPosition = localPoint;
    }

    private void HandleRotationDragEnd(UnityEngine.EventSystems.PointerEventData data)
    {
        _isRotatingPanel = false;

        if (_targetObject != null && !_isScalingMode)
            ShowPanels();
    }
    // PANEL VISIBILITY

    public void ShowPanels()
    {
        if (panelAbove != null)
            panelAbove.gameObject.SetActive(true);

        // The lower panel is the free-rotation drag handle. A wall-hosted opening has
        // no free rotation — its one turn (hinge-side flip) is the toolbar's Rotate
        // button — so showing a rotation gizmo for a door would be an affordance for
        // an operation that must not exist.
        if (panelBelow != null)
            panelBelow.gameObject.SetActive(!OpeningSelected);

        _isVisible = true;
        UpdatePanelPositions();
    }

    public void HidePanels()
    {
        if (panelAbove != null)
            panelAbove.gameObject.SetActive(false);

        if (panelBelow != null)
            panelBelow.gameObject.SetActive(false);

        _isVisible = false;
    }

    // PANEL POSITIONING

    private void UpdatePanelPositions()
    {
        if (_targetRenderer == null || _mainCamera == null)
            return;

        // Pass the renderers array directly so ScreenSpaceHelper can use OBB projection
        Renderer[] renderers = _targetObject != null 
            ? _targetObject.GetComponentsInChildren<Renderer>() 
            : new Renderer[] { _targetRenderer };

        bool isOnScreen = ScreenSpaceHelper.TryGetScreenSpaceBounds(
            renderers,
            _mainCamera,
            out ScreenSpaceHelper.ObjectScreenBounds screenBounds
        );

        if (!isOnScreen)
        {
            HidePanels();
            return;
        }

        Rect safeArea = GetConstrainedSafeArea();

        // Panel Above
        if (panelAbove != null && panelAbove.gameObject.activeSelf)
        {
            float halfW = panelAbove.rect.width * panelAbove.lossyScale.y * 0.5f;

            float x = Mathf.Clamp(screenBounds.CenterX,
                safeArea.xMin + halfW, safeArea.xMax - halfW);
            float y = PlaceClearOf(panelAbove, screenBounds, safeArea, above: true);

            panelAbove.position = new Vector3(x, y, 0f);
        }

        // Panel Below
        if (panelBelow != null && panelBelow.gameObject.activeSelf)
        {
            float scaleFactor = panelBelow.lossyScale.y;
            float halfW = panelBelow.rect.width  * scaleFactor * 0.5f;
            float halfH = panelBelow.rect.height * scaleFactor * 0.5f;

            float x = Mathf.Clamp(screenBounds.CenterX,
                safeArea.xMin + halfW, safeArea.xMax - halfW);
            float y = PlaceClearOf(panelBelow, screenBounds, safeArea, above: false);

            // Keep the two panels apart. When both end up on the SAME side of the
            // item (the safe area had no room on one side, so PlaceClearOf flipped
            // one of them), the old symmetric nudge pushed each halfway and left
            // them crowded against each other — and could shove one back over the
            // item. Stack the lower panel a full gap under the upper one instead,
            // and only nudge the upper one if the stack runs out of safe area.
            if (panelAbove != null && panelAbove.gameObject.activeSelf)
            {
                float aboveHalfH = panelAbove.rect.height * panelAbove.lossyScale.y * 0.5f;
                float aboveBottom = panelAbove.position.y - aboveHalfH;
                float wantTop = y + halfH;

                if (wantTop > aboveBottom - minVerticalSpacing)
                {
                    float stackedY = aboveBottom - minVerticalSpacing - halfH;
                    float floorY = safeArea.yMin + halfH;
                    if (stackedY >= floorY)
                    {
                        y = stackedY;              // room below: stack cleanly
                    }
                    else
                    {
                        // No room: hold this panel at the floor and lift the other
                        // one so the full gap still exists between them.
                        y = floorY;
                        float need = (y + halfH + minVerticalSpacing + aboveHalfH)
                                     - panelAbove.position.y;
                        if (need > 0f)
                        {
                            float ceilY = safeArea.yMax - aboveHalfH;
                            panelAbove.position = new Vector3(
                                panelAbove.position.x,
                                Mathf.Min(panelAbove.position.y + need, ceilY),
                                0f);
                        }
                    }
                }
            }

            panelBelow.position = new Vector3(x, y, 0f);
        }
    }

    /// <summary>
    /// Screen Y for a selection panel so its NEAR EDGE clears the item by
    /// verticalPadding — not its centre, which is what the old code positioned.
    ///
    /// This is load-bearing, not cosmetic. These panels are raycast targets, and
    /// InputManager drops any world press that starts over UI. Centring a ~100 px tall
    /// bar 20 px above the item's top edge parks half of it ON the item, and in the
    /// bird's-eye plan — where a chair is small on screen — the two panels together
    /// covered it completely: the item could be tapped (panels are hidden until
    /// something is selected) and rotated (the handle is itself UI), but never dragged,
    /// because no press on it ever reached the world.
    ///
    /// If the safe area cannot fit the panel on its preferred side, it flips to the
    /// other side rather than sitting on top of the item.
    /// </summary>
    private float PlaceClearOf(RectTransform panel,
                               ScreenSpaceHelper.ObjectScreenBounds bounds,
                               Rect safeArea,
                               bool above)
    {
        float scale = panel.lossyScale.y;
        // Offsets from the panel's pivot to its own edges (pivot may not be centred).
        float toTop = panel.rect.yMax * scale;
        float toBottom = panel.rect.yMin * scale;   // negative

        float minY = safeArea.yMin - toBottom;      // pivot Y with the bottom edge on the floor
        float maxY = safeArea.yMax - toTop;         // pivot Y with the top edge on the ceiling
        if (minY > maxY)                            // panel taller than the safe area
            return Mathf.Clamp(bounds.TopY, safeArea.yMin, safeArea.yMax);

        float wantAbove = bounds.TopY + verticalPadding - toBottom;
        float wantBelow = bounds.BottomY - verticalPadding - toTop;

        float first = above ? wantAbove : wantBelow;
        float second = above ? wantBelow : wantAbove;

        if (first >= minY && first <= maxY)
            return first;
        if (second >= minY && second <= maxY)
            return second;   // flip to the side that fits
        return Mathf.Clamp(first, minY, maxY);
    }

    public float topBarLimit = 72f;
    public float bottomBarLimit = 120f;
    private Rect GetConstrainedSafeArea()
    {
        Rect safeArea = Screen.safeArea;

        float topBarHeight = topBarLimit * (Screen.height / 1920f);
        float bottomBarHeight = bottomBarLimit * (Screen.height / 1920f);

        // Add horizontal padding so panels never touch screen edges
        float horizontalPadding = 16f * (Screen.width / 1080f);

        float yMin = safeArea.yMin + bottomBarHeight;
        float yMax = safeArea.yMax - topBarHeight;

        return new Rect(
            safeArea.x + horizontalPadding,
            yMin,
            safeArea.width - (horizontalPadding * 2f),
            Mathf.Max(0, yMax - yMin)
        );
    }
}
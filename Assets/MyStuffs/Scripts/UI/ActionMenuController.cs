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

    // SELECTION HANDLERS

    private void HandleObjectSelected(Transform target)
    {
        _isScalingMode = false;
        scaleRigUI?.HideRig();

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

        if (panelBelow != null)
            panelBelow.gameObject.SetActive(true);

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

        // Use combined bounds for multi-part objects
        Bounds boundsToUse = _hasCombinedBounds
            ? _targetCombinedBounds
            : _targetRenderer.bounds;

        // Recalculate combined bounds every frame since object may have moved
        if (_targetObject != null)
        {
            Renderer[] renderers = _targetObject.GetComponentsInChildren<Renderer>();
            if (renderers.Length > 0)
            {
                boundsToUse = renderers[0].bounds;
                foreach (var r in renderers)
                    boundsToUse.Encapsulate(r.bounds);
            }
        }

        bool isOnScreen = ScreenSpaceHelper.TryGetScreenSpaceBounds(
            boundsToUse,
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
            float scaleFactor = panelAbove.lossyScale.y;
            float halfW = panelAbove.rect.width  * scaleFactor * 0.5f;
            float halfH = panelAbove.rect.height * scaleFactor * 0.5f;

            float x = Mathf.Clamp(screenBounds.CenterX,
                safeArea.xMin + halfW, safeArea.xMax - halfW);
            float y = screenBounds.TopY + verticalPadding;
            y = Mathf.Clamp(y, safeArea.yMin + halfH, safeArea.yMax - halfH);

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
            float y = screenBounds.BottomY - verticalPadding;
            y = Mathf.Clamp(y, safeArea.yMin + halfH, safeArea.yMax - halfH);

            // Ensure minimum gap between above and below panels
            if (panelAbove != null && panelAbove.gameObject.activeSelf)
            {
                float aboveBottom = panelAbove.position.y
                    - panelAbove.rect.height * panelAbove.lossyScale.y * 0.5f;
                float belowTop = y + halfH;
                float overlap  = belowTop - aboveBottom + minVerticalSpacing;

                if (overlap > 0f)
                {
                    panelAbove.position = new Vector3(
                        panelAbove.position.x,
                        panelAbove.position.y + overlap * 0.5f,
                        0f);
                    y -= overlap * 0.5f;
                }
            }

            panelBelow.position = new Vector3(x, y, 0f);
        }
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
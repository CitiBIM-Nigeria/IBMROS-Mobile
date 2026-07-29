using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using System;

public class ScaleRigUI : MonoBehaviour
{
    [Header("Handle Prefabs")]
    [SerializeField] private GameObject cornerHandlePrefab;
    [SerializeField] private GameObject sideHandlePrefab;

    [Header("Dependencies")]
    [SerializeField] private SelectionManager selectionManager;
    [SerializeField] private ScreenLineRenderer lineRenderer;

    [Header("Settings")]
    [SerializeField] private Color lineColor = new Color(0.2f, 0.6f, 1f, 1f);

    public event Action<ScaleHandleUI, Vector2> OnHandleDown;
    public event Action<ScaleHandleUI, Vector2> OnHandleDrag;
    public event Action<ScaleHandleUI, Vector2> OnHandleUp;

    private Canvas _canvas;
    private Camera _mainCamera;
    private Transform _currentTarget;
    private List<ScaleHandleUI> _handles = new List<ScaleHandleUI>();
    private RectTransform _canvasRect;
    private bool _isActive = false;

    // Handle directions and types in order
    // 0-3 corners, 4-7 sides
    private static readonly (Vector3 dir, HandleType type)[] _handleDefs =
    {
        (new Vector3( 1, 0,  1), HandleType.Corner),
        (new Vector3(-1, 0,  1), HandleType.Corner),
        (new Vector3(-1, 0, -1), HandleType.Corner),
        (new Vector3( 1, 0, -1), HandleType.Corner),
        (new Vector3( 0, 0,  1), HandleType.AxisZ),
        (new Vector3(-1, 0,  0), HandleType.AxisX),
        (new Vector3( 0, 0, -1), HandleType.AxisZ),
        (new Vector3( 1, 0,  0), HandleType.AxisX),
    };

    void Awake()
    {
        _canvas = GetComponentInParent<Canvas>();
        _canvasRect = _canvas.GetComponent<RectTransform>();
        _mainCamera = Camera.main;

        if (selectionManager != null)
        {
            selectionManager.onObjectSelected += OnObjectSelected;
            selectionManager.onObjectDeselected += OnObjectDeselected;
        }
    }

    void OnDestroy()
    {
        if (selectionManager != null)
        {
            selectionManager.onObjectSelected -= OnObjectSelected;
            selectionManager.onObjectDeselected -= OnObjectDeselected;
        }
    }

    void Update()
    {
        if (!_isActive || _currentTarget == null)
            return;

        UpdateRig();
    }

    private void OnObjectSelected(Transform target)
    {
        _currentTarget = target;
        HideRig();
    }

    private void OnObjectDeselected()
    {
        _currentTarget = null;
        HideRig();
    }

    public void ShowRig()
    {
        if (_currentTarget == null)
            return;

        HideRig();
        SpawnHandles();
        _isActive = true;
        UpdateRig();
    }

    public void HideRig()
    {
        _isActive = false;

        foreach (var handle in _handles)
        {
            if (handle != null)
                Destroy(handle.gameObject);
        }

        _handles.Clear();
        lineRenderer?.ClearLines();
        SetDimensionLabel(null, default);
    }

    private void SpawnHandles()
    {
        foreach (var def in _handleDefs)
        {
            GameObject prefab = def.type == HandleType.Corner
                ? cornerHandlePrefab
                : sideHandlePrefab;

            if (prefab == null)
                continue;

            GameObject go = Instantiate(prefab, transform);
            ScaleHandleUI handle = go.GetComponent<ScaleHandleUI>();

            if (handle == null)
                handle = go.AddComponent<ScaleHandleUI>();

            handle.type = def.type;
            handle.direction = def.dir;

            handle.OnHandleDown += (h, pos) => OnHandleDown?.Invoke(h, pos);
            handle.OnHandleDrag += (h, pos) => OnHandleDrag?.Invoke(h, pos);
            handle.OnHandleUp += (h, pos) => OnHandleUp?.Invoke(h, pos);

            _handles.Add(handle);
        }
    }

    private void UpdateRig()
    {
        if (_currentTarget == null || _mainCamera == null)
            return;

        // A door/window is a vertical rectangle in its wall, not a footprint on the
        // floor — the furniture layout below would put all 8 handles in a horizontal
        // ring above it, which is meaningless for something you resize by width and
        // height. Openings get the reference layout: corners + side pills on the
        // rectangle itself, with a live dimension readout.
        if (UpdateOpeningRig())
            return;
        SetDimensionLabel(null, default);

        Renderer[] renderers = _currentTarget.GetComponentsInChildren<Renderer>();

        if (renderers.Length == 0)
            return;

        Bounds worldBounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            worldBounds.Encapsulate(renderers[i].bounds);

        Vector3 worldCenter = worldBounds.center;
        Vector3 halfSize = worldBounds.extents;
        float topY = worldBounds.max.y + 0.02f;

        Quaternion yRot = Quaternion.Euler(0, _currentTarget.eulerAngles.y, 0);

        Vector2[] canvasPositions = new Vector2[_handles.Count];

        for (int i = 0; i < _handles.Count; i++)
        {
            Vector3 dir = _handles[i].direction;

            Vector3 worldPos = new Vector3(worldCenter.x, topY, worldCenter.z)
                + yRot * new Vector3(
                    dir.x * halfSize.x,
                    0,
                    dir.z * halfSize.z);

            canvasPositions[i] = WorldToCanvas(worldPos);
            _handles[i].GetComponent<RectTransform>().anchoredPosition
                = canvasPositions[i];
        }

        // Corner canvas positions are indices 0 to 3
        Vector2 c0 = canvasPositions[0];
        Vector2 c1 = canvasPositions[1];
        Vector2 c2 = canvasPositions[2];
        Vector2 c3 = canvasPositions[3];

        // Rotate each side handle using its two nearest corners
        // Index 4: between corner 0 and 1
        AlignSideHandle(_handles[4], c0, c1);
        // Index 5: between corner 1 and 2
        AlignSideHandle(_handles[5], c1, c2);
        // Index 6: between corner 2 and 3
        AlignSideHandle(_handles[6], c2, c3);
        // Index 7: between corner 3 and 0
        AlignSideHandle(_handles[7], c3, c0);

        // Draw outline using corner positions
        Vector2[] corners = new Vector2[] { c0, c1, c2, c3 };
        lineRenderer?.DrawLines(corners, true);
    }

    /// <summary>
    /// Rig layout for a selected door/window: the opening's actual rectangle in its
    /// wall plane. Returns false when the selection is not an opening, so the caller
    /// falls through to the furniture footprint layout.
    ///
    /// The corner order (+t+up, −t+up, −t−up, +t−up) deliberately matches the
    /// side-handle pairing the furniture path already uses (4=top edge, 5=left,
    /// 6=bottom, 7=right), so AlignSideHandle and SetHandleHighlight work unchanged —
    /// and the AxisZ pills always mean HEIGHT and the AxisX pills always mean WIDTH,
    /// on a wall of any orientation.
    /// </summary>
    private bool UpdateOpeningRig()
    {
        var openings = IBMROS.Designer.Openings.OpeningInteraction.Instance;
        if (openings == null || !openings.HasSelection)
            return false;
        var ui = IBMROS.Designer.Openings.OpeningAnchor.Find(openings.SelectedId);
        Vector2? centre = IBMROS.Designer.Openings.OpeningAnchor.CentreOf(ui);
        if (ui == null || !centre.HasValue || _handles.Count < 8)
            return false;

        var slot = IBMROS.Designer.Openings.OpeningAnchor.SlotOf(ui);
        Vector2 t2 = slot.Valid ? slot.Tangent
                                : IBMROS.Designer.Openings.OpeningAnchor.StoredTangent(ui);
        Vector3 t3 = new Vector3(t2.x, 0f, t2.y);
        float w = ui.Width, h = ui.Height, yBase = Mathf.Max(0f, ui.YPos);
        Vector3 mid = new Vector3(centre.Value.x, yBase + h * 0.5f, centre.Value.y);

        Vector3[] world =
        {
            mid + t3 * (w * 0.5f) + Vector3.up * (h * 0.5f),   // 0 corner +t +up
            mid - t3 * (w * 0.5f) + Vector3.up * (h * 0.5f),   // 1 corner −t +up
            mid - t3 * (w * 0.5f) - Vector3.up * (h * 0.5f),   // 2 corner −t −up
            mid + t3 * (w * 0.5f) - Vector3.up * (h * 0.5f),   // 3 corner +t −up
            mid + Vector3.up * (h * 0.5f),                     // 4 top    (AxisZ → height)
            mid - t3 * (w * 0.5f),                             // 5 left   (AxisX → width)
            mid - Vector3.up * (h * 0.5f),                     // 6 bottom (AxisZ → height)
            mid + t3 * (w * 0.5f),                             // 7 right  (AxisX → width)
        };

        Vector2[] canvasPositions = new Vector2[_handles.Count];
        for (int i = 0; i < _handles.Count && i < world.Length; i++)
        {
            canvasPositions[i] = WorldToCanvas(world[i]);
            _handles[i].GetComponent<RectTransform>().anchoredPosition = canvasPositions[i];
        }

        AlignSideHandle(_handles[4], canvasPositions[0], canvasPositions[1]);
        AlignSideHandle(_handles[5], canvasPositions[1], canvasPositions[2]);
        AlignSideHandle(_handles[6], canvasPositions[2], canvasPositions[3]);
        AlignSideHandle(_handles[7], canvasPositions[3], canvasPositions[0]);

        lineRenderer?.DrawLines(new[]
        {
            canvasPositions[0], canvasPositions[1], canvasPositions[2], canvasPositions[3]
        }, true);

        // Live dimensions above the top edge — same feedback the reference shows,
        // updating every frame of the drag because the rig reruns per frame and reads
        // the document values the resize is writing.
        SetDimensionLabel(w.ToString("0.00") + " m  ×  " + h.ToString("0.00") + " m",
            canvasPositions[4] + new Vector2(0f, 46f));
        return true;
    }

    private RectTransform _dimLabelRoot;
    private TMPro.TextMeshProUGUI _dimLabelText;

    /// <summary>Shows/hides the W × H pill (null text = hide). Built lazily in code —
    /// the rig canvas has no authored label to reuse.</summary>
    private void SetDimensionLabel(string text, Vector2 canvasPos)
    {
        if (string.IsNullOrEmpty(text))
        {
            if (_dimLabelRoot != null)
                _dimLabelRoot.gameObject.SetActive(false);
            return;
        }
        if (_dimLabelRoot == null)
        {
            var go = new GameObject("OpeningDimensionLabel",
                typeof(RectTransform), typeof(Image));
            go.transform.SetParent(transform, false);
            _dimLabelRoot = (RectTransform)go.transform;
            _dimLabelRoot.sizeDelta = new Vector2(230f, 44f);
            var bg = go.GetComponent<Image>();
            bg.color = new Color(1f, 1f, 1f, 0.95f);
            bg.raycastTarget = false;

            var textGo = new GameObject("Text", typeof(RectTransform));
            textGo.transform.SetParent(go.transform, false);
            var rt = (RectTransform)textGo.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.sizeDelta = Vector2.zero;
            _dimLabelText = textGo.AddComponent<TMPro.TextMeshProUGUI>();
            _dimLabelText.fontSize = 24f;
            _dimLabelText.color = new Color(0.1f, 0.35f, 0.85f, 1f);
            _dimLabelText.alignment = TMPro.TextAlignmentOptions.Center;
            _dimLabelText.raycastTarget = false;
        }
        _dimLabelRoot.gameObject.SetActive(true);
        _dimLabelRoot.anchoredPosition = canvasPos;
        if (_dimLabelText.text != text)
            _dimLabelText.text = text;
    }

    private void AlignSideHandle(ScaleHandleUI handle, Vector2 cornerA, Vector2 cornerB)
    {
        if (handle == null)
            return;

        Vector2 edgeDir = (cornerB - cornerA).normalized;
        float angle = Mathf.Atan2(edgeDir.y, edgeDir.x) * Mathf.Rad2Deg;

        handle.GetComponent<RectTransform>().localRotation
            = Quaternion.Euler(0, 0, angle);
    }

    private Vector2 WorldToCanvas(Vector3 worldPos)
    {
        Vector3 screenPoint = _mainCamera.WorldToScreenPoint(worldPos);

        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            _canvasRect,
            screenPoint,
            _canvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? null
                : _canvas.worldCamera,
            out Vector2 localPoint
        );

        return localPoint;
    }

    public void SetHandleHighlight(ScaleHandleUI activeHandle)
    {
        foreach (var handle in _handles)
        {
            if (handle == null)
                continue;

            bool isActive = handle == activeHandle;
            bool isOpposite = handle.type == activeHandle.type
                && Vector3.Dot(handle.direction, activeHandle.direction) < -0.9f;

            handle.gameObject.SetActive(isActive || isOpposite);
            handle.SetHighlighted(isActive || isOpposite);
        }
    }

    public void ClearHighlights()
    {
        foreach (var handle in _handles)
        {
            if (handle == null)
                continue;

            handle.gameObject.SetActive(true);
            handle.SetHighlighted(false);
        }
    }
}
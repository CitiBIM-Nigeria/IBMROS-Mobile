using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using System;
using System.Collections.Generic;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;
using TouchPhase = UnityEngine.InputSystem.TouchPhase;

/// <summary>
/// Centralised input manager for the Room scene.
///
/// Single finger:
///   OnPointerDown / OnPointerMove / OnPointerUp / OnPointerClick
///   Used for object selection, drag, rotation handle.
///
/// Two finger:
///   OnTwoFingerPanDelta  — midpoint movement → camera walk
///   OnPinchDelta         — distance change   → available for future use
///   OnPinchStart / OnPinchEnd
///
/// Editor:
///   Left mouse  → single finger equivalent
///   Right mouse → two-finger pan equivalent  (hold RMB + drag)
///   Scroll wheel → pinch delta equivalent
/// </summary>
public class InputManager : MonoBehaviour
{
    // ---------------------------------------------------------------
    // EVENTS
    // ---------------------------------------------------------------

    // Single finger — world interaction (UI touches filtered out)
    public event Action<Vector2> OnPointerDown;
    public event Action<Vector2> OnPointerUp;
    public event Action<Vector2> OnPointerMove;
    public event Action<Vector2> OnPointerClick;

    // Two finger pan — midpoint screen-space delta
    public event Action<Vector2> OnTwoFingerPanDelta;

    // Pinch — distance delta between two fingers
    public event Action<float>   OnPinchStart;
    public event Action<float>   OnPinchDelta;
    public event Action          OnPinchEnd;

    // ---------------------------------------------------------------
    // SETTINGS
    // ---------------------------------------------------------------

    [Header("Settings")]
    [SerializeField] private float clickMoveThreshold = 10f;

    [Tooltip("Minimum pixel movement of midpoint before two-finger pan fires.")]
    [SerializeField] private float twoPanMinDelta = 2f;

    [Tooltip("Minimum pixel change in pinch distance before pinch delta fires.")]
    [SerializeField] private float pinchMinDelta = 1f;

    // ---------------------------------------------------------------
    // SINGLE TOUCH STATE
    // ---------------------------------------------------------------

    private bool    _trackingTouch       = false;
    private bool    _touchHasMoved       = false;
    private Vector2 _pointerDownPosition = Vector2.zero;

    // ---------------------------------------------------------------
    // TWO FINGER STATE
    // ---------------------------------------------------------------

    private bool    _twoFingerActive      = false;
    private Vector2 _lastTwoFingerMidpoint = Vector2.zero;
    private float   _lastPinchDistance    = 0f;

    // ---------------------------------------------------------------
    // EDITOR STATE
    // ---------------------------------------------------------------

#if UNITY_EDITOR
    private bool    _mouseDown         = false;
    private bool    _mouseHasMoved     = false;
    private Vector2 _mouseDownPosition = Vector2.zero;
    private bool    _rmbDown           = false;
    private Vector2 _lastRmbPosition   = Vector2.zero;
#endif

    // ---------------------------------------------------------------
    // UNITY LIFECYCLE
    // ---------------------------------------------------------------

    void OnEnable()  => EnhancedTouchSupport.Enable();
    void OnDisable() => EnhancedTouchSupport.Disable();

    void Update()
    {
#if UNITY_EDITOR
        HandleMouseInput();
#else
        HandleTouchInput();
#endif
    }

    // ---------------------------------------------------------------
    // TOUCH INPUT
    // ---------------------------------------------------------------

    private void HandleTouchInput()
    {
        var activeTouches = Touch.activeTouches;

        // ── TWO FINGER ───────────────────────────────────────────────
        if (activeTouches.Count >= 2)
        {
            // Cancel any active single-finger tracking
            CancelSingleTouch();

            Vector2 pos1 = activeTouches[0].screenPosition;
            Vector2 pos2 = activeTouches[1].screenPosition;

            Vector2 midpoint = (pos1 + pos2) * 0.5f;
            float   distance = Vector2.Distance(pos1, pos2);

            if (!_twoFingerActive)
            {
                // First frame — store reference values, fire pinch start
                _twoFingerActive       = true;
                _lastTwoFingerMidpoint = midpoint;
                _lastPinchDistance     = distance;
                OnPinchStart?.Invoke(distance);
            }
            else
            {
                // Pan delta — midpoint movement in screen space
                Vector2 panDelta = midpoint - _lastTwoFingerMidpoint;
                if (panDelta.magnitude > twoPanMinDelta)
                    OnTwoFingerPanDelta?.Invoke(panDelta);

                // Pinch delta — change in finger spread
                float pinchDelta = distance - _lastPinchDistance;
                if (Mathf.Abs(pinchDelta) > pinchMinDelta)
                    OnPinchDelta?.Invoke(pinchDelta);
            }

            _lastTwoFingerMidpoint = midpoint;
            _lastPinchDistance     = distance;
            return;
        }

        // ── END TWO FINGER ───────────────────────────────────────────
        if (_twoFingerActive && activeTouches.Count < 2)
        {
            _twoFingerActive = false;
            OnPinchEnd?.Invoke();
        }

        // ── NO TOUCHES ───────────────────────────────────────────────
        if (activeTouches.Count == 0)
        {
            _trackingTouch = false;
            return;
        }

        // ── SINGLE TOUCH ─────────────────────────────────────────────
        var touch = activeTouches[0];

        // Ignore touches that started over UI
        if (touch.phase == TouchPhase.Began &&
            IsPointerOverUI(touch.screenPosition))
        {
            _trackingTouch = false;
            return;
        }

        if (touch.phase == TouchPhase.Began)
        {
            _trackingTouch       = true;
            _touchHasMoved       = false;
            _pointerDownPosition = touch.screenPosition;
            OnPointerDown?.Invoke(touch.screenPosition);
        }

        if (!_trackingTouch)
            return;

        if (touch.phase == TouchPhase.Moved)
        {
            float dist = Vector2.Distance(
                touch.screenPosition, _pointerDownPosition);
            if (dist > clickMoveThreshold)
                _touchHasMoved = true;

            OnPointerMove?.Invoke(touch.screenPosition);
        }

        if (touch.phase == TouchPhase.Ended ||
            touch.phase == TouchPhase.Canceled)
        {
            if (!_touchHasMoved)
                OnPointerClick?.Invoke(touch.screenPosition);

            OnPointerUp?.Invoke(touch.screenPosition);
            _trackingTouch = false;
        }
    }

    private void CancelSingleTouch()
    {
        _trackingTouch = false;
        _touchHasMoved = false;
    }

    // ---------------------------------------------------------------
    // EDITOR MOUSE INPUT
    // ---------------------------------------------------------------

#if UNITY_EDITOR
    private void HandleMouseInput()
    {
        var mouse = Mouse.current;
        if (mouse == null) return;

        Vector2 mousePos = mouse.position.ReadValue();

        // ── LEFT MOUSE — single finger equivalent ────────────────────
        if (mouse.leftButton.wasPressedThisFrame)
        {
            if (!IsPointerOverUI(mousePos))
            {
                _mouseDown         = true;
                _mouseHasMoved     = false;
                _mouseDownPosition = mousePos;
                OnPointerDown?.Invoke(mousePos);
            }
        }

        if (_mouseDown)
        {
            if (mouse.leftButton.isPressed)
            {
                Vector2 delta = mouse.delta.ReadValue();
                if (delta.sqrMagnitude > 0.01f)
                {
                    float dist = Vector2.Distance(mousePos, _mouseDownPosition);
                    if (dist > clickMoveThreshold)
                        _mouseHasMoved = true;

                    OnPointerMove?.Invoke(mousePos);
                }
            }

            if (mouse.leftButton.wasReleasedThisFrame)
            {
                if (!_mouseHasMoved)
                    OnPointerClick?.Invoke(mousePos);

                OnPointerUp?.Invoke(mousePos);
                _mouseDown = false;
            }
        }

        // ── RIGHT MOUSE — two-finger pan equivalent ──────────────────
        if (mouse.rightButton.wasPressedThisFrame)
        {
            _rmbDown         = true;
            _lastRmbPosition = mousePos;
        }

        if (_rmbDown && mouse.rightButton.isPressed)
        {
            Vector2 rmsDelta = mousePos - _lastRmbPosition;
            if (rmsDelta.magnitude > twoPanMinDelta)
                OnTwoFingerPanDelta?.Invoke(rmsDelta);
            _lastRmbPosition = mousePos;
        }

        if (mouse.rightButton.wasReleasedThisFrame)
            _rmbDown = false;

        // ── SCROLL WHEEL — pinch equivalent ──────────────────────────
        float scroll = mouse.scroll.ReadValue().y;
        if (Mathf.Abs(scroll) > 0.01f)
            OnPinchDelta?.Invoke(scroll * 10f);
    }
#endif

    // ---------------------------------------------------------------
    // UI FILTER
    // ---------------------------------------------------------------

    private bool IsPointerOverUI(Vector2 screenPosition)
    {
        if (EventSystem.current == null) return false;

#if UNITY_EDITOR
        return EventSystem.current.IsPointerOverGameObject(-1);
#else
        var activeTouches = Touch.activeTouches;
        foreach (var touch in activeTouches)
        {
            if (EventSystem.current.IsPointerOverGameObject(touch.touchId))
                return true;
        }
        return false;
#endif
    }
}
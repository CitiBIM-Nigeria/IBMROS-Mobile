using UnityEngine;

/// <summary>
/// First-person room camera controller.
///
/// Movement sources:
///   1. Joystick          — walk forward/back/left/right at eye height
///   2. Two-finger pan    — same as joystick but driven by screen drag
///      Scroll down  → move forward
///      Scroll up    → move backward
///      Scroll left  → move right  (inverted so it feels natural)
///      Scroll right → move left
///
/// Look source:
///   Single-finger swipe via ObjectManipulator → RotateCamera()
///
/// Constraints:
///   - Y position locked to eyeHeight every frame — no floating
///   - Wall collision via SphereCast — slides along walls smoothly
///   - Vertical look clamped to maxLookUp / maxLookDown
/// </summary>
public class CameraController : MonoBehaviour
{
    // ---------------------------------------------------------------
    // DEPENDENCIES
    // ---------------------------------------------------------------

    [Header("Dependencies")]
    public JoystickController movementJoystick;

    // ---------------------------------------------------------------
    // CAMERA FEEL
    // ---------------------------------------------------------------

    [Header("Camera Feel")]
    [Tooltip("Single-finger look sensitivity. 1.5–2.5 is natural on mobile.")]
    public float rotationSpeed = 2f;

    [Tooltip("Joystick walk speed in world units per second.")]
    public float movementSpeed = 4f;

    // ---------------------------------------------------------------
    // TWO FINGER PAN
    // ---------------------------------------------------------------

    [Header("Two Finger Pan")]
    [Tooltip("How fast screen-space pan delta translates to world movement. " +
             "0.01–0.03 feels natural — tune to your room scale.")]
    public float twoFingerPanSpeed = 0.018f;

    // ---------------------------------------------------------------
    // EYE HEIGHT
    // ---------------------------------------------------------------

    [Header("Eye Height")]
    [Tooltip("Absolute world Y the camera is locked to every frame. " +
             "Set to your floor Y + ~1.65 for a standing human view.")]
    public float eyeHeight = 1.65f;

    [Tooltip("If true, eyeHeight is measured from the floor below the camera " +
             "via raycast — useful for rooms with steps. " +
             "If false, eyeHeight is an absolute world Y (recommended).")]
    public bool useFloorRelativeHeight = false;

    [Tooltip("Floor LayerMask — only needed when useFloorRelativeHeight is true.")]
    public LayerMask floorLayer;

    // ---------------------------------------------------------------
    // VERTICAL LOOK LIMITS
    // ---------------------------------------------------------------

    [Header("Vertical Look Limits")]
    [Tooltip("Max degrees to look UP. 25–30 feels like a real head tilt.")]
    public float maxLookUp = 25f;

    [Tooltip("Max degrees to look DOWN. 35–45 lets you see the floor nearby.")]
    public float maxLookDown = 40f;

    // ---------------------------------------------------------------
    // WALL COLLISION
    // ---------------------------------------------------------------

    [Header("Wall Collision")]
    [Tooltip("LayerMask containing your wall objects.")]
    public LayerMask wallLayer;

    [Tooltip("Camera stops this many units from wall surfaces. Min 0.15.")]
    public float wallClearance = 0.25f;

    [Tooltip("Sphere radius for wall probe. Should be >= wallClearance.")]
    public float collisionRadius = 0.3f;

    // ---------------------------------------------------------------
    // ROOM BOUNDS FALLBACK
    // ---------------------------------------------------------------

    [Header("Room Bounds (Optional Fallback)")]
    [Tooltip("If assigned, hard-clamps camera inside this box. " +
             "Use while wall colliders are not yet in place.")]
    public BoxCollider roomBoundsCollider;

    [Tooltip("Inward margin from box edges.")]
    public float roomBoundsMargin = 0.3f;

    // ---------------------------------------------------------------
    // PRIVATE STATE
    // ---------------------------------------------------------------

    private float _yaw   = 0f;
    private float _pitch = 0f;

    // ---------------------------------------------------------------
    // UNITY LIFECYCLE
    // ---------------------------------------------------------------

    void Start()
    {
        Vector3 angles = transform.eulerAngles;
        _yaw   = angles.y;
        _pitch = NormalisePitch(angles.x);
        LockEyeHeight();
    }

    void Update()
    {
        ApplyRotation();
        HandleJoystickMovement();
        LockEyeHeight();         // Always last — nothing overrides Y
    }

    // ---------------------------------------------------------------
    // PUBLIC — ROTATION (called by ObjectManipulator on single-finger drag)
    // ---------------------------------------------------------------

    public void RotateCamera(Vector2 screenDelta)
    {
        _yaw   -= screenDelta.x * rotationSpeed * 0.1f;
        _pitch -= screenDelta.y * rotationSpeed * 0.1f;
        _pitch  = Mathf.Clamp(_pitch, -maxLookUp, maxLookDown);
    }

    // ---------------------------------------------------------------
    // PUBLIC — TWO FINGER PAN (called by ObjectManipulator)
    //
    // Screen-space convention (matches user description):
    //   Scroll down  (negative Y) → move forward
    //   Scroll up    (positive Y) → move backward
    //   Scroll left  (negative X) → move right
    //   Scroll right (positive X) → move left
    // ---------------------------------------------------------------

    public void HandleTwoFingerPan(Vector2 screenDelta)
    {
        // Build flat directional basis from current yaw only
        // so panning never changes camera height
        Quaternion yawRot = Quaternion.Euler(0f, _yaw, 0f);
        Vector3 forward   = yawRot * Vector3.forward;
        Vector3 right     = yawRot * Vector3.right;

        // Invert both axes to match the natural "drag to move" feel:
        //   dragging two fingers down = camera moves forward
        //   dragging two fingers left = camera moves right
        Vector3 desiredDelta = (-right   * screenDelta.x
                               + forward * screenDelta.y)
                               * twoFingerPanSpeed;

        Vector3 safeDelta = ResolveWallCollision(desiredDelta);
        transform.position += safeDelta;

        if (roomBoundsCollider != null)
            ClampInsideRoomBounds();

        // Y is corrected by LockEyeHeight() at the end of Update
    }

    // ---------------------------------------------------------------
    // PRIVATE — JOYSTICK MOVEMENT
    // ---------------------------------------------------------------

    private void HandleJoystickMovement()
    {
        if (movementJoystick == null) return;

        Vector2 input = movementJoystick.InputVector;
        if (input.sqrMagnitude <= 0.01f) return;

        Quaternion yawRot = Quaternion.Euler(0f, _yaw, 0f);
        Vector3 forward   = yawRot * Vector3.forward;
        Vector3 right     = yawRot * Vector3.right;

        Vector3 moveDir      = (forward * input.y + right * input.x).normalized;
        Vector3 desiredDelta = moveDir * movementSpeed * Time.deltaTime;

        Vector3 safeDelta = ResolveWallCollision(desiredDelta);
        transform.position += safeDelta;

        if (roomBoundsCollider != null)
            ClampInsideRoomBounds();
    }

    // ---------------------------------------------------------------
    // PRIVATE — ROTATION APPLICATION
    // ---------------------------------------------------------------

    private void ApplyRotation()
    {
        transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
    }

    // ---------------------------------------------------------------
    // PRIVATE — WALL COLLISION
    //
    // SphereCast in the movement direction.
    // On hit: tries to slide along the wall surface.
    // If slide also blocked: moves only as far as safe.
    // ---------------------------------------------------------------

    private Vector3 ResolveWallCollision(Vector3 desiredDelta)
    {
        if (desiredDelta == Vector3.zero) return Vector3.zero;

        Vector3 origin    = transform.position;
        Vector3 direction = desiredDelta.normalized;
        float   distance  = desiredDelta.magnitude;

        if (Physics.SphereCast(
                origin, collisionRadius, direction,
                out RaycastHit hit,
                distance + wallClearance,
                wallLayer))
        {
            float safeDistance = Mathf.Max(0f, hit.distance - wallClearance);

            // Try sliding along the wall surface
            Vector3 slide = Vector3.ProjectOnPlane(desiredDelta, hit.normal);

            if (slide.sqrMagnitude > 0.001f)
            {
                Vector3 slideDir = slide.normalized;

                if (!Physics.SphereCast(
                        origin, collisionRadius, slideDir,
                        out _,
                        slide.magnitude + wallClearance,
                        wallLayer))
                {
                    return slide;  // Slide is clear
                }
            }

            // Both directions blocked — advance only to safe distance
            return direction * safeDistance;
        }

        return desiredDelta;  // Path clear
    }

    // ---------------------------------------------------------------
    // PRIVATE — EYE HEIGHT LOCK
    // ---------------------------------------------------------------

    private void LockEyeHeight()
    {
        Vector3 pos = transform.position;

        if (useFloorRelativeHeight)
        {
            if (Physics.Raycast(
                    new Vector3(pos.x, pos.y + 1f, pos.z),
                    Vector3.down,
                    out RaycastHit hit,
                    10f,
                    floorLayer))
            {
                pos.y = hit.point.y + eyeHeight;
            }
            else
            {
                pos.y = eyeHeight;  // Fallback
            }
        }
        else
        {
            pos.y = eyeHeight;
        }

        transform.position = pos;
    }

    // ---------------------------------------------------------------
    // PRIVATE — ROOM BOUNDS CLAMP
    // ---------------------------------------------------------------

    private void ClampInsideRoomBounds()
    {
        Vector3 localPos = roomBoundsCollider.transform
            .InverseTransformPoint(transform.position);

        Vector3 halfSize = roomBoundsCollider.size * 0.5f
                         - Vector3.one * roomBoundsMargin;
        Vector3 center   = roomBoundsCollider.center;

        localPos.x = Mathf.Clamp(
            localPos.x, center.x - halfSize.x, center.x + halfSize.x);
        localPos.z = Mathf.Clamp(
            localPos.z, center.z - halfSize.z, center.z + halfSize.z);
        // Y handled by LockEyeHeight — do not clamp here

        transform.position = roomBoundsCollider.transform
            .TransformPoint(localPos);
    }

    // ---------------------------------------------------------------
    // PRIVATE — UTILITY
    // ---------------------------------------------------------------

    private float NormalisePitch(float angle)
    {
        if (angle > 180f) angle -= 360f;
        return Mathf.Clamp(angle, -maxLookUp, maxLookDown);
    }
}
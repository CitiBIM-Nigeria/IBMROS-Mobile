using System.Collections.Generic;
using Exoa.Cameras;
using IBMROS.Designer.Plan;
using UnityEngine;

namespace IBMROS.Designer.ThreeD
{
    /// <summary>
    /// Self-contained first-person walkthrough for RoomDesigner.unity.
    ///
    /// Camera handoff (INTEGRATION_MAP §6): CameraModeSwitcher.LateUpdate
    /// overwrites the camera transform AND projectionMatrix every frame, so
    /// entering walkthrough disables the switcher + both mode cameras, resets
    /// the blended projection matrix, and takes the transform over; exiting
    /// re-enables them (the switcher immediately restores the orbit pose from
    /// its own serialized spring state).
    ///
    /// Movement is deliberately dependency-free so it works tonight without the
    /// legacy Room scene's joystick canvas:
    ///   • touch: left half of the screen = move stick, right half = look drag
    ///   • editor: WASD/arrows move, hold left mouse to look
    /// Collision: sphere-cast slide against Wall/ExteriorWall colliders (the
    /// generated meshes carry MeshColliders since the Phase-1 prefab fix).
    /// </summary>
    public sealed class WalkthroughController : MonoBehaviour
    {
        private const float EYE_HEIGHT_M = 1.6f;
        private const float MOVE_SPEED_MPS = 1.8f;
        private const float LOOK_DEG_PER_PX = 0.38f;
        private const float LOOK_SMOOTH_LAMBDA = 9f;   // higher = snappier stop
        private const float LOOK_INERTIA_LAMBDA = 4f;  // glide-out after release
        private const float BODY_RADIUS_M = 0.28f;
        private const float NEAR_PLANE_M = 0.12f;
        private const float PITCH_MIN = -75f, PITCH_MAX = 75f;
        // Optics copied from the Room scene's hand-tuned camera (physical
        // Super-35 @ 18 mm, gate fit horizontal ⇒ wide interior view).
        private static readonly Vector2 SENSOR_SIZE = new Vector2(24.89f, 18.66f);
        private const float FOCAL_LENGTH_MM = 18f;

        private Camera cam;
        private CameraModeSwitcher switcher;
        private MonoBehaviour ortho, persp; // CameraTopDownOrtho / CameraPerspective

        // On-screen joystick (legacy Room rig, transplanted): when present it
        // drives movement and this controller owns its canvas visibility.
        private JoystickController joystick;
        private GameObject joystickCanvas;

        public bool Active { get; private set; }

        private float yaw, pitch;
        private Vector2 lookVelocity; // deg/s, smoothed + inertial
        private int wallMask;

        // touch bookkeeping
        private int moveFingerId = -1, lookFingerId = -1;
        private Vector2 moveOrigin, lookLast;

        private void Awake()
        {
            wallMask = LayerMask.GetMask("Wall", "ExteriorWall");
            joystick = FindAnyObjectByType<JoystickController>(FindObjectsInactive.Include);
            if (joystick != null)
            {
                Canvas c = joystick.GetComponentInParent<Canvas>(true);
                joystickCanvas = c != null ? c.gameObject : joystick.gameObject;
            }
        }

        public void Enter()
        {
            if (Active)
                return;

            cam = Camera.main;
            if (cam == null)
                return;
            switcher = cam.GetComponent<CameraModeSwitcher>();
            ortho = cam.GetComponent<CameraTopDownOrtho>();
            persp = cam.GetComponent<CameraPerspective>();

            if (switcher != null) switcher.enabled = false;
            if (ortho != null) ortho.enabled = false;
            if (persp != null) persp.enabled = false;

            cam.orthographic = false;
            cam.ResetProjectionMatrix(); // clear the switcher's blended matrix
            cam.nearClipPlane = NEAR_PLANE_M;
            cam.usePhysicalProperties = true;
            cam.sensorSize = SENSOR_SIZE;
            cam.focalLength = FOCAL_LENGTH_MM;
            cam.gateFit = Camera.GateFitMode.Horizontal;

            Vector3 spawn = FindSpawnPoint(out float spawnYaw);
            cam.transform.position = new Vector3(spawn.x, EYE_HEIGHT_M, spawn.z);
            yaw = spawnYaw;
            pitch = 4f; // slight downward glance, like the reference
            lookVelocity = Vector2.zero;
            cam.transform.rotation = Quaternion.Euler(pitch, yaw, 0f);

            moveFingerId = lookFingerId = -1;
            if (joystickCanvas != null)
                joystickCanvas.SetActive(true);
            if (joystick != null && joystick.background != null)
            {
                // Bottom-center, floated ABOVE the HUD toolbar — it previously
                // overlapped the Add Furniture button, which both looked wrong
                // and swallowed the joystick's pointer events (reported broken).
                RectTransform bg = joystick.background;
                bg.anchorMin = bg.anchorMax = new Vector2(0.5f, 0f);
                bg.pivot = new Vector2(0.5f, 0.5f);
                bg.anchoredPosition = new Vector2(0f, 420f);
            }
            Active = true;
            DesignerModeController.Instance?.NotifyWalkthrough(true);
        }

        public void Exit()
        {
            if (!Active)
                return;
            Active = false;
            if (joystickCanvas != null)
                joystickCanvas.SetActive(false);
            if (cam != null)
                cam.usePhysicalProperties = false; // hand plain projection back to the rig

            // Re-enable the rig; its LateUpdate restores the orbit pose/matrix.
            if (ortho != null) ortho.enabled = true;
            if (persp != null) persp.enabled = true;
            if (switcher != null) switcher.enabled = true;

            DesignerModeController.Instance?.NotifyWalkthrough(false);
        }

        private void Update()
        {
            if (!Active || cam == null)
                return;

            Vector2 moveInput = Vector2.zero; // x = strafe, y = forward
            Vector2 lookDelta = Vector2.zero;

            bool joystickDriving = joystick != null && joystickCanvas != null &&
                                   joystickCanvas.activeInHierarchy;
            if (joystickDriving)
                moveInput = joystick.InputVector;

            if (Input.touchCount > 0)
            {
                if (joystickDriving)
                    ReadLookOnlyTouches(ref lookDelta); // joystick owns movement
                else
                    ReadTouches(ref moveInput, ref lookDelta);
            }
            else
            {
                moveInput.x = (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow) ? 1f : 0f)
                            - (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow) ? 1f : 0f);
                moveInput.y = (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow) ? 1f : 0f)
                            - (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow) ? 1f : 0f);
                if (Input.GetMouseButton(0) && !PointerOverUi())
                    lookDelta = new Vector2(Input.GetAxis("Mouse X") * 10f, Input.GetAxis("Mouse Y") * 10f);
            }

            // Smoothed, inertial look: input drives a velocity that eases toward
            // the target while dragging and glides out after release (the
            // "keeps moving a little, then settles" feel of the reference app).
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            bool hasInput = lookDelta.sqrMagnitude > 1e-6f;
            if (hasInput)
            {
                Vector2 target = lookDelta * (LOOK_DEG_PER_PX / dt);
                lookVelocity = Vector2.Lerp(lookVelocity, target,
                    1f - Mathf.Exp(-LOOK_SMOOTH_LAMBDA * dt));
            }
            else
            {
                lookVelocity *= Mathf.Exp(-LOOK_INERTIA_LAMBDA * dt);
                if (lookVelocity.sqrMagnitude < 0.5f)
                    lookVelocity = Vector2.zero;
            }
            yaw += lookVelocity.x * dt;
            pitch = Mathf.Clamp(pitch - lookVelocity.y * dt, PITCH_MIN, PITCH_MAX);
            cam.transform.rotation = Quaternion.Euler(pitch, yaw, 0f);

            if (moveInput.sqrMagnitude > 1e-4f)
            {
                moveInput = Vector2.ClampMagnitude(moveInput, 1f);
                Quaternion flatYaw = Quaternion.Euler(0f, yaw, 0f);
                Vector3 wish = flatYaw * new Vector3(moveInput.x, 0f, moveInput.y)
                               * (MOVE_SPEED_MPS * Time.deltaTime);
                cam.transform.position = SlideMove(cam.transform.position, wish);
            }
        }

        // ------------------------------------------------------------------ input

        private void ReadTouches(ref Vector2 move, ref Vector2 look)
        {
            for (int i = 0; i < Input.touchCount; i++)
            {
                Touch t = Input.GetTouch(i);
                bool leftHalf = t.position.x < Screen.width * 0.5f;

                switch (t.phase)
                {
                    case TouchPhase.Began:
                        if (PointerOverUi(t.fingerId))
                            break;
                        if (leftHalf && moveFingerId < 0)
                        {
                            moveFingerId = t.fingerId;
                            moveOrigin = t.position;
                        }
                        else if (!leftHalf && lookFingerId < 0)
                        {
                            lookFingerId = t.fingerId;
                            lookLast = t.position;
                        }
                        break;

                    case TouchPhase.Ended:
                    case TouchPhase.Canceled:
                        if (t.fingerId == moveFingerId) moveFingerId = -1;
                        if (t.fingerId == lookFingerId) lookFingerId = -1;
                        break;
                }

                if (t.fingerId == moveFingerId)
                {
                    Vector2 d = t.position - moveOrigin;
                    float radius = Screen.dpi > 0f ? Screen.dpi * 0.5f : 220f;
                    move = Vector2.ClampMagnitude(d / radius, 1f);
                }
                else if (t.fingerId == lookFingerId &&
                         (t.phase == TouchPhase.Moved || t.phase == TouchPhase.Stationary))
                {
                    look += t.position - lookLast;
                    lookLast = t.position;
                }
            }
        }

        /// <summary>All non-UI touches steer the view; the uGUI joystick owns movement
        /// (its touches register as over-UI, so they never leak into look).</summary>
        private void ReadLookOnlyTouches(ref Vector2 look)
        {
            for (int i = 0; i < Input.touchCount; i++)
            {
                Touch t = Input.GetTouch(i);
                switch (t.phase)
                {
                    case TouchPhase.Began:
                        if (lookFingerId < 0 && !PointerOverUi(t.fingerId))
                        {
                            lookFingerId = t.fingerId;
                            lookLast = t.position;
                        }
                        break;
                    case TouchPhase.Ended:
                    case TouchPhase.Canceled:
                        if (t.fingerId == lookFingerId) lookFingerId = -1;
                        break;
                }
                if (t.fingerId == lookFingerId &&
                    (t.phase == TouchPhase.Moved || t.phase == TouchPhase.Stationary))
                {
                    look += t.position - lookLast;
                    lookLast = t.position;
                }
            }
        }

        private static bool PointerOverUi(int? fingerId = null)
        {
            var es = UnityEngine.EventSystems.EventSystem.current;
            if (es == null) return false;
            return fingerId.HasValue ? es.IsPointerOverGameObject(fingerId.Value)
                                     : es.IsPointerOverGameObject();
        }

        // ------------------------------------------------------------------ movement

        /// <summary>Sphere-cast slide against walls (two deflection passes).</summary>
        private Vector3 SlideMove(Vector3 pos, Vector3 wish)
        {
            for (int pass = 0; pass < 2 && wish.sqrMagnitude > 1e-8f; pass++)
            {
                float dist = wish.magnitude;
                if (!Physics.SphereCast(pos, BODY_RADIUS_M, wish.normalized, out RaycastHit hit,
                        dist + 0.01f, wallMask, QueryTriggerInteraction.Ignore))
                {
                    pos += wish;
                    break;
                }
                float allowed = Mathf.Max(0f, hit.distance - 0.01f);
                pos += wish.normalized * allowed;
                Vector3 remainder = wish.normalized * (dist - allowed);
                wish = Vector3.ProjectOnPlane(remainder, hit.normal); // slide along the wall
            }
            pos.y = EYE_HEIGHT_M;
            return pos;
        }

        /// <summary>
        /// Spawn like the reference app: just inside the DOOR, looking across
        /// the room — the whole space is in view instead of a nearby wall.
        /// Fallback: largest room's centroid facing its longest horizontal span.
        /// </summary>
        private static Vector3 FindSpawnPoint(out float yawDeg)
        {
            // Largest room centroid (always needed as the look-at).
            float bestArea = -1f;
            Vector2 centroidBest = Vector2.zero;
            foreach (var ui in PlanEditorUtil.AllSpaces())
            {
                List<Vector3> pts = PlanEditorUtil.WorldPoints(ui);
                if (pts.Count < 3)
                    continue;
                float area2 = 0f;
                var centroid = Vector2.zero;
                for (int i = 0; i < pts.Count; i++)
                {
                    Vector2 a = PlanEditorUtil.WorldToMeters(pts[i]);
                    Vector2 b = PlanEditorUtil.WorldToMeters(pts[(i + 1) % pts.Count]);
                    area2 += a.x * b.y - b.x * a.y;
                    centroid += a;
                }
                float area = Mathf.Abs(area2) * 0.5f;
                if (area > bestArea)
                {
                    bestArea = area;
                    centroidBest = centroid / pts.Count;
                }
            }

            // Door position from the document, if any.
            Vector2? door = null;
            foreach (var ui in UnityEngine.Object.FindObjectsByType<Exoa.Designer.UIBaseItem>(FindObjectsSortMode.None))
            {
                if (ui.sequencingItemType != Exoa.Designer.DataModel.FloorMapItemType.Door || ui.cpc == null)
                    continue;
                List<Vector3> pts = ui.cpc.GetPointsWorldPositionList();
                if (pts != null && pts.Count > 0)
                {
                    door = new Vector2(pts[0].x, pts[0].z);
                    break;
                }
            }

            Vector2 eye;
            if (door.HasValue && (centroidBest - door.Value).sqrMagnitude > 0.25f)
                eye = door.Value + (centroidBest - door.Value).normalized * 0.7f; // step inside
            else
                eye = centroidBest;

            Vector2 lookDir = centroidBest - eye;
            if (lookDir.sqrMagnitude < 0.04f)
                lookDir = Vector2.up;
            yawDeg = Mathf.Atan2(lookDir.x, lookDir.y) * Mathf.Rad2Deg;
            return new Vector3(eye.x, 0f, eye.y);
        }
    }
}

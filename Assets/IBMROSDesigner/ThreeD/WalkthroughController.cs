using Exoa.Cameras;
using IBMROS.Designer.Plan;
using UnityEngine;

namespace IBMROS.Designer.ThreeD
{
    /// <summary>
    /// First-person mode handoff for RoomDesigner.unity.
    ///
    /// This class owns NO input. Movement, look and input arbitration are the
    /// legacy Room-scene stack, reused verbatim:
    ///   JoystickController → CameraController.HandleJoystickMovement (walk)
    ///   InputManager → ObjectManipulator → CameraController.RotateCamera (look)
    ///   ObjectManipulator priority: scale handle > furniture drag > camera
    /// Rolling our own touch reading here is what previously let one drag both
    /// move furniture AND pan the camera, and left the joystick inert.
    ///
    /// What this does own is the camera handoff (INTEGRATION_MAP §6):
    /// CameraModeSwitcher.LateUpdate overwrites transform AND projectionMatrix
    /// every frame, so entering disables the switcher + both Exoa mode cameras,
    /// resets the blended projection, applies the Room-scene optics, seats the
    /// camera inside the door, and enables CameraController. Exiting reverses it.
    /// </summary>
    public sealed class WalkthroughController : MonoBehaviour
    {
        private const float EYE_HEIGHT_M = 1.65f;
        private const float NEAR_PLANE_M = 0.12f;
        // Optics copied from the Room scene's hand-tuned camera (physical
        // Super-35 @ 18 mm, gate fit horizontal ⇒ wide interior view).
        private static readonly Vector2 SENSOR_SIZE = new Vector2(24.89f, 18.66f);
        private const float FOCAL_LENGTH_MM = 18f;

        private Camera cam;
        private CameraModeSwitcher switcher;
        private MonoBehaviour ortho, persp; // CameraTopDownOrtho / CameraPerspective
        private CameraController fpsController;
        private JoystickController joystick;
        private GameObject joystickCanvas;

        public bool Active { get; private set; }

        private void Awake()
        {
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
            fpsController = cam.GetComponent<CameraController>();

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
            cam.transform.SetPositionAndRotation(
                new Vector3(spawn.x, EYE_HEIGHT_M, spawn.z),
                Quaternion.Euler(4f, spawnYaw, 0f));

            // CameraController reads its yaw/pitch from the transform in Start,
            // so it must be enabled AFTER the pose is seated. Re-seat on every
            // entry (it only runs Start once).
            if (fpsController != null)
            {
                fpsController.eyeHeight = EYE_HEIGHT_M;
                fpsController.enabled = true;
                fpsController.SyncFromTransform();
            }

            Active = true; // before SetJoystickVisible — it gates on Active
            SetJoystickVisible(true);
            DesignerModeController.Instance?.NotifyWalkthrough(true);
        }

        public void Exit()
        {
            if (!Active)
                return;
            if (joystickCanvas != null)
                joystickCanvas.SetActive(false);
            Active = false;
            if (fpsController != null)
                fpsController.enabled = false;
            if (cam != null)
                cam.usePhysicalProperties = false; // hand plain projection back to the rig

            // Re-enable the rig; its LateUpdate restores the orbit pose/matrix.
            if (ortho != null) ortho.enabled = true;
            if (persp != null) persp.enabled = true;
            if (switcher != null) switcher.enabled = true;

            DesignerModeController.Instance?.NotifyWalkthrough(false);
        }

        /// <summary>Joystick belongs to walkthrough; furnish mode hides it too.</summary>
        public void SetJoystickVisible(bool visible)
        {
            if (joystickCanvas != null)
                joystickCanvas.SetActive(visible && Active);
        }

        // ------------------------------------------------------------------ spawn

        /// <summary>
        /// Spawn like the reference app: just inside the DOOR, looking across
        /// the room — the whole space is in view instead of a nearby wall.
        /// Fallback: largest room's centroid.
        /// </summary>
        private static Vector3 FindSpawnPoint(out float yawDeg)
        {
            float bestArea = -1f;
            Vector2 centroidBest = Vector2.zero;
            foreach (var ui in PlanEditorUtil.AllSpaces())
            {
                var pts = PlanEditorUtil.WorldPoints(ui);
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

            Vector2? door = null;
            foreach (var ui in FindObjectsByType<Exoa.Designer.UIBaseItem>(FindObjectsSortMode.None))
            {
                if (ui.sequencingItemType != Exoa.Designer.DataModel.FloorMapItemType.Door || ui.cpc == null)
                    continue;
                var pts = ui.cpc.GetPointsWorldPositionList();
                if (pts != null && pts.Count > 0)
                {
                    door = new Vector2(pts[0].x, pts[0].z);
                    break;
                }
            }

            Vector2 eye = door.HasValue && (centroidBest - door.Value).sqrMagnitude > 0.25f
                ? door.Value + (centroidBest - door.Value).normalized * 0.9f
                : centroidBest;

            Vector2 lookDir = centroidBest - eye;
            if (lookDir.sqrMagnitude < 0.04f)
                lookDir = Vector2.up;
            yawDeg = Mathf.Atan2(lookDir.x, lookDir.y) * Mathf.Rad2Deg;
            return new Vector3(eye.x, 0f, eye.y);
        }
    }
}

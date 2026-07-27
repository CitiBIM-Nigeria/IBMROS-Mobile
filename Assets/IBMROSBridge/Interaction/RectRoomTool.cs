using Exoa.Designer;
using IBMROS.Core;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace IBMROS.Bridge.Interaction
{
    /// <summary>
    /// P3.1 — rectangular-room quick tool. Tap the "▭ Room" button to arm it, then tap
    /// two opposite corners on the floor plane; a complete 4-point room is created in one
    /// undo step via the A3 gateway (FloorPlanEditor.CreateRectRoomFromCorners) — no more
    /// point-by-point drawing for the ~80% case that is a plain rectangle. Corners snap to
    /// the editor grid; a rubber-band outline previews the rectangle before the 2nd tap.
    /// Stays armed for consecutive rooms; tap the button again (or Esc) to exit.
    ///
    /// Runtime-built UI + self-bootstrapping like the other bridge tools; rides entirely
    /// on the existing gateway and grid (no engine changes).
    /// </summary>
    public sealed class RectRoomTool : MonoBehaviour
    {
        public static RectRoomTool Instance { get; private set; }
        public bool IsActive { get; private set; }

        private const float TAP_MAX_MOVE_PIXELS = 12f;
        private const float TAP_MAX_SECONDS = 0.35f;

        private Camera cam;
        private Exoa.Designer.Grid grid;
        private bool haveFirst;
        private Vector3 firstCorner;
        private Vector3 pointerDownPos;
        private float pointerDownTime;
        private bool pointerActive;

        private Canvas canvas;
        private Image buttonImage;
        private TextMeshProUGUI hint;
        private LineRenderer preview;

        private static readonly Color ArmedColor = new Color(0.20f, 0.55f, 0.30f, 0.96f);
        private static readonly Color IdleColor = new Color(0.11f, 0.12f, 0.16f, 0.94f);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            TryInstall();
            SceneManager.sceneLoaded += (s, m) => TryInstall();
        }

        private static void TryInstall()
        {
            if (Instance != null)
                return;
            if (GameObject.FindAnyObjectByType<FloorMapSerializer>() == null)
                return;
            AppController app = GameObject.FindAnyObjectByType<AppController>();
            if (app == null || app.currentState == AppController.States.PlayMode)
                return;
            new GameObject("IBMROS_RectRoomTool").AddComponent<RectRoomTool>();
        }

        private void Awake()
        {
            Instance = this;
            BuildUi();
        }

        private void OnDisable()
        {
            if (Instance == this)
                Instance = null;
        }

        // ------------------------------------------------------------------ public API

        public void SetActive(bool active)
        {
            IsActive = active;
            haveFirst = false;
            if (buttonImage != null) buttonImage.color = active ? ArmedColor : IdleColor;
            if (hint != null) hint.gameObject.SetActive(active);
            if (hint != null) hint.text = "Tap the first corner";
            if (preview != null) preview.enabled = false;
        }

        public void Toggle() => SetActive(!IsActive);

        /// <summary>
        /// Commits a rectangle from two world-space corners (used by the interactive path
        /// and by tests). Returns the new room id, or null if too small.
        /// </summary>
        public string Commit(Vector3 cornerAWorld, Vector3 cornerBWorld)
        {
            return FloorPlanEditor.CreateRectRoomFromCorners(
                new Vector2(cornerAWorld.x, cornerAWorld.z),
                new Vector2(cornerBWorld.x, cornerBWorld.z));
        }

        // ------------------------------------------------------------------ interaction

        private void Update()
        {
            if (!IsActive)
                return;

#if UNITY_EDITOR
            if (Input.GetKeyDown(KeyCode.Escape)) { SetActive(false); return; }
#endif
            UpdatePreview();
            HandleTaps();
        }

        private void HandleTaps()
        {
            if (Input.GetMouseButtonDown(0))
            {
                pointerActive = true;
                pointerDownPos = Input.mousePosition;
                pointerDownTime = Time.time;
            }
            else if (Input.GetMouseButtonUp(0) && pointerActive)
            {
                pointerActive = false;
                float moved = (Input.mousePosition - pointerDownPos).magnitude;
                float held = Time.time - pointerDownTime;
                if (moved > TAP_MAX_MOVE_PIXELS || held > TAP_MAX_SECONDS)
                    return; // a drag (camera move), not a placement tap
                if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                    return;
                Vector3 world;
                if (!GroundPoint(Input.mousePosition, out world))
                    return;
                world = Snap(world);
                if (!haveFirst)
                {
                    firstCorner = world;
                    haveFirst = true;
                    if (hint != null) hint.text = "Tap the opposite corner";
                }
                else
                {
                    string id = Commit(firstCorner, world);
                    haveFirst = false;
                    if (hint != null) hint.text = id != null ? "Room created — tap the first corner" : "Too small — tap the first corner";
                    if (preview != null) preview.enabled = false;
                }
            }
        }

        private void UpdatePreview()
        {
            if (!haveFirst)
            {
                if (preview != null) preview.enabled = false;
                return;
            }
            Vector3 cur;
            if (!GroundPoint(Input.mousePosition, out cur))
                return;
            cur = Snap(cur);
            EnsurePreview();
            preview.enabled = true;
            float y = 0.02f;
            preview.SetPosition(0, new Vector3(firstCorner.x, y, firstCorner.z));
            preview.SetPosition(1, new Vector3(cur.x, y, firstCorner.z));
            preview.SetPosition(2, new Vector3(cur.x, y, cur.z));
            preview.SetPosition(3, new Vector3(firstCorner.x, y, cur.z));
            preview.SetPosition(4, new Vector3(firstCorner.x, y, firstCorner.z));
        }

        private bool GroundPoint(Vector3 screenPos, out Vector3 world)
        {
            world = default;
            if (cam == null)
                cam = Camera.main != null ? Camera.main : GameObject.FindAnyObjectByType<Camera>();
            if (cam == null)
                return false;
            Ray ray = cam.ScreenPointToRay(screenPos);
            Plane ground = new Plane(Vector3.up, Vector3.zero);
            float enter;
            if (ground.Raycast(ray, out enter))
            {
                world = ray.GetPoint(enter);
                return true;
            }
            return false;
        }

        private Vector3 Snap(Vector3 world)
        {
            if (grid == null)
                grid = GameObject.FindAnyObjectByType<Exoa.Designer.Grid>();
            return grid != null ? grid.GetNearestPointOnGrid(world) : world;
        }

        private void EnsurePreview()
        {
            if (preview != null)
                return;
            GameObject go = new GameObject("IBMROS_RectPreview");
            preview = go.AddComponent<LineRenderer>();
            preview.positionCount = 5;
            preview.loop = false;
            preview.widthMultiplier = 0.04f;
            preview.useWorldSpace = true;
            preview.material = new Material(Shader.Find("Sprites/Default"));
            preview.startColor = preview.endColor = new Color(0.2f, 0.8f, 1f, 1f);
            preview.numCornerVertices = 2;
        }

        // ------------------------------------------------------------------ UI

        private void BuildUi()
        {
            canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 505;
            CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;
            gameObject.AddComponent<GraphicRaycaster>();

            // Button, bottom-right.
            GameObject go = new GameObject("RectRoomButton", typeof(RectTransform));
            RectTransform rt = (RectTransform)go.transform;
            rt.SetParent(transform, false);
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(1f, 0f);
            rt.sizeDelta = new Vector2(230f, 110f);
            rt.anchoredPosition = new Vector2(-24f, 150f); // above the bottom-right corner
            buttonImage = go.AddComponent<Image>();
            buttonImage.color = IdleColor;
            Button button = go.AddComponent<Button>();
            button.targetGraphic = buttonImage;
            button.onClick.AddListener(Toggle);

            GameObject textGo = new GameObject("Label", typeof(RectTransform));
            RectTransform textRt = (RectTransform)textGo.transform;
            textRt.SetParent(rt, false);
            textRt.anchorMin = Vector2.zero; textRt.anchorMax = Vector2.one;
            textRt.offsetMin = textRt.offsetMax = Vector2.zero;
            TextMeshProUGUI label = textGo.AddComponent<TextMeshProUGUI>();
            label.text = "Rect Room";
            label.fontSize = 38f;
            label.color = Color.white;
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;

            // Hint, top-centre.
            GameObject hintGo = new GameObject("Hint", typeof(RectTransform));
            RectTransform hintRt = (RectTransform)hintGo.transform;
            hintRt.SetParent(transform, false);
            hintRt.anchorMin = hintRt.anchorMax = new Vector2(0.5f, 1f);
            hintRt.pivot = new Vector2(0.5f, 1f);
            hintRt.sizeDelta = new Vector2(700f, 80f);
            hintRt.anchoredPosition = new Vector2(0f, -140f);
            hint = hintGo.AddComponent<TextMeshProUGUI>();
            hint.text = "Tap the first corner";
            hint.fontSize = 36f;
            hint.color = new Color(0.7f, 0.9f, 1f, 1f);
            hint.alignment = TextAlignmentOptions.Center;
            hint.raycastTarget = false;
            hintGo.SetActive(false);
        }
    }
}

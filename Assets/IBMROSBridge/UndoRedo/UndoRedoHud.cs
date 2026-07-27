using Exoa.Designer;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace IBMROS.Bridge.UndoRedo
{
    /// <summary>
    /// IBMROS P2 — touch-first Undo/Redo controls (primary interaction per the mobile-first
    /// mandate; the keyboard shortcuts in UndoRedoService are editor-testing conveniences).
    ///
    /// Builds its own screen-space overlay canvas at runtime, so FloorMapEditor.unity is
    /// never edited (R3/R6). Two thumb-sized buttons stack vertically at the RIGHT EDGE,
    /// vertically centred (owner feedback 2026-07-18: top-centre collided with the Exoa
    /// menus/dialogs), grey out with history state via UndoRedoButton, and hide during
    /// building preview. Deliberately framework-light: when the P2 UI evaluation lands
    /// on a long-term approach, this HUD is a single file to restyle or replace.
    /// </summary>
    public class UndoRedoHud : MonoBehaviour
    {
        // Reference resolution 1080x1920; 150px ≈ 50dp on a typical 2.75x-density phone.
        private const float BUTTON_WIDTH = 150f;
        private const float BUTTON_HEIGHT = 110f;
        private const float BUTTON_GAP = 14f;
        private const float EDGE_MARGIN = 16f;

        private static UndoRedoHud instance;

        private Canvas canvas;
        private RectTransform container;
        private float lastSafeAreaInset = -1f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            TryInstall();
            SceneManager.sceneLoaded += (scene, mode) => TryInstall();
        }

        private static void TryInstall()
        {
            if (instance != null)
                return;
            if (GameObject.FindAnyObjectByType<FloorMapSerializer>() == null)
                return;
            AppController app = GameObject.FindAnyObjectByType<AppController>();
            if (app == null || app.currentState == AppController.States.PlayMode)
                return;
            GameObject go = new GameObject("IBMROS_UndoRedoHud");
            go.AddComponent<UndoRedoHud>();
        }

        private void Awake()
        {
            instance = this;
            BuildUi();
        }

        private void OnEnable()
        {
            AppController.OnAppStateChange += OnAppStateChange;
        }

        private void OnDisable()
        {
            AppController.OnAppStateChange -= OnAppStateChange;
            if (instance == this)
                instance = null;
        }

        private void OnAppStateChange(AppController.States state)
        {
            // Editing states only; hidden while previewing the full building or playing.
            bool visible = state == AppController.States.Idle || state == AppController.States.Draw;
            if (canvas != null)
                canvas.enabled = visible;
        }

        private void Update()
        {
            // Track rotation / notch changes cheaply.
            if (Time.frameCount % 30 == 0)
                ApplySafeArea();
        }

        private void BuildUi()
        {
            canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 500; // above the Exoa editor canvas

            CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;

            gameObject.AddComponent<GraphicRaycaster>();

            GameObject containerGo = new GameObject("Container", typeof(RectTransform));
            container = (RectTransform)containerGo.transform;
            container.SetParent(transform, false);
            // Right edge, vertically centred — clear of Exoa's top menus and side panels.
            container.anchorMin = container.anchorMax = new Vector2(1f, 0.5f);
            container.pivot = new Vector2(1f, 0.5f);
            container.sizeDelta = new Vector2(BUTTON_WIDTH, BUTTON_HEIGHT * 2f + BUTTON_GAP);

            Sprite rounded = CreateRoundedSprite();
            CreateButton("Undo", UndoRedoButton.Kind.Undo, rounded,
                new Vector2(0f, (BUTTON_HEIGHT + BUTTON_GAP) * 0.5f));
            CreateButton("Redo", UndoRedoButton.Kind.Redo, rounded,
                new Vector2(0f, -(BUTTON_HEIGHT + BUTTON_GAP) * 0.5f));

            ApplySafeArea();
        }

        private void CreateButton(string label, UndoRedoButton.Kind kind, Sprite background, Vector2 offset)
        {
            GameObject go = new GameObject(label + "Button", typeof(RectTransform));
            RectTransform rt = (RectTransform)go.transform;
            rt.SetParent(container, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(BUTTON_WIDTH, BUTTON_HEIGHT);
            rt.anchoredPosition = offset;

            Image image = go.AddComponent<Image>();
            image.sprite = background;
            image.type = Image.Type.Sliced;
            image.color = new Color(0.11f, 0.12f, 0.16f, 0.92f);

            Button button = go.AddComponent<Button>();
            button.targetGraphic = image;
            ColorBlock colors = button.colors;
            colors.disabledColor = new Color(1f, 1f, 1f, 0.35f);
            colors.pressedColor = new Color(0.7f, 0.7f, 0.7f, 1f);
            button.colors = colors;

            GameObject textGo = new GameObject("Label", typeof(RectTransform));
            RectTransform textRt = (RectTransform)textGo.transform;
            textRt.SetParent(rt, false);
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = textRt.offsetMax = Vector2.zero;
            TextMeshProUGUI text = textGo.AddComponent<TextMeshProUGUI>();
            text.text = label;
            text.fontSize = 40f;
            text.color = Color.white;
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;

            UndoRedoButton wiring = go.AddComponent<UndoRedoButton>();
            wiring.kind = kind;
        }

        private void ApplySafeArea()
        {
            if (container == null || canvas == null)
                return;
            float safeRightPx = Screen.width - Screen.safeArea.xMax;
            if (Mathf.Approximately(safeRightPx, lastSafeAreaInset))
                return;
            lastSafeAreaInset = safeRightPx;
            float scale = canvas.scaleFactor <= 0f ? 1f : canvas.scaleFactor;
            container.anchoredPosition = new Vector2(-(safeRightPx / scale + EDGE_MARGIN), 0f);
        }

        /// <summary>Procedural 9-sliced rounded-rect sprite so no art assets are needed.</summary>
        private static Sprite CreateRoundedSprite()
        {
            const int size = 48;
            const float radius = 18f;
            Texture2D tex = new Texture2D(size, size, TextureFormat.ARGB32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    // Signed distance to a rounded rectangle centred in the texture.
                    float dx = Mathf.Max(Mathf.Abs(x + 0.5f - size * 0.5f) - (size * 0.5f - radius), 0f);
                    float dy = Mathf.Max(Mathf.Abs(y + 0.5f - size * 0.5f) - (size * 0.5f - radius), 0f);
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    float alpha = Mathf.Clamp01(radius - dist + 0.5f);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }
            tex.Apply();
            float border = radius + 2f;
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, new Vector4(border, border, border, border));
        }
    }
}

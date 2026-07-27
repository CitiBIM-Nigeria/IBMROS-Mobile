using Exoa.Designer;
using IBMROS.Core;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace IBMROS.Bridge.Interaction
{
    /// <summary>
    /// P2.6 — the contextual action bar for the current selection (touch-first). Appears
    /// bottom-centre only while something is selected and offers Duplicate / Delete,
    /// routed through the A3 gateway (FloorPlanEditor) so each is one undo step. This is
    /// also the touch-deletion path the roadmap calls for ("select item → contextual
    /// delete"). Runtime-built like the other HUDs (no scene edits); one file to restyle
    /// when the P2 UI-framework decision lands.
    /// </summary>
    public sealed class SelectionActionBar : MonoBehaviour
    {
        private const float BUTTON_WIDTH = 200f;
        private const float BUTTON_HEIGHT = 110f;
        private const float GAP = 16f;
        private const float BOTTOM_MARGIN = 28f;

        private static SelectionActionBar instance;
        private Canvas canvas;
        private RectTransform bar;
        private float lastBottomInset = -1f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            TryInstall();
            SceneManager.sceneLoaded += (s, m) => TryInstall();
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
            new GameObject("IBMROS_SelectionActionBar").AddComponent<SelectionActionBar>();
        }

        private void Awake()
        {
            instance = this;
            BuildUi();
            Refresh();
        }

        private void OnEnable() { SelectionService.OnSelectionChanged += Refresh; }

        private void OnDisable()
        {
            SelectionService.OnSelectionChanged -= Refresh;
            if (instance == this) instance = null;
        }

        private void Update()
        {
            if (Time.frameCount % 20 == 0) ApplySafeArea();
        }

        private void Refresh()
        {
            bool show = SelectionService.Instance != null && SelectionService.Instance.HasSelection;
            if (canvas != null) canvas.enabled = show;
        }

        private void OnDelete()
        {
            SelectionService sel = SelectionService.Instance;
            if (sel == null || !sel.HasSelection) return;
            string id = sel.SelectedId;
            sel.Deselect();
            FloorPlanEditor.DeleteItem(id);
        }

        private void OnDuplicate()
        {
            SelectionService sel = SelectionService.Instance;
            if (sel == null || !sel.HasSelection) return;
            string newId = FloorPlanEditor.DuplicateItem(sel.SelectedId);
            if (!string.IsNullOrEmpty(newId))
                sel.SelectById(newId);
        }

        private void BuildUi()
        {
            canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 510;
            CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;
            gameObject.AddComponent<GraphicRaycaster>();

            GameObject barGo = new GameObject("Bar", typeof(RectTransform));
            bar = (RectTransform)barGo.transform;
            bar.SetParent(transform, false);
            bar.anchorMin = bar.anchorMax = new Vector2(0.5f, 0f);
            bar.pivot = new Vector2(0.5f, 0f);
            bar.sizeDelta = new Vector2(BUTTON_WIDTH * 2f + GAP, BUTTON_HEIGHT);

            Sprite rounded = CreateRoundedSprite();
            CreateButton("Duplicate", rounded, new Vector2(-(BUTTON_WIDTH + GAP) * 0.5f, 0f),
                new Color(0.11f, 0.12f, 0.16f, 0.94f), OnDuplicate);
            CreateButton("Delete", rounded, new Vector2((BUTTON_WIDTH + GAP) * 0.5f, 0f),
                new Color(0.55f, 0.16f, 0.18f, 0.94f), OnDelete);

            ApplySafeArea();
        }

        private void CreateButton(string label, Sprite bg, Vector2 offset, Color color, UnityEngine.Events.UnityAction onClick)
        {
            GameObject go = new GameObject(label + "Button", typeof(RectTransform));
            RectTransform rt = (RectTransform)go.transform;
            rt.SetParent(bar, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(BUTTON_WIDTH, BUTTON_HEIGHT);
            rt.anchoredPosition = offset;

            Image image = go.AddComponent<Image>();
            image.sprite = bg;
            image.type = Image.Type.Sliced;
            image.color = color;

            Button button = go.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(onClick);

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
        }

        private void ApplySafeArea()
        {
            if (bar == null || canvas == null) return;
            float bottomPx = Screen.safeArea.yMin;
            if (Mathf.Approximately(bottomPx, lastBottomInset)) return;
            lastBottomInset = bottomPx;
            float scale = canvas.scaleFactor <= 0f ? 1f : canvas.scaleFactor;
            bar.anchoredPosition = new Vector2(0f, bottomPx / scale + BOTTOM_MARGIN);
        }

        private static Sprite CreateRoundedSprite()
        {
            const int size = 48; const float radius = 18f;
            Texture2D tex = new Texture2D(size, size, TextureFormat.ARGB32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = Mathf.Max(Mathf.Abs(x + 0.5f - size * 0.5f) - (size * 0.5f - radius), 0f);
                    float dy = Mathf.Max(Mathf.Abs(y + 0.5f - size * 0.5f) - (size * 0.5f - radius), 0f);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(radius - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f)));
                }
            tex.Apply();
            float b = radius + 2f;
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, new Vector4(b, b, b, b));
        }
    }
}

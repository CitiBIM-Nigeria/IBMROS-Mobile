using System.Text.RegularExpressions;
using IBMROS.Designer;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

/// <summary>
/// Populates the main screen's "My Rooms" row with saved plans, newest first,
/// styled like the New Room card (130×150, rounded): a real plan THUMBNAIL
/// (Exoa renders "Floormap_{floorId}_persp.png" on every save) with a name +
/// date footer. Adds pointer-drag scrolling so the row pans with mouse or
/// finger (UI Toolkit only wheel-scrolls by default).
/// </summary>
public class SavedRoomsRow : MonoBehaviour
{
    private VisualElement _row;
    private VisualElement _emptyHint;
    private ScrollView _scroll;
    private bool _bound;

    // drag-to-scroll state
    private bool _dragging;
    private Vector2 _dragStart;
    private float _scrollStart;
    private float _dragDistance; // px moved this gesture — suppresses tap-open
    private const float DRAG_THRESHOLD_PX = 8f;

    private void OnEnable()
    {
        UIManager.OnScreensReady += Bind;
        ScreenNavigator.OnScreenChanged += OnScreenChanged;
        if (UIManager.Instance != null && UIManager.Instance.IsReady)
            Bind();
    }

    private void OnDisable()
    {
        UIManager.OnScreensReady -= Bind;
        ScreenNavigator.OnScreenChanged -= OnScreenChanged;
    }

    private void OnScreenChanged(ScreenName screen)
    {
        if (screen == ScreenName.MainApp && _bound)
            Refresh();
    }

    private void Bind()
    {
        if (_bound)
            return;
        VisualElement container = UIManager.Instance.GetScreenContainer(ScreenName.MainApp);
        if (container == null)
            return;
        _row = container.Q<VisualElement>("RoomsRow");
        _emptyHint = container.Q<VisualElement>("RoomsEmptyHint");
        _scroll = container.Q<ScrollView>("RoomsScroll");
        if (_row == null)
        {
            Debug.LogError("[SavedRoomsRow] RoomsRow missing from MainAppScreen.uxml.");
            return;
        }
        if (_scroll != null)
        {
            _scroll.mode = ScrollViewMode.Horizontal;
            _scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            _scroll.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            _scroll.touchScrollBehavior = ScrollView.TouchScrollBehavior.Elastic;
            _scroll.scrollDecelerationRate = 0.135f;
            _scroll.elasticity = 0.1f;

            // THE fix for "can't scroll": the row was being stretched to the
            // viewport width, so the content was never wider than the view and
            // there was nothing to scroll — the cards just overflowed invisibly.
            // Let the row size to its children instead.
            _scroll.contentContainer.style.flexDirection = FlexDirection.Row;
            _row.style.flexDirection = FlexDirection.Row;
            _row.style.flexShrink = 0;
            _row.style.flexGrow = 0;
            _row.style.width = StyleKeyword.Auto;
            _row.style.minWidth = StyleKeyword.Auto;

            EnableDragScroll(_scroll);
        }
        _bound = true;
        Refresh();
    }

    /// <summary>
    /// Pointer-drag panning for the horizontal row. Registered in TRICKLE-DOWN on
    /// the ScrollView so it still sees moves after a card Button has captured the
    /// pointer (ancestors are on a captured element's propagation path); once the
    /// gesture reads as a drag we steal the capture so the button can't also fire.
    /// Needed because UI Toolkit only drag-scrolls touch input, never the mouse.
    /// </summary>
    private void EnableDragScroll(ScrollView scroll)
    {
        scroll.RegisterCallback<PointerDownEvent>(e =>
        {
            _dragging = true;
            _dragStart = e.position;
            _scrollStart = scroll.scrollOffset.x;
            _dragDistance = 0f;
        }, TrickleDown.TrickleDown);

        scroll.RegisterCallback<PointerMoveEvent>(e =>
        {
            if (!_dragging)
                return;
            float dx = e.position.x - _dragStart.x;
            _dragDistance = Mathf.Max(_dragDistance, Mathf.Abs(dx));
            if (_dragDistance <= DRAG_THRESHOLD_PX)
                return;

            if (scroll.panel != null && scroll.panel.GetCapturingElement(e.pointerId) != scroll)
                scroll.CapturePointer(e.pointerId);

            scroll.scrollOffset = new Vector2(
                Mathf.Max(0f, _scrollStart - dx), scroll.scrollOffset.y);
            e.StopPropagation();
        }, TrickleDown.TrickleDown);

        scroll.RegisterCallback<PointerUpEvent>(e =>
        {
            if (scroll.HasPointerCapture(e.pointerId))
                scroll.ReleasePointer(e.pointerId);
            _dragging = false;
        }, TrickleDown.TrickleDown);

        scroll.RegisterCallback<PointerCaptureOutEvent>(_ => _dragging = false);
    }

    private void Refresh()
    {
        while (_row.childCount > 1) // everything after the AddRoomButton is ours
            _row.RemoveAt(_row.childCount - 1);

        string dir = System.IO.Path.Combine(Application.persistentDataPath, "FloorMaps");
        var plans = new System.Collections.Generic.List<System.IO.FileInfo>();
        if (System.IO.Directory.Exists(dir))
        {
            foreach (string f in System.IO.Directory.GetFiles(dir, "*.json"))
            {
                if (f.EndsWith(".furniture.json") || f.Contains(".json.bak"))
                    continue;
                plans.Add(new System.IO.FileInfo(f));
            }
        }
        plans.Sort((a, b) => b.LastWriteTime.CompareTo(a.LastWriteTime));

        if (_emptyHint != null)
            _emptyHint.style.display = plans.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;

        int shown = 0;
        foreach (System.IO.FileInfo fi in plans)
        {
            if (shown++ >= 12)
                break;
            _row.Add(BuildCard(fi));
        }
    }

    private VisualElement BuildCard(System.IO.FileInfo planFile)
    {
        string name = System.IO.Path.GetFileNameWithoutExtension(planFile.Name);

        var card = new Button();
        card.AddToClassList("main-room-card");
        card.style.height = 150;
        card.style.flexShrink = 0;
        card.style.paddingTop = 0; card.style.paddingBottom = 0;
        card.style.paddingLeft = 0; card.style.paddingRight = 0;
        card.style.justifyContent = Justify.FlexEnd;

        // Thumbnail (the perspective render Exoa writes on save).
        Texture2D thumb = LoadThumbnail(planFile.FullName);
        var image = new VisualElement();
        image.style.flexGrow = 1f;
        if (thumb != null)
        {
            image.style.backgroundImage = new StyleBackground(thumb);
            image.style.unityBackgroundScaleMode = ScaleMode.ScaleAndCrop;
        }
        else
        {
            image.style.backgroundColor = new Color(0.95f, 0.93f, 0.89f);
            var initial = new Label(name.Length > 0 ? name.Substring(0, 1) : "?");
            initial.style.flexGrow = 1f;
            initial.style.fontSize = 40;
            initial.style.color = new Color(0.75f, 0.7f, 0.62f);
            initial.style.unityTextAlign = TextAnchor.MiddleCenter;
            image.Add(initial);
        }
        card.Add(image);

        // Footer: name + date.
        var footer = new VisualElement();
        footer.style.backgroundColor = new Color(1f, 1f, 1f, 0.96f);
        footer.style.paddingTop = 6; footer.style.paddingBottom = 8;
        footer.style.paddingLeft = 10; footer.style.paddingRight = 10;
        var title = new Label(name);
        title.style.fontSize = 13;
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        title.style.color = new Color(0.12f, 0.12f, 0.13f);
        footer.Add(title);
        var when = new Label(planFile.LastWriteTime.ToString("d MMM, HH:mm"));
        when.style.fontSize = 10;
        when.style.color = new Color(0.55f, 0.55f, 0.57f);
        when.style.marginTop = 1;
        footer.Add(when);
        card.Add(footer);

        card.clicked += () =>
        {
            // Suppress accidental opens at the end of a drag-scroll gesture.
            if (_dragDistance > DRAG_THRESHOLD_PX)
                return;
            Debug.Log($"[SavedRoomsRow] Opening saved plan '{name}'.");
            RoomDesignLaunch.SetLoadPlan(name);
            SceneManager.LoadScene("RoomDesigner");
        };
        return card;
    }

    /// <summary>
    /// Thumbnails are keyed by the plan's FLOOR id, not its file name:
    /// Thumbnails/Floormap_{floorUniqueId}_persp.png — pull the id out of the
    /// plan json (first uniqueId in the floors array).
    /// </summary>
    private static Texture2D LoadThumbnail(string planPath)
    {
        try
        {
            string json = System.IO.File.ReadAllText(planPath);
            Match m = Regex.Match(json, "\"uniqueId\"\\s*:\\s*\"([^\"]+)\"");
            if (!m.Success)
                return null;
            string path = System.IO.Path.Combine(Application.persistentDataPath,
                "Thumbnails", $"Floormap_{m.Groups[1].Value}_persp.png");
            if (!System.IO.File.Exists(path))
                return null;
            byte[] bytes = System.IO.File.ReadAllBytes(path);
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            return tex.LoadImage(bytes) ? tex : null;
        }
        catch
        {
            return null;
        }
    }
}

using IBMROS.Designer;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

/// <summary>
/// Preset-picker screen (Main.unity). Builds one card per RoomPresets entry —
/// the thumbnail is painted from the preset polygon itself (painter2D), so the
/// picker can never drift from what actually gets generated. Selecting a card
/// stashes the launch payload and enters the RoomDesigner scene.
/// </summary>
public class PresetPickerController : MonoBehaviour
{
    private VisualElement _screen;
    private VisualElement _savedSection;
    private bool _bound;

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
        // Saved rooms change while the user is away in the designer — rebuild
        // the section every time this screen is shown.
        if (screen == ScreenName.PresetPicker && _bound)
            RefreshSavedRooms();
    }

    private void Bind()
    {
        if (_bound)
            return;

        VisualElement container = UIManager.Instance.GetScreenContainer(ScreenName.PresetPicker);
        if (container == null)
        {
            Debug.LogError("[PresetPickerController] PresetPicker container missing.");
            return;
        }

        _screen = container.Q<VisualElement>("PresetPickerScreen");
        Button back = container.Q<Button>("PresetBackButton");
        VisualElement grid = container.Q<VisualElement>("PresetGrid");
        if (_screen == null || back == null || grid == null)
        {
            Debug.LogError("[PresetPickerController] PresetPickerScreen.uxml structure missing.");
            return;
        }

        back.clicked += () => ScreenNavigator.Instance.NavigateTo(ScreenName.MainApp);

        // "Your rooms" (saved plans) sits above the preset grid, inside the scroll.
        ScrollView scroll = container.Q<ScrollView>("PresetScroll");
        _savedSection = new VisualElement();
        _savedSection.name = "SavedRoomsSection";
        scroll?.Insert(0, _savedSection);
        RefreshSavedRooms();

        grid.Clear();
        foreach (RoomPresets.Preset preset in RoomPresets.All)
            grid.Add(BuildCard(preset));

        _bound = true;
    }

    // ------------------------------------------------------------------ saved rooms

    private void RefreshSavedRooms()
    {
        if (_savedSection == null)
            return;
        _savedSection.Clear();

        string dir = System.IO.Path.Combine(Application.persistentDataPath, "FloorMaps");
        if (!System.IO.Directory.Exists(dir))
            return;

        var plans = new System.Collections.Generic.List<System.IO.FileInfo>();
        foreach (string f in System.IO.Directory.GetFiles(dir, "*.json"))
        {
            if (f.EndsWith(".furniture.json") || f.Contains(".json.bak"))
                continue;
            plans.Add(new System.IO.FileInfo(f));
        }
        if (plans.Count == 0)
            return;
        plans.Sort((a, b) => b.LastWriteTime.CompareTo(a.LastWriteTime));

        var header = new Label("Your rooms");
        header.AddToClassList("preset-picker__section-title");
        _savedSection.Add(header);

        var row = new ScrollView(ScrollViewMode.Horizontal);
        row.AddToClassList("saved-rooms__row");
        // Structural sizing inline — code-built elements must not depend on
        // stylesheet load order for layout, only for theming.
        row.style.height = 100;
        row.style.flexShrink = 0;
        _savedSection.Add(row);

        int shown = 0;
        foreach (System.IO.FileInfo fi in plans)
        {
            if (shown++ >= 12)
                break;
            string name = System.IO.Path.GetFileNameWithoutExtension(fi.Name);
            var card = new VisualElement();
            card.AddToClassList("saved-room-card");
            card.style.width = 150;
            card.style.minHeight = 72;
            card.style.flexShrink = 0;
            card.style.paddingTop = 12; card.style.paddingBottom = 12;
            card.style.paddingLeft = 12; card.style.paddingRight = 12;
            card.style.justifyContent = Justify.Center;
            var title = new Label(name);
            title.AddToClassList("saved-room-card__name");
            title.style.fontSize = 15;
            card.Add(title);
            var when = new Label(fi.LastWriteTime.ToString("d MMM, HH:mm"));
            when.AddToClassList("saved-room-card__date");
            when.style.fontSize = 12;
            when.style.marginTop = 4;
            card.Add(when);
            card.RegisterCallback<ClickEvent>(_ => OpenSaved(name));
            row.Add(card);
        }

        var presetsHeader = new Label("New from preset");
        presetsHeader.AddToClassList("preset-picker__section-title");
        _savedSection.Add(presetsHeader);
    }

    private void OpenSaved(string planName)
    {
        Debug.Log($"[PresetPickerController] Opening saved plan '{planName}'.");
        RoomDesignLaunch.SetLoadPlan(planName);
        SceneManager.LoadScene("RoomDesigner");
    }

    private VisualElement BuildCard(RoomPresets.Preset preset)
    {
        var card = new VisualElement();
        card.AddToClassList("preset-card");

        var thumb = new VisualElement();
        thumb.AddToClassList("preset-card__thumb");
        thumb.generateVisualContent += ctx => DrawPresetThumb(ctx, preset);
        card.Add(thumb);

        var label = new Label(preset.DisplayName);
        label.AddToClassList("preset-card__label");
        card.Add(label);

        card.RegisterCallback<ClickEvent>(_ => Launch(preset));
        return card;
    }

    private void Launch(RoomPresets.Preset preset)
    {
        Debug.Log($"[PresetPickerController] Launching RoomDesigner with preset '{preset.Id}'.");
        RoomDesignLaunch.SetNewFromPreset(preset.Id);
        SceneManager.LoadScene("RoomDesigner");
    }

    // ------------------------------------------------------------------ thumbnail

    private static void DrawPresetThumb(MeshGenerationContext ctx, RoomPresets.Preset preset)
    {
        Rect rect = ctx.visualElement.contentRect;
        if (rect.width <= 0f || rect.height <= 0f || preset.Polygon == null || preset.Polygon.Length < 3)
            return;

        // Fit the polygon (meters) into the thumb rect with padding; plan +Z is up
        // on screen (screen Y grows downward → flip).
        Vector2 min = preset.Polygon[0], max = preset.Polygon[0];
        foreach (Vector2 p in preset.Polygon)
        {
            min = Vector2.Min(min, p);
            max = Vector2.Max(max, p);
        }
        Vector2 size = max - min;
        const float pad = 16f;
        float scale = Mathf.Min((rect.width - 2f * pad) / Mathf.Max(size.x, 0.01f),
                                (rect.height - 2f * pad) / Mathf.Max(size.y, 0.01f));
        Vector2 center = (min + max) * 0.5f;

        Vector2 ToScreen(Vector2 m) => new Vector2(
            rect.width * 0.5f + (m.x - center.x) * scale,
            rect.height * 0.5f - (m.y - center.y) * scale);

        Painter2D paint = ctx.painter2D;

        // Floor fill
        paint.fillColor = new Color(0.93f, 0.89f, 0.82f);
        paint.BeginPath();
        paint.MoveTo(ToScreen(preset.Polygon[0]));
        for (int i = 1; i < preset.Polygon.Length; i++)
            paint.LineTo(ToScreen(preset.Polygon[i]));
        paint.ClosePath();
        paint.Fill();

        // Walls
        paint.strokeColor = new Color(0.22f, 0.25f, 0.29f);
        paint.lineWidth = 6f;
        paint.lineJoin = LineJoin.Round;
        paint.BeginPath();
        paint.MoveTo(ToScreen(preset.Polygon[0]));
        for (int i = 1; i < preset.Polygon.Length; i++)
            paint.LineTo(ToScreen(preset.Polygon[i]));
        paint.ClosePath();
        paint.Stroke();

        // Openings: white gap across the wall; doors add a quarter-circle swing.
        if (preset.Openings == null)
            return;
        foreach (RoomPresets.OpeningSpec o in preset.Openings)
        {
            Vector2 t = o.Tangent.normalized;
            Vector2 a = ToScreen(o.Position - t * (o.Width * 0.5f));
            Vector2 b = ToScreen(o.Position + t * (o.Width * 0.5f));

            paint.strokeColor = new Color(0.97f, 0.96f, 0.95f);
            paint.lineWidth = 8f;
            paint.BeginPath();
            paint.MoveTo(a);
            paint.LineTo(b);
            paint.Stroke();

            if (o.Type == Exoa.Designer.DataModel.FloorMapItemType.Door)
            {
                float r = (b - a).magnitude;
                paint.strokeColor = new Color(0.45f, 0.48f, 0.52f);
                paint.lineWidth = 2f;
                paint.BeginPath();
                paint.Arc(a, r, Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg,
                    Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg + 90f);
                paint.Stroke();
            }
            else // window: thin double line across the gap
            {
                paint.strokeColor = new Color(0.45f, 0.48f, 0.52f);
                paint.lineWidth = 2f;
                paint.BeginPath();
                paint.MoveTo(a);
                paint.LineTo(b);
                paint.Stroke();
            }
        }
    }
}

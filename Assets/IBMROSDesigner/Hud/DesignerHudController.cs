using System;
using System.Globalization;
using System.Collections.Generic;
using System.IO;
using Exoa.Designer;
using IBMROS.Bridge.UndoRedo;
using IBMROS.Bridge.Interaction;
using IBMROS.Core;
using IBMROS.Designer.Plan;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace IBMROS.Designer.Hud
{
    /// <summary>
    /// UI Toolkit HUD for RoomDesigner.unity, structured like the reference app:
    ///   • 2D browse bar: [3D · Open 3D plan] [Edit Walls] [Add Furniture]
    ///   • Edit mode: header shows ONLY "Done"; bar is [Draw Wall][Resize][Add]
    ///     where Add opens the FURNITURE panel (the plugin's Door/Window/Room
    ///     menu is intentionally not exposed in this workflow)
    ///   • 3D bar: [2D · Open 2D plan] [joystick] [Add Furniture]
    /// Icons are painter-drawn (generateVisualContent) — the UI font lacks the
    /// undo/camera glyphs, which rendered as empty boxes on device.
    /// All writes go through the gateway/controllers; this class only binds.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class DesignerHudController : MonoBehaviour
    {
        private static readonly Color ICON_COLOR = new Color(0.13f, 0.13f, 0.14f);
        private static readonly Color ICON_COLOR_DARK = new Color(0.97f, 0.97f, 0.98f);

        /// <summary>Painter icons read this so they match the active variant.</summary>
        private static Color iconColor = ICON_COLOR;

        private VisualElement root;
        private Label planName;
        private Button back, shot, undo, redo;
        private VisualElement browseBar, editBar, viewBar, sheet;
        private Button btnOpen3D, btnEditWalls, btnAddFurn2D;
        private Button btnDone, btnDrawWall, btnResize, btnAddOpening;
        private Button btnOpen2D, btnAddFurn3D;
        private TextField widthField, lengthField, ceilField, thickField;
        private Button sheetApply, sheetClose;

        private string sheetItemId;   // room the sheet is editing
        private bool editingWalls;    // Edit Walls sub-mode of Plan2D

        private void OnEnable()
        {
            root = GetComponent<UIDocument>().rootVisualElement;

            planName = root.Q<Label>("PlanNameLabel");
            back = root.Q<Button>("BackButton");
            shot = root.Q<Button>("ShotButton");
            undo = root.Q<Button>("UndoButton");
            redo = root.Q<Button>("RedoButton");

            browseBar = root.Q<VisualElement>("BrowseToolbar");
            editBar = root.Q<VisualElement>("EditToolbar");
            viewBar = root.Q<VisualElement>("ViewToolbar");
            btnOpen3D = root.Q<Button>("BtnOpen3D");
            btnEditWalls = root.Q<Button>("BtnEditWalls");
            btnAddFurn2D = root.Q<Button>("BtnAddFurn2D");
            btnDone = root.Q<Button>("DoneButton");
            btnDrawWall = root.Q<Button>("BtnDrawWall");
            btnResize = root.Q<Button>("BtnResize");
            btnAddOpening = root.Q<Button>("BtnAddOpening");
            btnOpen2D = root.Q<Button>("BtnOpen2D");
            btnAddFurn3D = root.Q<Button>("BtnAddFurn3D");

            sheet = root.Q<VisualElement>("RoomSizeSheet");
            widthField = root.Q<TextField>("WidthField");
            lengthField = root.Q<TextField>("LengthField");
            ceilField = root.Q<TextField>("CeilHeightField");
            thickField = root.Q<TextField>("WallThickField");
            sheetApply = root.Q<Button>("SheetApplyButton");
            sheetClose = root.Q<Button>("SheetCloseButton");

            // Painter icons (no font dependency).
            BindIcon("ShotIcon", DrawCameraIcon);
            BindIcon("UndoIcon", ctx => DrawUndoIcon(ctx, false));
            BindIcon("RedoIcon", ctx => DrawUndoIcon(ctx, true));
            BindIcon("EditWallsIcon", DrawEditWallsIcon);
            BindIcon("AddIcon2D", DrawPlusIcon);
            BindIcon("AddIcon3D", DrawPlusIcon);
            BindIcon("DrawWallIcon", DrawPencilIcon);
            BindIcon("ResizeIcon", DrawResizeIcon);
            BindIcon("AddOpeningIcon", DrawPlusIcon);

            back.clicked += OnBack;
            shot.clicked += OnScreenshot;
            undo.clicked += () => UndoRedoService.Instance?.Undo();
            redo.clicked += () => UndoRedoService.Instance?.Redo();

            btnOpen3D.clicked += () => DesignerModeController.Instance?.Set3D();
            btnOpen2D.clicked += () => DesignerModeController.Instance?.Set2D();
            btnEditWalls.clicked += () => SetEditingWalls(true);
            btnDone.clicked += () =>
            {
                PlanTouchController.Instance?.SetTool(PlanToolMode.Browse);
                ShowSheet(false);
                SetEditingWalls(false);
            };
            btnAddFurn2D.clicked += OnFurnish;
            btnAddFurn3D.clicked += OnFurnish;
            btnDrawWall.clicked += () =>
                PlanTouchController.Instance?.SetTool(PlanToolMode.DrawRect);
            // "Add" in edit mode = ADD FURNITURE (the plugin's Door/Window/Room
            // menu is deliberately not exposed in this workflow).
            btnAddOpening.clicked += OnFurnish;
            btnResize.clicked += OpenSheet;

            sheetApply.clicked += ApplySheet;
            sheetClose.clicked += () => ShowSheet(false);

            UndoRedoService.OnHistoryChanged += RefreshHistoryButtons;
            DesignerModeController.OnModeChanged += RefreshMode;
            PlanTouchController.OnToolChanged += RefreshToolStates;

            ApplySafeArea();
            RefreshHistoryButtons();
            RefreshMode(DesignerModeController.Instance != null
                ? DesignerModeController.Instance.Mode : DesignerMode.Plan2D);
            RefreshToolStates(PlanTouchController.Instance != null
                ? PlanTouchController.Instance.Tool : PlanToolMode.Browse);
            ShowSheet(false);
        }

        private void OnDisable()
        {
            UndoRedoService.OnHistoryChanged -= RefreshHistoryButtons;
            DesignerModeController.OnModeChanged -= RefreshMode;
            PlanTouchController.OnToolChanged -= RefreshToolStates;
        }

        private void Start()
        {
            // The bridge self-installs floating uGUI HUDs; this HUD owns those
            // affordances. RectRoomTool keeps running (Room button delegates to
            // it); only its arm-button canvas is hidden.
            UndoRedoHud bridgeHud = FindAnyObjectByType<UndoRedoHud>();
            if (bridgeHud != null)
                bridgeHud.gameObject.SetActive(false);
            SelectionActionBar bridgeBar = FindAnyObjectByType<SelectionActionBar>();
            if (bridgeBar != null)
                bridgeBar.gameObject.SetActive(false);
            RectRoomTool rect = RectRoomTool.Instance;
            if (rect != null)
            {
                var rectCanvas = rect.GetComponent<UnityEngine.Canvas>();
                if (rectCanvas != null)
                    rectCanvas.enabled = false;
            }
        }

        private void Update()
        {
            string name = RoomDesignerBootstrap.PlanName;
            if (!string.IsNullOrEmpty(name) && planName.text != name)
                planName.text = name;
        }

        /// <summary>
        /// Show/hide the whole HUD (furnish mode takes the screen). Hides the
        /// document ROOT, deliberately: hiding the individual bars loses to
        /// RefreshMode, which re-asserts their display on every mode change —
        /// and SetActive(false) on the GameObject would unsubscribe everything
        /// and force UIDocument to rebuild the tree on re-enable.
        /// </summary>
        public void SetHudVisible(bool visible)
        {
            if (root != null)
                root.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        // ------------------------------------------------------------------ painter icons

        private void BindIcon(string elementName, Action<MeshGenerationContext> draw)
        {
            VisualElement el = root.Q<VisualElement>(elementName);
            if (el != null)
                el.generateVisualContent += draw;
        }

        private static void DrawUndoIcon(MeshGenerationContext ctx, bool mirrored)
        {
            Rect r = ctx.visualElement.contentRect;
            var p = ctx.painter2D;
            float cx = r.width * 0.5f, cy = r.height * 0.55f;
            float rad = Mathf.Min(r.width, r.height) * 0.32f;
            p.strokeColor = iconColor;
            p.lineWidth = 2.2f;
            p.lineCap = LineCap.Round;
            // open arc with an arrowhead at its start (top)
            float a0 = mirrored ? -60f : 240f;
            float a1 = mirrored ? 240f : -60f;
            p.BeginPath();
            p.Arc(new Vector2(cx, cy), rad, a0, a1,
                mirrored ? ArcDirection.Clockwise : ArcDirection.CounterClockwise);
            p.Stroke();
            float tipX = cx + rad * Mathf.Cos(a0 * Mathf.Deg2Rad);
            float tipY = cy + rad * Mathf.Sin(a0 * Mathf.Deg2Rad);
            float dir = mirrored ? 1f : -1f;
            p.BeginPath();
            p.MoveTo(new Vector2(tipX + dir * 5f, tipY - 4f));
            p.LineTo(new Vector2(tipX, tipY));
            p.LineTo(new Vector2(tipX + dir * 5f, tipY + 4f));
            p.Stroke();
        }

        private static void DrawCameraIcon(MeshGenerationContext ctx)
        {
            Rect r = ctx.visualElement.contentRect;
            var p = ctx.painter2D;
            p.strokeColor = iconColor;
            p.lineWidth = 2f;
            p.lineJoin = LineJoin.Round;
            float w = r.width, h = r.height;
            // body
            p.BeginPath();
            p.MoveTo(new Vector2(w * 0.08f, h * 0.3f));
            p.LineTo(new Vector2(w * 0.32f, h * 0.3f));
            p.LineTo(new Vector2(w * 0.4f, h * 0.16f));
            p.LineTo(new Vector2(w * 0.6f, h * 0.16f));
            p.LineTo(new Vector2(w * 0.68f, h * 0.3f));
            p.LineTo(new Vector2(w * 0.92f, h * 0.3f));
            p.LineTo(new Vector2(w * 0.92f, h * 0.85f));
            p.LineTo(new Vector2(w * 0.08f, h * 0.85f));
            p.ClosePath();
            p.Stroke();
            // lens
            p.BeginPath();
            p.Arc(new Vector2(w * 0.5f, h * 0.56f), w * 0.17f, 0f, 360f);
            p.Stroke();
        }

        private static void DrawEditWallsIcon(MeshGenerationContext ctx)
        {
            Rect r = ctx.visualElement.contentRect;
            var p = ctx.painter2D;
            float w = r.width, h = r.height;
            p.strokeColor = iconColor;
            p.lineWidth = 2.2f;
            p.lineJoin = LineJoin.Round;
            // rectangle outline
            p.BeginPath();
            p.MoveTo(new Vector2(w * 0.18f, h * 0.18f));
            p.LineTo(new Vector2(w * 0.82f, h * 0.18f));
            p.LineTo(new Vector2(w * 0.82f, h * 0.82f));
            p.LineTo(new Vector2(w * 0.18f, h * 0.82f));
            p.ClosePath();
            p.Stroke();
            // corner handles
            p.fillColor = iconColor;
            foreach (Vector2 c in new[]
            {
                new Vector2(w * 0.18f, h * 0.18f), new Vector2(w * 0.82f, h * 0.18f),
                new Vector2(w * 0.82f, h * 0.82f), new Vector2(w * 0.18f, h * 0.82f),
            })
            {
                p.BeginPath();
                p.Arc(c, 3.2f, 0f, 360f);
                p.Fill();
            }
        }

        private static void DrawPencilIcon(MeshGenerationContext ctx)
        {
            Rect r = ctx.visualElement.contentRect;
            var p = ctx.painter2D;
            float w = r.width, h = r.height;
            p.strokeColor = iconColor;
            p.lineWidth = 2.2f;
            p.lineJoin = LineJoin.Round;
            // pencil body
            p.BeginPath();
            p.MoveTo(new Vector2(w * 0.22f, h * 0.78f));
            p.LineTo(new Vector2(w * 0.3f, h * 0.55f));
            p.LineTo(new Vector2(w * 0.7f, h * 0.15f));
            p.LineTo(new Vector2(w * 0.85f, h * 0.3f));
            p.LineTo(new Vector2(w * 0.45f, h * 0.7f));
            p.ClosePath();
            p.Stroke();
            // baseline (the wall being drawn)
            p.BeginPath();
            p.MoveTo(new Vector2(w * 0.14f, h * 0.88f));
            p.LineTo(new Vector2(w * 0.86f, h * 0.88f));
            p.Stroke();
        }

        private static void DrawResizeIcon(MeshGenerationContext ctx)
        {
            Rect r = ctx.visualElement.contentRect;
            var p = ctx.painter2D;
            float w = r.width, h = r.height;
            p.strokeColor = iconColor;
            p.lineWidth = 2.4f;
            p.lineCap = LineCap.Round;
            // diagonal double arrow
            p.BeginPath();
            p.MoveTo(new Vector2(w * 0.2f, h * 0.8f));
            p.LineTo(new Vector2(w * 0.8f, h * 0.2f));
            p.Stroke();
            p.BeginPath();
            p.MoveTo(new Vector2(w * 0.2f, h * 0.52f));
            p.LineTo(new Vector2(w * 0.2f, h * 0.8f));
            p.LineTo(new Vector2(w * 0.48f, h * 0.8f));
            p.Stroke();
            p.BeginPath();
            p.MoveTo(new Vector2(w * 0.52f, h * 0.2f));
            p.LineTo(new Vector2(w * 0.8f, h * 0.2f));
            p.LineTo(new Vector2(w * 0.8f, h * 0.48f));
            p.Stroke();
        }

        private static void DrawPlusIcon(MeshGenerationContext ctx)
        {
            Rect r = ctx.visualElement.contentRect;
            var p = ctx.painter2D;
            float cx = r.width * 0.5f, cy = r.height * 0.5f;
            float arm = Mathf.Min(r.width, r.height) * 0.36f;
            p.strokeColor = iconColor;
            p.lineWidth = 3f;
            p.lineCap = LineCap.Round;
            p.BeginPath();
            p.MoveTo(new Vector2(cx - arm, cy));
            p.LineTo(new Vector2(cx + arm, cy));
            p.MoveTo(new Vector2(cx, cy - arm));
            p.LineTo(new Vector2(cx, cy + arm));
            p.Stroke();
        }

        // ------------------------------------------------------------------ actions

        private void OnBack()
        {
            if (UISaving.instance != null && !string.IsNullOrEmpty(RoomDesignerBootstrap.PlanName))
                UISaving.instance.SaveInternal(RoomDesignerBootstrap.PlanName);
            SceneTransition.SetSkipSplash(true);
            SceneManager.LoadScene("Main");
        }

        private void OnScreenshot()
        {
            string dir = Path.Combine(Application.persistentDataPath, "Screenshots");
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, $"Room_{DateTime.Now:yyyyMMdd_HHmmss}.png");
            ScreenCapture.CaptureScreenshot(file);
            Debug.Log($"[DesignerHud] Screenshot → {file}");
        }

        private void OnFurnish()
        {
            var adapter = Furnish.FurnishModeAdapter.Instance;
            if (adapter != null)
                adapter.OpenCatalog();
            else
                Debug.Log("[DesignerHud] Furniture stack not present in this scene.");
        }

        private void SetEditingWalls(bool editing)
        {
            editingWalls = editing;
            RefreshMode(DesignerModeController.Instance != null
                ? DesignerModeController.Instance.Mode : DesignerMode.Plan2D);
        }

        // ------------------------------------------------------------------ room-size sheet

        private void OpenSheet()
        {
            UIBaseItem item = PlanEditorUtil.FindItem(SelectionService.Instance?.SelectedId);
            if (item == null || PlanEditorUtil.IsOpening(item))
            {
                List<UIBaseItem> spaces = PlanEditorUtil.AllSpaces();
                item = spaces.Count > 0 ? spaces[0] : null;
            }
            if (item == null)
                return;

            sheetItemId = item.ItemUniqueId;
            Rect bounds = BoundsOf(item);
            widthField.value = bounds.width.ToString("0.##", CultureInfo.InvariantCulture);
            lengthField.value = bounds.height.ToString("0.##", CultureInfo.InvariantCulture);

            AppController app = AppController.Instance;
            if (app != null)
            {
                ceilField.value = app.wallsHeight.ToString("0.##", CultureInfo.InvariantCulture);
                thickField.value = app.exteriorWallThickness.ToString("0.##", CultureInfo.InvariantCulture);
            }
            ShowSheet(true);
        }

        private void ApplySheet()
        {
            UIBaseItem item = PlanEditorUtil.FindItem(sheetItemId);
            if (item != null &&
                TryParse(widthField.value, out float w) &&
                TryParse(lengthField.value, out float l) &&
                w >= 0.5f && l >= 0.5f)
            {
                Rect b = BoundsOf(item);
                if (b.width > 1e-3f && b.height > 1e-3f &&
                    (Mathf.Abs(b.width - w) > 1e-3f || Mathf.Abs(b.height - l) > 1e-3f))
                {
                    Vector2 center = b.center;
                    float sx = w / b.width, sy = l / b.height;
                    var pts = new List<Vector2>();
                    foreach (Vector3 wp in PlanEditorUtil.WorldPoints(item))
                    {
                        Vector2 m = PlanEditorUtil.WorldToMeters(wp);
                        pts.Add(center + Vector2.Scale(m - center, new Vector2(sx, sy)));
                    }
                    FloorPlanEditor.MoveItemPoints(item.ItemUniqueId, pts);
                }
            }

            bool hasCeil = TryParse(ceilField.value, out float ceil) && ceil >= 2f && ceil <= 6f;
            bool hasThick = TryParse(thickField.value, out float thick) && thick >= 0.02f && thick <= 0.5f;
            if (hasCeil || hasThick)
            {
                FloorPlanEditor.SetBuildingSettings(
                    wallsHeight: hasCeil ? ceil : (float?)null,
                    exteriorWallThickness: hasThick ? thick : (float?)null);
            }

            ShowSheet(false);
        }

        private static bool TryParse(string s, out float v) =>
            float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);

        private static Rect BoundsOf(UIBaseItem item)
        {
            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);
            foreach (Vector3 wp in PlanEditorUtil.WorldPoints(item))
            {
                Vector2 m = PlanEditorUtil.WorldToMeters(wp);
                min = Vector2.Min(min, m);
                max = Vector2.Max(max, m);
            }
            return new Rect(min, max - min);
        }

        private void ShowSheet(bool show) =>
            sheet.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;

        // ------------------------------------------------------------------ selection actions


        // ------------------------------------------------------------------ state refresh

        private void RefreshHistoryButtons()
        {
            UndoRedoService svc = UndoRedoService.Instance;
            undo.SetEnabled(svc != null && svc.CanUndo);
            redo.SetEnabled(svc != null && svc.CanRedo);
        }

        private void RefreshMode(DesignerMode mode)
        {
            bool plan = mode == DesignerMode.Plan2D;

            // Contrast variant: light pills on the bright 2D plan, dark scrim
            // pills inside the lit 3D room (a translucent white pill vanished
            // against white walls once the solid panels were removed).
            if (plan) root.RemoveFromClassList("hud--dark");
            else root.AddToClassList("hud--dark");
            iconColor = plan ? ICON_COLOR : ICON_COLOR_DARK;
            root.Query<VisualElement>().Class("icon").ForEach(e => e.MarkDirtyRepaint());
            root.Query<VisualElement>().Class("tool-col__icon").ForEach(e => e.MarkDirtyRepaint());

            browseBar.style.display = plan && !editingWalls ? DisplayStyle.Flex : DisplayStyle.None;
            editBar.style.display = plan && editingWalls ? DisplayStyle.Flex : DisplayStyle.None;
            viewBar.style.display = plan ? DisplayStyle.None : DisplayStyle.Flex;

            // Editing shows ONLY "Done" top-left; browse shows close + plan name.
            bool editing = plan && editingWalls;
            btnDone.style.display = editing ? DisplayStyle.Flex : DisplayStyle.None;
            back.style.display = editing ? DisplayStyle.None : DisplayStyle.Flex;
            planName.style.display = editing ? DisplayStyle.None : DisplayStyle.Flex;
            if (!plan)
            {
                ShowSheet(false);
                editingWalls = false;
            }
        }


        private void RefreshToolStates(PlanToolMode tool)
        {
            SetArmed(btnDrawWall, tool == PlanToolMode.DrawRect);
        }

        private static void SetArmed(Button b, bool armed)
        {
            if (armed) b.AddToClassList("btn--armed");
            else b.RemoveFromClassList("btn--armed");
        }

        private void ApplySafeArea()
        {
            Rect safe = Screen.safeArea;
            float scale = root.panel != null ? root.panel.scaledPixelsPerPoint : 1f;
            root.style.paddingTop = (Screen.height - safe.yMax) / scale;
            root.style.paddingBottom = safe.yMin / scale;
            root.style.paddingLeft = safe.xMin / scale;
            root.style.paddingRight = (Screen.width - safe.xMax) / scale;
        }
    }
}

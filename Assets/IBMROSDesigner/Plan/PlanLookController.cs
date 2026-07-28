using Exoa.Cameras;
using IBMROS.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace IBMROS.Designer.Plan
{
    /// <summary>
    /// The 2D plan's "drawing" look (reference-app style):
    ///   • wall meshes read as thick DARK slabs from above — a MaterialPropertyBlock
    ///     tint on Wall/ExteriorWall renderers while in Plan2D (the ground-level
    ///     LineRenderer is hidden underneath 3 m walls, so the wall TOPS are the
    ///     visible outline)
    ///   • flat bright ambient so the canvas reads paper-white (the 3D trilight
    ///     makes a lit top-down plane render mid-gray)
    /// Both fully revert outside Plan2D. Re-applied on a short cadence because
    /// rebuilds create fresh renderers (MPBs don't survive that), same pattern
    /// SelectionService uses for its highlight.
    /// </summary>
    public sealed class PlanLookController : MonoBehaviour
    {
        private static readonly Color WALL_TINT = new Color(0.27f, 0.30f, 0.34f, 1f);
        private static readonly Color WALL_TINT_SELECTED = new Color(0.16f, 0.47f, 0.85f, 1f);
        private static readonly Color PLAN_AMBIENT = new Color(0.96f, 0.96f, 0.95f);
        private const float REAPPLY_INTERVAL_S = 0.25f;

        // Exoa/VertexColor_URP has NO color property — meshes are colored purely
        // by vertex colors, so MaterialPropertyBlock tints are silent no-ops on
        // them. The plan look therefore SWAPS wall materials (dark slab; accent
        // blue on the selected room) and restores the originals on exit.
        [Header("3D interior materials (Room-scene textures)")]
        [SerializeField] private Material wallMaterial3D;    // plaster white
        [SerializeField] private Material floorMaterial3D;   // laminate wood
        [SerializeField] private Material ceilingMaterial3D; // plaster ceiling

        private Material darkWallMat;
        private Material selectedWallMat;
        private Material darkLineMat;     // Line.mat clone, opaque dark slate
        private Material selectedLineMat; // Line.mat clone, accent blue
        private readonly System.Collections.Generic.Dictionary<Renderer, Material> originals =
            new System.Collections.Generic.Dictionary<Renderer, Material>();
        // Restore fallbacks — wall GameObjects are POOLED across vendor rebuilds
        // (mesh swapped in place), so dictionary keys can go stale while a live
        // renderer still carries our material. Remember one original per kind.
        private readonly System.Collections.Generic.Dictionary<int, Material> fallbackByLayer =
            new System.Collections.Generic.Dictionary<int, Material>();
        private Material fallbackLineMat;

        private MaterialPropertyBlock mpb;
        private int wallMask;
        private bool planMode;
        private float nextApply;
        private bool framedOnce;

        // 3D ambient = LightingRig's trilight (kept in lockstep by value; the
        // rig's Start order vs ours is unreliable, so no save/restore dance)
        // Interiors have no baked bounce, so ambient carries the room: lifted
        // well above the outdoor-ish defaults or walls read flat gray.
        private static readonly Color AMBIENT_SKY = new Color(0.86f, 0.87f, 0.90f);
        private static readonly Color AMBIENT_EQUATOR = new Color(0.72f, 0.71f, 0.69f);
        private static readonly Color AMBIENT_GROUND = new Color(0.45f, 0.43f, 0.40f);

        private void Awake()
        {
            mpb = new MaterialPropertyBlock();
            wallMask = LayerMask.GetMask("Wall", "ExteriorWall");

            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            darkWallMat = new Material(lit) { color = WALL_TINT, name = "PlanWall_Dark" };
            selectedWallMat = new Material(lit) { color = WALL_TINT_SELECTED, name = "PlanWall_Selected" };
        }

        private void OnEnable()
        {
            DesignerModeController.OnModeChanged += OnModeChanged;
        }

        private void OnDisable()
        {
            DesignerModeController.OnModeChanged -= OnModeChanged;
            if (planMode)
                RestoreLook();
        }

        private void Start()
        {
            // Runs after LightingRig.Start (component order) — whatever mode we
            // booted into, assert its look now so the rig's trilight can't stomp
            // the plan's flat ambient.
            planMode = !(DesignerModeController.Instance != null &&
                         DesignerModeController.Instance.Mode == DesignerMode.Plan2D);
            OnModeChanged(DesignerModeController.Instance != null
                ? DesignerModeController.Instance.Mode : DesignerMode.Plan2D);
        }

        private void OnModeChanged(DesignerMode mode)
        {
            bool wantPlan = mode == DesignerMode.Plan2D;
            if (wantPlan == planMode)
                return;
            planMode = wantPlan;
            if (planMode)
            {
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = PLAN_AMBIENT;
                IBMROS.Bridge.Interaction.SelectionService.HighlightLayerExclusionMask = wallMask;
                IBMROS.Bridge.Interaction.SelectionService.Suspended = false;
                nextApply = 0f; // tint immediately
                FramePlan();
            }
            else
            {
                // Room selection is a PLAN concept. Leaving it live in 3D let the
                // room-wide highlight tint repaint every wall/floor (rooms went
                // navy on entering furniture mode) and let its unmasked pick ray
                // fight FurniturePlacer for the placement tap.
                IBMROS.Bridge.Interaction.SelectionService.Suspended = true;
                RestoreLook();
            }
        }

        private void RestoreLook()
        {
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = AMBIENT_SKY;
            RenderSettings.ambientEquatorColor = AMBIENT_EQUATOR;
            RenderSettings.ambientGroundColor = AMBIENT_GROUND;
            ClearWallTint();
            Apply3DLook();
        }

        /// <summary>
        /// Frames the whole plan with margin on entering 2D. Without this the
        /// ortho camera kept whatever zoom it had, which often cropped the room's
        /// corners off-screen — and corners you cannot see are corners you cannot
        /// drag.
        /// </summary>
        public void FramePlan()
        {
            var ortho = Camera.main != null ? Camera.main.GetComponent<CameraTopDownOrtho>() : null;
            if (ortho == null)
                return;

            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);
            bool any = false;
            foreach (var ui in PlanEditorUtil.AllSpaces())
            {
                foreach (Vector3 wp in PlanEditorUtil.WorldPoints(ui))
                {
                    Vector2 m = PlanEditorUtil.WorldToMeters(wp);
                    min = Vector2.Min(min, m);
                    max = Vector2.Max(max, m);
                    any = true;
                }
            }
            if (!any)
                return;

            Vector2 centre = (min + max) * 0.5f;
            Vector2 size = max - min;
            float aspect = Camera.main.aspect > 0.01f ? Camera.main.aspect : 0.5f;
            // Ortho size is the vertical half-extent; respect width via aspect.
            float needed = Mathf.Max(size.y * 0.5f, size.x * 0.5f / aspect);
            float orthoSize = needed * 1.35f; // margin for handles + dimension labels

            ortho.SetResetValues(new Vector3(centre.x, 0f, centre.y),
                Quaternion.Euler(90f, 0f, 0f), orthoSize);
            ortho.ResetCamera();
        }

        /// <summary>
        /// The 2D plan's line/handle visuals use the vendor "Exoa/AlwaysOnTop"
        /// shader (ZTest Always, Queue Transparent+100), so in 3D they punch
        /// straight through walls and furniture — that was the black line drawn
        /// across every sofa at floor level. They are plan-only affordances, so
        /// their renderers are switched off outside Plan2D. Renderers only, never
        /// the GameObjects: PlanTouchController still hit-tests these transforms.
        /// </summary>
        private void SetPlanVisualsVisible(bool visible)
        {
            foreach (var cpc in FindObjectsByType<Exoa.Designer.ControlPointsController>(FindObjectsSortMode.None))
            {
                var lr = cpc.GetComponent<LineRenderer>();
                if (lr != null && lr.enabled != visible)
                    lr.enabled = visible;

                // Corner handles: the balls the user drags. Kept ACTIVE in the
                // plan (the vendor only showed them mid-draw) so every vertex is
                // always grabbable, and off in 3D.
                var pts = cpc.GetPointsList();
                if (pts != null)
                {
                    foreach (var cp in pts)
                    {
                        if (cp == null) continue;
                        if (cp.gameObject.activeSelf != visible)
                            cp.gameObject.SetActive(visible);
                    }
                }

                foreach (Renderer r in cpc.GetComponentsInChildren<Renderer>(true))
                {
                    if (r is LineRenderer)
                        continue;
                    if (r.enabled != visible)
                        r.enabled = visible;
                }
                foreach (Canvas c in cpc.GetComponentsInChildren<Canvas>(true))
                {
                    if (c.enabled != visible)
                        c.enabled = visible;
                }
            }
        }

        /// <summary>
        /// Interior texture pass for 3D (user direction: use the Room scene's
        /// textures) — generated room walls/floors/ceilings get the plaster +
        /// laminate materials. Only renderers OWNED by a room (SpaceController
        /// parent) are touched; the grid backdrop and exterior shell keep their
        /// own materials.
        /// </summary>
        private void Apply3DLook()
        {
            if (wallMaterial3D == null && floorMaterial3D == null && ceilingMaterial3D == null)
                return;
            int floorLayer = LayerMask.NameToLayer("ExoaFloor");
            int wallLayer = LayerMask.NameToLayer("Wall");
            int ceilLayer = LayerMask.NameToLayer("Ceil");
            foreach (MeshRenderer r in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                int layer = r.gameObject.layer;
                Material want = null;
                if (layer == wallLayer) want = wallMaterial3D;
                else if (layer == floorLayer) want = floorMaterial3D;
                else if (layer == ceilLayer) want = ceilingMaterial3D;
                if (want == null || r.sharedMaterial == want)
                    continue;
                if (r.GetComponentInParent<Exoa.Designer.SpaceController>() == null)
                    continue; // not room geometry (grid plane, shell, furniture)
                if (r.sharedMaterial != darkWallMat && r.sharedMaterial != selectedWallMat &&
                    !fallbackByLayer.ContainsKey(layer))
                    fallbackByLayer[layer] = r.sharedMaterial;
                r.sharedMaterial = want;
            }
        }

        private void Update()
        {
            if (Time.unscaledTime < nextApply)
                return;
            nextApply = Time.unscaledTime + REAPPLY_INTERVAL_S;
            SetPlanVisualsVisible(planMode);

            // The document loads a frame or two after this component starts, so
            // frame once as soon as there is geometry to frame.
            if (planMode && !framedOnce && PlanEditorUtil.AllSpaces().Count > 0)
            {
                framedOnce = true;
                FramePlan();
            }
            if (!planMode)
            {
                Apply3DLook(); // textured interior (rebuilds recreate renderers)
                return;
            }

            Transform selRoot = null;
            var ui = PlanEditorUtil.FindItem(
                IBMROS.Bridge.Interaction.SelectionService.Instance != null
                    ? IBMROS.Bridge.Interaction.SelectionService.Instance.SelectedId : null);
            if (ui != null && ui.drawer != null && ui.drawer.GO != null)
                selRoot = ui.drawer.GO.transform;

            foreach (MeshRenderer r in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                if (((1 << r.gameObject.layer) & wallMask) == 0)
                    continue;
                Material want = (selRoot != null && r.transform.IsChildOf(selRoot))
                    ? selectedWallMat : darkWallMat;
                if (r.sharedMaterial == want)
                    continue;
                if (r.sharedMaterial != darkWallMat && r.sharedMaterial != selectedWallMat)
                {
                    originals[r] = r.sharedMaterial; // remember before first swap
                    if (!fallbackByLayer.ContainsKey(r.gameObject.layer))
                        fallbackByLayer[r.gameObject.layer] = r.sharedMaterial;
                }
                r.sharedMaterial = want;
            }

            // Returning from 3D: floors/ceilings still carry the interior
            // textures — restore the pastel plan fill.
            if (floorMaterial3D != null || ceilingMaterial3D != null)
            {
                foreach (MeshRenderer r in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
                {
                    Material cur = r.sharedMaterial;
                    if (cur == null || (cur != floorMaterial3D && cur != ceilingMaterial3D))
                        continue;
                    Material orig;
                    if (fallbackByLayer.TryGetValue(r.gameObject.layer, out orig) && orig != null)
                        r.sharedMaterial = orig;
                }
            }

            // The visible plan "walls" are the CPC LineRenderers (interior wall
            // meshes are cm-thin from above) — accent the selected item's path.
            string selId = IBMROS.Bridge.Interaction.SelectionService.Instance != null
                ? IBMROS.Bridge.Interaction.SelectionService.Instance.SelectedId : null;
            foreach (var cpc in FindObjectsByType<Exoa.Designer.ControlPointsController>(FindObjectsSortMode.None))
            {
                var lr = cpc.GetComponent<LineRenderer>();
                if (lr == null || lr.sharedMaterial == null)
                    continue;
                if (selectedLineMat == null)
                {
                    // Clone the vendor line shader but own the colors outright —
                    // the shared asset ships semi-transparent (alpha 0.8) which
                    // washes any dark tint out over the white canvas.
                    Material template = lr.sharedMaterial;
                    darkLineMat = new Material(template) { name = "PlanLine_Dark" };
                    darkLineMat.color = WALL_TINT;
                    selectedLineMat = new Material(template) { name = "PlanLine_Selected" };
                    selectedLineMat.color = WALL_TINT_SELECTED;
                }
                bool isSelected = !string.IsNullOrEmpty(selId) && ItemOfCpc(cpc) == selId;
                Material wantLine = isSelected ? selectedLineMat : darkLineMat;
                if (lr.sharedMaterial == wantLine)
                    continue;
                if (lr.sharedMaterial != darkLineMat && lr.sharedMaterial != selectedLineMat)
                {
                    originals[lr] = lr.sharedMaterial;
                    if (fallbackLineMat == null)
                        fallbackLineMat = lr.sharedMaterial;
                }
                lr.sharedMaterial = wantLine;
            }
        }

        private static string ItemOfCpc(Exoa.Designer.ControlPointsController cpc)
        {
            foreach (var ui in FindObjectsByType<Exoa.Designer.UIBaseItem>(FindObjectsSortMode.None))
                if (ui.cpc == cpc)
                    return ui.ItemUniqueId;
            return null;
        }

        private void ClearWallTint()
        {
            // Sweep EVERY live renderer — dictionary keys go stale when the
            // vendor pools wall objects across rebuilds, so membership in
            // `originals` is an optimization, not the source of truth.
            foreach (Renderer r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (r == null || r.sharedMaterial == null)
                    continue;
                Material cur = r.sharedMaterial;
                if (cur != darkWallMat && cur != selectedWallMat &&
                    cur != darkLineMat && cur != selectedLineMat)
                    continue;
                Material orig;
                if (!originals.TryGetValue(r, out orig) || orig == null)
                {
                    if (cur == darkLineMat || cur == selectedLineMat)
                        orig = fallbackLineMat;
                    else
                        fallbackByLayer.TryGetValue(r.gameObject.layer, out orig);
                }
                if (orig != null)
                    r.sharedMaterial = orig;
            }
            originals.Clear();
        }
    }
}

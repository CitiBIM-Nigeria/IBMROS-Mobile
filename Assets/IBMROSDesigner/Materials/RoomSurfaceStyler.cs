using System.Collections.Generic;
using Exoa.Designer;
using IBMROS.Core;
using UnityEngine;
using static Exoa.Designer.DataModel;

namespace IBMROS.Designer.Materials
{
    /// <summary>
    /// Gives every room independent floor / wall / ceiling materials, persisted in
    /// the document.
    ///
    /// Why this exists rather than "just set renderer.material": the plugin builds
    /// walls, floor and ceiling as three MeshFilters that all share ONE material
    /// (RoomColorMaterial, shader Exoa/VertexColor_URP) and colours them by painting
    /// vertex colours during generation. That shader samples no texture at all, so a
    /// textured finish is impossible without replacing the material per renderer.
    /// Rooms also regenerate constantly (any edit, undo, load), and each rebuild
    /// re-applies the shared material — so styling has to be re-asserted after every
    /// rebuild, which is what the DocumentEvents subscription below is for.
    ///
    /// The plugin's own SpaceMaterialController is used where it is present (it owns
    /// tiling and the separate-wall list); this component supplies the per-room
    /// choice, the Resources-backed library and the persistence the module lacks.
    /// </summary>
    public sealed class RoomSurfaceStyler : MonoBehaviour
    {
        public static RoomSurfaceStyler Instance { get; private set; }

        /// <summary>Raised after styles are (re)applied — UI can refresh from this.</summary>
        public static event System.Action OnStylesChanged;

        private const float REASSERT_INTERVAL_S = 0.35f;
        private float nextReassert;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        private void OnEnable() => DocumentEvents.OnDocumentChanged += OnDocChanged;
        private void OnDisable() => DocumentEvents.OnDocumentChanged -= OnDocChanged;

        private void OnDestroy() { if (Instance == this) Instance = null; }

        private void OnDocChanged(DocumentChange change) => nextReassert = 0f;

        private void Update()
        {
            // Rebuilds are throttled and can land a frame or two after the event, and
            // the vendor re-assigns the shared material on every one of them. A cheap
            // periodic re-assert is far more robust here than trying to chase each
            // rebuild's completion.
            if (Time.unscaledTime < nextReassert)
                return;
            nextReassert = Time.unscaledTime + REASSERT_INTERVAL_S;
            ApplyAll();
        }

        // ------------------------------------------------------------------ query

        /// <summary>The stored style for a room item (defaults where unset).</summary>
        public static RoomStyle GetStyle(string itemId)
        {
            UIBaseItem ui = FindRoom(itemId);
            return ui != null ? StyleOf(ui) : RoomStyle.Default;
        }

        private static RoomStyle StyleOf(UIBaseItem ui)
        {
            return new RoomStyle
            {
                Floor = string.IsNullOrEmpty(ui.FloorMaterial)
                    ? RoomMaterialLibrary.DEFAULT_FLOOR : ui.FloorMaterial,
                Wall = string.IsNullOrEmpty(ui.WallMaterial)
                    ? RoomMaterialLibrary.DEFAULT_WALL : ui.WallMaterial,
                Ceiling = string.IsNullOrEmpty(ui.CeilingMaterial)
                    ? RoomMaterialLibrary.DEFAULT_CEILING : ui.CeilingMaterial,
                FloorTiling = ui.FloorTiling,
                WallTiling = ui.WallTiling,
                CeilingTiling = ui.CeilingTiling,
            };
        }

        // ------------------------------------------------------------------ mutate

        /// <summary>
        /// Sets one surface's material on a room and persists it. Routed through the
        /// undo gateway so a material change is one reversible step like any edit.
        /// </summary>
        public static bool SetMaterial(string itemId, RoomMaterialLibrary.Surface surface,
            string materialName, float tiling = 0f)
        {
            UIBaseItem ui = FindRoom(itemId);
            if (ui == null || string.IsNullOrEmpty(materialName))
                return false;

            var svc = IBMROS.Bridge.UndoRedo.UndoRedoService.Instance;
            System.IDisposable scope = svc != null
                ? (System.IDisposable)svc.BeginAction("Change " + surface + " Material")
                : null;
            try
            {
                // Write to the item's own appearance state — GetData mirrors it into
                // the document, so it saves, loads and undoes with everything else.
                switch (surface)
                {
                    case RoomMaterialLibrary.Surface.Floor:
                        ui.FloorMaterial = materialName;
                        if (tiling > 0f) ui.FloorTiling = tiling;
                        break;
                    case RoomMaterialLibrary.Surface.Wall:
                        ui.WallMaterial = materialName;
                        if (tiling > 0f) ui.WallTiling = tiling;
                        break;
                    default:
                        ui.CeilingMaterial = materialName;
                        if (tiling > 0f) ui.CeilingTiling = tiling;
                        break;
                }
                DocumentEvents.RaiseChanged(DocumentChangeKind.ItemSettings,
                    "Change " + surface + " Material");
            }
            finally
            {
                scope?.Dispose();
            }

            Instance?.ApplyAll();
            OnStylesChanged?.Invoke();
            return true;
        }

        // ------------------------------------------------------------------ apply

        /// <summary>Re-asserts every room's stored style onto its live renderers.</summary>
        public void ApplyAll()
        {
            foreach (UIBaseItem ui in FindObjectsByType<UIBaseItem>(FindObjectsSortMode.None))
            {
                if (ui.sequencingItemType != FloorMapItemType.Room &&
                    ui.sequencingItemType != FloorMapItemType.Outside)
                    continue;
                Apply(ui, StyleOf(ui));
            }
        }

        private static void Apply(UIBaseItem ui, RoomStyle style)
        {
            if (ui.drawer == null || ui.drawer.GO == null)
                return;
            Transform root = ui.drawer.GO.transform;

            Material floorMat = RoomMaterialLibrary.Resolve(RoomMaterialLibrary.Surface.Floor, style.Floor);
            Material wallMat = RoomMaterialLibrary.Resolve(RoomMaterialLibrary.Surface.Wall, style.Wall);
            Material ceilMat = RoomMaterialLibrary.Resolve(RoomMaterialLibrary.Surface.Ceiling, style.Ceiling);

            // Prefer the plugin's own controller when the prefab carries it: it owns
            // tiling and the separate-wall list, and keeps its TextureSetting state
            // consistent for anything else in the plugin that reads it.
            //
            // It assigns via .material (an INSTANCE per renderer), so it must only be
            // driven when the choice actually changed — calling it on every re-assert
            // would allocate a fresh material instance several times a second.
            var smc = root.GetComponentInChildren<SpaceMaterialController>(true);
            if (smc != null)
            {
                if (!StyleChanged(ui, style))
                    return;
                if (floorMat != null) smc.ApplyFloorMaterial(floorMat);
                if (ceilMat != null) smc.ApplyCeilingMaterial(ceilMat);
                if (wallMat != null) smc.ApplyInteriorWallMaterial(wallMat);
                // Vendor SetScale drives _MainTex, which URP/Lit ignores (it samples
                // _BaseMap), so tiling is applied here instead of via ApplyXTiling.
                ApplyTiling(root, style);
                return;
            }

            // Fallback: assign by layer. Same result, no prefab dependency — used
            // until/unless SpaceMaterialController is present on the room prefab.
            int wallLayer = LayerMask.NameToLayer("Wall");
            int floorLayer = LayerMask.NameToLayer("ExoaFloor");
            int ceilLayer = LayerMask.NameToLayer("Ceil");

            // In the 2D plan the WALLS are drawn as dark slabs (PlanLookController
            // owns that convention, matching the reference app); floor and ceiling
            // still show their real finishes. Don't fight over the wall renderers.
            bool planMode = DesignerModeController.Instance != null &&
                            DesignerModeController.Instance.Mode == DesignerMode.Plan2D;

            foreach (MeshRenderer r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                int layer = r.gameObject.layer;
                if (planMode && layer == wallLayer)
                    continue;
                Material want = layer == wallLayer ? wallMat
                              : layer == floorLayer ? floorMat
                              : layer == ceilLayer ? ceilMat : null;
                if (want != null && r.sharedMaterial != want)
                    r.sharedMaterial = want;
            }
        }

        // ------------------------------------------------------------------ plumbing

        // Last style pushed through the (instancing) plugin path, per item.
        private static readonly Dictionary<string, string> appliedStyle =
            new Dictionary<string, string>();

        private static bool StyleChanged(UIBaseItem ui, RoomStyle style)
        {
            string key = ui.ItemUniqueId;
            string sig = style.Floor + "|" + style.Wall + "|" + style.Ceiling + "|" +
                         style.FloorTiling + "|" + style.WallTiling + "|" + style.CeilingTiling + "|" +
                         ui.drawer.GO.GetInstanceID(); // regenerated room = fresh renderers
            if (appliedStyle.TryGetValue(key, out string prev) && prev == sig)
                return false;
            appliedStyle[key] = sig;
            return true;
        }

        /// <summary>Tiling on URP/Lit: the sampled texture is _BaseMap, not _MainTex.</summary>
        private static void ApplyTiling(Transform root, RoomStyle style)
        {
            int wallLayer = LayerMask.NameToLayer("Wall");
            int floorLayer = LayerMask.NameToLayer("ExoaFloor");
            int ceilLayer = LayerMask.NameToLayer("Ceil");
            foreach (MeshRenderer r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                int layer = r.gameObject.layer;
                float t = layer == wallLayer ? style.WallTiling
                        : layer == floorLayer ? style.FloorTiling
                        : layer == ceilLayer ? style.CeilingTiling : 0f;
                if (t <= 0f || r.sharedMaterial == null)
                    continue;
                r.sharedMaterial.SetTextureScale("_BaseMap", Vector2.one * t);
            }
        }

        private static UIBaseItem FindRoom(string itemId)
        {
            if (string.IsNullOrEmpty(itemId))
                return null;
            foreach (UIBaseItem ui in FindObjectsByType<UIBaseItem>(FindObjectsSortMode.None))
                if (ui.ItemUniqueId == itemId)
                    return ui;
            return null;
        }

        public struct RoomStyle
        {
            public string Floor, Wall, Ceiling;
            public float FloorTiling, WallTiling, CeilingTiling;

            public static RoomStyle Default => new RoomStyle
            {
                Floor = RoomMaterialLibrary.DEFAULT_FLOOR,
                Wall = RoomMaterialLibrary.DEFAULT_WALL,
                Ceiling = RoomMaterialLibrary.DEFAULT_CEILING,
                FloorTiling = 1f, WallTiling = 1f, CeilingTiling = 1f,
            };
        }
    }
}

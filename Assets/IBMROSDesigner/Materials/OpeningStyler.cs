using Exoa.Designer;
using IBMROS.Core;
using UnityEngine;
using static Exoa.Designer.DataModel;

namespace IBMROS.Designer.Materials
{
    /// <summary>
    /// Applies each door/window's chosen MODEL and per-part materials.
    ///
    /// Separation of concerns, deliberately: the wall hole is cut by the room
    /// from the opening's control point and is completely independent of what
    /// the opening looks like. This component only touches the visual — so a
    /// custom door model can never break wall cutting, placement, snapping or
    /// undo.
    ///
    /// Model swap: the plugin instantiates its procedural OpeningPrefab per
    /// control point and generates leaf/glass/handle meshes into it. When an item
    /// names a model, that procedural visual is deactivated and the model prefab
    /// is parented in its place at identity local transform (the plugin has
    /// already positioned and oriented the parent along the wall), then scaled
    /// from the model's authored reference size to the opening's real size.
    ///
    /// Empty model id = the procedural door/window, i.e. previous behaviour.
    /// </summary>
    public sealed class OpeningStyler : MonoBehaviour
    {
        public static OpeningStyler Instance { get; private set; }

        private const string MODEL_CHILD = "IBMROS_OpeningModel";
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
            // Openings are re-instantiated on rebuilds, so the visual has to be
            // re-asserted rather than applied once.
            if (Time.unscaledTime < nextReassert)
                return;
            nextReassert = Time.unscaledTime + REASSERT_INTERVAL_S;
            ApplyAll();
        }

        // ------------------------------------------------------------------ mutate

        /// <summary>Sets the visual model for an opening (null/empty = procedural).</summary>
        public static bool SetModel(string itemId, string modelId)
        {
            UIBaseItem ui = FindOpening(itemId);
            if (ui == null)
                return false;
            Edit(ui, "Change Opening Model", () => ui.OpeningModel = modelId);
            return true;
        }

        public enum Part { Frame, Glass, Handle }

        /// <summary>Sets one part's material on an opening.</summary>
        public static bool SetPartMaterial(string itemId, Part part, string materialName)
        {
            UIBaseItem ui = FindOpening(itemId);
            if (ui == null)
                return false;
            Edit(ui, "Change " + part + " Material", () =>
            {
                switch (part)
                {
                    case Part.Frame: ui.FrameMaterial = materialName; break;
                    case Part.Glass: ui.GlassMaterial = materialName; break;
                    default: ui.HandleMaterial = materialName; break;
                }
            });
            return true;
        }

        /// <summary>One reversible step + document announcement, then re-apply.</summary>
        private static void Edit(UIBaseItem ui, string label, System.Action mutate)
        {
            var svc = IBMROS.Bridge.UndoRedo.UndoRedoService.Instance;
            System.IDisposable scope = svc != null
                ? (System.IDisposable)svc.BeginAction(label) : null;
            try
            {
                mutate();
                DocumentEvents.RaiseChanged(DocumentChangeKind.ItemSettings, label);
            }
            finally { scope?.Dispose(); }
            Instance?.ApplyAll();
        }

        // ------------------------------------------------------------------ apply

        public void ApplyAll()
        {
            foreach (UIBaseItem ui in FindObjectsByType<UIBaseItem>(FindObjectsSortMode.None))
            {
                if (!IsOpening(ui) || ui.drawer == null || ui.drawer.GO == null)
                    continue;
                Apply(ui);
            }
        }

        private static void Apply(UIBaseItem ui)
        {
            OpeningStyleLibrary.OpeningModel model =
                OpeningStyleLibrary.GetModel(ui.OpeningModel);

            // The plugin makes one visual instance per control point.
            foreach (ProceduralOpening po in
                     ui.drawer.GO.GetComponentsInChildren<ProceduralOpening>(true))
            {
                Transform host = po.transform;
                Transform custom = host.Find(MODEL_CHILD);

                if (model == null)
                {
                    // Procedural look: drop any custom model, re-show the generated
                    // meshes, and tint the generated parts.
                    if (custom != null)
                        Destroy(custom.gameObject);
                    SetProceduralVisible(po, true);
                    ApplyPartMaterials(ui, po.doorMf, po.glassMf, po.handleMf);
                    ApplyFacing(ui, po, null);
                    continue;
                }

                SetProceduralVisible(po, false);
                if (custom == null || custom.name != MODEL_CHILD ||
                    custom.GetComponent<ModelTag>()?.id != model.Id)
                {
                    if (custom != null)
                        Destroy(custom.gameObject);
                    GameObject inst = Instantiate(model.Prefab, host);
                    inst.name = MODEL_CHILD;
                    inst.AddComponent<ModelTag>().id = model.Id;
                    inst.transform.localPosition = Vector3.zero;
                    inst.transform.localRotation = Quaternion.identity;
                    custom = inst.transform;
                }

                // Scale the authored model to this opening's real size. Width runs
                // along local X (the wall tangent), height along local Y.
                float w = ui.Width > 0.01f ? ui.Width : model.ReferenceSize.x;
                float h = ui.Height > 0.01f ? ui.Height : model.ReferenceSize.y;
                custom.localScale = new Vector3(
                    w / Mathf.Max(0.01f, model.ReferenceSize.x),
                    h / Mathf.Max(0.01f, model.ReferenceSize.y),
                    1f);

                ApplyModelMaterials(ui, custom);
                ApplyFacing(ui, po, custom);
            }
        }

        /// <summary>
        /// Turns the opening's visual around within its wall plane (item field
        /// openingFlipped) — the reference app's "Rotate" for a door: hinge side and
        /// swing direction mirror, nothing else changes.
        ///
        /// Applied to the CHILD meshes, never to the instance itself: the plugin rewrites
        /// the instance's world rotation from the wall tangent on every rebuild, so a
        /// flip put there survives no time at all. Touching only children also keeps the
        /// wall hole out of it — the cut is generated from the control point, so however
        /// the door is turned the opening in the wall is identical.
        ///
        /// THE PIVOT MATTERS. The generated meshes are NOT centred on their transform:
        /// ProceduralOpening builds them offset −Width/2 along local X (that is how the
        /// instance-at-edge / hole-at-control-point geometry cancels out). A naive
        /// 180° localRotation therefore swings the whole leaf to the OTHER side of the
        /// pivot — a full width out of the hole, hanging in the room — which is exactly
        /// the detached-door screenshot. So after rotating, each part is translated
        /// along the wall so its rendered bounds centre lands back where it started;
        /// only the mirroring (and which face of the wall the leaf swings toward)
        /// remains. Re-measured every apply, so it is exact for any mesh layout,
        /// including handle spheres and custom models with arbitrary pivots.
        /// </summary>
        private static void ApplyFacing(UIBaseItem ui, ProceduralOpening po, Transform custom)
        {
            FacingTag tag = po.GetComponent<FacingTag>();
            bool want = ui.OpeningFlipped;
            // Idempotence: rebuilds re-run this constantly; only touch transforms when
            // the facing, the geometry (width regenerates the meshes) or the custom
            // model instance changed — a freshly swapped model needs its flip too.
            if (tag != null && tag.flipped == want && tag.width == ui.Width && tag.custom == custom)
                return;
            if (tag == null)
                tag = po.gameObject.AddComponent<FacingTag>();
            tag.flipped = want;
            tag.width = ui.Width;
            tag.custom = custom;

            Vector3 tangent = po.transform.right.normalized; // instance X runs along the wall (up to sign — reflection is sign-agnostic)
            Quaternion rot = want ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity;

            // The mirror plane passes through the OPENING's centre (the control point).
            // Reflecting each part about its OWN bounds centre would pin everything in
            // place — a symmetric handle sphere would flip to exactly where it started,
            // erasing the hinge swap that is the whole point of the operation.
            Vector2? c = IBMROS.Designer.Openings.OpeningAnchor.CentreOf(ui);
            Vector3 mirrorPoint = c.HasValue
                ? new Vector3(c.Value.x, 0f, c.Value.y)
                : po.transform.position;

            FlipPart(po, po.doorMf != null ? po.doorMf.transform : null, rot, tangent, mirrorPoint, want);
            FlipPart(po, po.glassMf != null ? po.glassMf.transform : null, rot, tangent, mirrorPoint, want);
            FlipPart(po, po.handleMf != null ? po.handleMf.transform : null, rot, tangent, mirrorPoint, want);
            FlipPart(po, custom, rot, tangent, mirrorPoint, want);
        }

        private static void FlipPart(ProceduralOpening po, Transform part, Quaternion rot,
            Vector3 tangent, Vector3 mirrorPoint, bool flipped)
        {
            if (part == null || part == po.transform)
                return;

            // Reset to the authored pose, measure, rotate.
            part.localRotation = Quaternion.identity;
            part.localPosition = Vector3.zero;
            Bounds? before = RenderedBounds(part);
            part.localRotation = rot;
            if (!flipped || !before.HasValue)
                return;
            Bounds? after = RenderedBounds(part);
            if (!after.HasValue)
                return;

            // True mirror: this part's centre must land at the reflection of where it
            // started, about the plane through the opening centre normal to the wall.
            // For the full-width leaf that is (almost) where it already was; for the
            // handle it is the OTHER side of the doorway — the visible hinge swap.
            // Height is untouched by a yaw; the wall-normal offset is left to the
            // rotation on purpose (the leaf swinging toward the other face IS the flip).
            float along = Vector3.Dot(before.Value.center - mirrorPoint, tangent);
            Vector3 wanted = before.Value.center - 2f * along * tangent;
            Vector3 drift = after.Value.center - wanted;
            part.position -= Vector3.Project(drift, tangent);
        }

        private static Bounds? RenderedBounds(Transform t)
        {
            Renderer[] rs = t.GetComponentsInChildren<Renderer>(true);
            bool has = false;
            Bounds b = default;
            foreach (Renderer r in rs)
            {
                if (!has) { b = r.bounds; has = true; }
                else b.Encapsulate(r.bounds);
            }
            return has ? b : (Bounds?)null;
        }

        /// <summary>Remembers what facing/width a visual instance was last styled for.</summary>
        private sealed class FacingTag : MonoBehaviour
        {
            public bool flipped;
            public float width;
            public Transform custom;
        }

        private static void SetProceduralVisible(ProceduralOpening po, bool visible)
        {
            SetRendererEnabled(po.doorMf, visible);
            SetRendererEnabled(po.glassMf, visible);
            SetRendererEnabled(po.handleMf, visible);
        }

        private static void SetRendererEnabled(MeshFilter mf, bool on)
        {
            if (mf == null) return;
            var r = mf.GetComponent<Renderer>();
            if (r != null && r.enabled != on) r.enabled = on;
        }

        /// <summary>Per-part override on the plugin's generated meshes.</summary>
        private static void ApplyPartMaterials(UIBaseItem ui, MeshFilter frame,
            MeshFilter glass, MeshFilter handle)
        {
            Assign(frame, OpeningStyleLibrary.ResolveMaterial(ui.FrameMaterial));
            Assign(glass, OpeningStyleLibrary.ResolveMaterial(ui.GlassMaterial));
            Assign(handle, OpeningStyleLibrary.ResolveMaterial(ui.HandleMaterial));
        }

        /// <summary>Per-part override on a custom model, via its marker's renderers.</summary>
        private static void ApplyModelMaterials(UIBaseItem ui, Transform model)
        {
            var marker = model.GetComponent<OpeningModelMarker>();
            Material frame = OpeningStyleLibrary.ResolveMaterial(ui.FrameMaterial);
            Material glass = OpeningStyleLibrary.ResolveMaterial(ui.GlassMaterial);
            Material handle = OpeningStyleLibrary.ResolveMaterial(ui.HandleMaterial);

            if (marker != null)
            {
                Assign(marker.frameRenderer, frame);
                Assign(marker.glassRenderer, glass);
                Assign(marker.handleRenderer, handle);
                return;
            }
            // No marker: the frame material is the sensible whole-model override.
            if (frame == null)
                return;
            foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true))
                if (r.sharedMaterial != frame)
                    r.sharedMaterial = frame;
        }

        private static void Assign(MeshFilter mf, Material m)
        {
            if (mf == null || m == null) return;
            Assign(mf.GetComponent<Renderer>(), m);
        }

        private static void Assign(Renderer r, Material m)
        {
            if (r == null || m == null) return;
            if (r.sharedMaterial != m)
                r.sharedMaterial = m;
        }

        // ------------------------------------------------------------------ helpers

        private static bool IsOpening(UIBaseItem ui) =>
            ui.sequencingItemType == FloorMapItemType.Door ||
            ui.sequencingItemType == FloorMapItemType.Window ||
            ui.sequencingItemType == FloorMapItemType.Opening;

        private static UIBaseItem FindOpening(string itemId)
        {
            if (string.IsNullOrEmpty(itemId))
                return null;
            foreach (UIBaseItem ui in FindObjectsByType<UIBaseItem>(FindObjectsSortMode.None))
                if (ui.ItemUniqueId == itemId && IsOpening(ui))
                    return ui;
            return null;
        }

        private sealed class ModelTag : MonoBehaviour { public string id; }
    }
}

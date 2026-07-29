using System.Collections;
using IBMROS.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace IBMROS.Designer.ThreeD
{
    /// <summary>
    /// Runtime lighting for procedurally generated rooms (INTEGRATION_MAP §8).
    /// Generated meshes can't be lightmapped, so the look is a tuned realtime rig:
    ///   • warm soft-shadowed key directional (also RenderSettings.sun)
    ///   • Trilight ambient so interiors aren't flat gray
    ///   • one box-projected realtime reflection probe re-rendered after the
    ///     document changes (ViaScripting — free except on demand)
    ///   • solid-color camera background per mode (paper white in 2D plan,
    ///     soft neutral in 3D)
    /// </summary>
    public sealed class LightingRig : MonoBehaviour
    {
        private static readonly Color KEY_COLOR = new Color(1f, 0.96f, 0.84f);
        private static readonly Color AMBIENT_SKY = new Color(0.55f, 0.57f, 0.60f);
        private static readonly Color AMBIENT_EQUATOR = new Color(0.42f, 0.42f, 0.42f);
        private static readonly Color AMBIENT_GROUND = new Color(0.25f, 0.24f, 0.22f);
        private static readonly Color BG_PLAN = new Color(0.93f, 0.93f, 0.92f);
        private static readonly Color BG_3D = new Color(0.80f, 0.83f, 0.86f);
        private const float PROBE_DEBOUNCE_S = 1.0f;

        private ReflectionProbe probe;
        private Coroutine probeRefresh;

        private void Start()
        {
            // Key light — reuse the scene's directional light.
            Light key = RenderSettings.sun;
            if (key == null)
            {
                foreach (Light l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                    if (l.type == LightType.Directional) { key = l; break; }
            }
            if (key != null)
            {
                key.color = KEY_COLOR;
                key.intensity = 1.25f;
                // Sun travels toward -Z: it enters through the presets' WINDOW
                // wall (+Z) and cannot beam through the door on the -Z wall
                // (user report: "light coming from the door"). Steep pitch keeps
                // interior beams short.
                key.transform.rotation = Quaternion.Euler(62f, 180f, 0f);
                key.shadows = LightShadows.Soft;
                key.shadowStrength = 0.5f;
                RenderSettings.sun = key;
            }

            // Ambient is owned by PlanLookController (flat white in 2D plan,
            // this rig's trilight values in 3D) — setting it here raced its
            // Start order and stomped the plan look.

            var probeGo = new GameObject("RoomReflectionProbe");
            probeGo.transform.SetParent(transform, false);
            probe = probeGo.AddComponent<ReflectionProbe>();
            probe.mode = UnityEngine.Rendering.ReflectionProbeMode.Realtime;
            probe.refreshMode = UnityEngine.Rendering.ReflectionProbeRefreshMode.ViaScripting;
            probe.timeSlicingMode = UnityEngine.Rendering.ReflectionProbeTimeSlicingMode.AllFacesAtOnce;
            probe.resolution = 128;
            probe.boxProjection = true;

            DocumentEvents.OnDocumentChanged += OnDocChanged;
            DesignerModeController.OnModeChanged += ApplyBackground;
            ApplyBackground(DesignerModeController.Instance != null
                ? DesignerModeController.Instance.Mode : DesignerMode.Plan2D);
            ScheduleProbeRefresh();
        }

        private void OnDestroy()
        {
            DocumentEvents.OnDocumentChanged -= OnDocChanged;
            DesignerModeController.OnModeChanged -= ApplyBackground;
        }

        private void OnDocChanged(DocumentChange change)
        {
            if (change.Kind == DocumentChangeKind.Geometry ||
                change.Kind == DocumentChangeKind.Opening ||
                change.Kind == DocumentChangeKind.BuildingSettings ||
                change.Kind == DocumentChangeKind.Delete ||
                change.Kind == DocumentChangeKind.Duplicate)
                ScheduleProbeRefresh();
        }

        /// <summary>Gradient sky shown through openings in 3D. Resources so it ships.</summary>
        private const string SKY_MATERIAL = "Sky/Sky_Daylight";
        private static Material sky;

        private void ApplyBackground(DesignerMode mode)
        {
            Camera cam = Camera.main;
            if (cam == null)
                return;

            if (mode == DesignerMode.Plan2D)
            {
                // The plan is a drawing: flat paper, no horizon.
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = BG_PLAN;
                return;
            }

            // A window used to look out onto the camera's flat clear colour, because the
            // scene had no skybox assigned at all (m_SkyboxMaterial: 0) and the camera
            // cleared to a solid grey. So the one thing that makes an interior read as
            // real — that you can see OUT of it — was simply absent: every opening was a
            // grey rectangle. A gradient sky costs nothing on mobile (no cubemap, no
            // texture fetch) and gives openings a horizon.
            if (sky == null)
                sky = Resources.Load<Material>(SKY_MATERIAL);
            if (sky != null)
            {
                RenderSettings.skybox = sky;
                cam.clearFlags = CameraClearFlags.Skybox;
            }
            else
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = BG_3D;
            }
        }

        private void ScheduleProbeRefresh()
        {
            if (!isActiveAndEnabled)
                return;
            if (probeRefresh != null)
                StopCoroutine(probeRefresh);
            probeRefresh = StartCoroutine(RefreshProbe());
        }

        private IEnumerator RefreshProbe()
        {
            // Let the scoped rebuild + shell rebuild settle before rendering.
            yield return new WaitForSeconds(PROBE_DEBOUNCE_S);
            probeRefresh = null;

            Bounds b = RoomBounds();
            if (b.size.sqrMagnitude < 0.01f)
                yield break;
            probe.transform.position = b.center;
            probe.center = Vector3.zero;
            probe.size = b.size + new Vector3(0.5f, 0.5f, 0.5f);
            probe.RenderProbe();
        }

        /// <summary>World bounds of all generated room geometry (walls layer 10, floors 9, ceilings 14).</summary>
        private static Bounds RoomBounds()
        {
            var bounds = new Bounds();
            bool first = true;
            int mask = LayerMask.GetMask("Wall", "ExoaFloor", "Ceil");
            foreach (MeshRenderer r in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                if (((1 << r.gameObject.layer) & mask) == 0)
                    continue;
                if (first) { bounds = r.bounds; first = false; }
                else bounds.Encapsulate(r.bounds);
            }
            return first ? new Bounds(Vector3.zero, Vector3.zero) : bounds;
        }
    }
}

using System;
using Exoa.Cameras;
using Exoa.Events;
using UnityEngine;

namespace IBMROS.Designer
{
    public enum DesignerMode { Plan2D, Orbit3D, Walkthrough }

    /// <summary>
    /// Single authority for the designer's view mode. 2D↔3D rides the existing
    /// CameraModeSwitcher (no public toggle method exists — the supported entry
    /// is CameraEvents.OnRequestButtonAction(SwitchPerspective), INTEGRATION_MAP §6).
    /// Walkthrough is a Phase-3 state: it requires the camera-component handoff
    /// (disable switcher + both mode cameras, reset projection); until then
    /// SetMode(Walkthrough) is rejected with a warning.
    /// </summary>
    public sealed class DesignerModeController : MonoBehaviour
    {
        public static DesignerModeController Instance { get; private set; }

        /// <summary>Raised after the mode actually changed (camera switch confirmed).</summary>
        public static event Action<DesignerMode> OnModeChanged;

        public DesignerMode Mode { get; private set; } = DesignerMode.Plan2D;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void Start()
        {
            // The switcher fires its initial switch events in its own Awake —
            // before anyone can subscribe — so query instead (INTEGRATION_MAP §3).
            CameraModeSwitcher sw = CameraModeSwitcher.Instance;
            Mode = (sw == null || sw.CurrentCameraMode is CameraTopDownOrtho)
                ? DesignerMode.Plan2D
                : DesignerMode.Orbit3D;
            CameraEvents.OnAfterSwitchPerspective += HandleAfterSwitch;
            OnModeChanged?.Invoke(Mode);
        }

        private void OnDestroy()
        {
            CameraEvents.OnAfterSwitchPerspective -= HandleAfterSwitch;
            if (Instance == this)
                Instance = null;
        }

        private void HandleAfterSwitch(bool orthoMode)
        {
            if (Mode == DesignerMode.Walkthrough)
                return; // Phase 3 owns transitions in/out of walkthrough
            DesignerMode next = orthoMode ? DesignerMode.Plan2D : DesignerMode.Orbit3D;
            if (next == Mode)
                return;
            Mode = next;
            OnModeChanged?.Invoke(Mode);
        }

        /// <summary>Animated ortho↔perspective switch via the vendor rig.</summary>
        public void Toggle2D3D()
        {
            CameraEvents.OnRequestButtonAction?.Invoke(CameraEvents.Action.SwitchPerspective, true);
        }

        public void Set2D()
        {
            if (Mode == DesignerMode.Orbit3D)
                Toggle2D3D();
        }

        public void Set3D()
        {
            if (Mode == DesignerMode.Plan2D)
                Toggle2D3D();
        }
    }
}

using System;
using Exoa.Cameras;
using Exoa.Events;
using UnityEngine;

namespace IBMROS.Designer.Plan
{
    /// <summary>
    /// The single owner of "may the Exoa camera rig respond to drags?".
    ///
    /// WHY THIS EXISTS. DisableCameraMoves is one global flag that four unrelated call
    /// sites used to write directly: the plan touch layer on press, its drag-state
    /// teardown, the furnish adapter on grab/release, and the vendor's own
    /// ControlPointsController. Each writer knew only its own reason, so whichever ran
    /// last in a frame decided the outcome — and since none of those components
    /// declares an execution order, "last" was undefined. Two real failures came out
    /// of that:
    ///
    ///   • PlanTouchController.Update calls CancelDrag() on every frame that furniture
    ///     owns the pointer, and the teardown inside it handed the camera back. So
    ///     every frame of a furniture drag re-enabled the ortho pan: the item followed
    ///     the finger AND the whole plan slid under it — the reported "I can click the
    ///     chair but dragging just moves the whole building".
    ///   • After any furniture drag the adapter released suppression and nothing
    ///     re-asserted the plan's own policy, leaving Plan2D freely pannable
    ///     (measured: DisableMoves == false at rest, though the policy says muted).
    ///
    /// The fix is ownership, not another ordering patch. Callers now declare a REASON
    /// they want the rig held ("furniture owns the pointer") and this class derives the
    /// flag from the set of live reasons. Suppression is their union, so no writer can
    /// cancel another's intent, and a reason can only be cleared by whoever raised it.
    ///
    /// The state is re-asserted every frame from a negative execution order — ahead of
    /// every camera that samples input — which makes the outcome independent of
    /// component order, and bounds any stray writer to zero frames of effect (the
    /// vendor event is intercepted too, see OnVendorRequest).
    ///
    /// Panning the plan is OPT-IN: Plan2D holds PlanMode for its whole duration, and a
    /// gesture positively identified as starting on empty canvas releases that one
    /// token for the length of the gesture. A press we fail to classify therefore does
    /// nothing at all, instead of dragging the room out from under the finger.
    /// </summary>
    [DefaultExecutionOrder(ORDER)]
    public sealed class PlanCameraGate : MonoBehaviour
    {
        /// <summary>
        /// Runs before the Exoa cameras (all of which sit at the default 0), so the
        /// flag they read in their own Update is the one this frame's reasons imply.
        /// </summary>
        public const int ORDER = -500;

        /// <summary>Why the rig is being held. Suppression is the union of these.</summary>
        [Flags]
        public enum Reason
        {
            None = 0,
            /// <summary>The 2D plan is open: a one-finger drag edits the plan, it does not pan.</summary>
            PlanMode = 1 << 0,
            /// <summary>A plan geometry drag (corner / wall / room / opening) is in flight.</summary>
            PlanDrag = 1 << 1,
            /// <summary>A furniture drag, rotate or scale owns the pointer.</summary>
            Furniture = 1 << 2,
            /// <summary>The catalog is open, or a ghost is steering with the pointer.</summary>
            FurnishUi = 1 << 3,
        }

        private static Reason held;
        private static PlanCameraGate instance;
        private static CameraBase[] rigs;

        /// <summary>The reasons currently held (diagnostics / tests).</summary>
        public static Reason Held => held;

        /// <summary>True when the rig is standing down.</summary>
        public static bool Suppressed => held != Reason.None;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            held = Reason.None;
            instance = null;
            rigs = null;
        }

        /// <summary>
        /// Creates the gate on demand, so it needs no scene wiring (same self-install
        /// pattern the bridge services use).
        /// </summary>
        public static void Ensure()
        {
            if (instance != null)
                return;
            instance = FindAnyObjectByType<PlanCameraGate>(FindObjectsInactive.Include);
            if (instance != null)
                return;
            instance = new GameObject("IBMROS_PlanCameraGate").AddComponent<PlanCameraGate>();
        }

        public static void Hold(Reason reason) => Set(reason, true);
        public static void Release(Reason reason) => Set(reason, false);

        /// <summary>Raises or clears one reason. Takes effect immediately, not next frame.</summary>
        public static void Set(Reason reason, bool hold)
        {
            Reason next = hold ? (held | reason) : (held & ~reason);
            if (next == held)
                return;
            held = next;
            Apply();
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(this);
                return;
            }
            instance = this;
        }

        private void OnEnable()
        {
            // Intercept the vendor transport as well: anything that invokes the event
            // directly (the vendor ControlPointsController does) is overridden in the
            // same frame rather than winning until our next Update.
            CameraEvents.OnRequestButtonAction += OnVendorRequest;
        }

        private void OnDisable()
        {
            CameraEvents.OnRequestButtonAction -= OnVendorRequest;
        }

        private void OnDestroy()
        {
            if (instance == this)
                instance = null;
        }

        private static void OnVendorRequest(CameraEvents.Action action, bool active)
        {
            if (action != CameraEvents.Action.DisableCameraMoves)
                return;
            // Our reasons decide, not the caller's opinion. The vendor's listeners have
            // already applied `active` by the time this runs (delegate order), so put
            // the derived value back.
            Apply();
        }

        // Cheap (two field compares) and it makes ordering irrelevant.
        private void Update() => Apply();

        private static void Apply()
        {
            bool suppress = held != Reason.None;
            foreach (CameraBase rig in Rigs())
            {
                if (rig != null && rig.DisableMoves != suppress)
                    rig.DisableMoves = suppress;
            }
        }

        /// <summary>
        /// The ortho + perspective mode components. Cached; refreshed when the cache is
        /// empty or any entry has been destroyed (scene change, rig rebuild).
        /// </summary>
        private static CameraBase[] Rigs()
        {
            if (rigs != null && rigs.Length > 0)
            {
                bool intact = true;
                for (int i = 0; i < rigs.Length; i++)
                {
                    if (rigs[i] == null)
                    {
                        intact = false;
                        break;
                    }
                }
                if (intact)
                    return rigs;
            }
            rigs = FindObjectsByType<CameraBase>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            return rigs;
        }
    }
}

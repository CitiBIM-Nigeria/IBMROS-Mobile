using Exoa.Events;
using UnityEngine;

namespace IBMROS.Core
{
    /// <summary>
    /// A7 — defers the building-shell rebuild by one frame so it runs AFTER the
    /// deleted/edited geometry it depends on is actually gone. Unity's Destroy is
    /// end-of-frame, so firing OnRequestRebuildBuilding synchronously from a delete
    /// path rebuilt the exterior shell while the deleted room still existed → the shell
    /// wrongly kept the removed room's contour (caught by the scoped==full regression).
    /// Coalesces many requests in a frame into a single rebuild. First step toward the
    /// unified rebuild scheduler of A7 stage 3.
    /// </summary>
    public sealed class RebuildScheduler : MonoBehaviour
    {
        private static RebuildScheduler instance;
        private int fireOnFrame = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap() => Ensure();

        private static void Ensure()
        {
            if (instance != null)
                return;
            instance = GameObject.FindAnyObjectByType<RebuildScheduler>();
            if (instance == null)
                instance = new GameObject("IBMROS_RebuildScheduler").AddComponent<RebuildScheduler>();
        }

        /// <summary>Rebuild the building shell/roof next frame (after this frame's destroys settle).</summary>
        public static void RequestBuildingRebuild()
        {
            Ensure();
            // +1 so it fires on a later frame than the request; end-of-frame Destroy of
            // the current edit's GameObjects will have completed by then.
            instance.fireOnFrame = Time.frameCount + 1;
        }

        private void Update()
        {
            if (fireOnFrame >= 0 && Time.frameCount >= fireOnFrame)
            {
                fireOnFrame = -1;
                GameEditorEvents.OnRequestRebuildBuilding?.Invoke();
            }
        }
    }
}

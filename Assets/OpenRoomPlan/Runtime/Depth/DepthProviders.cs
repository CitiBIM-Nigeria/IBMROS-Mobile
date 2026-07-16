using UnityEngine;
using OpenRoomPlan.Core;

namespace OpenRoomPlan.Depth
{
    /// <summary>
    /// Registers the on-device depth models into <see cref="DepthModelRegistry"/> so the benchmark / eval
    /// harness can create them by id without referencing this assembly directly ("add a model = a file +
    /// one registration line", spec §13). Runs at load in both player and editor.
    /// </summary>
    public static class DepthProviders
    {
        public const string DAV2SmallId = "depth-anything-v2-small";

        static bool _registered;

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#endif
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void RegisterAll()
        {
            if (_registered) return;
            _registered = true;

            DepthModelRegistry.Register(DAV2SmallId, () => new DANetDepthProvider(new DANetConfig()));
        }
    }
}

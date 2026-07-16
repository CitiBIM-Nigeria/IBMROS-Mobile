using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR.ARFoundation;

namespace OpenRoomPlan.Editor
{
    /// <summary>
    /// Ensures the URP renderer assets carry the renderer features AR Foundation needs:
    ///   • <see cref="ARBackgroundRendererFeature"/> — actually draws the camera image under URP.
    ///     Without it the AR session runs (pose, planes) but the view shows the skybox.
    ///   • ARCommandBufferSupportRendererFeature — required by AR Foundation 6.x (hard error on
    ///     Android/Vulkan, previously added to Mobile_Renderer by hand).
    /// Applies to Mobile_Renderer (Android + iPhone both use the Mobile quality tier) and PC_Renderer
    /// (Editor / XR Simulation). Idempotent — safe to re-run. Menu: OpenRoomPlan ▸ Fix URP AR Renderer Features.
    /// </summary>
    public static class UrpArSetup
    {
        static readonly string[] RendererPaths =
        {
            "Assets/Settings/Mobile_Renderer.asset",
            "Assets/Settings/PC_Renderer.asset",
        };

        // Self-heal on load (once per editor session): the missing background feature shows as
        // "AR runs but the view is skybox", which is too easy to ship by accident.
        [InitializeOnLoadMethod]
        static void EnsureOnLoad()
        {
            if (SessionState.GetBool("orp.urpArSetup.ran", false)) return;
            SessionState.SetBool("orp.urpArSetup.ran", true);
            EditorApplication.delayCall += EnsureArRendererFeatures; // after asset db is ready
        }

        [MenuItem("OpenRoomPlan/Fix URP AR Renderer Features")]
        public static void EnsureArRendererFeatures()
        {
            int added = 0;
            foreach (var path in RendererPaths)
            {
                var data = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(path);
                if (data == null) { Debug.LogWarning($"[ORP] Renderer data not found: {path}"); continue; }

                added += EnsureFeature<ARBackgroundRendererFeature>(data, path) ? 1 : 0;
                added += EnsureFeature<ARCommandBufferSupportRendererFeature>(data, path) ? 1 : 0;
            }

            if (added > 0) AssetDatabase.SaveAssets();
            Debug.Log($"[ORP] URP AR renderer features: {added} added (0 added = already configured).");
        }

        /// <summary>Add feature T to the renderer data as a sub-asset if not present. True if added.</summary>
        static bool EnsureFeature<T>(ScriptableRendererData data, string path) where T : ScriptableRendererFeature
        {
            if (data.rendererFeatures.Any(f => f is T)) return false;

            var feature = ScriptableObject.CreateInstance<T>();
            feature.name = typeof(T).Name;
            AssetDatabase.AddObjectToAsset(feature, data);
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out _, out long localId);

            // Mirror URP's own editor: append to both the feature list and the id map.
            var so = new SerializedObject(data);
            var list = so.FindProperty("m_RendererFeatures");
            list.arraySize++;
            list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = feature;
            var map = so.FindProperty("m_RendererFeatureMap");
            map.arraySize++;
            map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(data);
            Debug.Log($"[ORP] Added {typeof(T).Name} to {path}");
            return true;
        }
    }
}

using System.Collections;
using System.IO;
using Exoa.Designer;
using IBMROS.Core;
using UnityEngine;

namespace IBMROS.Designer
{
    /// <summary>
    /// Scene entry for RoomDesigner.unity.
    ///
    /// Waits for the Exoa runtime to finish booting (UISaving.startAction=CreateNew
    /// loads an empty document during its Start), then applies the launch payload:
    ///   NewFromPreset → generate the preset through the gateway (one undo step),
    ///                   name the plan uniquely and write the first save,
    ///   LoadPlan      → UISaving.LoadInternal(name),
    ///   None (direct editor play) → default preset, same as NewFromPreset.
    ///
    /// Guard rails (INTEGRATION_MAP §2/§11 risk 1): never mutate before
    /// FloorPlanEditor.IsAvailable AND serializer.Document.IsLoaded — gateway
    /// creates silently no-op (returning a phantom id) on an unloaded document.
    /// </summary>
    public sealed class RoomDesignerBootstrap : MonoBehaviour
    {
        private const float BOOT_TIMEOUT_SECONDS = 10f;
        private const float FIRST_SAVE_SETTLE_SECONDS = 1.5f;

        /// <summary>Set once the payload has been applied (presets built / plan loaded).</summary>
        public static bool Ready { get; private set; }

        /// <summary>The current plan name (file name without extension).</summary>
        public static string PlanName { get; private set; }

        private void Awake()
        {
            Ready = false;
            PlanName = null;
#if UNITY_EDITOR
            // Unfocused editors honor runInBackground=false (mobile default) and
            // freeze the player loop, which stalls camera springs/coroutines when
            // the editor is driven headlessly (MCP). Editor-only override.
            Application.runInBackground = true;
#endif
        }

        private IEnumerator Start()
        {
            FloorMapSerializer serializer = null;
            float deadline = Time.realtimeSinceStartup + BOOT_TIMEOUT_SECONDS;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (serializer == null)
                    serializer = FindAnyObjectByType<FloorMapSerializer>();
                if (serializer != null && serializer.Document != null &&
                    serializer.Document.IsLoaded && FloorPlanEditor.IsAvailable)
                    break;
                yield return null;
            }

            if (serializer == null || serializer.Document == null || !serializer.Document.IsLoaded)
            {
                Debug.LogError("[RoomDesignerBootstrap] Exoa runtime never became ready " +
                               "(no serializer / document not loaded) — designer is inert.");
                yield break;
            }

            RoomDesignLaunch.Payload launch = RoomDesignLaunch.Consume();

            if (launch.Kind == RoomDesignLaunch.Kind.LoadPlan && !string.IsNullOrEmpty(launch.PlanName))
            {
                UISaving.instance.LoadInternal(launch.PlanName);
                PlanName = launch.PlanName;
                Ready = true;
                Debug.Log($"[RoomDesignerBootstrap] Loaded plan '{PlanName}'.");
                yield break;
            }

            // New room from preset (or direct entry → default preset).
            string presetId = string.IsNullOrEmpty(launch.PresetId) ? RoomPresets.DefaultId : launch.PresetId;
            RoomPresets.Preset preset = RoomPresets.Get(presetId);
            if (preset == null)
            {
                Debug.LogError($"[RoomDesignerBootstrap] Unknown preset '{presetId}'.");
                yield break;
            }

            string roomId = RoomPresets.Apply(preset);
            if (roomId == null)
            {
                Debug.LogError($"[RoomDesignerBootstrap] Preset '{presetId}' failed to generate.");
                yield break;
            }

            PlanName = UniquePlanName("Room");
            if (UISaving.instance != null && UISaving.instance.saveNameInputField != null)
                UISaving.instance.saveNameInputField.text = PlanName; // autosave keys off this

            Ready = true;
            Debug.Log($"[RoomDesignerBootstrap] Preset '{presetId}' generated (room {roomId}) as '{PlanName}'.");

            // Let rebuilds settle (0.1 s throttle + next-frame shell), then write the
            // first save so the plan shows up in the rooms list immediately.
            yield return new WaitForSeconds(FIRST_SAVE_SETTLE_SECONDS);
            if (UISaving.instance != null)
                UISaving.instance.SaveInternal(PlanName);
        }

        /// <summary>"Room", "Room 2", "Room 3", … based on files already in FloorMaps/.</summary>
        private static string UniquePlanName(string baseName)
        {
            string dir = Path.Combine(Application.persistentDataPath, HDSettings.EXT_FLOORMAP_FOLDER);
            string name = baseName;
            int n = 1;
            while (File.Exists(Path.Combine(dir, name + ".json")))
            {
                n++;
                name = baseName + " " + n;
            }
            return name;
        }
    }
}

namespace IBMROS.Designer
{
    /// <summary>
    /// Cross-scene launch payload for RoomDesigner.unity (mirrors the
    /// ScannedProductRouter consume-once pattern — INTEGRATION_MAP §4: the
    /// vendor defaultFileToOpen path is dead code, so a static payload is the
    /// supported handoff). Set before SceneManager.LoadScene("RoomDesigner");
    /// RoomDesignerBootstrap consumes it exactly once.
    /// </summary>
    public static class RoomDesignLaunch
    {
        public enum Kind { None, NewFromPreset, LoadPlan }

        public sealed class Payload
        {
            public Kind Kind;
            public string PresetId;  // NewFromPreset
            public string PlanName;  // LoadPlan (file name without .json)
        }

        private static Payload pending;

        public static void SetNewFromPreset(string presetId) =>
            pending = new Payload { Kind = Kind.NewFromPreset, PresetId = presetId };

        public static void SetLoadPlan(string planName) =>
            pending = new Payload { Kind = Kind.LoadPlan, PlanName = planName };

        /// <summary>Returns the pending payload (Kind.None when launched directly, e.g. editor play).</summary>
        public static Payload Consume()
        {
            Payload p = pending;
            pending = null;
            return p ?? new Payload { Kind = Kind.None };
        }
    }
}

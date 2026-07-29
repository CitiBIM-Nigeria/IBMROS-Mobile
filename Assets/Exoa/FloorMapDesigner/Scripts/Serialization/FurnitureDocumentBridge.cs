using System;
using System.Collections.Generic;
using static Exoa.Designer.DataModel;

namespace Exoa.Designer
{
    /// <summary>
    /// IBMROS: the seam that lets furniture live inside the floor-map document
    /// without the serializer depending on the furniture stack.
    ///
    /// FloorMapSerializer gathers the current floor's furniture through
    /// <see cref="Provider"/> when saving, and hands the restored list to
    /// <see cref="Restorer"/> after a load. Both are set by the designer scene
    /// (FurniturePersistence); when they are null the serializer behaves exactly
    /// as before, so the plugin's own scenes and the golden harness are unaffected.
    /// </summary>
    public static class FurnitureDocumentBridge
    {
        /// <summary>Returns the furniture currently placed on the active floor.</summary>
        public static Func<List<FurnitureRecord>> Provider;

        /// <summary>Recreates furniture for a freshly loaded floor.</summary>
        public static Action<List<FurnitureRecord>> Restorer;

        [UnityEngine.RuntimeInitializeOnLoadMethod(
            UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Provider = null;
            Restorer = null;
        }
    }
}

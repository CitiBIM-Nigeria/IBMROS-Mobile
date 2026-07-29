using UnityEngine;

namespace IBMROS.Designer.Materials
{
    /// <summary>
    /// Put this on a door/window model prefab to declare the size it was modelled
    /// at, so the opening system can scale it to the actual opening. Without it a
    /// 1 x 2.1 m door is assumed.
    /// </summary>
    public sealed class OpeningModelMarker : MonoBehaviour
    {
        [Tooltip("Width/height in metres this model was authored at.")]
        public Vector2 referenceSize = new Vector2(1f, 2.1f);

        [Tooltip("Renderer showing the frame/leaf. Left empty = first renderer found.")]
        public Renderer frameRenderer;
        [Tooltip("Renderer showing the glazing, if any.")]
        public Renderer glassRenderer;
        [Tooltip("Renderer showing the handle, if any.")]
        public Renderer handleRenderer;
    }
}

using UnityEngine;

/// <summary>
/// Shows a socket's info panel when the player walks up to it and hides it when
/// they leave. Kept separate from <see cref="VRFurnitureSocket"/> so proximity
/// detection is swappable (trigger volume here, but a gaze/controller-ray variant
/// could replace it without touching the socket).
///
/// SETUP: put this on a child of the socket with a trigger Collider sized to the
/// "browse zone". Tag the player (or the XR Rig's body collider) so only they
/// trip it. If the Socket reference is left blank it auto-finds the one on this
/// object or a parent.
/// </summary>
[RequireComponent(typeof(Collider))]
public class VRSocketProximity : MonoBehaviour
{
    [Tooltip("Socket to show/hide. Auto-found on this object or a parent if blank.")]
    [SerializeField] private VRFurnitureSocket socket;

    [Tooltip("Only colliders with this tag open the panel. Blank = anything.")]
    [SerializeField] private string playerTag = "Player";

    [Tooltip("Log enter/exit events for debugging.")]
    [SerializeField] private bool verboseLogging;

    void Reset()
    {
        // Make the collider a trigger by default so designers don't forget.
        var col = GetComponent<Collider>();
        if (col != null) col.isTrigger = true;
    }

    void Awake()
    {
        if (socket == null) socket = GetComponentInParent<VRFurnitureSocket>();
        if (socket == null)
            Debug.LogError($"[VRSocketProximity] {name} has no VRFurnitureSocket " +
                           "assigned or in its parents — panel will never show.");

        var col = GetComponent<Collider>();
        if (col != null && !col.isTrigger)
            Debug.LogWarning($"[VRSocketProximity] {name}'s collider is not a trigger; " +
                             "enter/exit events won't fire.");
    }

    void OnTriggerEnter(Collider other)
    {
        if (!IsPlayer(other) || socket == null) return;
        if (verboseLogging) Debug.Log($"[VRSocketProximity] player entered {socket.DisplayLabel}.");
        socket.ShowInfoPanel();
    }

    void OnTriggerExit(Collider other)
    {
        if (!IsPlayer(other) || socket == null) return;
        if (verboseLogging) Debug.Log($"[VRSocketProximity] player left {socket.DisplayLabel}.");
        socket.HideInfoPanel();
    }

    private bool IsPlayer(Collider other) =>
        string.IsNullOrEmpty(playerTag) || other.CompareTag(playerTag);
}

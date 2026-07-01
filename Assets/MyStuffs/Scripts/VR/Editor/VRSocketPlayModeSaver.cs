using UnityEditor;
using UnityEngine;
using UnityEditor.SceneManagement;

/// <summary>
/// Catches the moment the Unity Editor leaves Play Mode and saves the VR Socket
/// product selections permanently into the scene.
/// 
/// Also cleans up any `glTF-StableFramerate` objects left behind by glTFast.
/// </summary>
[InitializeOnLoad]
public static class VRSocketPlayModeSaver
{
    static VRSocketPlayModeSaver()
    {
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredEditMode)
        {
            bool sceneChanged = false;

            // 1. Restore the socket selections made during Play Mode
            var sockets = Object.FindObjectsOfType<VRFurnitureSocket>(true);
            foreach (var socket in sockets)
            {
                string keyId = $"VRSocket_{socket.SocketId}_SelectedProduct";
                string keyUrl = $"VRSocket_{socket.SocketId}_SelectedModelUrl";

                if (EditorPrefs.HasKey(keyId))
                {
                    string savedId = EditorPrefs.GetString(keyId);
                    string savedUrl = EditorPrefs.HasKey(keyUrl) ? EditorPrefs.GetString(keyUrl) : "";

                    if (socket.SelectedProductId != savedId)
                    {
                        socket.SetSelectedProduct(savedId, savedUrl);
                        EditorUtility.SetDirty(socket);
                        sceneChanged = true;
                        socket.RegeneratePreview();
                        Debug.Log($"[VRSocket Saver] Saved choice '{savedId}' for {socket.gameObject.name}");
                    }

                    EditorPrefs.DeleteKey(keyId);
                    if (EditorPrefs.HasKey(keyUrl)) EditorPrefs.DeleteKey(keyUrl);
                }
            }

            if (sceneChanged)
            {
                var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
                if (scene.IsValid() && !EditorApplication.isPlaying)
                {
                    EditorSceneManager.MarkSceneDirty(scene);
                    Debug.Log("[VRSocket Saver] All Play Mode socket choices were successfully saved to the scene!");
                }
            }

            // 2. Clean up any glTFast garbage left behind
            var gltfGarbage = GameObject.Find("glTF-StableFramerate");
            if (gltfGarbage != null)
            {
                Object.DestroyImmediate(gltfGarbage);
                Debug.Log("[VRSocket Saver] Cleaned up leftover glTF-StableFramerate object.");
            }
        }
    }
}

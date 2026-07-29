#if UNITY_EDITOR
// Temporary helper: points the MCP-for-Unity bridge at port 8090 (8080 is taken
// by another local service) and starts it once per editor session. Safe to delete.
using System;
using System.IO;
using MCPForUnity.Editor.Services;
using MCPForUnity.Editor.Services.Transport;
using UnityEditor;

[InitializeOnLoad]
internal static class McpBridgeKick
{
    private const string StatusPath = "/private/tmp/claude-501/-Users-samuelshonubi-Documents-Dev-IBMROS-Mobile/ae9b87f5-9bf2-4ac9-8a15-6554113381f2/scratchpad/mcp-bridge-kick.log";
    private const string SessionKey = "McpBridgeKick.Done.v3";

    static McpBridgeKick()
    {
        if (SessionState.GetBool(SessionKey, false)) return;
        SessionState.SetBool(SessionKey, true);

        EditorApplication.delayCall += async () =>
        {
            try
            {
                EditorPrefs.SetString("MCPForUnity.HttpUrl", "http://127.0.0.1:8090");
                bool ok = await MCPServiceLocator.TransportManager.StartAsync(TransportMode.Http);
                File.WriteAllText(StatusPath, $"{DateTime.Now:O} bridge-start ok={ok} url={EditorPrefs.GetString("MCPForUnity.HttpUrl")}\n");
            }
            catch (Exception e)
            {
                try { File.WriteAllText(StatusPath, $"{DateTime.Now:O} bridge-start EXCEPTION {e}\n"); } catch { }
            }
        };
    }
}
#endif

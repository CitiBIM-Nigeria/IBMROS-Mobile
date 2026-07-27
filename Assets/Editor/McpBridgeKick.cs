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
    private const string StatusPath = "/private/tmp/claude-501/-Users-samuelshonubi-Documents-Dev-IBMROS-Mobile/1b41a91a-c0b8-425e-996d-85320fc19e52/scratchpad/mcp-bridge-kick.log";
    private const string SessionKey = "McpBridgeKick.Done.v2";

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

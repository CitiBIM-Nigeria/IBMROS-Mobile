using System;
using System.IO;
using System.IO.Compression;
using UnityEngine;

namespace OpenRoomPlan.Capture
{
    /// <summary>
    /// Gets a recorded session off the device without cables or adb:
    ///   1. Zip the session folder (one file instead of hundreds).
    ///   2. Android: publish the zip into the system Downloads collection via MediaStore — visible in the
    ///      Files app under Downloads/OpenRoomPlan, shareable to any app from there. No permissions or
    ///      plugins needed on API 29+ (apps own what they insert into MediaStore).
    ///      iOS: the zip stays under Documents, which the Files app exposes once UIFileSharingEnabled +
    ///      LSSupportsOpeningDocumentsInPlace are set (done by the iOS build postprocessor).
    /// </summary>
    public static class SessionExporter
    {
        public static string ExportsDir =>
            Path.Combine(Application.persistentDataPath, "OpenRoomPlan", "Exports");

        public struct Result
        {
            public bool ok;
            public string zipPath;        // absolute path of the produced zip
            public string userFacing;     // where the USER finds it, for the status label
            public string error;
        }

        /// <summary>Zip a session and surface it to the user. Safe to call repeatedly (overwrites).</summary>
        public static Result Export(string sessionId)
        {
            try
            {
                string sessionDir = SessionIO.SessionDir(sessionId);
                if (!Directory.Exists(sessionDir))
                    return new Result { ok = false, error = $"session folder not found: {sessionId}" };

                Directory.CreateDirectory(ExportsDir);
                string zipPath = Path.Combine(ExportsDir, sessionId + ".zip");
                if (File.Exists(zipPath)) File.Delete(zipPath);
                ZipFile.CreateFromDirectory(sessionDir, zipPath, System.IO.Compression.CompressionLevel.Fastest,
                                            includeBaseDirectory: true);

#if UNITY_ANDROID && !UNITY_EDITOR
                string publicLocation = PublishToAndroidDownloads(zipPath, sessionId + ".zip");
                return new Result
                {
                    ok = true, zipPath = zipPath,
                    userFacing = publicLocation ?? $"app files: {zipPath} (pull via USB)",
                };
#elif UNITY_IOS && !UNITY_EDITOR
                return new Result
                {
                    ok = true, zipPath = zipPath,
                    userFacing = "Files app ▸ On My iPhone ▸ IBMROS ▸ OpenRoomPlan/Exports",
                };
#else
                return new Result { ok = true, zipPath = zipPath, userFacing = zipPath };
#endif
            }
            catch (Exception e)
            {
                Debug.LogError($"[ORP] Export failed: {e}");
                return new Result { ok = false, error = e.Message };
            }
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        /// <summary>
        /// Insert the zip into MediaStore.Downloads (Download/OpenRoomPlan/<name>). Returns the
        /// user-facing location, or null on failure (zip still exists in app storage).
        /// </summary>
        static string PublishToAndroidDownloads(string zipPath, string fileName)
        {
            try
            {
                using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                using var resolver = activity.Call<AndroidJavaObject>("getContentResolver");

                using var values = new AndroidJavaObject("android.content.ContentValues");
                values.Call("put", "_display_name", fileName);
                values.Call("put", "mime_type", "application/zip");
                values.Call("put", "relative_path", "Download/OpenRoomPlan");

                using var downloadsUriClass = new AndroidJavaClass("android.provider.MediaStore$Downloads");
                using var collection = downloadsUriClass.GetStatic<AndroidJavaObject>("EXTERNAL_CONTENT_URI");
                using var itemUri = resolver.Call<AndroidJavaObject>("insert", collection, values);
                if (itemUri == null) return null;

                using var stream = resolver.Call<AndroidJavaObject>("openOutputStream", itemUri);
                var bytes = File.ReadAllBytes(zipPath);
                // JNI signature write(byte[], int, int); managed byte[] maps to Java byte[] via sbyte[].
                var sbytes = (sbyte[])(Array)bytes;
                const int chunk = 1 << 20;
                for (int off = 0; off < sbytes.Length; off += chunk)
                {
                    int len = Math.Min(chunk, sbytes.Length - off);
                    var part = new sbyte[len];
                    Array.Copy(sbytes, off, part, 0, len);
                    stream.Call("write", part);
                }
                stream.Call("flush");
                stream.Call("close");
                return $"Files ▸ Downloads ▸ OpenRoomPlan ▸ {fileName}";
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ORP] MediaStore publish failed (zip still in app storage): {e.Message}");
                return null;
            }
        }
#endif
    }
}

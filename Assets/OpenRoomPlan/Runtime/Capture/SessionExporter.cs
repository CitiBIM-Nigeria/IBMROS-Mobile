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
    ///   3. The caller then usually hands <see cref="Result.contentUri"/> (or the zip path) to
    ///      <see cref="SessionShare"/>, which opens the system share sheet — that is what makes a
    ///      scan reachable without a USB cable at all.
    ///
    /// Step 2 is kept even though step 3 exists: landing the zip in Downloads means the file is still
    /// findable later, after whatever the share sheet did or did not do.
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

            /// <summary>
            /// content:// URI of the published copy, when MediaStore accepted it. Directly
            /// shareable (SessionShare) with no FileProvider involved, because an app may
            /// hand out rows it inserted itself. Null when publishing was skipped or failed.
            /// </summary>
            public string contentUri;

            /// <summary>
            /// Why MediaStore did not publish, when it did not. Surfaced rather than only
            /// logged: this path can fail on a specific ROM and never in the Editor, so the
            /// reason has to be readable on the device that failed.
            /// </summary>
            public string publishWarning;
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
                string contentUri = PublishToAndroidDownloads(zipPath, sessionId + ".zip", out string warning);
                return new Result
                {
                    ok = true, zipPath = zipPath,
                    contentUri = contentUri,
                    publishWarning = warning,
                    userFacing = contentUri != null
                        ? $"Files ▸ Downloads ▸ OpenRoomPlan ▸ {sessionId}.zip"
                        : $"app files: {zipPath} (pull via USB)",
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
        /// Insert the zip into MediaStore.Downloads (Download/OpenRoomPlan/&lt;name&gt;). Returns the
        /// resulting content:// URI — shareable as-is — or null on failure, with the reason in
        /// <paramref name="warning"/>. The zip always remains in app storage either way.
        /// </summary>
        static string PublishToAndroidDownloads(string zipPath, string fileName, out string warning)
        {
            warning = null;
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
                if (itemUri == null)
                {
                    warning = "MediaStore refused the insert (returned no URI)";
                    return null;
                }

                using var stream = resolver.Call<AndroidJavaObject>("openOutputStream", itemUri);

                // Stream it. Two deliberate choices here, both fixes:
                //   • Read in chunks instead of File.ReadAllBytes — a session of ~900 frames
                //     zips to tens of MB, and holding the whole thing plus a second managed
                //     copy for JNI is avoidable pressure on a phone.
                //   • Convert byte[]→sbyte[] with Buffer.BlockCopy rather than casting via
                //     (sbyte[])(Array)bytes. That cast leans on CLR array-covariance between
                //     same-width primitives; Mono tolerates it, IL2CPP is not guaranteed to,
                //     and an InvalidCastException here would be swallowed into exactly the
                //     silent "publish failed, pull via USB" fallback. BlockCopy is defined
                //     for primitive arrays and needs no such tolerance.
                const int chunk = 1 << 20;
                var buffer = new byte[chunk];
                var part = new sbyte[chunk];
                using (var file = File.OpenRead(zipPath))
                {
                    int read;
                    while ((read = file.Read(buffer, 0, chunk)) > 0)
                    {
                        if (read == chunk)
                        {
                            Buffer.BlockCopy(buffer, 0, part, 0, read);
                            stream.Call("write", part);
                        }
                        else
                        {
                            // Final short chunk: write(byte[]) writes the WHOLE array, so it
                            // has to be exactly the remaining length or the file gains padding.
                            var tail = new sbyte[read];
                            Buffer.BlockCopy(buffer, 0, tail, 0, read);
                            stream.Call("write", tail);
                        }
                    }
                }
                stream.Call("flush");
                stream.Call("close");

                return itemUri.Call<string>("toString");
            }
            catch (Exception e)
            {
                warning = e.Message;
                Debug.LogWarning($"[ORP] MediaStore publish failed (zip still in app storage): {e}");
                return null;
            }
        }
#endif
    }
}

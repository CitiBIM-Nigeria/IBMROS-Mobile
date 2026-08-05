using System;
using UnityEngine;

namespace OpenRoomPlan.Capture
{
    /// <summary>
    /// Hands an exported session zip to the OS share sheet, so a scan can leave the phone
    /// through whatever the user already has (Drive, Gmail, WhatsApp, Files, Nearby Share)
    /// instead of only being reachable over USB from app-private storage.
    ///
    /// WHY THIS IS NOT NativeGallery. NativeGallery saves and picks *media* — its whole
    /// surface is SaveImageToGallery / SaveVideoToGallery / Get*FromGallery. It has no
    /// file-sharing entry point, and the gallery cannot hold a .zip, so it cannot do this
    /// job at all. Sharing an arbitrary file is ACTION_SEND, which is what this wraps.
    /// No plugin, no .aar, nothing to keep in sync.
    ///
    /// TWO WAYS TO NAME THE FILE, IN ORDER. ACTION_SEND needs a content:// URI; a file://
    /// path throws FileUriExposedException on API 24+.
    ///   1. The MediaStore URI the exporter already gets back when it publishes into
    ///      Downloads. Free, and needs no manifest declaration — the app owns rows it
    ///      inserted, and other apps can read them.
    ///   2. A FileProvider URI over the app's own files dir, for when MediaStore refuses
    ///      (some OEM ROMs do). Needs the <provider> in AndroidManifest.xml plus
    ///      res/xml/orp_file_paths.xml — both committed alongside this file.
    ///
    /// WHY THE JNI IS HAND-BOUND. Unity derives a JNI signature from the *managed*
    /// argument types, so `Call("putExtra", key, uriObject)` asks for
    /// putExtra(String, Object) — which does not exist; the real overload takes a
    /// Parcelable. Same trap for createChooser (CharSequence, not String) and
    /// FileProvider.getUriForFile (Context, not Activity). Each would throw
    /// NoSuchMethodError at runtime, so those three are looked up by their true
    /// signatures instead of letting Unity guess.
    /// </summary>
    public static class SessionShare
    {
        /// <summary>Suffix appended to the package name to form the FileProvider authority.</summary>
        public const string FileProviderSuffix = ".orpfileprovider";

        private const string MimeZip = "application/zip";

        /// <summary>True where a share sheet exists at all (Android device builds today).</summary>
        public static bool IsSupported
        {
            get
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                return true;
#else
                return false;
#endif
            }
        }

        /// <summary>
        /// Opens the system share sheet for an exported zip.
        ///
        /// Returns true once the sheet has been *dispatched* — the user's choice of app
        /// happens afterwards and is not reported back. Failures that can be detected up
        /// front (no shareable URI, missing provider) return false with a reason, because
        /// those are the ones worth putting on screen.
        /// </summary>
        /// <param name="zipPath">Absolute path of the zip in app storage.</param>
        /// <param name="mediaStoreUri">
        /// content:// URI from the exporter's MediaStore insert, or null/empty when that
        /// did not happen — in which case a FileProvider URI is built instead.
        /// </param>
        /// <param name="subject">Share subject / chooser-visible title.</param>
        public static bool TryShare(string zipPath, string mediaStoreUri, string subject, out string error)
        {
            error = null;
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                if (activity == null)
                {
                    error = "no Android activity";
                    return false;
                }

                // Resolve the URI on this thread so a bad authority / missing provider is
                // reported to the caller rather than vanishing into the UI thread.
                IntPtr uriLocal = !string.IsNullOrEmpty(mediaStoreUri)
                    ? ParseUri(mediaStoreUri)
                    : FileProviderUri(activity, zipPath, out error);

                if (uriLocal == IntPtr.Zero)
                {
                    if (string.IsNullOrEmpty(error)) error = "could not build a shareable content URI";
                    activity.Dispose();
                    return false;
                }

                // Local refs die with the current JNI frame, and the runnable below runs
                // later on another thread — promote it.
                IntPtr uriGlobal = AndroidJNI.NewGlobalRef(uriLocal);
                AndroidJNI.DeleteLocalRef(uriLocal);

                // startActivity belongs on the Android UI thread, not Unity's. Everything
                // else is built there too, so every local ref lives and dies in one frame.
                activity.Call("runOnUiThread", new AndroidJavaRunnable(() =>
                {
                    try
                    {
                        SendOnUiThread(activity, uriGlobal, subject);
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"[ORP] Share sheet failed to open: {e}");
                    }
                    finally
                    {
                        AndroidJNI.DeleteGlobalRef(uriGlobal);
                        activity.Dispose();
                    }
                }));
                return true;
            }
            catch (Exception e)
            {
                error = e.Message;
                Debug.LogError($"[ORP] Share failed: {e}");
                return false;
            }
#else
            error = "sharing is Android-only in this build";
            return false;
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        /// <summary>Builds ACTION_SEND + chooser and starts it. Must run on the UI thread.</summary>
        private static void SendOnUiThread(AndroidJavaObject activity, IntPtr uri, string subject)
        {
            IntPtr intentClass = AndroidJNI.FindClass("android/content/Intent");
            IntPtr ctor = AndroidJNI.GetMethodID(intentClass, "<init>", "(Ljava/lang/String;)V");
            IntPtr putParcelable = AndroidJNI.GetMethodID(intentClass, "putExtra",
                "(Ljava/lang/String;Landroid/os/Parcelable;)Landroid/content/Intent;");
            IntPtr putString = AndroidJNI.GetMethodID(intentClass, "putExtra",
                "(Ljava/lang/String;Ljava/lang/String;)Landroid/content/Intent;");
            IntPtr setType = AndroidJNI.GetMethodID(intentClass, "setType",
                "(Ljava/lang/String;)Landroid/content/Intent;");
            IntPtr addFlags = AndroidJNI.GetMethodID(intentClass, "addFlags",
                "(I)Landroid/content/Intent;");
            IntPtr createChooser = AndroidJNI.GetStaticMethodID(intentClass, "createChooser",
                "(Landroid/content/Intent;Ljava/lang/CharSequence;)Landroid/content/Intent;");

            IntPtr action = AndroidJNI.NewStringUTF("android.intent.action.SEND");
            var one = new jvalue[1];
            one[0].l = action;
            IntPtr intent = AndroidJNI.NewObject(intentClass, ctor, one);
            AndroidJNI.DeleteLocalRef(action);

            // setType("application/zip")
            IntPtr mime = AndroidJNI.NewStringUTF(MimeZip);
            one[0].l = mime;
            AndroidJNI.DeleteLocalRef(AndroidJNI.CallObjectMethod(intent, setType, one));
            AndroidJNI.DeleteLocalRef(mime);

            // putExtra(EXTRA_STREAM, uri) — the Parcelable overload, hence the hand binding.
            var two = new jvalue[2];
            IntPtr keyStream = AndroidJNI.NewStringUTF("android.intent.extra.STREAM");
            two[0].l = keyStream;
            two[1].l = uri;
            AndroidJNI.DeleteLocalRef(AndroidJNI.CallObjectMethod(intent, putParcelable, two));
            AndroidJNI.DeleteLocalRef(keyStream);

            if (!string.IsNullOrEmpty(subject))
            {
                IntPtr keySubject = AndroidJNI.NewStringUTF("android.intent.extra.SUBJECT");
                IntPtr subjectValue = AndroidJNI.NewStringUTF(subject);
                two[0].l = keySubject;
                two[1].l = subjectValue;
                AndroidJNI.DeleteLocalRef(AndroidJNI.CallObjectMethod(intent, putString, two));
                AndroidJNI.DeleteLocalRef(keySubject);
                AndroidJNI.DeleteLocalRef(subjectValue);
            }

            // FLAG_GRANT_READ_URI_PERMISSION — without it the receiving app cannot open a
            // FileProvider URI. Harmless on a MediaStore URI.
            one[0].i = 1;
            AndroidJNI.DeleteLocalRef(AndroidJNI.CallObjectMethod(intent, addFlags, one));

            // Force the sheet rather than silently launching a default handler.
            IntPtr title = AndroidJNI.NewStringUTF(string.IsNullOrEmpty(subject) ? "Share scan" : subject);
            two[0].l = intent;
            two[1].l = title;
            IntPtr chooser = AndroidJNI.CallStaticObjectMethod(intentClass, createChooser, two);
            AndroidJNI.DeleteLocalRef(title);

            IntPtr activityClass = AndroidJNI.FindClass("android/app/Activity");
            IntPtr startActivity = AndroidJNI.GetMethodID(activityClass, "startActivity",
                "(Landroid/content/Intent;)V");
            one[0].l = chooser;
            AndroidJNI.CallVoidMethod(activity.GetRawObject(), startActivity, one);

            AndroidJNI.DeleteLocalRef(chooser);
            AndroidJNI.DeleteLocalRef(intent);
            AndroidJNI.DeleteLocalRef(activityClass);
            AndroidJNI.DeleteLocalRef(intentClass);
        }

        /// <summary>Uri.parse(s) — returns a JNI local ref, or Zero.</summary>
        private static IntPtr ParseUri(string uriString)
        {
            IntPtr uriClass = AndroidJNI.FindClass("android/net/Uri");
            try
            {
                IntPtr parse = AndroidJNI.GetStaticMethodID(uriClass, "parse",
                    "(Ljava/lang/String;)Landroid/net/Uri;");
                IntPtr s = AndroidJNI.NewStringUTF(uriString);
                var args = new jvalue[1];
                args[0].l = s;
                IntPtr uri = AndroidJNI.CallStaticObjectMethod(uriClass, parse, args);
                AndroidJNI.DeleteLocalRef(s);
                return uri;
            }
            finally
            {
                AndroidJNI.DeleteLocalRef(uriClass);
            }
        }

        /// <summary>
        /// FileProvider.getUriForFile(context, authority, file) — returns a JNI local ref,
        /// or Zero with a reason. Bound by hand because the first parameter is a Context
        /// and Unity would offer it an Activity.
        /// </summary>
        private static IntPtr FileProviderUri(AndroidJavaObject activity, string path, out string error)
        {
            error = null;
            IntPtr providerClass = AndroidJNI.FindClass("androidx/core/content/FileProvider");
            if (providerClass == IntPtr.Zero || AndroidJNI.ExceptionOccurred() != IntPtr.Zero)
            {
                AndroidJNI.ExceptionClear();
                error = "androidx FileProvider not present in this build";
                return IntPtr.Zero;
            }

            IntPtr fileClass = IntPtr.Zero;
            try
            {
                IntPtr getUriForFile = AndroidJNI.GetStaticMethodID(providerClass, "getUriForFile",
                    "(Landroid/content/Context;Ljava/lang/String;Ljava/io/File;)Landroid/net/Uri;");

                fileClass = AndroidJNI.FindClass("java/io/File");
                IntPtr fileCtor = AndroidJNI.GetMethodID(fileClass, "<init>", "(Ljava/lang/String;)V");
                IntPtr pathStr = AndroidJNI.NewStringUTF(path);
                var one = new jvalue[1];
                one[0].l = pathStr;
                IntPtr file = AndroidJNI.NewObject(fileClass, fileCtor, one);
                AndroidJNI.DeleteLocalRef(pathStr);

                string authority = activity.Call<string>("getPackageName") + FileProviderSuffix;
                IntPtr authorityStr = AndroidJNI.NewStringUTF(authority);

                var three = new jvalue[3];
                three[0].l = activity.GetRawObject();
                three[1].l = authorityStr;
                three[2].l = file;
                IntPtr uri = AndroidJNI.CallStaticObjectMethod(providerClass, getUriForFile, three);

                if (AndroidJNI.ExceptionOccurred() != IntPtr.Zero)
                {
                    AndroidJNI.ExceptionClear();
                    // Nearly always the provider missing from the merged manifest, or the
                    // file sitting outside every declared <paths> root.
                    error = $"FileProvider rejected the path (authority '{authority}' declared? " +
                            "path inside res/xml/orp_file_paths.xml?)";
                    uri = IntPtr.Zero;
                }

                AndroidJNI.DeleteLocalRef(authorityStr);
                AndroidJNI.DeleteLocalRef(file);
                return uri;
            }
            catch (Exception e)
            {
                error = e.Message;
                return IntPtr.Zero;
            }
            finally
            {
                if (fileClass != IntPtr.Zero) AndroidJNI.DeleteLocalRef(fileClass);
                AndroidJNI.DeleteLocalRef(providerClass);
            }
        }
#endif
    }
}

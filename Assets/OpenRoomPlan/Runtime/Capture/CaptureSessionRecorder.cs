using System.IO;
using Unity.Collections;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using OpenRoomPlan.Core;

namespace OpenRoomPlan.Capture
{
    /// <summary>
    /// Records a live AR session to disk in the OpenRoomPlan session format: downscaled RGB (JPG),
    /// platform environment depth + confidence (.bin), LiDAR depth GT when present (.bin), camera
    /// intrinsics + VIO pose, and sparse VIO point counts. Replayable offline by CaptureSessionPlayer.
    ///
    /// Attach to a GameObject in an AR scene alongside ARCameraManager + AROcclusionManager (+ optional
    /// ARPointCloudManager). Call StartRecording()/StopRecording() from UI.
    ///
    /// NOTE (device tuning): frame throttling, output resolution, and the depth/confidence image formats
    /// vary by device/provider — validate the plane formats on the first on-device run (logged below).
    /// </summary>
    // NOTE: deliberately no [RequireComponent(ARCameraManager)] — the recorder lives on its own
    // GameObject and CONSUMES the rig's managers via the serialized references below. RequireComponent
    // here would force a second ARCameraManager (and its required Camera) onto this object, off the
    // XR rig, fighting ARCore for the camera.
    public sealed class CaptureSessionRecorder : MonoBehaviour
    {
        [Header("AR references")]
        [SerializeField] ARCameraManager cameraManager;
        [SerializeField] AROcclusionManager occlusionManager;   // environment + (LiDAR) depth
        [SerializeField] ARPointCloudManager pointCloudManager; // optional, for VIO anchor counts
        [SerializeField] Camera arCamera;                       // pose source; defaults to Camera.main

        [Header("Capture settings")]
        [Tooltip("Long-edge resolution fed to the ML nets and stored as RGB. 256–384 px is the sweet spot.")]
        [SerializeField] int rgbLongEdge = 384;
        [Tooltip("Max frames per second to record. Perception runs slow; no need to log at 60.")]
        [SerializeField] float recordHz = 8f;
        [Tooltip("Skip frames where the camera barely moved (meters). 0 = record every eligible frame.")]
        [SerializeField] float minTranslationMeters = 0.02f;
        [SerializeField] bool captureLiDARWhenAvailable = true;

        public bool IsRecording { get; private set; }
        public int FrameCount { get; private set; }
        public string CurrentSessionId { get; private set; }

        float _lastCaptureTime;
        Vector3 _lastCapturePos;
        bool _loggedFormats;
        readonly System.Collections.Generic.List<Vector3> _pointBuffer = new();

        void Reset() => cameraManager = FindAnyObjectByType<ARCameraManager>();

        void Awake()
        {
            // Fallbacks find the XR rig's managers in the scene; never add components to this object.
            if (cameraManager == null) cameraManager = FindAnyObjectByType<ARCameraManager>();
            if (occlusionManager == null) occlusionManager = FindAnyObjectByType<AROcclusionManager>();
            if (pointCloudManager == null) pointCloudManager = FindAnyObjectByType<ARPointCloudManager>();
            if (arCamera == null) arCamera = cameraManager != null ? cameraManager.GetComponent<Camera>() : Camera.main;
            if (cameraManager == null)
                Debug.LogError("[ORP] CaptureSessionRecorder: no ARCameraManager found in scene — recording will not work.");
        }

        public void StartRecording(string roomNotes = "")
        {
            if (IsRecording) return;
            CurrentSessionId = $"sess_{System.DateTime.Now:yyyyMMdd_HHmmss}";
            SessionIO.EnsureSessionDirs(CurrentSessionId);
            FrameCount = 0;
            _lastCaptureTime = 0f;
            _lastCapturePos = new Vector3(float.NaN, 0, 0);

            var m = new SessionManifest
            {
                sessionId = CurrentSessionId,
                deviceModel = SystemInfo.deviceModel,
                platform = Application.platform == RuntimePlatform.IPhonePlayer ? "iOS" : "Android",
                arProvider = Application.platform == RuntimePlatform.IPhonePlayer ? "ARKit" : "ARCore",
                hasLiDAR = false, // set true on first LiDAR frame
                hasRoomPlanGT = false,
                rgbWidth = 0, rgbHeight = 0, depthWidth = 0, depthHeight = 0,
                frameCount = 0,
                startTimestampNs = System.DateTime.Now.Ticks * 100,
                notes = roomNotes,
                schemaVersion = SessionIO.SchemaVersion,
                rgbTransform = RgbTransform.ToString(),
                rgbAlignedWithDepth = true,   // v2 writes RGB in the depth's frame (EncodeRgbJpg)
                screenOrientation = Screen.orientation.ToString(),
            };
            SessionIO.WriteManifest(CurrentSessionId, m);
            _pendingManifest = m;

            if (occlusionManager != null)
                occlusionManager.requestedEnvironmentDepthMode = EnvironmentDepthMode.Fastest;

            _writer = new SessionWriteQueue();
            cameraManager.frameReceived += OnFrameReceived;
            IsRecording = true;
            Debug.Log($"[ORP] Recording started: {CurrentSessionId} -> {SessionIO.SessionDir(CurrentSessionId)}");
        }

        public void StopRecording()
        {
            if (!IsRecording) return;
            cameraManager.frameReceived -= OnFrameReceived;
            IsRecording = false;

            // Drain before the manifest lands, so a session that reports "stopped" is complete
            // on disk rather than still being written behind the user's back.
            if (_writer != null)
            {
                int pending = _writer.Pending;
                if (pending > 0) Debug.Log($"[ORP] Flushing {pending} queued writes…");
                _writer.CompleteAndWait();
                _writer.Dispose();
                _writer = null;
            }

            _pendingManifest.frameCount = FrameCount;
            SessionIO.WriteManifest(CurrentSessionId, _pendingManifest);
            Debug.Log($"[ORP] Recording stopped: {CurrentSessionId}, {FrameCount} frames");
        }

        void OnDisable()
        {
            // Never leave the writer thread holding a half-written session.
            if (IsRecording) StopRecording();
        }

        SessionManifest _pendingManifest;
        SessionWriteQueue _writer;

        void OnFrameReceived(ARCameraFrameEventArgs args)
        {
            if (!IsRecording) return;

            // rate + motion gate
            float now = Time.unscaledTime;
            if (now - _lastCaptureTime < 1f / Mathf.Max(1f, recordHz)) return;
            Vector3 pos = arCamera.transform.position;
            if (minTranslationMeters > 0f && !float.IsNaN(_lastCapturePos.x) &&
                Vector3.Distance(pos, _lastCapturePos) < minTranslationMeters) return;

            if (!cameraManager.TryGetIntrinsics(out XRCameraIntrinsics intr)) return;
            if (!cameraManager.TryAcquireLatestCpuImage(out XRCpuImage rgbImage)) return;

            // The pose was read for the frame this callback is delivering; the IMAGE may be an
            // older one. Record both clocks so the gap is measurable instead of assumed — see
            // FrameRecord.frameTimestampNs.
            long frameTsNs = args.timestampNs ?? 0L;
            long imageTsNs = (long)(rgbImage.timestamp * 1e9);
            if (imageTsNs <= 0L) imageTsNs = frameTsNs;   // provider gave no image clock

            int idx = FrameCount;
            CameraIntrinsics scaledIntr;
            using (rgbImage)
            {
                int outW, outH;
                if (rgbImage.width >= rgbImage.height)
                {
                    outW = rgbLongEdge; outH = Mathf.RoundToInt(rgbLongEdge * (float)rgbImage.height / rgbImage.width);
                }
                else
                {
                    outH = rgbLongEdge; outW = Mathf.RoundToInt(rgbLongEdge * (float)rgbImage.width / rgbImage.height);
                }

                byte[] jpg = EncodeRgbJpg(rgbImage, outW, outH);
                if (jpg == null || jpg.Length == 0) return;
                _writer.Write(RgbPath(idx), jpg);
                scaledIntr = ScaleIntrinsics(intr, outW, outH);

                if (_pendingManifest.rgbWidth == 0)
                {
                    _pendingManifest.rgbWidth = outW; _pendingManifest.rgbHeight = outH;
                }
            }

            // Environment depth. Platform-dependent meaning (dual-path GT design, see Phase 1 spec §3):
            //   Android  -> ARCore depth is motion-stereo (NON-LiDAR): store in depth/ as the CANDIDATE.
            //   iOS Pro  -> ARKit environment depth IS LiDAR: store in lidar/ as GROUND TRUTH. The iOS
            //               non-LiDAR candidate is the monocular net alone (produced later, offline/on-device).
            bool isIOS = Application.platform == RuntimePlatform.IPhonePlayer;
            string depthRel = "";
            string lidarRel = "";
            if (occlusionManager != null &&
                occlusionManager.TryAcquireEnvironmentDepthCpuImage(out XRCpuImage depthImage))
            {
                using (depthImage)
                {
                    var depth = ReadDepthPlaneMeters(depthImage, out int dw, out int dh);
                    if (isIOS && captureLiDARWhenAvailable)
                    {
                        _writer.Write(Path.Combine(SessionIO.SessionDir(CurrentSessionId), "lidar", $"{idx:D6}.bin"),
                                      SessionIO.EncodeDepthBin(depth, dw, dh));
                        lidarRel = $"lidar/{idx:D6}.bin";
                        _pendingManifest.hasLiDAR = true;
                    }
                    else
                    {
                        _writer.Write(DepthPath(idx), SessionIO.EncodeDepthBin(depth, dw, dh));
                        depthRel = $"depth/{idx:D6}.bin";
                    }
                    if (_pendingManifest.depthWidth == 0) { _pendingManifest.depthWidth = dw; _pendingManifest.depthHeight = dh; }
                    if (!_loggedFormats)
                    {
                        // Log what was DECODED, not just the declared format. A format
                        // mismatch produces plausible-looking files full of ~1e14, and the
                        // only cheap way to catch that on the device is to state the range
                        // once: a room is metres, so anything outside ~0.1–30 m is a bug.
                        float lo = float.MaxValue, hi = 0f; int valid = 0;
                        for (int i = 0; i < depth.Length; i++)
                        {
                            float d = depth[i];
                            if (d <= 0.001f || float.IsNaN(d) || float.IsInfinity(d)) continue;
                            valid++;
                            if (d < lo) lo = d;
                            if (d > hi) hi = d;
                        }
                        string range = valid > 0 ? $"{lo:F2}–{hi:F2} m" : "no valid pixels";
                        Debug.Log($"[ORP] env-depth {dw}x{dh} fmt={depthImage.format} " +
                                  $"stride={depthImage.GetPlane(0).pixelStride} decoded={range} " +
                                  $"valid={100f * valid / depth.Length:F0}% " +
                                  $"platform={(isIOS ? "iOS/GT" : "Android/candidate")}");
                        if (valid == 0 || hi > 100f)
                            Debug.LogError("[ORP] env-depth decoded outside any plausible room range — " +
                                           "the plane format is not what ReadDepthPlaneMeters assumed. " +
                                           "This session's depth is unusable; fix before capturing more.");
                    }
                }

                if (occlusionManager.TryAcquireEnvironmentDepthConfidenceCpuImage(out XRCpuImage confImage))
                    using (confImage)
                    {
                        var conf = ReadBytePlane(confImage, out int cw, out int ch);
                        _writer.Write(Path.Combine(SessionIO.SessionDir(CurrentSessionId), "depth_conf", $"{idx:D6}.bin"),
                                      SessionIO.EncodeConfidenceBin(conf, cw, ch));
                    }
            }

            _loggedFormats = true;

            // Sparse VIO feature points (world space) — metric anchors for offline scale-alignment
            // (ScaleAligner.AlignToAnchors), the on-device path when no platform depth exists.
            string pointsRel = "";
            if (pointCloudManager != null)
            {
                _pointBuffer.Clear();
                foreach (var pc in pointCloudManager.trackables)
                    if (pc.positions.HasValue)
                    {
                        var arr = pc.positions.Value;
                        for (int p = 0; p < arr.Length; p++) _pointBuffer.Add(arr[p]);
                    }
                if (_pointBuffer.Count > 0)
                {
                    _writer.Write(Path.Combine(SessionIO.SessionDir(CurrentSessionId), "points", $"{idx:D6}.bin"),
                                  SessionIO.EncodePointsBin(_pointBuffer));
                    pointsRel = $"points/{idx:D6}.bin";
                }
            }

            var rec = new FrameRecord
            {
                index = idx,
                timestampNs = imageTsNs,
                frameTimestampNs = frameTsNs,
                intrinsics = scaledIntr,
                cameraPose = new Pose(pos, arCamera.transform.rotation),
                poseTracked = true,
                screenOrientation = (int)Screen.orientation,
                sparsePointCount = _pointBuffer.Count,
                rgbFile = $"frames/{idx:D6}.jpg",
                depthFile = depthRel,
                lidarFile = lidarRel,
                pointsFile = pointsRel,
            };
            _writer.Append(SessionIO.FrameRecordPath(CurrentSessionId), SessionIO.EncodeFrameRecord(rec));

            FrameCount++;
            _lastCaptureTime = now;
            _lastCapturePos = pos;
        }

        // ---- helpers ----
        string RgbPath(int i) => Path.Combine(SessionIO.SessionDir(CurrentSessionId), "frames", $"{i:D6}.jpg");
        string DepthPath(int i) => Path.Combine(SessionIO.SessionDir(CurrentSessionId), "depth", $"{i:D6}.bin");

        static CameraIntrinsics ScaleIntrinsics(in XRCameraIntrinsics intr, int outW, int outH)
        {
            float sx = outW / (float)intr.resolution.x;
            float sy = outH / (float)intr.resolution.y;
            return new CameraIntrinsics
            {
                fx = intr.focalLength.x * sx, fy = intr.focalLength.y * sy,
                cx = intr.principalPoint.x * sx, cy = intr.principalPoint.y * sy,
                width = outW, height = outH,
            };
        }

        /// <summary>
        /// The transformation applied when writing RGB. See <see cref="EncodeRgbJpg"/> — this is
        /// recorded in the manifest so offline consumers never have to assume.
        /// </summary>
        public const XRCpuImage.Transformation RgbTransform = XRCpuImage.Transformation.MirrorX;

        private byte[] _rgbScratch;

        /// <summary>
        /// Encode one camera image as JPEG bytes, in the SAME orientation as the depth map.
        ///
        /// WHY MirrorX AND NOT MirrorY. Two flips compose here, and v1 got the pair wrong:
        ///   • XRCpuImage.Transformation mirrors ACROSS the named axis, so MirrorY mirrors across
        ///     the y-axis — a HORIZONTAL flip. MirrorX is the vertical one.
        ///   • Unity's raw texture rows run bottom-up while JPEG rows run top-down, so any
        ///     encode path through raw RGBA adds a vertical flip of its own.
        /// v1 used MirrorY, so it got a horizontal flip from the conversion plus a vertical flip
        /// from the encode: together a 180 degree rotation of every recorded JPEG relative to its
        /// own depth map and to the camera pose. Nothing detected it, and it silently transposes
        /// any RGB-driven depth onto the scene — measured net-vs-ARCore correlation was NEGATIVE
        /// (-0.12) until the rotation was undone, +0.46 after. MirrorX cancels the encode's
        /// vertical flip and introduces no horizontal one, leaving RGB in the depth's frame.
        ///
        /// It also no longer round-trips through a Texture2D. EncodeArrayToJPG encodes the raw
        /// bytes directly, which drops a per-frame texture allocation, a GPU upload via Apply()
        /// that was pure waste (the pixels were only ever read back on the CPU), and a Destroy.
        /// The conversion buffer is reused across frames.
        ///
        /// THE ONE UNVERIFIED ASSUMPTION HERE. That the v1 Texture2D path flipped vertically
        /// is *measured*: MirrorY is horizontal, the observed result was 180 degrees, so the
        /// encode contributed the vertical half. Whether EncodeArrayToJPG treats row 0 as the
        /// bottom the same way LoadRawTextureData does is NOT measured — it is inferred from
        /// Unity's texture convention, and this device has not run it yet. If the convention
        /// differs, v2 RGB comes out vertically mirrored instead of aligned.
        ///
        /// So: on the FIRST v2 capture, re-run the registration table in
        /// Tooling/OfflineDepthBake/README.md. "identity" winning confirms this; "mirrorV"
        /// winning means EncodeArrayToJPG does not flip, and the fix is one token —
        /// RgbTransform becomes Transformation.None. Nothing else has to change, because the
        /// manifest records which transform was used and the bake tool reads it from there.
        /// </summary>
        byte[] EncodeRgbJpg(XRCpuImage image, int outW, int outH)
        {
            var conv = new XRCpuImage.ConversionParams
            {
                inputRect = new RectInt(0, 0, image.width, image.height),
                outputDimensions = new Vector2Int(outW, outH),
                outputFormat = TextureFormat.RGBA32,
                transformation = RgbTransform,
            };
            int size = image.GetConvertedDataSize(conv);
            using var buffer = new NativeArray<byte>(size, Allocator.Temp);
            image.Convert(conv, buffer);

            if (_rgbScratch == null || _rgbScratch.Length != size) _rgbScratch = new byte[size];
            buffer.CopyTo(_rgbScratch);

            return ImageConversion.EncodeArrayToJPG(
                _rgbScratch, UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_SRGB,
                (uint)outW, (uint)outH, 0, 90);
        }

        /// <summary>
        /// Read a single-channel depth CPU image into METRES, honouring the plane's actual
        /// format and row stride.
        ///
        /// WHY THIS BRANCHES ON FORMAT. The two platforms disagree, and assuming either one
        /// silently destroys the other's data:
        ///   ARCore  -> XRCpuImage.Format.DepthUint16, pixelStride 2, value = MILLIMETRES.
        ///   ARKit   -> DepthFloat32 (sceneDepth), pixelStride 4, value = metres.
        /// This method used to hardcode the float32 case while still advancing by the real
        /// pixelStride, so on Android it read OVERLAPPING 4-byte windows across a 2-byte
        /// plane and reinterpreted them as floats. That decodes to ~1e14 or ~0 — the first
        /// Pixel 8 Pro session recorded 423 frames of it with no error anywhere, because
        /// nothing downstream range-checked the result. Hence also the sanity log below:
        /// a depth reader that can be wrong in silence must say what it decoded.
        ///
        /// Byte assembly is done with shifts rather than BitConverter over a temporary
        /// array: the old version allocated a 4-byte array PER PIXEL, i.e. 14,400 per frame
        /// at 160x90, which on a phone is pure GC pressure during capture.
        /// </summary>
        static float[] ReadDepthPlaneMeters(XRCpuImage img, out int w, out int h)
        {
            w = img.width; h = img.height;
            var plane = img.GetPlane(0);
            var raw = plane.data;
            var outArr = new float[w * h];
            int rowStride = plane.rowStride, pxStride = plane.pixelStride;

            // Trust the declared format; fall back to the stride when a provider reports
            // something unexpected (OneComponent32 is float, OneComponent16/Depth16 is not).
            bool millimetreUint16 = img.format == XRCpuImage.Format.DepthUint16 || pxStride == 2;

            for (int y = 0; y < h; y++)
            {
                int row = y * rowStride;
                int outRow = y * w;
                if (millimetreUint16)
                {
                    for (int x = 0; x < w; x++)
                    {
                        int b = row + x * pxStride;
                        int mm = raw[b] | (raw[b + 1] << 8);
                        outArr[outRow + x] = mm * 0.001f;
                    }
                }
                else
                {
                    for (int x = 0; x < w; x++)
                    {
                        int b = row + x * pxStride;
                        int bits = raw[b] | (raw[b + 1] << 8) | (raw[b + 2] << 16) | (raw[b + 3] << 24);
                        outArr[outRow + x] = System.BitConverter.Int32BitsToSingle(bits);
                    }
                }
            }
            return outArr;
        }

        static byte[] ReadBytePlane(XRCpuImage img, out int w, out int h)
        {
            w = img.width; h = img.height;
            var plane = img.GetPlane(0);
            var raw = plane.data;
            var outArr = new byte[w * h];
            int rowStride = plane.rowStride, pxStride = plane.pixelStride;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    outArr[y * w + x] = raw[y * rowStride + x * pxStride];
            return outArr;
        }
    }
}

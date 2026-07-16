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
            };
            SessionIO.WriteManifest(CurrentSessionId, m);
            _pendingManifest = m;

            if (occlusionManager != null)
                occlusionManager.requestedEnvironmentDepthMode = EnvironmentDepthMode.Fastest;

            cameraManager.frameReceived += OnFrameReceived;
            IsRecording = true;
            Debug.Log($"[ORP] Recording started: {CurrentSessionId} -> {SessionIO.SessionDir(CurrentSessionId)}");
        }

        public void StopRecording()
        {
            if (!IsRecording) return;
            cameraManager.frameReceived -= OnFrameReceived;
            IsRecording = false;
            _pendingManifest.frameCount = FrameCount;
            SessionIO.WriteManifest(CurrentSessionId, _pendingManifest);
            Debug.Log($"[ORP] Recording stopped: {CurrentSessionId}, {FrameCount} frames");
        }

        SessionManifest _pendingManifest;

        void OnFrameReceived(ARCameraFrameEventArgs _)
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

                if (!WriteRgbJpg(rgbImage, outW, outH, RgbPath(idx))) return;
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
                    var depth = ReadFloatPlane(depthImage, out int dw, out int dh);
                    if (isIOS && captureLiDARWhenAvailable)
                    {
                        SessionIO.WriteDepthBin(Path.Combine(SessionIO.SessionDir(CurrentSessionId), "lidar", $"{idx:D6}.bin"), depth, dw, dh);
                        lidarRel = $"lidar/{idx:D6}.bin";
                        _pendingManifest.hasLiDAR = true;
                    }
                    else
                    {
                        SessionIO.WriteDepthBin(DepthPath(idx), depth, dw, dh);
                        depthRel = $"depth/{idx:D6}.bin";
                    }
                    if (_pendingManifest.depthWidth == 0) { _pendingManifest.depthWidth = dw; _pendingManifest.depthHeight = dh; }
                    if (!_loggedFormats) Debug.Log($"[ORP] env-depth {dw}x{dh} fmt={depthImage.format} platform={(isIOS ? "iOS/GT" : "Android/candidate")}");
                }

                if (occlusionManager.TryAcquireEnvironmentDepthConfidenceCpuImage(out XRCpuImage confImage))
                    using (confImage)
                    {
                        var conf = ReadBytePlane(confImage, out int cw, out int ch);
                        SessionIO.WriteConfidenceBin(Path.Combine(SessionIO.SessionDir(CurrentSessionId), "depth_conf", $"{idx:D6}.bin"), conf, cw, ch);
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
                    SessionIO.WritePointsBin(
                        Path.Combine(SessionIO.SessionDir(CurrentSessionId), "points", $"{idx:D6}.bin"),
                        _pointBuffer);
                    pointsRel = $"points/{idx:D6}.bin";
                }
            }

            var rec = new FrameRecord
            {
                index = idx,
                timestampNs = (long)(now * 1e9),
                intrinsics = scaledIntr,
                cameraPose = new Pose(pos, arCamera.transform.rotation),
                poseTracked = true,
                sparsePointCount = _pointBuffer.Count,
                rgbFile = $"frames/{idx:D6}.jpg",
                depthFile = depthRel,
                lidarFile = lidarRel,
                pointsFile = pointsRel,
            };
            SessionIO.AppendFrameRecord(CurrentSessionId, rec);

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

        static bool WriteRgbJpg(XRCpuImage image, int outW, int outH, string absPath)
        {
            var conv = new XRCpuImage.ConversionParams
            {
                inputRect = new RectInt(0, 0, image.width, image.height),
                outputDimensions = new Vector2Int(outW, outH),
                outputFormat = TextureFormat.RGBA32,
                transformation = XRCpuImage.Transformation.MirrorY,
            };
            int size = image.GetConvertedDataSize(conv);
            using var buffer = new NativeArray<byte>(size, Allocator.Temp);
            image.Convert(conv, buffer);

            var tex = new Texture2D(outW, outH, TextureFormat.RGBA32, false);
            tex.LoadRawTextureData(buffer);
            tex.Apply();
            var jpg = tex.EncodeToJPG(90);
            Object.Destroy(tex);
            File.WriteAllBytes(absPath, jpg);
            return true;
        }

        /// <summary>Read a single-channel float32 depth CPU image (meters) honoring row stride.</summary>
        static float[] ReadFloatPlane(XRCpuImage img, out int w, out int h)
        {
            w = img.width; h = img.height;
            var plane = img.GetPlane(0);
            var raw = plane.data;
            var outArr = new float[w * h];
            int rowStride = plane.rowStride, pxStride = plane.pixelStride; // expect pxStride == 4
            for (int y = 0; y < h; y++)
            {
                int row = y * rowStride;
                for (int x = 0; x < w; x++)
                {
                    int b = row + x * pxStride;
                    outArr[y * w + x] = System.BitConverter.ToSingle(new byte[] { raw[b], raw[b + 1], raw[b + 2], raw[b + 3] }, 0);
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

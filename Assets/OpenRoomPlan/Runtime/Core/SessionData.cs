using System;
using UnityEngine;

namespace OpenRoomPlan.Core
{
    /// <summary>
    /// On-disk schema for a recorded capture session. A session is a folder:
    ///   frames/000123.jpg          (RGB, downscaled)
    ///   depth/000123.exr           (platform depth, metric, float)
    ///   depth_conf/000123.exr      (platform depth confidence, optional)
    ///   lidar/000123.exr           (LiDAR depth GT, LiDAR devices only)
    ///   models/&lt;modelId&gt;/000123.exr (depth produced offline by a benchmarked model)
    ///   roomplan.json              (RoomPlan GT export, LiDAR iPhone only)
    ///   manifest.json              (this SessionManifest)
    ///   frames.jsonl               (one FrameRecord per line)
    ///
    /// The replay harness reads this so any depth model / pipeline change can be
    /// re-evaluated offline against the exact same captured data, on any machine.
    /// </summary>
    [Serializable]
    public struct SessionManifest
    {
        public string sessionId;
        public string deviceModel;      // e.g. "iPhone15,3" / "SM-S911B"
        public string platform;         // "iOS" | "Android"
        public bool hasLiDAR;           // GT depth path available
        public bool hasRoomPlanGT;      // parametric GT walls available
        public string arProvider;       // "ARKit" | "ARCore"
        public int rgbWidth;            // recorded RGB resolution (downscaled)
        public int rgbHeight;
        public int depthWidth;          // platform depth native resolution
        public int depthHeight;
        public int frameCount;
        public long startTimestampNs;
        public string notes;            // room label, lighting, "blank walls", etc.
        public string schemaVersion;    // bump when the format changes

        /// <summary>
        /// The XRCpuImage.Transformation applied when writing RGB ("MirrorX", "MirrorY", ...).
        /// Recorded because it decides whether the JPEG is in the same frame as the depth map,
        /// and getting that wrong is silent: v1 sessions used MirrorY, which combined with
        /// Unity's bottom-up raw texture rows to leave the JPEG 180 deg rotated relative to
        /// its own depth. Offline consumers branch on this instead of assuming.
        /// </summary>
        public string rgbTransform;

        /// <summary>
        /// True when RGB and depth share an orientation, i.e. a pixel at (u,v) in the JPEG is
        /// the same ray as (u,v) in the depth map (modulo resolution and FOV crop). False for
        /// v1 sessions, which need a 180 deg rotation first.
        /// </summary>
        public bool rgbAlignedWithDepth;

        /// <summary>Screen.orientation at StartRecording, e.g. "Portrait".</summary>
        public string screenOrientation;
    }

    /// <summary>Pinhole intrinsics for the resolution they were captured at.</summary>
    [Serializable]
    public struct CameraIntrinsics
    {
        public float fx, fy, cx, cy;
        public int width, height;

        public bool IsValid => fx > 0f && fy > 0f && width > 0 && height > 0;
    }

    /// <summary>Per-frame metadata (pixel data lives in the sibling files by index).</summary>
    [Serializable]
    public struct FrameRecord
    {
        public int index;

        /// <summary>
        /// Timestamp of the RGB IMAGE itself (XRCpuImage.timestamp), not the app clock. v1
        /// recorded Time.unscaledTime, which cannot be compared against anything the platform
        /// produced and so made pose/image desync undiagnosable.
        /// </summary>
        public long timestampNs;

        /// <summary>
        /// Timestamp of the camera frame whose arrival triggered this capture, and hence of the
        /// pose in <see cref="cameraPose"/>. Differs from <see cref="timestampNs"/> when
        /// TryAcquireLatestCpuImage hands back an older image than the frame just delivered —
        /// that difference IS the pose/image desynchronisation, and it is worth measuring
        /// because the resulting error scales with rotation rate: at 13 deg/s, 228 ms of lag is
        /// 0.16 m of misplacement on a surface 3 m away, against a 4 cm RANSAC threshold.
        /// 0 when the platform reported no frame timestamp.
        /// </summary>
        public long frameTimestampNs;

        public CameraIntrinsics intrinsics;
        public Pose cameraPose;         // world-space VIO pose (position + rotation)
        public bool poseTracked;        // ARSession tracking state was good this frame

        /// <summary>
        /// Screen.orientation as an int at capture time. The CPU images are in SENSOR
        /// orientation while the camera pose is in DISPLAY orientation, so anything that maps
        /// image axes onto camera axes needs to know this; v1 stored it nowhere, leaving the
        /// relationship unresolvable after the fact.
        /// </summary>
        public int screenOrientation;
        public int sparsePointCount;    // VIO feature points available for scale anchoring
        public string rgbFile;          // relative path, e.g. "frames/000123.jpg"
        public string depthFile;        // relative path, "" if none this frame
        public string lidarFile;        // relative path, "" if none
        public string pointsFile;       // relative path to sparse VIO points (points/000123.bin), "" if none
    }
}

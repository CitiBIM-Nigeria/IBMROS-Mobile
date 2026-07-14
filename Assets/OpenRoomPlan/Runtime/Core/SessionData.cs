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
        public long timestampNs;
        public CameraIntrinsics intrinsics;
        public Pose cameraPose;         // world-space VIO pose (position + rotation)
        public bool poseTracked;        // ARSession tracking state was good this frame
        public int sparsePointCount;    // VIO feature points available for scale anchoring
        public string rgbFile;          // relative path, e.g. "frames/000123.jpg"
        public string depthFile;        // relative path, "" if none this frame
        public string lidarFile;        // relative path, "" if none
    }
}

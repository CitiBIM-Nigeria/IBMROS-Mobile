using System.Collections.Generic;
using System.IO;
using UnityEngine;
using OpenRoomPlan.Core;

namespace OpenRoomPlan.Capture
{
    /// <summary>
    /// Reads a recorded session back into <see cref="DepthFrameInput"/>s so any depth model / pipeline
    /// change can be re-evaluated offline against the exact captured data. Plain C# (no MonoBehaviour) so
    /// it runs in the Editor and in offline eval tools, not just at runtime.
    /// </summary>
    public sealed class CaptureSessionPlayer
    {
        public SessionManifest Manifest { get; }
        public IReadOnlyList<FrameRecord> Frames => _frames;
        readonly List<FrameRecord> _frames;
        readonly string _sessionId;

        public CaptureSessionPlayer(string sessionId)
        {
            _sessionId = sessionId;
            Manifest = SessionIO.ReadManifest(sessionId);
            _frames = SessionIO.ReadFrameRecords(sessionId);
        }

        /// <summary>Load one frame's RGB + metadata as a model input. Pixel buffer is freshly allocated.</summary>
        public bool TryLoadInput(int i, out DepthFrameInput input)
        {
            input = default;
            if (i < 0 || i >= _frames.Count) return false;
            var f = _frames[i];

            string rgbPath = Path.Combine(SessionIO.SessionDir(_sessionId), f.rgbFile);
            if (!File.Exists(rgbPath)) return false;

            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            tex.LoadImage(File.ReadAllBytes(rgbPath)); // JPG -> texture
            var rgb = tex.GetPixels32();
            int w = tex.width, h = tex.height;
            Object.DestroyImmediate(tex);

            input = new DepthFrameInput
            {
                frameIndex = f.index,
                width = w,
                height = h,
                rgb = rgb,
                intrinsics = f.intrinsics,
                cameraPose = f.cameraPose,
                timestampNs = f.timestampNs,
                metricAnchorsWorld = null, // only counts were recorded; wire full VIO points later if needed
            };
            return true;
        }

        /// <summary>Recorded platform depth for a frame (meters), if present.</summary>
        public bool TryLoadPlatformDepth(int i, out float[] depth, out int w, out int h)
        {
            depth = null; w = h = 0;
            if (i < 0 || i >= _frames.Count) return false;
            var f = _frames[i];
            if (string.IsNullOrEmpty(f.depthFile)) return false;
            string p = Path.Combine(SessionIO.SessionDir(_sessionId), f.depthFile);
            if (!File.Exists(p)) return false;
            depth = SessionIO.ReadDepthBin(p, out w, out h);
            return true;
        }

        /// <summary>LiDAR GT depth for a frame (meters), if present.</summary>
        public bool TryLoadLiDARDepth(int i, out float[] depth, out int w, out int h)
        {
            depth = null; w = h = 0;
            if (i < 0 || i >= _frames.Count) return false;
            var f = _frames[i];
            if (string.IsNullOrEmpty(f.lidarFile)) return false;
            string p = Path.Combine(SessionIO.SessionDir(_sessionId), f.lidarFile);
            if (!File.Exists(p)) return false;
            depth = SessionIO.ReadDepthBin(p, out w, out h);
            return true;
        }

        public static IEnumerable<string> ListSessions()
        {
            if (!Directory.Exists(SessionIO.SessionsRoot)) yield break;
            foreach (var dir in Directory.GetDirectories(SessionIO.SessionsRoot))
                yield return Path.GetFileName(dir);
        }
    }
}

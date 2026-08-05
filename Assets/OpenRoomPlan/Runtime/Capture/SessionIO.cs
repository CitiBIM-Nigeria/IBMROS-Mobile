using System.Collections.Generic;
using System.IO;
using UnityEngine;
using OpenRoomPlan.Core;

namespace OpenRoomPlan.Capture
{
    /// <summary>
    /// Read/write helpers for the on-disk session format (see SessionData.cs).
    /// Depth is stored as self-describing raw float32 binary (.bin) rather than EXR, because
    /// Unity's runtime image loader cannot decode EXR — .bin round-trips losslessly on-device.
    /// Layout: [int32 width][int32 height][float32 * w*h], row-major, meters, NaN = invalid.
    /// </summary>
    public static class SessionIO
    {
        /// <summary>
        /// v2 (2026-08-05): frame timestamps became the CAMERA IMAGE's own timestamp rather
        /// than Unity's clock, <c>frameTimestampNs</c> and <c>screenOrientation</c> were added,
        /// and RGB is written in the same orientation as the depth map (v1 JPEGs are 180 deg
        /// rotated relative to their depth — see CaptureSessionRecorder.EncodeRgbJpg). Readers
        /// that care about RGB/depth alignment must check this.
        /// </summary>
        public const string SchemaVersion = "orp-session-2";
        const string ManifestFile = "manifest.json";
        const string FramesFile = "frames.jsonl";

        /// <summary>Absolute path of the frame-record file, for queued appends.</summary>
        public static string FrameRecordPath(string sessionId) =>
            Path.Combine(SessionDir(sessionId), FramesFile);

        /// <summary>One JSON-Lines record, ready to append.</summary>
        public static string EncodeFrameRecord(in FrameRecord r) => JsonUtility.ToJson(r) + "\n";

        public static string SessionsRoot =>
            Path.Combine(Application.persistentDataPath, "OpenRoomPlan", "Sessions");

        public static string SessionDir(string sessionId) => Path.Combine(SessionsRoot, sessionId);

        public static void EnsureSessionDirs(string sessionId)
        {
            string root = SessionDir(sessionId);
            Directory.CreateDirectory(Path.Combine(root, "frames"));
            Directory.CreateDirectory(Path.Combine(root, "depth"));
            Directory.CreateDirectory(Path.Combine(root, "depth_conf"));
            Directory.CreateDirectory(Path.Combine(root, "lidar"));
            Directory.CreateDirectory(Path.Combine(root, "points"));
            Directory.CreateDirectory(Path.Combine(root, "models"));
        }

        // ---- manifest ----
        public static void WriteManifest(string sessionId, in SessionManifest m) =>
            File.WriteAllText(Path.Combine(SessionDir(sessionId), ManifestFile), JsonUtility.ToJson(m, true));

        public static SessionManifest ReadManifest(string sessionId) =>
            JsonUtility.FromJson<SessionManifest>(File.ReadAllText(Path.Combine(SessionDir(sessionId), ManifestFile)));

        // ---- frame records (JSON Lines: one FrameRecord per line) ----
        public static void AppendFrameRecord(string sessionId, in FrameRecord r) =>
            File.AppendAllText(Path.Combine(SessionDir(sessionId), FramesFile), JsonUtility.ToJson(r) + "\n");

        public static List<FrameRecord> ReadFrameRecords(string sessionId)
        {
            var list = new List<FrameRecord>();
            string path = Path.Combine(SessionDir(sessionId), FramesFile);
            if (!File.Exists(path)) return list;
            foreach (var line in File.ReadAllLines(path))
                if (!string.IsNullOrWhiteSpace(line))
                    list.Add(JsonUtility.FromJson<FrameRecord>(line));
            return list;
        }

        // ---- encoders ----
        // These exist so capture can serialize on the main thread (a BlockCopy) and hand the
        // bytes to SessionWriteQueue, keeping file I/O off the frame path. They also replace
        // per-element BinaryWriter loops, which cost one virtual call per float.
        // Little-endian is written explicitly rather than inherited from BitConverter, so the
        // format does not silently depend on host endianness.

        static void PutInt32(byte[] b, int off, int v)
        {
            b[off] = (byte)v; b[off + 1] = (byte)(v >> 8);
            b[off + 2] = (byte)(v >> 16); b[off + 3] = (byte)(v >> 24);
        }

        /// <summary>[int32 w][int32 h][float32 * w*h] metres — identical bytes to WriteDepthBin.</summary>
        public static byte[] EncodeDepthBin(float[] depth, int width, int height)
        {
            var bytes = new byte[8 + depth.Length * 4];
            PutInt32(bytes, 0, width);
            PutInt32(bytes, 4, height);
            System.Buffer.BlockCopy(depth, 0, bytes, 8, depth.Length * 4);
            return bytes;
        }

        public static byte[] EncodeConfidenceBin(byte[] conf, int width, int height)
        {
            var bytes = new byte[8 + conf.Length];
            PutInt32(bytes, 0, width);
            PutInt32(bytes, 4, height);
            System.Buffer.BlockCopy(conf, 0, bytes, 8, conf.Length);
            return bytes;
        }

        /// <summary>[int32 count][float32 x,y,z] * count, world space.</summary>
        public static byte[] EncodePointsBin(IReadOnlyList<Vector3> pts)
        {
            var bytes = new byte[4 + pts.Count * 12];
            PutInt32(bytes, 0, pts.Count);
            var scratch = new float[pts.Count * 3];
            for (int i = 0; i < pts.Count; i++)
            {
                scratch[i * 3] = pts[i].x; scratch[i * 3 + 1] = pts[i].y; scratch[i * 3 + 2] = pts[i].z;
            }
            System.Buffer.BlockCopy(scratch, 0, bytes, 4, scratch.Length * 4);
            return bytes;
        }

        // ---- depth binary (self-describing) ----
        public static void WriteDepthBin(string absPath, float[] depth, int width, int height) =>
            File.WriteAllBytes(absPath, EncodeDepthBin(depth, width, height));

        public static float[] ReadDepthBin(string absPath, out int width, out int height)
        {
            using var br = new BinaryReader(File.Open(absPath, FileMode.Open));
            width = br.ReadInt32();
            height = br.ReadInt32();
            var depth = new float[width * height];
            for (int i = 0; i < depth.Length; i++) depth[i] = br.ReadSingle();
            return depth;
        }

        public static void WriteConfidenceBin(string absPath, byte[] conf, int width, int height) =>
            File.WriteAllBytes(absPath, EncodeConfidenceBin(conf, width, height));

        public static byte[] ReadConfidenceBin(string absPath, out int width, out int height)
        {
            using var br = new BinaryReader(File.Open(absPath, FileMode.Open));
            width = br.ReadInt32();
            height = br.ReadInt32();
            return br.ReadBytes(width * height);
        }

        // ---- sparse VIO points (world space): [int32 count][float32 x,y,z]*count ----
        public static void WritePointsBin(string absPath, IReadOnlyList<Vector3> pts) =>
            File.WriteAllBytes(absPath, EncodePointsBin(pts));

        public static Vector3[] ReadPointsBin(string absPath)
        {
            using var br = new BinaryReader(File.Open(absPath, FileMode.Open));
            int n = br.ReadInt32();
            var pts = new Vector3[n];
            for (int i = 0; i < n; i++)
                pts[i] = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
            return pts;
        }

        /// <summary>Depth produced by an offline/cloud model, keyed under models/&lt;modelId&gt;/.</summary>
        public static string ModelDepthPath(string sessionId, string modelId, int frameIndex)
        {
            string dir = Path.Combine(SessionDir(sessionId), "models", modelId);
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, $"{frameIndex:D6}.bin");
        }
    }
}

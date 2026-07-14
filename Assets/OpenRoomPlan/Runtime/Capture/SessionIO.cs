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
        public const string SchemaVersion = "orp-session-1";
        const string ManifestFile = "manifest.json";
        const string FramesFile = "frames.jsonl";

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

        // ---- depth binary (self-describing) ----
        public static void WriteDepthBin(string absPath, float[] depth, int width, int height)
        {
            using var bw = new BinaryWriter(File.Open(absPath, FileMode.Create));
            bw.Write(width);
            bw.Write(height);
            for (int i = 0; i < depth.Length; i++) bw.Write(depth[i]);
        }

        public static float[] ReadDepthBin(string absPath, out int width, out int height)
        {
            using var br = new BinaryReader(File.Open(absPath, FileMode.Open));
            width = br.ReadInt32();
            height = br.ReadInt32();
            var depth = new float[width * height];
            for (int i = 0; i < depth.Length; i++) depth[i] = br.ReadSingle();
            return depth;
        }

        public static void WriteConfidenceBin(string absPath, byte[] conf, int width, int height)
        {
            using var bw = new BinaryWriter(File.Open(absPath, FileMode.Create));
            bw.Write(width);
            bw.Write(height);
            bw.Write(conf, 0, conf.Length);
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

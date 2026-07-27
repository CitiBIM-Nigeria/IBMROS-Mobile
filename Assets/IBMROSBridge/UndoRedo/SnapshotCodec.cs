using System.IO;
using System.IO.Compression;
using System.Text;

namespace IBMROS.Bridge.UndoRedo
{
    /// <summary>
    /// Encodes document snapshots for in-memory history storage. Small states stay as raw
    /// UTF-8; larger ones are gzipped (floor-plan JSON compresses ~10:1, so a 50-step
    /// history of a large furnished plan stays in the hundreds of kilobytes on mobile).
    /// Pure C# — no Unity dependency — so it is unit-testable outside the editor.
    /// GZipStream is fully supported by IL2CPP on Android/iOS.
    /// </summary>
    public static class SnapshotCodec
    {
        /// <summary>States at or below this UTF-8 size are stored raw; gzip gains nothing.</summary>
        public const int COMPRESS_THRESHOLD_BYTES = 4096;

        public static byte[] Encode(string state, out bool compressed)
        {
            byte[] raw = Encoding.UTF8.GetBytes(state);
            if (raw.Length <= COMPRESS_THRESHOLD_BYTES)
            {
                compressed = false;
                return raw;
            }
            using (MemoryStream ms = new MemoryStream(raw.Length / 4))
            {
                using (GZipStream gz = new GZipStream(ms, CompressionLevel.Fastest, leaveOpen: true))
                {
                    gz.Write(raw, 0, raw.Length);
                }
                byte[] packed = ms.ToArray();
                // Pathological already-compressed content could inflate; keep the smaller form.
                if (packed.Length < raw.Length)
                {
                    compressed = true;
                    return packed;
                }
            }
            compressed = false;
            return raw;
        }

        public static string Decode(byte[] data, bool compressed)
        {
            if (!compressed)
                return Encoding.UTF8.GetString(data);
            using (MemoryStream input = new MemoryStream(data))
            using (GZipStream gz = new GZipStream(input, CompressionMode.Decompress))
            using (MemoryStream output = new MemoryStream(data.Length * 4))
            {
                gz.CopyTo(output);
                return Encoding.UTF8.GetString(output.GetBuffer(), 0, (int)output.Length);
            }
        }
    }
}

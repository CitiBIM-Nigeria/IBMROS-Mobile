using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;
using UnityEngine;

namespace OpenRoomPlan.Capture
{
    /// <summary>
    /// Moves a session's disk writes off the main thread.
    ///
    /// WHY THIS EXISTS. The first real capture requested 8 Hz and achieved 4.4 Hz (median
    /// 228 ms between frames, 2.6% of gaps over 500 ms, worst 1167 ms), because every
    /// recorded frame did a synchronous JPEG encode plus four blocking File.WriteAllBytes
    /// calls — roughly 75 KB across depth, confidence, points and the frame record, each
    /// paying its own open/write/close. That is not merely a comfort problem: image-vs-pose
    /// desynchronisation scales with the frame interval, and at the session's median 13.1
    /// deg/s of rotation one 228 ms interval is 3.0 deg of angular error, which is 0.16 m of
    /// positional error on a surface 3 m away. RANSAC's inlier threshold is 4 cm. Halving the
    /// interval roughly halves that error, so throughput here is reconstruction accuracy.
    ///
    /// Nothing on this thread touches a Unity API — only byte[] and File — which is what
    /// makes it safe. Callers encode on the main thread (cheap: Buffer.BlockCopy) and hand
    /// over bytes.
    ///
    /// BOUNDED ON PURPOSE. The queue holds a limited number of items; when the writer falls
    /// behind, Enqueue blocks the caller instead of growing without limit. On a phone,
    /// unbounded buffering of ~75 KB per frame would trade a visible frame-rate problem for
    /// an invisible out-of-memory one. Back-pressure keeps the failure honest, and
    /// <see cref="DroppedFrames"/> is not a thing precisely because nothing is dropped.
    /// </summary>
    public sealed class SessionWriteQueue : IDisposable
    {
        private struct Item
        {
            public string path;
            public byte[] bytes;   // null for a text append
            public string text;    // non-null for an append
        }

        private const int Capacity = 96;

        /// <summary>
        /// UTF-8 with NO byte-order mark. Encoding.UTF8 — the static property — emits one when
        /// it creates a file, and File.AppendAllText's own default does not. Passing
        /// Encoding.UTF8 to match the previous behaviour therefore silently CHANGED it: the
        /// first v2 session put an EF BB BF in front of frames.jsonl, and strict JSON parsers
        /// reject line 1 outright (Python's json.loads raises, and JsonUtility would too).
        /// Text files in this format must stay BOM-free.
        /// </summary>
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        private readonly BlockingCollection<Item> _queue =
            new BlockingCollection<Item>(new ConcurrentQueue<Item>(), Capacity);
        private readonly Thread _thread;
        private int _written;
        private volatile string _firstError;

        /// <summary>Files fully written so far (for diagnostics).</summary>
        public int Written => _written;

        /// <summary>First write failure, or null. Surfaced rather than swallowed.</summary>
        public string FirstError => _firstError;

        /// <summary>Items still waiting to hit disk.</summary>
        public int Pending => _queue.Count;

        public SessionWriteQueue()
        {
            _thread = new Thread(Pump)
            {
                Name = "ORP SessionWriter",
                IsBackground = true,     // never keeps the process alive on its own
                Priority = System.Threading.ThreadPriority.BelowNormal,
            };
            _thread.Start();
        }

        /// <summary>Queue a whole-file write. Blocks if the writer is behind (see class note).</summary>
        public void Write(string absPath, byte[] bytes)
        {
            if (bytes == null) return;
            try { _queue.Add(new Item { path = absPath, bytes = bytes }); }
            catch (InvalidOperationException) { /* completed; recording already stopped */ }
        }

        /// <summary>Queue an append (used for frames.jsonl).</summary>
        public void Append(string absPath, string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            try { _queue.Add(new Item { path = absPath, text = text }); }
            catch (InvalidOperationException) { }
        }

        private void Pump()
        {
            foreach (Item item in _queue.GetConsumingEnumerable())
            {
                try
                {
                    if (item.text != null)
                        File.AppendAllText(item.path, item.text, Utf8NoBom);
                    else
                        File.WriteAllBytes(item.path, item.bytes);
                    Interlocked.Increment(ref _written);
                }
                catch (Exception e)
                {
                    // Keep draining: one bad path must not silently abandon the rest of the
                    // session. The first reason is kept for the caller to report.
                    if (_firstError == null) _firstError = $"{Path.GetFileName(item.path)}: {e.Message}";
                }
            }
        }

        /// <summary>
        /// Stops accepting work and blocks until everything queued has been written, so a
        /// session on disk is complete the moment recording reports that it stopped.
        /// </summary>
        public void CompleteAndWait(float timeoutSeconds = 15f)
        {
            _queue.CompleteAdding();
            if (!_thread.Join((int)(timeoutSeconds * 1000f)))
                Debug.LogWarning($"[ORP] Session writer still draining after {timeoutSeconds}s " +
                                 $"({_queue.Count} items left) — session may be incomplete.");
            if (_firstError != null)
                Debug.LogError($"[ORP] Session write error: {_firstError}");
        }

        public void Dispose()
        {
            if (!_queue.IsAddingCompleted) CompleteAndWait();
            _queue.Dispose();
        }
    }
}

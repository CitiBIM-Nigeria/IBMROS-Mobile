using System.Collections.Generic;

namespace IBMROS.Bridge.UndoRedo
{
    /// <summary>
    /// The undo/redo document history: a linear list of labelled state mementos with a
    /// cursor, bounded by both step count and total byte budget. Storage-format-agnostic
    /// (states are opaque strings — today FloorMapV2 JSON) and pure C# with no Unity
    /// dependency, so the entire undo data structure is unit-testable outside Unity.
    ///
    /// Invariants:
    ///  - entries[cursor] is always the current document state;
    ///  - the current state is additionally cached uncompressed, so duplicate-push
    ///    rejection is an exact string comparison (never a hash gamble);
    ///  - eviction only ever removes the oldest entries and never the current one, so
    ///    the cursor stays valid; a Push after Undo truncates the redo tail first.
    /// </summary>
    public sealed class SnapshotHistory
    {
        private sealed class Entry
        {
            public string Label;
            public byte[] Data;
            public bool Compressed;
            public int Bytes => Data.Length;
        }

        private readonly List<Entry> entries = new List<Entry>();
        private readonly int maxEntries;
        private readonly long maxTotalBytes;
        private int cursor = -1;
        private string currentCache;

        public SnapshotHistory(int maxEntries = 50, long maxTotalBytes = 4L * 1024 * 1024)
        {
            this.maxEntries = maxEntries < 2 ? 2 : maxEntries;
            this.maxTotalBytes = maxTotalBytes;
        }

        public int Count => entries.Count;
        public int Cursor => cursor;
        public long TotalBytes { get; private set; }
        public bool CanUndo => cursor > 0;
        public bool CanRedo => cursor >= 0 && cursor < entries.Count - 1;

        /// <summary>Label of the step Undo would revert (the action that produced the current state).</summary>
        public string UndoLabel => CanUndo ? entries[cursor].Label : null;

        /// <summary>Label of the step Redo would re-apply.</summary>
        public string RedoLabel => CanRedo ? entries[cursor + 1].Label : null;

        /// <summary>The current state, or null when the history is empty.</summary>
        public string Current => currentCache;

        /// <summary>
        /// Records a new state. Returns false (and stores nothing) when the state is
        /// identical to the current one. A push while undone discards the redo tail.
        /// </summary>
        public bool Push(string state, string label)
        {
            if (state == null)
                return false;
            if (cursor >= 0 && state == currentCache)
                return false;

            if (cursor < entries.Count - 1)
            {
                for (int i = entries.Count - 1; i > cursor; i--)
                {
                    TotalBytes -= entries[i].Bytes;
                    entries.RemoveAt(i);
                }
            }

            bool compressed;
            byte[] data = SnapshotCodec.Encode(state, out compressed);
            entries.Add(new Entry { Label = label, Data = data, Compressed = compressed });
            TotalBytes += data.Length;
            cursor = entries.Count - 1;
            currentCache = state;

            EvictToBudget();
            return true;
        }

        /// <summary>
        /// Overwrites the CURRENT entry's state without touching the cursor, the redo
        /// tail, or the label. Used to re-canonicalize the baseline after a restore:
        /// serialize(deserialize(S)) is not byte-identical to S in this engine (openings'
        /// directions are recomputed by re-snap, floats drift), so after rebuilding a
        /// state the history must adopt the scene's OWN serialization as current —
        /// otherwise the next capture sees a phantom "change", pushes a bogus step and
        /// truncates redo. Returns false when the history is empty.
        /// </summary>
        public bool ReplaceCurrent(string state)
        {
            if (cursor < 0 || state == null)
                return false;
            if (state == currentCache)
                return true;
            TotalBytes -= entries[cursor].Bytes;
            bool compressed;
            byte[] data = SnapshotCodec.Encode(state, out compressed);
            entries[cursor].Data = data;
            entries[cursor].Compressed = compressed;
            TotalBytes += data.Length;
            currentCache = state;
            return true;
        }

        /// <summary>Moves back one step and returns that state, or null if impossible.</summary>
        public string Undo()
        {
            if (!CanUndo)
                return null;
            cursor--;
            currentCache = SnapshotCodec.Decode(entries[cursor].Data, entries[cursor].Compressed);
            return currentCache;
        }

        /// <summary>Moves forward one step and returns that state, or null if impossible.</summary>
        public string Redo()
        {
            if (!CanRedo)
                return null;
            cursor++;
            currentCache = SnapshotCodec.Decode(entries[cursor].Data, entries[cursor].Compressed);
            return currentCache;
        }

        public void Clear()
        {
            entries.Clear();
            cursor = -1;
            currentCache = null;
            TotalBytes = 0;
        }

        private void EvictToBudget()
        {
            // Never evict the current entry; undo depth simply shrinks under pressure.
            while (cursor > 0 && (entries.Count > maxEntries || TotalBytes > maxTotalBytes))
            {
                TotalBytes -= entries[0].Bytes;
                entries.RemoveAt(0);
                cursor--;
            }
        }
    }
}

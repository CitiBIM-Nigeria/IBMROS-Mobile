using System.Collections.Generic;

namespace IBMROS.Bridge.UndoRedo
{
    /// <summary>
    /// The editor's single undo history: one linear, cursor-bearing list of steps that
    /// covers BOTH editable domains, so the order the user performed things in is the
    /// order Undo walks back through.
    ///
    /// Two kinds of entry live in the same list:
    ///  - STATE entries — a labelled memento of the whole floor-plan document (opaque
    ///    string, today FloorMapV2 JSON), byte-budgeted and gzip-compressed. Room
    ///    geometry has no reliable per-mutation event coverage in the vendor code, so
    ///    it is captured wholesale (see UndoRedoService's header).
    ///  - COMMAND entries — a furniture operation (add / move / rotate / scale /
    ///    delete / duplicate) expressed as its own inverse. Furniture lives in scene
    ///    GameObjects, not in the plan document, so replaying a document snapshot
    ///    could never restore it; a command is both exact and cheap.
    ///
    /// The two compose because they are DISJOINT: a furniture command never changes the
    /// plan document, so the document state that applies at any cursor position is the
    /// nearest STATE entry at or before it. That is the whole trick — command entries
    /// carry no Data and resolve their state by walking back.
    ///
    /// Invariants:
    ///  - StateAt(cursor) is always the current document state;
    ///  - entry 0 always owns a state (eviction hands its bytes forward if needed), so
    ///    every reachable cursor position resolves;
    ///  - the current state is cached uncompressed, so duplicate-push rejection is an
    ///    exact string comparison (never a hash gamble);
    ///  - eviction only removes the oldest entries and never the current one, so the
    ///    cursor stays valid; a push after Undo truncates the redo tail first.
    ///
    /// Pure C# with no Unity dependency (IUndoableAction is itself a plain interface),
    /// so the entire undo data structure is unit-testable outside Unity.
    /// </summary>
    public sealed class SnapshotHistory
    {
        /// <summary>What a cursor move produced: the document state now in effect, and
        /// the furniture command (if any) whose inverse the caller must apply.</summary>
        public readonly struct Step
        {
            /// <summary>The document state at the new cursor, or null when nothing moved.</summary>
            public readonly string State;

            /// <summary>Non-null when the step crossed a furniture command entry.</summary>
            public readonly IUndoableAction Command;

            public Step(string state, IUndoableAction command)
            {
                State = state;
                Command = command;
            }

            public bool Moved => State != null || Command != null;
        }

        private sealed class Entry
        {
            public string Label;
            /// <summary>Null on command entries — they inherit the previous state.</summary>
            public byte[] Data;
            public bool Compressed;
            /// <summary>Null on state entries.</summary>
            public IUndoableAction Command;
            public int Bytes => Data != null ? Data.Length : 0;
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

        /// <summary>The current document state, or null when the history is empty.</summary>
        public string Current => currentCache;

        /// <summary>True when the entry the cursor sits on is a furniture command.</summary>
        public bool CurrentIsCommand => cursor >= 0 && entries[cursor].Command != null;

        /// <summary>
        /// Records a new document state. Returns false (and stores nothing) when the
        /// state is identical to the current one. A push while undone discards the redo
        /// tail.
        /// </summary>
        public bool Push(string state, string label)
        {
            if (state == null)
                return false;
            if (cursor >= 0 && state == currentCache)
                return false;

            TruncateRedoTail();

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
        /// Records a furniture operation as its own step, in sequence with the document
        /// steps around it. Rejected when the history has no baseline yet: every cursor
        /// position must resolve to a document state, and entry 0 is what guarantees it.
        /// </summary>
        public bool PushCommand(IUndoableAction command, string label)
        {
            if (command == null || cursor < 0)
                return false;

            TruncateRedoTail();

            entries.Add(new Entry { Label = label, Command = command });
            cursor = entries.Count - 1;
            // currentCache is unchanged: a furniture command does not touch the document.

            EvictToBudget();
            return true;
        }

        /// <summary>
        /// Overwrites the CURRENT document state without touching the cursor, the redo
        /// tail, or any label. Used to re-canonicalize the baseline after a restore:
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
            int owner = OwnerIndex(cursor);
            if (owner < 0)
                return false;
            TotalBytes -= entries[owner].Bytes;
            bool compressed;
            byte[] data = SnapshotCodec.Encode(state, out compressed);
            entries[owner].Data = data;
            entries[owner].Compressed = compressed;
            TotalBytes += data.Length;
            currentCache = state;
            return true;
        }

        /// <summary>
        /// Moves back one step. The returned Step names the document state now in effect
        /// and, when the reverted entry was a furniture command, that command — the
        /// caller applies its inverse instead of restoring the (unchanged) document.
        /// </summary>
        public Step Undo()
        {
            if (!CanUndo)
                return default;
            IUndoableAction reverted = entries[cursor].Command;
            cursor--;
            currentCache = StateAt(cursor);
            return new Step(currentCache, reverted);
        }

        /// <summary>Moves forward one step; see <see cref="Undo"/> for the Step contract.</summary>
        public Step Redo()
        {
            if (!CanRedo)
                return default;
            cursor++;
            currentCache = StateAt(cursor);
            return new Step(currentCache, entries[cursor].Command);
        }

        public void Clear()
        {
            entries.Clear();
            cursor = -1;
            currentCache = null;
            TotalBytes = 0;
        }

        // ------------------------------------------------------------------ internals

        private void TruncateRedoTail()
        {
            if (cursor >= entries.Count - 1)
                return;
            for (int i = entries.Count - 1; i > cursor; i--)
            {
                TotalBytes -= entries[i].Bytes;
                entries.RemoveAt(i);
            }
        }

        /// <summary>Index of the entry that owns the document state in effect at <paramref name="index"/>.</summary>
        private int OwnerIndex(int index)
        {
            for (int i = index; i >= 0; i--)
                if (entries[i].Data != null)
                    return i;
            return -1;
        }

        private string StateAt(int index)
        {
            int owner = OwnerIndex(index);
            return owner < 0 ? null : SnapshotCodec.Decode(entries[owner].Data, entries[owner].Compressed);
        }

        private void EvictToBudget()
        {
            // Never evict the current entry; undo depth simply shrinks under pressure.
            while (cursor > 0 && (entries.Count > maxEntries || TotalBytes > maxTotalBytes))
            {
                Entry oldest = entries[0];
                // The evicted entry may be the state owner for the command entries that
                // follow it. Hand the bytes forward so entry 0 always resolves.
                if (oldest.Data != null && entries.Count > 1 && entries[1].Data == null)
                {
                    entries[1].Data = oldest.Data;
                    entries[1].Compressed = oldest.Compressed;
                }
                else
                {
                    TotalBytes -= oldest.Bytes;
                }
                entries.RemoveAt(0);
                cursor--;
            }
        }
    }
}

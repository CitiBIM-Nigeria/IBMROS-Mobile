using System;
using UnityEngine;

namespace IBMROS.Core
{
    /// <summary>
    /// Architecture track A1 — the document mutation pipe.
    ///
    /// One observable channel that every floor-plan mutation announces itself on, raised
    /// at the mutation *source* (tagged `// IBMROS:` edits in the editing code), not
    /// inferred from rebuild plumbing. Rebuild events fire for non-mutations too (loads,
    /// undo restores, re-snaps), which is why they can't serve as the mutation signal.
    ///
    /// Consumers today: UndoRedoService (capture + step labels) and AutosaveService
    /// (dirty tracking). Future consumers: dirty-scoped rebuilds (A5), telemetry (P10).
    /// This pipe is the observability half of the mutation gateway; A3 adds the API half
    /// (all edits become gateway calls, which raise these events themselves).
    /// </summary>
    public static class DocumentEvents
    {
        public static event Action<DocumentChange> OnDocumentChanged;

        /// <summary>Announce a document mutation. Label is optional UI-facing text ("Rename").</summary>
        public static void RaiseChanged(DocumentChangeKind kind, string label = null)
        {
            OnDocumentChanged?.Invoke(new DocumentChange(kind, label));
        }

        // Static events survive disabled domain reload between play sessions; reset
        // explicitly so stale subscribers can never leak across runs.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            OnDocumentChanged = null;
        }
    }

    public enum DocumentChangeKind
    {
        Geometry,        // control points of a room/outside area moved, added, removed
        Opening,         // a door/window/opening placed or moved
        ItemSettings,    // per-item properties (dimensions, window params, colors)
        Rename,          // item name changed (was a silent mutation before A1)
        Delete,
        Duplicate,
        Floor,           // floor added / duplicated / removed
        BuildingSettings,// global construction settings (labels arrive with A3 gateway)
        External         // raised via UndoRedoService.NotifyMutation by non-Exoa features
    }

    public readonly struct DocumentChange
    {
        public readonly DocumentChangeKind Kind;
        public readonly string Label;

        public DocumentChange(DocumentChangeKind kind, string label)
        {
            Kind = kind;
            Label = label;
        }
    }
}

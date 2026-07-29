using System.Collections.Generic;
using Exoa.Designer;
using IBMROS.Core;
using IBMROS.Designer.Plan;
using UnityEngine;
using static Exoa.Designer.DataModel;

namespace IBMROS.Designer.Openings
{
    /// <summary>
    /// The Wall → Opening → Door/Window relationship, expressed as geometry, plus every
    /// mutation a door or window supports.
    ///
    /// A door is NOT furniture. It has no free position: it owns a slot in a wall
    /// segment, and everything the user can do to it is a change to that slot (slide it
    /// along, widen it, raise it) or to its appearance. So this class deals in
    /// "distance along the wall" rather than world positions, and every write goes
    /// through the FloorPlanEditor gateway, which is what makes 2D↔3D sync and undo
    /// automatic: the document is the single state, DocumentEvents fire, ScopedRebuild
    /// re-cuts the affected walls, and each op is one labelled undo step.
    ///
    /// TWO PIECES OF PLUGIN GEOMETRY THIS HAS TO HONOUR (both verified in
    /// OpeningController.Rebuild):
    ///   1. THE CONTROL POINT IS THE OPENING'S CENTRE.
    ///      This is easy to get wrong from the source alone: OpeningController.Rebuild
    ///      places each instance at worldPos + direction·Width/2, which reads as though
    ///      the control point were an edge. It is not — ProceduralOpening builds its mesh
    ///      offset by −Width/2 in local space, so that instance offset cancels out.
    ///      Measured in play mode for a 1.20 m door on the z=−2 wall: control point
    ///      x=0.900, instance transform x=1.500, but the rendered bounds centre is x=0.900
    ///      and the hole cut in the wall spans [0.310, 1.490] — centre 0.900, width 1.180.
    ///      So the hole and the visual are both centred on the control point: a move
    ///      writes the wanted centre directly, and a width change does not shift the
    ///      opening at all.
    ///   2. The stored `directions` vector is DERIVED, not authored:
    ///      ControlPointsController.ReSnapControlPoints overwrites every control point's
    ///      dir from whichever wall it snaps to. That is why a move needs no orientation
    ///      bookkeeping at all — the opening re-aligns to its wall for free — and why
    ///      facing has to live in item state (openingFlipped) instead of in dir.
    /// </summary>
    public static class OpeningAnchor
    {
        /// <summary>Narrowest sensible opening (metres). A door leaf below this is not a door.</summary>
        public const float MIN_WIDTH = 0.4f;

        /// <summary>Shortest sensible opening (metres).</summary>
        public const float MIN_HEIGHT = 0.4f;

        /// <summary>Kept clear of the wall top so an opening never becomes a gap in the ceiling line.</summary>
        public const float HEAD_CLEARANCE = 0.05f;

        /// <summary>How far from a wall line a control point still counts as sitting on it.</summary>
        private const float ON_WALL_TOLERANCE_M = 0.35f;

        // ------------------------------------------------------------------ the slot

        /// <summary>One wall segment, and the range of slots an opening can occupy in it.</summary>
        public struct WallSlot
        {
            public bool Valid;
            public string RoomId;
            public int SegIndex;
            /// <summary>Segment ends, metres, world XZ.</summary>
            public Vector2 A, B;
            /// <summary>Unit vector A→B — the direction the wall runs.</summary>
            public Vector2 Tangent;
            public float Length;

            /// <summary>Distance along the wall from A for a world point.</summary>
            public float AlongOf(Vector2 p) => Vector2.Dot(p - A, Tangent);

            public Vector2 PointAt(float along) => A + Tangent * along;

            /// <summary>
            /// The centre range an opening of this width can occupy without any part of
            /// it leaving the segment.
            /// </summary>
            public float MinCentre(float width) => width * 0.5f;
            public float MaxCentre(float width) => Length - width * 0.5f;

            /// <summary>
            /// The widest this opening can be while staying centred where it is. Doubling
            /// the distance to the nearer end is the binding constraint, because widening
            /// grows the opening symmetrically about its centre.
            /// </summary>
            public float MaxWidthAt(float centreAlong) =>
                2f * Mathf.Max(0f, Mathf.Min(centreAlong, Length - centreAlong));
        }

        // ------------------------------------------------------------------ queries

        public static bool IsOpening(UIBaseItem ui) =>
            ui != null && (ui.sequencingItemType == FloorMapItemType.Door ||
                           ui.sequencingItemType == FloorMapItemType.Window ||
                           ui.sequencingItemType == FloorMapItemType.Opening);

        /// <summary>The live item for an opening id, or null.</summary>
        public static UIBaseItem Find(string itemId)
        {
            if (string.IsNullOrEmpty(itemId))
                return null;
            UIBaseItem ui = PlanEditorUtil.FindItem(itemId);
            return IsOpening(ui) ? ui : null;
        }

        /// <summary>The opening's control point in metres (world XZ), or null when unavailable.</summary>
        public static Vector2? ControlPointOf(UIBaseItem ui)
        {
            if (ui == null || ui.cpc == null)
                return null;
            List<Vector3> pts = ui.cpc.GetPointsWorldPositionList();
            if (pts == null || pts.Count == 0)
                return null;
            return PlanEditorUtil.WorldToMeters(pts[0]);
        }

        /// <summary>
        /// The opening's centre in metres. That is the control point itself — both the
        /// wall hole and the rendered door are centred on it (see the class notes).
        /// </summary>
        public static Vector2? CentreOf(UIBaseItem ui) => ControlPointOf(ui);

        /// <summary>
        /// The wall segment this opening is hosted by.
        ///
        /// Prefers the room OpeningHostRegistry recorded — that binding was written when
        /// a room authoritatively claimed the opening during its own rebuild, so it
        /// follows the wall even after a large move — and falls back to the nearest wall
        /// across all spaces for openings that have not been claimed yet.
        /// </summary>
        public static WallSlot SlotOf(string itemId) => SlotOf(Find(itemId));

        public static WallSlot SlotOf(UIBaseItem ui)
        {
            var slot = new WallSlot();
            Vector2? cp = ControlPointOf(ui);
            if (!cp.HasValue)
                return slot;

            // The control point is the opening's centre, so it is already the best probe:
            // it sits on the wall line, mid-opening, furthest from either corner.
            Vector2 probe = cp.Value;

            string hostRoom = OpeningHostRegistry.HostOf(ui.ItemUniqueId);
            if (!string.IsNullOrEmpty(hostRoom) &&
                TryNearestEdgeOf(hostRoom, probe, ref slot))
                return slot;

            foreach (UIBaseItem space in PlanEditorUtil.AllSpaces())
                TryNearestEdgeOf(space.ItemUniqueId, probe, ref slot);
            return slot;
        }

        private static bool TryNearestEdgeOf(string roomId, Vector2 probe, ref WallSlot best)
        {
            UIBaseItem room = PlanEditorUtil.FindItem(roomId);
            if (room == null)
                return false;
            List<Vector3> world = PlanEditorUtil.WorldPoints(room);
            if (world.Count < 2)
                return false;

            float bestD = best.Valid
                ? DistanceTo(best, probe)
                : ON_WALL_TOLERANCE_M * ON_WALL_TOLERANCE_M;
            bool found = false;

            for (int i = 0; i < world.Count; i++)
            {
                Vector2 a = PlanEditorUtil.WorldToMeters(world[i]);
                Vector2 b = PlanEditorUtil.WorldToMeters(world[(i + 1) % world.Count]);
                Vector2 edge = b - a;
                if (edge.sqrMagnitude < 1e-8f)
                    continue;
                float d = PlanEditorUtil.DistToSegmentSq(probe, a, b, out _);
                if (d >= bestD)
                    continue;
                bestD = d;
                found = true;
                best = new WallSlot
                {
                    Valid = true,
                    RoomId = roomId,
                    SegIndex = i,
                    A = a,
                    B = b,
                    Tangent = edge.normalized,
                    Length = edge.magnitude,
                };
            }
            return found;
        }

        private static float DistanceTo(WallSlot slot, Vector2 p)
        {
            return PlanEditorUtil.DistToSegmentSq(p, slot.A, slot.B, out _);
        }

        /// <summary>
        /// The wall direction the plugin currently has stored for this opening, or +X when
        /// it has none yet. Public because "which way does this opening's wall run" is the
        /// question a caller needs to decide whether a gesture is along the wall or across
        /// it (OpeningInteraction.ResizeFromHandle) when no slot has resolved yet.
        /// </summary>
        public static Vector2 StoredTangent(UIBaseItem ui)
        {
            if (ui != null && ui.cpc != null)
            {
                List<Vector3> dirs = ui.cpc.GetPointsDirectionList();
                if (dirs != null && dirs.Count > 0 && dirs[0].sqrMagnitude > 1e-6f)
                {
                    Vector3 d = dirs[0].normalized;
                    return new Vector2(d.x, d.z);
                }
            }
            return Vector2.right;
        }

        /// <summary>Widest this opening may become where it currently sits.</summary>
        public static float MaxWidthOf(UIBaseItem ui)
        {
            WallSlot slot = SlotOf(ui);
            if (!slot.Valid)
                return ui != null ? ui.Width : MIN_WIDTH;
            Vector2? centre = CentreOf(ui);
            if (!centre.HasValue)
                return slot.Length;
            return Mathf.Max(MIN_WIDTH, slot.MaxWidthAt(slot.AlongOf(centre.Value)));
        }

        /// <summary>Tallest this opening may become, given the wall height.</summary>
        public static float MaxHeightOf(UIBaseItem ui)
        {
            float wall = AppController.Instance != null ? AppController.Instance.wallsHeight : 2.5f;
            float sill = ui != null ? Mathf.Max(0f, ui.YPos) : 0f;
            return Mathf.Max(MIN_HEIGHT, wall - sill - HEAD_CLEARANCE);
        }

        // ------------------------------------------------------------------ mutations

        /// <summary>
        /// Slides the opening along its wall so its centre lands as close to
        /// <paramref name="targetCentreMeters"/> as the wall allows. The target is
        /// PROJECTED onto the host segment and then clamped to the range that keeps the
        /// whole opening inside it, so this can never drift off the wall, never leave
        /// the wall's ends, and never turn into a free-floating move.
        ///
        /// One undo step. MoveItemPoints re-cuts both the wall being left and the wall
        /// being joined, and the plugin re-derives the facing from the new wall.
        /// </summary>
        public static bool MoveTo(string itemId, Vector2 targetCentreMeters)
        {
            UIBaseItem ui = Find(itemId);
            if (ui == null)
                return false;
            WallSlot slot = SlotOf(ui);
            if (!slot.Valid)
                return false;

            float width = Mathf.Max(MIN_WIDTH, ui.Width);
            if (slot.Length <= width)
                return false;   // the opening fills the wall; nowhere to slide

            float along = Mathf.Clamp(slot.AlongOf(targetCentreMeters),
                                      slot.MinCentre(width), slot.MaxCentre(width));
            Vector2 point = slot.PointAt(along);   // centre == control point

            return FloorPlanEditor.MoveItemPoints(itemId, new List<Vector2> { point });
        }

        /// <summary>Same slide, expressed as a distance along the wall from its start.</summary>
        public static bool MoveAlong(string itemId, float centreAlongMeters)
        {
            WallSlot slot = SlotOf(itemId);
            if (!slot.Valid)
                return false;
            return MoveTo(itemId, slot.PointAt(centreAlongMeters));
        }

        /// <summary>
        /// Resizes the opening. Width grows about its CENTRE — so the control point
        /// moves back half the delta, otherwise widening would walk the opening along
        /// the wall — and is capped by the space left in the segment. Height is capped
        /// by the wall height above the sill. Any null argument is left alone.
        ///
        /// One undo step covering both the settings change and the control-point move,
        /// so undo can never leave a half-resized opening.
        /// </summary>
        public static bool Resize(string itemId, float? width, float? height, float? ypos)
        {
            UIBaseItem ui = Find(itemId);
            if (ui == null)
                return false;

            WallSlot slot = SlotOf(ui);
            Vector2? centre = CentreOf(ui);

            float newHeight = height.HasValue
                ? Mathf.Clamp(height.Value, MIN_HEIGHT, MaxHeightOf(ui)) : ui.Height;
            float newYPos = ypos.HasValue
                ? Mathf.Clamp(ypos.Value, 0f, Mathf.Max(0f, MaxHeightOf(ui) - MIN_HEIGHT)) : ui.YPos;

            float newWidth = ui.Width;
            if (width.HasValue)
            {
                float cap = slot.Valid && centre.HasValue
                    ? slot.MaxWidthAt(slot.AlongOf(centre.Value))
                    : ui.Width;
                newWidth = Mathf.Clamp(width.Value, MIN_WIDTH, Mathf.Max(MIN_WIDTH, cap));
            }

            var svc = IBMROS.Bridge.UndoRedo.UndoRedoService.Instance;
            System.IDisposable scope = svc != null
                ? (System.IDisposable)svc.BeginAction("Resize " + ui.sequencingItemType) : null;
            try
            {
                FloorPlanEditor.SetItemSettings(itemId,
                    width: newWidth, height: newHeight, ypos: newYPos);

                // The centre needs no correction — it IS the control point, which this did
                // not touch. What can still go wrong is a widened opening no longer fitting
                // where it sits, so re-clamp the centre into the range the new width allows.
                if (slot.Valid && centre.HasValue)
                {
                    float along = slot.AlongOf(centre.Value);
                    float clamped = Mathf.Clamp(along,
                        slot.MinCentre(newWidth), slot.MaxCentre(newWidth));
                    if (Mathf.Abs(clamped - along) > 1e-4f)
                        FloorPlanEditor.MoveItemPoints(itemId,
                            new List<Vector2> { slot.PointAt(clamped) });
                }
            }
            finally { scope?.Dispose(); }
            return true;
        }

        /// <summary>
        /// Turns the opening around within its wall plane — the only rotation a
        /// wall-hosted element has. Stored as item state because the plugin re-derives
        /// the live direction from the wall on every rebuild, and applied to the visual
        /// only, so the wall cut is untouched and the opening cannot detach.
        /// </summary>
        public static bool Flip(string itemId)
        {
            UIBaseItem ui = Find(itemId);
            if (ui == null)
                return false;
            var svc = IBMROS.Bridge.UndoRedo.UndoRedoService.Instance;
            System.IDisposable scope = svc != null
                ? (System.IDisposable)svc.BeginAction("Turn " + ui.sequencingItemType) : null;
            try
            {
                ui.OpeningFlipped = !ui.OpeningFlipped;
                DocumentEvents.RaiseChanged(DocumentChangeKind.ItemSettings, "Turn Opening");
            }
            finally { scope?.Dispose(); }
            Materials.OpeningStyler.Instance?.ApplyAll();
            return true;
        }

        /// <summary>
        /// Deletes the opening THROUGH the document, not by destroying its GameObject:
        /// the gateway removes the item, which closes the hole because the wall's mesh
        /// is generated from the room's opening list, and the same document change
        /// updates the 2D plan. Undo restores the item with its original identity.
        /// </summary>
        public static bool Delete(string itemId)
        {
            if (Find(itemId) == null)
                return false;
            bool ok = FloorPlanEditor.DeleteItem(itemId);
            if (ok)
                OpeningHostRegistry.Forget(itemId);
            return ok;
        }

        /// <summary>
        /// Duplicates the opening and slides the copy clear of the original along the
        /// same wall, so a "Copy" lands in a usable place instead of exactly on top.
        /// Falls back to the plain duplicate when the wall has no room.
        /// </summary>
        public static string Duplicate(string itemId)
        {
            UIBaseItem ui = Find(itemId);
            if (ui == null)
                return null;
            WallSlot slot = SlotOf(ui);
            Vector2? centre = CentreOf(ui);

            var svc = IBMROS.Bridge.UndoRedo.UndoRedoService.Instance;
            System.IDisposable scope = svc != null
                ? (System.IDisposable)svc.BeginAction("Copy " + ui.sequencingItemType) : null;
            string newId;
            try
            {
                newId = FloorPlanEditor.DuplicateItem(itemId);
                if (!string.IsNullOrEmpty(newId) && slot.Valid && centre.HasValue)
                {
                    float w = Mathf.Max(MIN_WIDTH, ui.Width);
                    float from = slot.AlongOf(centre.Value);
                    float wanted = from + w * 1.15f;
                    if (wanted > slot.MaxCentre(w))
                        wanted = from - w * 1.15f;   // no room ahead, place it behind
                    if (wanted >= slot.MinCentre(w) && wanted <= slot.MaxCentre(w))
                        MoveAlong(newId, wanted);
                }
            }
            finally { scope?.Dispose(); }
            return newId;
        }
    }
}

using System;
using System.Collections.Generic;
using Exoa.Designer;
using IBMROS.Bridge.Interaction;
using IBMROS.Core;
using UnityEngine;

namespace IBMROS.Designer.Plan
{
    /// <summary>
    /// Touch affordance for FloorPlanEditor.SplitRoom.
    ///
    /// The gateway op, its geometry (PolygonSplit) and its undo behaviour have been
    /// complete and apitest-covered since 2026-07-18, but there was no way to reach it
    /// from the app — it was logged as deferred because the interaction, not the
    /// operation, was the missing part.
    ///
    /// GESTURE. Press to anchor, slide to aim, release to cut. If the press never
    /// travels past tap slop it only SETS the anchor, and the next press-release
    /// commits — so a swipe and a two-tap both work. A literal two-tap-only flow was
    /// rejected because touch has no hover: there would be no live preview between the
    /// taps, which is exactly the trap the vendor RectRoomTool falls into (it reads
    /// Input.mousePosition to update its rubber band, so on device the preview freezes
    /// at the last touch instead of following the finger).
    ///
    /// SAFETY. PolygonSplit is pure, public and Unity-free, so this runs the IDENTICAL
    /// test every preview frame that the gateway will run on commit. The destructive
    /// part of SplitRoom (delete the room, create two) is therefore only ever invoked
    /// for a line already proven to divide it, and the user sees validity — green line
    /// vs red — before letting go.
    ///
    /// The cut is applied to the smallest space CONTAINING the anchor, never to the
    /// current selection: the tool arms from the toolbar with nothing selected, and the
    /// finger is the unambiguous statement of which room is meant.
    /// </summary>
    public sealed class SplitRoomTool : MonoBehaviour
    {
        /// <summary>Raised with a short message the HUD can surface (empty = clear).</summary>
        public static event Action<string> OnHint;

        private const float LINE_WIDTH_M = 0.06f;
        private const float LINE_Y = 0.05f;
        private const float MIN_AIM_M = 0.15f;
        private static readonly Color VALID = new Color(0.13f, 0.55f, 0.24f, 1f);
        private static readonly Color INVALID = new Color(0.78f, 0.18f, 0.18f, 1f);

        private LineRenderer line;
        private bool hasAnchor;
        private Vector2 anchor;
        private Vector2 aim;
        private string roomId;

        /// <summary>True once an anchor is down, so the caller knows a cut is in progress.</summary>
        public bool HasAnchor => hasAnchor;

        private void Awake()
        {
            var go = new GameObject("SplitPreviewLine");
            go.transform.SetParent(transform, false);
            line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.widthMultiplier = LINE_WIDTH_M;
            line.numCapVertices = line.numCornerVertices = 4;
            line.material = new Material(Shader.Find("Sprites/Default"));
            line.enabled = false;
        }

        private void OnDestroy()
        {
            if (line != null && line.material != null)
                Destroy(line.material);
        }

        /// <summary>Clears any half-made cut (tool disarmed, mode changed, Done).</summary>
        public void Reset()
        {
            hasAnchor = false;
            roomId = null;
            if (line != null)
                line.enabled = false;
            OnHint?.Invoke(string.Empty);
        }

        /// <summary>
        /// Press. Sets the anchor and resolves which room is being cut, or hints and
        /// stays armed when the press lands outside every space.
        /// </summary>
        public void Press(Vector2 groundMeters)
        {
            UIBaseItem room = SmallestSpaceContaining(groundMeters);
            if (room == null)
            {
                OnHint?.Invoke("Press inside the room you want to divide");
                return;
            }
            hasAnchor = true;
            anchor = aim = groundMeters;
            roomId = room.ItemUniqueId;
            line.enabled = false; // nothing to draw until the aim moves
            OnHint?.Invoke("Drag across the room, then let go to cut");
        }

        /// <summary>Aim. Redraws the preview and reports whether the line would divide.</summary>
        public void Aim(Vector2 groundMeters)
        {
            if (!hasAnchor)
                return;
            aim = groundMeters;
            if ((aim - anchor).magnitude < MIN_AIM_M)
            {
                line.enabled = false;
                return;
            }

            bool ok = WouldSplit(out float areaA, out float areaB);
            line.enabled = true;
            line.startColor = line.endColor = ok ? VALID : INVALID;
            line.SetPosition(0, new Vector3(anchor.x, LINE_Y, anchor.y));
            line.SetPosition(1, new Vector3(aim.x, LINE_Y, aim.y));
            OnHint?.Invoke(ok
                ? areaA.ToString("0.0") + " m²  ·  " + areaB.ToString("0.0") + " m²"
                : "That line doesn't divide the room");
        }

        /// <summary>
        /// Release. Commits the cut when the line divides the room; otherwise keeps the
        /// anchor so re-aiming is one press, and says why. Returns true if a cut landed.
        /// </summary>
        public bool Release(Vector2 groundMeters, bool wasDrag)
        {
            if (!hasAnchor)
                return false;

            // A press that never travelled is the first tap of a two-tap cut: keep the
            // anchor and wait for the aiming press.
            if (!wasDrag && (groundMeters - anchor).magnitude < MIN_AIM_M)
            {
                OnHint?.Invoke("Now tap across the room to cut");
                return false;
            }

            aim = groundMeters;
            if (!WouldSplit(out _, out _))
            {
                OnHint?.Invoke("That line doesn't divide the room — aim right across it");
                line.startColor = line.endColor = INVALID;
                return false;
            }

            (string a, string b) = FloorPlanEditor.SplitRoom(roomId, anchor, aim);
            line.enabled = false;
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
            {
                // The preview said yes, so this is a gateway-side refusal, not aim.
                OnHint?.Invoke("Could not divide that room");
                return false;
            }

            hasAnchor = false;
            roomId = null;
            SelectionService.Instance?.SelectById(a);
            OnHint?.Invoke("Divided into two rooms");
            return true;
        }

        // ------------------------------------------------------------------ geometry

        /// <summary>
        /// Runs the exact test the gateway will run, so the preview can never promise a
        /// cut the commit refuses.
        /// </summary>
        private bool WouldSplit(out float areaA, out float areaB)
        {
            areaA = areaB = 0f;
            UIBaseItem ui = PlanEditorUtil.FindItem(roomId);
            if (ui == null || ui.cpc == null)
                return false;

            List<Vector3> world = ui.cpc.GetPointsWorldPositionList();
            if (world == null || world.Count < 3)
                return false;

            var poly = new List<PolygonSplit.Pt>(world.Count);
            foreach (Vector3 w in world)
                poly.Add(new PolygonSplit.Pt(w.x, w.z));

            PolygonSplit.Result res = PolygonSplit.Split(poly, anchor.x, anchor.y, aim.x, aim.y);
            if (!res.ok)
                return false;

            // PolygonSplit.Area is the same measure its own degeneracy test uses, so the
            // areas reported to the user cannot disagree with what it accepted.
            areaA = Mathf.Abs(PolygonSplit.Area(res.a));
            areaB = Mathf.Abs(PolygonSplit.Area(res.b));
            return true;
        }

        /// <summary>
        /// The smallest space whose polygon contains the point, so a room nested inside
        /// an outside area cuts the room rather than the terrace around it.
        /// </summary>
        private static UIBaseItem SmallestSpaceContaining(Vector2 p)
        {
            UIBaseItem best = null;
            float bestArea = float.MaxValue;
            foreach (UIBaseItem ui in PlanEditorUtil.AllSpaces())
            {
                List<Vector3> world = PlanEditorUtil.WorldPoints(ui);
                if (world.Count < 3)
                    continue;
                var poly = new List<Vector2>(world.Count);
                foreach (Vector3 w in world)
                    poly.Add(PlanEditorUtil.WorldToMeters(w));
                if (!Furnish.RoomFootprint.Contains(poly, p))
                    continue;

                float a2 = 0f;
                for (int i = 0; i < poly.Count; i++)
                {
                    Vector2 u = poly[i], v = poly[(i + 1) % poly.Count];
                    a2 += u.x * v.y - v.x * u.y;
                }
                float area = Mathf.Abs(a2) * 0.5f;
                if (area < bestArea)
                {
                    bestArea = area;
                    best = ui;
                }
            }
            return best;
        }
    }
}

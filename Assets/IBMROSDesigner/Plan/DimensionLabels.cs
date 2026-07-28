using System.Collections.Generic;
using Exoa.Designer;
using IBMROS.Bridge.Interaction;
using IBMROS.Core;
using TMPro;
using UnityEngine;

namespace IBMROS.Designer.Plan
{
    /// <summary>
    /// World-space wall-length labels for the 2D plan (reference-app style):
    /// one label per wall segment of the SELECTED space item, lying flat on the
    /// ground plane just outside the wall, plus a name + area label at the
    /// centroid. Purely visual — reads geometry, never writes.
    ///
    /// Refresh sources: selection changes, document changes, live drag previews
    /// (PlanTouchController.OnPlanVisualsDirty). Hidden outside Plan2D mode.
    /// </summary>
    public sealed class DimensionLabels : MonoBehaviour
    {
        private const float LABEL_OUT_OFFSET_M = 0.45f;
        private const float LABEL_Y = 0.03f;
        private const float WALL_FONT_SIZE = 2.6f;
        private const float CENTER_FONT_SIZE = 3.2f;
        private static readonly Color LABEL_COLOR = new Color(0.07f, 0.09f, 0.12f, 1f);
        private static readonly Color CENTER_COLOR = new Color(0.07f, 0.09f, 0.12f, 0.5f);

        private readonly List<TextMeshPro> pool = new List<TextMeshPro>();
        private TextMeshPro centerLabel;
        private bool planMode = true;

        private void OnEnable()
        {
            SelectionService.OnSelectionChanged += Refresh;
            DocumentEvents.OnDocumentChanged += OnDocChanged;
            PlanTouchController.OnPlanVisualsDirty += Refresh;
            DesignerModeController.OnModeChanged += OnModeChanged;
            Refresh();
        }

        private void OnDisable()
        {
            SelectionService.OnSelectionChanged -= Refresh;
            DocumentEvents.OnDocumentChanged -= OnDocChanged;
            PlanTouchController.OnPlanVisualsDirty -= Refresh;
            DesignerModeController.OnModeChanged -= OnModeChanged;
            HideAll();
        }

        private void OnDocChanged(DocumentChange change) => Refresh();

        private void OnModeChanged(DesignerMode mode)
        {
            planMode = mode == DesignerMode.Plan2D;
            Refresh();
        }

        private void Refresh()
        {
            if (!planMode)
            {
                HideAll();
                return;
            }

            UIBaseItem item = PlanEditorUtil.FindItem(SelectionService.Instance?.SelectedId);
            if (item == null || PlanEditorUtil.IsOpening(item))
            {
                HideAll();
                return;
            }

            List<Vector3> world = PlanEditorUtil.WorldPoints(item);
            if (world.Count < 3)
            {
                HideAll();
                return;
            }

            // Winding sign so the outward offset works for either polygon direction.
            float area2 = 0f;
            var m = new List<Vector2>(world.Count);
            foreach (Vector3 w in world)
                m.Add(PlanEditorUtil.WorldToMeters(w));
            for (int i = 0; i < m.Count; i++)
            {
                Vector2 a = m[i], b = m[(i + 1) % m.Count];
                area2 += a.x * b.y - b.x * a.y;
            }
            float outwardSign = area2 >= 0f ? -1f : 1f; // CCW → left normal points inward

            var centroid = Vector2.zero;
            foreach (Vector2 p in m)
                centroid += p;
            centroid /= m.Count;

            EnsurePool(m.Count);
            for (int i = 0; i < pool.Count; i++)
            {
                if (i >= m.Count)
                {
                    pool[i].gameObject.SetActive(false);
                    continue;
                }
                Vector2 a = m[i], b = m[(i + 1) % m.Count];
                float len = (b - a).magnitude;
                if (len < 0.35f)
                {
                    pool[i].gameObject.SetActive(false);
                    continue;
                }

                Vector2 t = (b - a) / len;
                Vector2 n = new Vector2(-t.y, t.x) * outwardSign;
                Vector2 mid = (a + b) * 0.5f + n * LABEL_OUT_OFFSET_M;

                TextMeshPro label = pool[i];
                label.text = len.ToString("0.00") + " m";
                Transform tr = label.transform;
                tr.position = new Vector3(mid.x, LABEL_Y, mid.y);
                // Flat on the ground, reading along the wall, never upside-down.
                float yaw = Mathf.Atan2(t.x, t.y) * Mathf.Rad2Deg - 90f;
                if (yaw > 90f || yaw < -90f)
                    yaw += 180f;
                tr.rotation = Quaternion.Euler(90f, yaw, 0f);
                label.gameObject.SetActive(true);
            }

            // Name + area at the centroid.
            float area = Mathf.Abs(area2) * 0.5f;
            EnsureCenterLabel();
            centerLabel.text = (string.IsNullOrEmpty(item.Name) ? "Room" : item.Name)
                               + "\n<size=70%>" + area.ToString("0.0") + " m²</size>";
            centerLabel.transform.position = new Vector3(centroid.x, LABEL_Y, centroid.y);
            centerLabel.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            centerLabel.gameObject.SetActive(true);
        }

        private void EnsurePool(int count)
        {
            while (pool.Count < count)
                pool.Add(MakeLabel("WallDimLabel", WALL_FONT_SIZE, LABEL_COLOR));
        }

        private void EnsureCenterLabel()
        {
            if (centerLabel == null)
                centerLabel = MakeLabel("RoomCenterLabel", CENTER_FONT_SIZE, CENTER_COLOR);
        }

        private TextMeshPro MakeLabel(string name, float size, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var tmp = go.AddComponent<TextMeshPro>();
            tmp.fontSize = size;
            tmp.color = color;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.raycastTarget = false;
            var rt = tmp.rectTransform;
            rt.sizeDelta = new Vector2(4f, 1.2f);
            go.SetActive(false);
            return tmp;
        }

        private void HideAll()
        {
            foreach (TextMeshPro l in pool)
                if (l != null)
                    l.gameObject.SetActive(false);
            if (centerLabel != null)
                centerLabel.gameObject.SetActive(false);
        }
    }
}

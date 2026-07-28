using System;
using System.Collections.Generic;
using Exoa.Designer;
using Exoa.Events;
using IBMROS.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace IBMROS.Bridge.Interaction
{
    /// <summary>
    /// P2.6 — selection workflow. Tapping a room / door / window / outside area in the
    /// 3D view selects it; tapping empty space deselects. Selection is expressed as the
    /// item's stable identity (A2), so it survives rebuilds and lines up with the A3
    /// gateway (delete/duplicate the SelectedId). A raycast hit is mapped back to its
    /// owning item through IObjectDrawer.UI.ItemUniqueId.
    ///
    /// Highlight is a reversible per-renderer _BaseColor tint via MaterialPropertyBlock
    /// (no material instances, no leaks), re-applied after rebuilds since the item's
    /// meshes are regenerated. If the selected item disappears (deleted/undone) the
    /// selection clears itself.
    ///
    /// Mobile-first: a tap is pointer down+up with little movement and short duration,
    /// so it never fights TouchCameraLite's orbit/pan drags; taps over UI are ignored.
    /// </summary>
    public sealed class SelectionService : MonoBehaviour
    {
        public static SelectionService Instance { get; private set; }
        public static event Action OnSelectionChanged;

        public string SelectedId { get; private set; }
        public bool HasSelection => !string.IsNullOrEmpty(SelectedId);

        private const float TAP_MAX_MOVE_PIXELS = 12f;
        private const float TAP_MAX_SECONDS = 0.35f;
        private const float REAPPLY_INTERVAL = 0.2f;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly Color HighlightTint = new Color(0.35f, 0.75f, 1f, 1f);

        private Camera cam;
        private Vector3 pointerDownPos;
        private float pointerDownTime;
        private bool pointerActive;
        private float lastReapply;
        private MaterialPropertyBlock mpb;
        private readonly List<MeshRenderer> highlighted = new List<MeshRenderer>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            TryInstall();
            SceneManager.sceneLoaded += (s, m) => TryInstall();
        }

        private static void TryInstall()
        {
            if (Instance != null)
                return;
            if (GameObject.FindAnyObjectByType<FloorMapSerializer>() == null)
                return;
            AppController app = GameObject.FindAnyObjectByType<AppController>();
            if (app == null || app.currentState == AppController.States.PlayMode)
                return;
            new GameObject("IBMROS_SelectionService").AddComponent<SelectionService>();
        }

        private void Awake()
        {
            Instance = this;
            mpb = new MaterialPropertyBlock();
        }

        private void OnEnable()
        {
            DocumentEvents.OnDocumentChanged += OnDocumentChanged;
            GameEditorEvents.OnRequestClearAll += OnClearAll;
        }

        private void OnDisable()
        {
            DocumentEvents.OnDocumentChanged -= OnDocumentChanged;
            GameEditorEvents.OnRequestClearAll -= OnClearAll;
            if (Instance == this)
                Instance = null;
        }

        private void Update()
        {
            HandleTap();
            if (HasSelection && Time.time - lastReapply > REAPPLY_INTERVAL)
            {
                lastReapply = Time.time;
                // Meshes are rebuilt on edits/undo; if our highlighted renderers were
                // destroyed, re-resolve and re-tint. Also self-clear if the item is gone.
                if (!ReapplyHighlight())
                    Deselect();
            }
        }

        // ------------------------------------------------------------------ public API

        public void SelectById(string id)
        {
            if (string.IsNullOrEmpty(id) || id == SelectedId)
                return;
            ClearHighlightRenderers();
            SelectedId = id;
            ReapplyHighlight();
            OnSelectionChanged?.Invoke();
        }

        public void Deselect()
        {
            if (!HasSelection)
                return;
            ClearHighlightRenderers();
            SelectedId = null;
            OnSelectionChanged?.Invoke();
        }

        /// <summary>Maps a raycast-hit GameObject to the owning item's id, or null.</summary>
        public static string ResolveId(GameObject hit)
        {
            if (hit == null)
                return null;
            IObjectDrawer drawer = hit.GetComponentInParent<IObjectDrawer>();
#if FLOORMAP_MODULE
            if (drawer != null && drawer.UI != null)
                return drawer.UI.ItemUniqueId;
#endif
            return null;
        }

        // ------------------------------------------------------------------ input

        private void HandleTap()
        {
            if (Input.GetMouseButtonDown(0))
            {
                pointerActive = true;
                pointerDownPos = Input.mousePosition;
                pointerDownTime = Time.time;
            }
            else if (Input.GetMouseButtonUp(0) && pointerActive)
            {
                pointerActive = false;
                // A drawing tool owns taps while armed — don't also select.
                if (RectRoomTool.Instance != null && RectRoomTool.Instance.IsActive)
                    return;
                float moved = (Input.mousePosition - pointerDownPos).magnitude;
                float held = Time.time - pointerDownTime;
                if (moved <= TAP_MAX_MOVE_PIXELS && held <= TAP_MAX_SECONDS && !IsPointerOverUI())
                    PickAt(Input.mousePosition);
            }
        }

        private static bool IsPointerOverUI()
        {
            return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        }

        private void PickAt(Vector3 screenPos)
        {
            if (cam == null)
                cam = Camera.main != null ? Camera.main : GameObject.FindAnyObjectByType<Camera>();
            if (cam == null)
                return;
            Ray ray = cam.ScreenPointToRay(screenPos);
            RaycastHit hit;
            if (Physics.Raycast(ray, out hit, 5000f))
            {
                string id = ResolveId(hit.collider.gameObject);
                if (!string.IsNullOrEmpty(id))
                {
                    SelectById(id);
                    return;
                }
            }
            Deselect(); // tapped empty space (or something non-selectable)
        }

        // ------------------------------------------------------------------ highlight

        /// <summary>
        /// Layers the highlight must leave alone (bitmask). The 2D plan look
        /// tints wall renderers dark via its own MPB; re-tinting them blue every
        /// 0.2 s here fought that and washed the selected room's walls out.
        /// PlanLookController sets this to the wall layers while in Plan2D.
        /// </summary>
        public static int HighlightLayerExclusionMask = 0;

        private bool ReapplyHighlight()
        {
            IObjectDrawer drawer = FindDrawer(SelectedId);
            if (drawer == null || drawer.GO == null)
                return false;
            ClearHighlightRenderers();
            drawer.GO.GetComponentsInChildren(true, highlighted);
            highlighted.RemoveAll(r =>
                r == null || ((1 << r.gameObject.layer) & HighlightLayerExclusionMask) != 0);
            foreach (MeshRenderer r in highlighted)
            {
                r.GetPropertyBlock(mpb);
                mpb.SetColor(BaseColorId, HighlightTint);
                r.SetPropertyBlock(mpb);
            }
            return true;
        }

        private void ClearHighlightRenderers()
        {
            foreach (MeshRenderer r in highlighted)
            {
                if (r == null)
                    continue;
                r.GetPropertyBlock(mpb);
                mpb.Clear();
                r.SetPropertyBlock(mpb);
            }
            highlighted.Clear();
        }

        private static IObjectDrawer FindDrawer(string id)
        {
#if FLOORMAP_MODULE
            foreach (UIBaseItem ui in GameObject.FindObjectsByType<UIBaseItem>())
                if (ui.ItemUniqueId == id)
                    return ui.drawer;
#endif
            return null;
        }

        // ------------------------------------------------------------------ events

        private void OnDocumentChanged(DocumentChange change)
        {
            // A delete/undo may have removed the selected item; verify next Update tick.
            lastReapply = 0f;
        }

        private void OnClearAll(bool a, bool b, bool c)
        {
            Deselect();
        }
    }
}

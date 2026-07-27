using UnityEngine;
using UnityEngine.UI;

namespace IBMROS.Bridge.UndoRedo
{
    /// <summary>
    /// IBMROS P2 — drop this on a uGUI Button in FloorMapEditor.unity to make it an
    /// Undo or Redo button. Wires itself to UndoRedoService and greys out when the
    /// corresponding history direction is empty. No Exoa code involved.
    /// </summary>
    [RequireComponent(typeof(Button))]
    public class UndoRedoButton : MonoBehaviour
    {
        public enum Kind { Undo, Redo }
        public Kind kind = Kind.Undo;

        private Button button;

        private void Awake()
        {
            button = GetComponent<Button>();
            button.onClick.AddListener(OnClick);
        }

        private void OnEnable()
        {
            UndoRedoService.OnHistoryChanged += RefreshInteractable;
            RefreshInteractable();
        }

        private void OnDisable()
        {
            UndoRedoService.OnHistoryChanged -= RefreshInteractable;
        }

        private void OnClick()
        {
            UndoRedoService svc = UndoRedoService.Instance;
            if (svc == null)
                return;
            if (kind == Kind.Undo)
                svc.Undo();
            else
                svc.Redo();
        }

        private void RefreshInteractable()
        {
            UndoRedoService svc = UndoRedoService.Instance;
            bool can = svc != null && (kind == Kind.Undo ? svc.CanUndo : svc.CanRedo);
            button.interactable = can;
        }

        private void Update()
        {
            // Service bootstraps after scene load; keep state fresh until it exists,
            // and after captures that happen without a history event (cheap check).
            if (Time.frameCount % 30 == 0)
                RefreshInteractable();
        }
    }
}

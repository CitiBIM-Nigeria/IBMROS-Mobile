using System;
using System.Collections.Generic;
using UnityEngine;
using UndoRedoService = IBMROS.Bridge.UndoRedo.UndoRedoService;

/// <summary>
/// Furniture command recorder.
///
/// This is the single funnel every furniture operation already goes through
/// (drag, rotate, scale, place, delete, duplicate all call <see cref="Record"/>),
/// which makes it the right place to join the editor's ONE history rather than
/// run a second, competing stack.
///
/// When the room designer's UndoRedoService is present it owns the timeline: this
/// class forwards commands to it, so a furniture step and a wall step sit in the
/// same ordered list and the HUD's Undo button walks back through both in the
/// order the user worked. When it is absent (the standalone Room scene, tests),
/// the local stack below is used exactly as before.
/// </summary>
public class UndoRedoManager : MonoBehaviour
{
    public static UndoRedoManager Instance { get; private set; }

    [Header("Settings")]
    [SerializeField] private int maxHistorySize = 50;

    // Events consumed by ToolbarController
    public event Action OnHistoryChanged;

    private Stack<IUndoableAction> _undoStack = new Stack<IUndoableAction>();
    private Stack<IUndoableAction> _redoStack = new Stack<IUndoableAction>();

    /// <summary>The unified history when the designer is running, otherwise null.</summary>
    private static UndoRedoService Service => UndoRedoService.Instance;

    public bool CanUndo => Service != null ? Service.CanUndo : _undoStack.Count > 0;
    public bool CanRedo => Service != null ? Service.CanRedo : _redoStack.Count > 0;
    public int UndoCount => Service != null ? Service.StepCount : _undoStack.Count;
    public int RedoCount => _redoStack.Count;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    void OnEnable()
    {
        // Keep listeners (ToolbarController) correct whichever history is in charge.
        UndoRedoService.OnHistoryChanged += RaiseHistoryChanged;
    }

    void OnDisable()
    {
        UndoRedoService.OnHistoryChanged -= RaiseHistoryChanged;
    }

    private void RaiseHistoryChanged() => OnHistoryChanged?.Invoke();

    public void Record(IUndoableAction action)
    {
        if (action == null)
            return;

        UndoRedoService service = Service;
        if (service != null)
        {
            // The unified history raises its own OnHistoryChanged, which we re-emit.
            service.RecordCommand(action, action.Description);
            return;
        }

        // Clear redo history whenever a new action is recorded
        _redoStack.Clear();

        _undoStack.Push(action);

        // Trim history if it exceeds the limit
        if (_undoStack.Count > maxHistorySize)
            TrimUndoStack();

        Debug.Log($"[UndoRedoManager] Recorded: {action.Description}. " +
                  $"Undo stack: {_undoStack.Count}");

        OnHistoryChanged?.Invoke();
    }

    public void Undo()
    {
        UndoRedoService service = Service;
        if (service != null)
        {
            service.Undo();
            return;
        }

        if (!CanUndo)
        {
            Debug.Log("[UndoRedoManager] Nothing to undo.");
            return;
        }

        IUndoableAction action = _undoStack.Pop();
        action.Undo();
        _redoStack.Push(action);

        Debug.Log($"[UndoRedoManager] Undid: {action.Description}. " +
                  $"Undo stack: {_undoStack.Count}");

        OnHistoryChanged?.Invoke();
    }

    public void Redo()
    {
        UndoRedoService service = Service;
        if (service != null)
        {
            service.Redo();
            return;
        }

        if (!CanRedo)
        {
            Debug.Log("[UndoRedoManager] Nothing to redo.");
            return;
        }

        IUndoableAction action = _redoStack.Pop();
        action.Redo();
        _undoStack.Push(action);

        Debug.Log($"[UndoRedoManager] Redid: {action.Description}. " +
                  $"Undo stack: {_undoStack.Count}");

        OnHistoryChanged?.Invoke();
    }

    /// <summary>
    /// Drops the local stack. Never clears the unified history — that one spans the
    /// whole document and is reset by the service itself on load / new plan.
    /// </summary>
    public void Clear()
    {
        _undoStack.Clear();
        _redoStack.Clear();
        OnHistoryChanged?.Invoke();
        Debug.Log("[UndoRedoManager] History cleared.");
    }

    private void TrimUndoStack()
    {
        var temp = new Stack<IUndoableAction>();
        int count = 0;

        foreach (var action in _undoStack)
        {
            if (count >= maxHistorySize)
                break;

            temp.Push(action);
            count++;
        }

        _undoStack.Clear();

        foreach (var action in temp)
            _undoStack.Push(action);
    }
}

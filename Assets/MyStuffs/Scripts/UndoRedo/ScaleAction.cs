using UnityEngine;

/// <summary>
/// A scale gesture. Position is part of the step, not an afterthought: the scale rig
/// anchors the opposite side and re-snaps the item to the floor while dragging, so
/// restoring the scale alone would leave the item sitting somewhere it never was.
/// </summary>
public class ScaleAction : IUndoableAction
{
    public string Description => $"Scale {_target.name}";

    private readonly Transform _target;
    private readonly Vector3 _previousScale;
    private readonly Vector3 _nextScale;
    private readonly Vector3 _previousPosition;
    private readonly Vector3 _nextPosition;

    public ScaleAction(Transform target, Vector3 previousScale, Vector3 nextScale)
        : this(target, previousScale, nextScale,
               target != null ? target.position : Vector3.zero,
               target != null ? target.position : Vector3.zero)
    {
    }

    public ScaleAction(Transform target, Vector3 previousScale, Vector3 nextScale,
                       Vector3 previousPosition, Vector3 nextPosition)
    {
        _target = target;
        _previousScale = previousScale;
        _nextScale = nextScale;
        _previousPosition = previousPosition;
        _nextPosition = nextPosition;
    }

    public void Execute()
    {
        if (_target == null)
            return;
        _target.localScale = _nextScale;
        _target.position = _nextPosition;
    }

    public void Undo()
    {
        if (_target == null)
            return;
        _target.localScale = _previousScale;
        _target.position = _previousPosition;
    }

    public void Redo()
    {
        Execute();
    }
}

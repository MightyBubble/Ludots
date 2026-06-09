using System;
using System.Numerics;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Scripting;
using MassNavigationMod.Runtime;

namespace MassNavigationMod.Systems;

internal sealed class MassNavigationRuntimeBakeAuthoringInputSystem : ISystem<float>
{
    private readonly GameEngine _engine;
    private readonly MassNavigationSimulationRuntime _simulation;
    private readonly MassNavigationShowcaseGuideRuntime _guide;

    public MassNavigationRuntimeBakeAuthoringInputSystem(
        GameEngine engine,
        MassNavigationSimulationRuntime simulation,
        MassNavigationShowcaseGuideRuntime guide)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _simulation = simulation ?? throw new ArgumentNullException(nameof(simulation));
        _guide = guide ?? throw new ArgumentNullException(nameof(guide));
    }

    public void Initialize() { }
    public void BeforeUpdate(in float dt) { }
    public void AfterUpdate(in float dt) { }
    public void Dispose() { }

    public void Update(in float dt)
    {
        if (!IsRuntimeAuthoringActive() ||
            _engine.GetService(CoreServiceKeys.UiCaptured) ||
            !PointerInteractionSnapshotReader.TryRead(_engine.GlobalContext, out PointerInteractionSnapshot pointer))
        {
            return;
        }

        if (pointer.Cancel.PressedThisFrame)
        {
            _engine.SetService(CoreServiceKeys.PointerInputCaptured, true);
            _guide.CancelRuntimeObstacleAuthoring();
            _simulation.AcceptanceDiagnostics.RecordRuntimeNavDataUpdate(_guide.RuntimeBakeAuthoring.CreateSnapshot());
            return;
        }

        if (!pointer.HasGroundPoint ||
            (!pointer.Confirm.PressedThisFrame && !pointer.Command.PressedThisFrame))
        {
            return;
        }

        Vector2 pickedWorldCm = new(pointer.GroundWorldCm.X, pointer.GroundWorldCm.Y);
        _engine.SetService(CoreServiceKeys.PointerInputCaptured, true);
        if (!_simulation.ContainsWorldPoint(pickedWorldCm.X, pickedWorldCm.Y))
        {
            _simulation.RejectCommandOutsideWorld(pickedWorldCm.X, pickedWorldCm.Y);
            _guide.RecordRuntimeObstacleAuthoringFailure(pickedWorldCm, "outside_world_bounds");
            _simulation.AcceptanceDiagnostics.RecordRuntimeNavDataUpdate(_guide.RuntimeBakeAuthoring.CreateSnapshot());
            return;
        }

        if (pointer.Command.PressedThisFrame)
        {
            RecordFinalPointAndClose(pickedWorldCm);
            return;
        }

        if (!_guide.RuntimeBakeAuthoring.TryAddObstaclePoint(
                pickedWorldCm,
                _simulation.BakeDataDiagnostics,
                out MassNavigationRuntimeDirtyChunk dirtyChunk,
                out string failureReason))
        {
            _guide.RecordRuntimeObstacleAuthoringFailure(pickedWorldCm, failureReason);
            _simulation.AcceptanceDiagnostics.RecordRuntimeNavDataUpdate(_guide.RuntimeBakeAuthoring.CreateSnapshot());
            return;
        }

        _guide.RecordRuntimeObstaclePoint(pickedWorldCm, dirtyChunk);
        _simulation.AcceptanceDiagnostics.RecordRuntimeNavDataUpdate(_guide.RuntimeBakeAuthoring.CreateSnapshot());
    }

    private void RecordFinalPointAndClose(Vector2 pickedWorldCm)
    {
        bool pointAdded = _guide.RuntimeBakeAuthoring.TryAddObstaclePoint(
            pickedWorldCm,
            _simulation.BakeDataDiagnostics,
            out MassNavigationRuntimeDirtyChunk dirtyChunk,
            out string addFailure);
        if (pointAdded)
        {
            _guide.RecordRuntimeObstaclePoint(pickedWorldCm, dirtyChunk);
        }
        else if (_guide.RuntimeBakeAuthoring.DraftPointCount < 3)
        {
            _guide.RecordRuntimeObstacleAuthoringFailure(pickedWorldCm, addFailure);
            _simulation.AcceptanceDiagnostics.RecordRuntimeNavDataUpdate(_guide.RuntimeBakeAuthoring.CreateSnapshot());
            return;
        }

        if (_guide.RuntimeBakeAuthoring.TryCloseObstaclePolygon(_simulation.BakeDataDiagnostics, out string closeFailure))
        {
            _guide.RecordRuntimeObstacleClosed();
        }
        else
        {
            _guide.RecordRuntimeObstacleAuthoringFailure(pickedWorldCm, closeFailure);
        }

        _simulation.AcceptanceDiagnostics.RecordRuntimeNavDataUpdate(_guide.RuntimeBakeAuthoring.CreateSnapshot());
    }

    private bool IsRuntimeAuthoringActive()
    {
        return MassNavigationIds.IsCurrentNavigationMap(_engine) &&
            _guide.FocusedPanel &&
            _guide.IsRuntimeObstacleAuthoringActive();
    }
}

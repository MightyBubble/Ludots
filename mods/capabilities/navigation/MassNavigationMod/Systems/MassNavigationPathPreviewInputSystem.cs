using System;
using System.Numerics;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Navigation.Pathing;
using Ludots.Core.Scripting;
using MassNavigationMod.Runtime;

namespace MassNavigationMod.Systems;

internal sealed class MassNavigationPathPreviewInputSystem : ISystem<float>
{
    private readonly GameEngine _engine;
    private readonly MassNavigationSimulationRuntime _simulation;
    private readonly MassNavigationShowcaseGuideRuntime _guide;
    private Vector2 _startWorldCm;
    private Vector2 _goalWorldCm;
    private bool _hasStart;
    private bool _hasGoal;
    private int _observedGuideSessionRevision;

    public MassNavigationPathPreviewInputSystem(
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
        ResetLocalPicksWhenGuideSessionChanges();

        if (!IsPathPreviewActive() ||
            _engine.GetService(CoreServiceKeys.UiCaptured) ||
            _engine.GetService(CoreServiceKeys.PointerInputCaptured) ||
            !PointerInteractionSnapshotReader.TryRead(_engine.GlobalContext, out PointerInteractionSnapshot pointer) ||
            !pointer.HasGroundPoint)
        {
            return;
        }

        Vector2 pickedWorldCm = new(pointer.GroundWorldCm.X, pointer.GroundWorldCm.Y);
        if (!pointer.Confirm.PressedThisFrame && !pointer.Command.PressedThisFrame)
        {
            return;
        }

        _engine.SetService(CoreServiceKeys.PointerInputCaptured, true);
        if (!_simulation.ContainsWorldPoint(pickedWorldCm.X, pickedWorldCm.Y))
        {
            _simulation.RejectCommandOutsideWorld(pickedWorldCm.X, pickedWorldCm.Y);
            _guide.RecordPathPreviewPickFailure(pickedWorldCm, "outside_world_bounds");
            return;
        }

        if (_guide.CurrentStepId == MassNavigationShowcaseStepId.WaypointAuthoring &&
            _hasStart &&
            _hasGoal &&
            pointer.Confirm.PressedThisFrame)
        {
            RecordWaypointEdit(pickedWorldCm);
            return;
        }

        if (pointer.Confirm.PressedThisFrame)
        {
            _startWorldCm = pickedWorldCm;
            _hasStart = true;
            _guide.RecordPathPreviewPick("start", pickedWorldCm, _hasGoal);
        }

        if (pointer.Command.PressedThisFrame)
        {
            _goalWorldCm = pickedWorldCm;
            _hasGoal = true;
            _guide.RecordPathPreviewPick("goal", pickedWorldCm, _hasStart);
        }

        if (!_hasStart || !_hasGoal)
        {
            return;
        }

        if (_engine.GetService(CoreServiceKeys.PathService) is not IPathService pathService ||
            _engine.GetService(CoreServiceKeys.PathStore) is not PathStore pathStore)
        {
            throw new InvalidOperationException("MassNavigation path preview requires CoreServiceKeys.PathService and PathStore.");
        }

        int before = _simulation.CommandCountFrame + _simulation.PendingCommandCount;
        _simulation.AcceptanceDiagnostics.RecordPathOnlyPreviewQuery(
            pathService,
            pathStore,
            _startWorldCm,
            _goalWorldCm,
            PathDomain.NavMesh);
        int after = _simulation.CommandCountFrame + _simulation.PendingCommandCount;
        _guide.RecordPathPreviewQueryResult(
            _startWorldCm,
            _goalWorldCm,
            orderDelta: after - before,
            _simulation.AcceptanceDiagnostics.PathOnlyQuery);
    }

    private void RecordWaypointEdit(Vector2 authoredMidpointWorldCm)
    {
        if (_engine.GetService(CoreServiceKeys.PathService) is not IPathService pathService ||
            _engine.GetService(CoreServiceKeys.PathStore) is not PathStore pathStore)
        {
            throw new InvalidOperationException("MassNavigation waypoint edit requires CoreServiceKeys.PathService and PathStore.");
        }

        int before = _simulation.CommandCountFrame + _simulation.PendingCommandCount;
        if (!_simulation.AcceptanceDiagnostics.TryRecordWaypointPlanEdit(
                pathService,
                pathStore,
                authoredMidpointWorldCm,
                out string failureReason))
        {
            _guide.RecordWaypointEditFailure(authoredMidpointWorldCm, failureReason);
            return;
        }

        int after = _simulation.CommandCountFrame + _simulation.PendingCommandCount;
        _guide.RecordWaypointEditResult(
            authoredMidpointWorldCm,
            orderDelta: after - before,
            _simulation.AcceptanceDiagnostics.WaypointPath);
    }

    private bool IsPathPreviewActive()
    {
        return MassNavigationIds.IsCurrentNavigationMap(_engine) &&
            _guide.FocusedPanel &&
            MassNavigationShowcaseGuideRuntime.IsPathDrivenStep(_guide.CurrentStepId) &&
            !_guide.IsRuntimeObstacleAuthoringActive();
    }

    private void ResetLocalPicksWhenGuideSessionChanges()
    {
        if (_observedGuideSessionRevision == _guide.PathPreviewSessionRevision)
        {
            return;
        }

        _observedGuideSessionRevision = _guide.PathPreviewSessionRevision;
        _hasStart = false;
        _hasGoal = false;
        _startWorldCm = default;
        _goalWorldCm = default;
    }
}

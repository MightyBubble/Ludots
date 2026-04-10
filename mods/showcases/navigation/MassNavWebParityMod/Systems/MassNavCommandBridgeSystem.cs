using System;
using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Mathematics;
using Ludots.Core.Scripting;
using MassNavWebParityMod.Runtime;

namespace MassNavWebParityMod.Systems;

internal sealed class MassNavCommandBridgeSystem : ISystem<float>
{
    private readonly GameEngine _engine;
    private readonly MassNavSimulationRuntime _simulation;

    public MassNavCommandBridgeSystem(GameEngine engine, MassNavSimulationRuntime simulation)
    {
        _engine = engine;
        _simulation = simulation;
    }

    public void Initialize() { }
    public void BeforeUpdate(in float dt) { }
    public void AfterUpdate(in float dt) { }
    public void Dispose() { }

    public void Update(in float dt)
    {
        if (!MassNavWebParityIds.IsCurrentPlaygroundMap(_engine))
        {
            return;
        }

        if (_engine.GetService(CoreServiceKeys.UiCaptured) ||
            _engine.GetService(CoreServiceKeys.AuthoritativeInput) is not IInputActionReader input)
        {
            return;
        }

        InteractionActionBindings bindings = InteractionActionBindingsResolver.Require(
            _engine.GlobalContext,
            nameof(MassNavCommandBridgeSystem));
        if (!input.PressedThisFrame(bindings.CommandActionId) ||
            !AuthoritativeGroundPointerHelper.TryRead(input, out WorldCmInt2 worldCm))
        {
            return;
        }

        ApplyMoveCommand(new Vector2(worldCm.X, worldCm.Y));
    }

    private void ApplyMoveCommand(Vector2 centerCm)
    {
        ReadOnlySpan<Entity> selected = _simulation.SelectedEntities;
        if (selected.Length <= 0)
        {
            _simulation.WebParity.SetTeamTarget(_simulation.SelectedTeamId, centerCm);
            _simulation.MarkStructuralChange();
            return;
        }

        int assigned = _simulation.FormationRuntime.AssignFormation(
            _simulation.WebParity,
            _simulation.AgentState,
            selected,
            centerCm,
            _simulation.FormationMode);
        if (assigned > 0)
        {
            _simulation.MarkStructuralChange();
        }
    }
}

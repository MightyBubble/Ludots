using System;
using Arch.Core;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Input.EntityView;
using Ludots.Core.Input.MassNavigation;
using Ludots.Core.Input.Runtime;
using Ludots.Core.MassNavigation;
using Ludots.Core.MassNavigation.Runtime;
using Ludots.Core.Scripting;

namespace FormationCapabilityShowcaseMod.Systems;

internal sealed class FormationCapabilityDebugInputSystem : ISystem<float>
{
    public const string RotateLeftActionId = "MassNavigation_RotateLeft";
    public const string RotateRightActionId = "MassNavigation_RotateRight";
    public const string ResetSceneActionId = "MassNavigation_ResetScene";

    private readonly GameEngine _engine;
    private readonly Runtime.FormationCapabilityShowcaseRuntime _showcase;
    private Entity[] _selectionScratch = new Entity[16];

    public FormationCapabilityDebugInputSystem(GameEngine engine, Runtime.FormationCapabilityShowcaseRuntime showcase)
    {
        _engine = engine;
        _showcase = showcase;
    }

    public void Initialize() { }
    public void BeforeUpdate(in float dt) { }
    public void AfterUpdate(in float dt) { }
    public void Dispose() { }

    public void Update(in float dt)
    {
        if (!_showcase.IsCurrentShowcaseMap(_engine) ||
            !MassNavigationIds.IsCurrentNavigationRuntimeReady(_engine) ||
            _engine.GetService(CoreServiceKeys.UiCaptured) ||
            _engine.GetService(MassNavigationKeys.SimulationRuntime) is not MassNavigationSimulationRuntime simulation ||
            _engine.GetService(CoreServiceKeys.AuthoritativeInput) is not IInputActionReader input)
        {
            return;
        }

        if (input.PressedThisFrame(ResetSceneActionId))
        {
            simulation.RequestSceneReset();
            return;
        }

        float deltaRadians = 0f;
        if (input.IsDown(RotateLeftActionId))
        {
            deltaRadians -= simulation.Config.Semantics.Group.FormationRotationSpeedRadiansPerSecond * dt;
        }

        if (input.IsDown(RotateRightActionId))
        {
            deltaRadians += simulation.Config.Semantics.Group.FormationRotationSpeedRadiansPerSecond * dt;
        }

        if (MathF.Abs(deltaRadians) <= simulation.Config.Semantics.Group.FormationRotationEpsilonRadians ||
            !TryCopyCommandSourceEntities(out int written) ||
            written <= 0)
        {
            return;
        }

        int playerId = ResolveLocalPlayerId();
        ReadOnlySpan<Entity> selected = _selectionScratch.AsSpan(0, written);
        if (!MassNavigationMoveOrderSubmitter.CanSubmitSelectionMoveOrders(_engine.World, selected, playerId))
        {
            simulation.RejectCommandUnauthorizedSelection(0f, 0f);
            return;
        }

        simulation.NavGroupRuntime.RotateSelected(_engine.World, simulation.AgentState, selected, deltaRadians);
        simulation.MarkCommandApply();
    }

    private bool TryCopyCommandSourceEntities(out int written)
    {
        written = 0;
        EntityViewRuntimeConfig config = _engine.GetService(CoreServiceKeys.EntityViewConfig)
            ?? throw new InvalidOperationException("Formation Capability debug input requires EntityViewConfig.");
        int required = EntityViewRuntime.GetCommandSourceCount(_engine.World, _engine.GlobalContext, config);
        if (required <= 0)
        {
            return false;
        }

        EnsureSelectionScratch(required);
        written = EntityViewRuntime.CopyCommandSourceEntities(
            _engine.World,
            _engine.GlobalContext,
            config,
            _selectionScratch.AsSpan(0, required));
        return written > 0;
    }

    private void EnsureSelectionScratch(int required)
    {
        if (required <= _selectionScratch.Length)
        {
            return;
        }

        int nextSize = _selectionScratch.Length;
        while (nextSize < required)
        {
            nextSize *= 2;
        }

        Array.Resize(ref _selectionScratch, nextSize);
    }

    private int ResolveLocalPlayerId()
    {
        if (!_engine.GlobalContext.TryGetValue(CoreServiceKeys.LocalPlayerEntity.Name, out object? localObj) ||
            localObj is not Entity local ||
            !_engine.World.IsAlive(local))
        {
            throw new InvalidOperationException("Formation Capability debug input requires LocalPlayerEntity.");
        }

        return _engine.World.Get<PlayerOwner>(local).PlayerId;
    }
}

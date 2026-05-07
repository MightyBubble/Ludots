using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Input.Selection;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Scripting;
using MassNavWebParityMod.Runtime;

namespace MassNavWebParityMod.Systems;

internal sealed class MassNavSpawnReceiptBindingSystem : ISystem<float>
{
    private readonly GameEngine _engine;
    private readonly MassNavSimulationRuntime _simulation;

    public MassNavSpawnReceiptBindingSystem(GameEngine engine, MassNavSimulationRuntime simulation)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _simulation = simulation ?? throw new ArgumentNullException(nameof(simulation));
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

        RuntimeEntitySpawnReceiptQueue receipts = _engine.GetService(CoreServiceKeys.RuntimeEntitySpawnReceiptQueue)
            ?? throw new InvalidOperationException("MassNavWebParityMod requires RuntimeEntitySpawnReceiptQueue.");
        bool boundAny = false;
        while (receipts.TryDequeueForChannel(MassNavWebParityIds.RuntimeSpawnReceiptChannelId, out RuntimeEntitySpawnReceipt receipt))
        {
            if (!_simulation.SpawnReceipts.TryConsume(receipt.ReceiptId, out MassNavSpawnReceiptBinding binding))
            {
                throw new InvalidOperationException($"MassNavWebParityMod received unknown spawn receipt id {receipt.ReceiptId}.");
            }

            BindReceipt(in receipt, in binding);
            boundAny = true;
        }

        if (boundAny)
        {
            _simulation.MarkStructuralChange();
        }
    }

    private void BindReceipt(in RuntimeEntitySpawnReceipt receipt, in MassNavSpawnReceiptBinding binding)
    {
        if (receipt.Kind != RuntimeEntitySpawnKind.Template)
        {
            throw new InvalidOperationException($"MassNavWebParityMod expected template spawn receipt, got {receipt.Kind}.");
        }

        if (!string.Equals(receipt.TemplateId, binding.TemplateId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"MassNavWebParityMod spawn receipt template mismatch: expected '{binding.TemplateId}', got '{receipt.TemplateId}'.");
        }

        Entity entity = receipt.Entity;
        if (!_engine.World.IsAlive(entity))
        {
            throw new InvalidOperationException($"MassNavWebParityMod spawn receipt id {receipt.ReceiptId} returned a dead entity.");
        }

        switch (binding.Kind)
        {
            case MassNavSpawnReceiptKind.Agent:
                BindAgent(entity, in binding);
                return;
            case MassNavSpawnReceiptKind.Blocker:
                BindBlocker(entity, in binding);
                return;
            case MassNavSpawnReceiptKind.WorldMarker:
                BindWorldMarker(entity, in binding);
                return;
            default:
                throw new InvalidOperationException($"MassNavWebParityMod unsupported spawn receipt kind {binding.Kind}.");
        }
    }

    private void BindAgent(Entity entity, in MassNavSpawnReceiptBinding binding)
    {
        RequireComponent<MassNavAgentTag>(entity, binding.TemplateId);
        RequireComponent<MassNavControllable>(entity, binding.TemplateId);
        RequireComponent<Team>(entity, binding.TemplateId);
        RequireComponent<OrderBuffer>(entity, binding.TemplateId);
        RequireComponent<SelectionSelectableTag>(entity, binding.TemplateId);
        RequireComponent<SelectionSelectableState>(entity, binding.TemplateId);
        RequireComponent<WorldPositionCm>(entity, binding.TemplateId);
        RequireComponent<PreviousWorldPositionCm>(entity, binding.TemplateId);
        RequireComponent<FacingDirection>(entity, binding.TemplateId);
        RequireComponent<VisualTransform>(entity, binding.TemplateId);
        RequireComponent<CullState>(entity, binding.TemplateId);

        ref readonly Team team = ref _engine.World.Get<Team>(entity);
        if (team.Id != binding.ExpectedTeamId)
        {
            throw new InvalidOperationException(
                $"MassNavWebParityMod template '{binding.TemplateId}' has Team.Id={team.Id}; expected {binding.ExpectedTeamId}.");
        }

        if (_engine.World.Has<MassNavAgentIndex>(entity) || _engine.World.Has<MassNavAgentProfile>(entity))
        {
            throw new InvalidOperationException($"MassNavWebParityMod entity from template '{binding.TemplateId}' was already bound as an agent.");
        }

        _engine.World.Add(entity, new MassNavAgentIndex { Value = binding.UnitIndex });
        _engine.World.Add(entity, new MassNavAgentProfile
        {
            NavMass = binding.NavMass,
            VisualScale = binding.VisualScale,
        });
        _simulation.AgentState.RegisterAgent(entity, controllable: true);
    }

    private void BindBlocker(Entity entity, in MassNavSpawnReceiptBinding binding)
    {
        RequireComponent<MassNavBlocker>(entity, binding.TemplateId);
        RequireComponent<WorldPositionCm>(entity, binding.TemplateId);
        RequireComponent<PreviousWorldPositionCm>(entity, binding.TemplateId);
        RequireComponent<VisualTransform>(entity, binding.TemplateId);
        RequireComponent<CullState>(entity, binding.TemplateId);

        if (_engine.World.Has<MassNavBlockerProfile>(entity))
        {
            throw new InvalidOperationException($"MassNavWebParityMod entity from template '{binding.TemplateId}' was already bound as a blocker.");
        }

        _engine.World.Add(entity, new MassNavBlockerProfile { RadiusCm = binding.BlockerRadiusCm });
        _simulation.AgentState.RegisterBlocker(entity);
    }

    private void BindWorldMarker(Entity entity, in MassNavSpawnReceiptBinding binding)
    {
        RequireComponent<MassNavHotspotMarker>(entity, binding.TemplateId);
        RequireComponent<WorldPositionCm>(entity, binding.TemplateId);
        RequireComponent<PreviousWorldPositionCm>(entity, binding.TemplateId);
        RequireComponent<VisualTransform>(entity, binding.TemplateId);
        _simulation.AgentState.RegisterWorldMarker(entity);
    }

    private void RequireComponent<T>(Entity entity, string templateId)
    {
        if (!_engine.World.Has<T>(entity))
        {
            throw new InvalidOperationException($"MassNavWebParityMod template '{templateId}' must author component {typeof(T).Name}.");
        }
    }
}

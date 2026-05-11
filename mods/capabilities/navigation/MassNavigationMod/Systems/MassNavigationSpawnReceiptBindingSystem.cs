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
using MassNavigationMod.Runtime;

namespace MassNavigationMod.Systems;

internal sealed class MassNavigationSpawnReceiptBindingSystem : ISystem<float>
{
    private readonly GameEngine _engine;
    private readonly MassNavigationSimulationRuntime _simulation;
    private int _receiptChannelId;

    public MassNavigationSpawnReceiptBindingSystem(GameEngine engine, MassNavigationSimulationRuntime simulation)
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
        if (!MassNavigationIds.IsCurrentNavigationMap(_engine))
        {
            return;
        }

        RuntimeEntitySpawnReceiptQueue receipts = _engine.GetService(CoreServiceKeys.RuntimeEntitySpawnReceiptQueue)
            ?? throw new InvalidOperationException("MassNavigationMod requires RuntimeEntitySpawnReceiptQueue.");
        int receiptChannelId = ResolveReceiptChannelId();
        bool boundAny = false;
        while (receipts.TryDequeueForChannel(receiptChannelId, out RuntimeEntitySpawnReceipt receipt))
        {
            if (!_simulation.SpawnReceipts.TryConsume(receipt.ReceiptId, out MassNavigationSpawnReceiptBinding binding))
            {
                throw new InvalidOperationException($"MassNavigationMod received unknown spawn receipt id {receipt.ReceiptId}.");
            }

            BindReceipt(in receipt, in binding);
            boundAny = true;
        }

        if (boundAny)
        {
            _simulation.MarkStructuralChange();
        }
    }

    private int ResolveReceiptChannelId()
    {
        if (_receiptChannelId > 0)
        {
            return _receiptChannelId;
        }

        RuntimeEntitySpawnReceiptChannelRegistry channels = _engine.GetService(CoreServiceKeys.RuntimeEntitySpawnReceiptChannelRegistry)
            ?? throw new InvalidOperationException("MassNavigationMod requires RuntimeEntitySpawnReceiptChannelRegistry.");
        _receiptChannelId = channels.Register(MassNavigationIds.RuntimeSpawnReceiptChannelKey);
        return _receiptChannelId;
    }

    private void BindReceipt(in RuntimeEntitySpawnReceipt receipt, in MassNavigationSpawnReceiptBinding binding)
    {
        if (receipt.Kind != RuntimeEntitySpawnKind.Template)
        {
            throw new InvalidOperationException($"MassNavigationMod expected template spawn receipt, got {receipt.Kind}.");
        }

        if (!string.Equals(receipt.TemplateId, binding.TemplateId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"MassNavigationMod spawn receipt template mismatch: expected '{binding.TemplateId}', got '{receipt.TemplateId}'.");
        }

        Entity entity = receipt.Entity;
        if (!_engine.World.IsAlive(entity))
        {
            throw new InvalidOperationException($"MassNavigationMod spawn receipt id {receipt.ReceiptId} returned a dead entity.");
        }

        switch (binding.Kind)
        {
            case MassNavigationSpawnReceiptKind.Agent:
                BindAgent(entity, in binding);
                return;
            case MassNavigationSpawnReceiptKind.Blocker:
                BindBlocker(entity, in binding);
                return;
            case MassNavigationSpawnReceiptKind.WorldMarker:
                BindWorldMarker(entity, in binding);
                return;
            default:
                throw new InvalidOperationException($"MassNavigationMod unsupported spawn receipt kind {binding.Kind}.");
        }
    }

    private void BindAgent(Entity entity, in MassNavigationSpawnReceiptBinding binding)
    {
        RequireComponent<MassNavigationAgentTag>(entity, binding.TemplateId);
        RequireComponent<MassNavigationControllable>(entity, binding.TemplateId);
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
                $"MassNavigationMod template '{binding.TemplateId}' has Team.Id={team.Id}; expected {binding.ExpectedTeamId}.");
        }

        if (_engine.World.Has<MassNavigationAgentIndex>(entity) || _engine.World.Has<MassNavigationAgentProfile>(entity))
        {
            throw new InvalidOperationException($"MassNavigationMod entity from template '{binding.TemplateId}' was already bound as an agent.");
        }

        _engine.World.Add(entity, new MassNavigationAgentIndex { Value = binding.UnitIndex });
        _engine.World.Add(entity, new MassNavigationAgentProfile
        {
            Heavy = binding.Heavy,
            NavMass = binding.NavMass,
            VisualScale = binding.VisualScale,
        });
        _simulation.AgentState.RegisterAgent(entity, controllable: true);
    }

    private void BindBlocker(Entity entity, in MassNavigationSpawnReceiptBinding binding)
    {
        RequireComponent<MassNavigationBlocker>(entity, binding.TemplateId);
        RequireComponent<WorldPositionCm>(entity, binding.TemplateId);
        RequireComponent<PreviousWorldPositionCm>(entity, binding.TemplateId);
        RequireComponent<VisualTransform>(entity, binding.TemplateId);
        RequireComponent<CullState>(entity, binding.TemplateId);

        if (_engine.World.Has<MassNavigationBlockerProfile>(entity))
        {
            throw new InvalidOperationException($"MassNavigationMod entity from template '{binding.TemplateId}' was already bound as a blocker.");
        }

        _engine.World.Add(entity, new MassNavigationBlockerProfile { RadiusCm = binding.BlockerRadiusCm });
        _simulation.AgentState.RegisterBlocker(entity);
    }

    private void BindWorldMarker(Entity entity, in MassNavigationSpawnReceiptBinding binding)
    {
        RequireComponent<MassNavigationHotspotMarker>(entity, binding.TemplateId);
        RequireComponent<WorldPositionCm>(entity, binding.TemplateId);
        RequireComponent<PreviousWorldPositionCm>(entity, binding.TemplateId);
        RequireComponent<VisualTransform>(entity, binding.TemplateId);
        _simulation.AgentState.RegisterWorldMarker(entity);
    }

    private void RequireComponent<T>(Entity entity, string templateId)
    {
        if (!_engine.World.Has<T>(entity))
        {
            throw new InvalidOperationException($"MassNavigationMod template '{templateId}' must author component {typeof(T).Name}.");
        }
    }
}


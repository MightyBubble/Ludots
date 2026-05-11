using System;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Input.Selection;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Scripting;
using MassNavigationMod.Runtime;

namespace MassNavigationTotalWarEntryMod.Runtime;

internal sealed class TotalWarSpawnReceiptBindingSystem : ISystem<float>
{
    private readonly GameEngine _engine;
    private readonly TotalWarShowcaseRuntime _runtime;
    private readonly MassNavigationSimulationRuntime _simulation;
    private int _receiptChannelId;

    public TotalWarSpawnReceiptBindingSystem(
        GameEngine engine,
        TotalWarShowcaseRuntime runtime,
        MassNavigationSimulationRuntime simulation)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _simulation = simulation ?? throw new ArgumentNullException(nameof(simulation));
    }

    public void Initialize() { }
    public void BeforeUpdate(in float dt) { }
    public void AfterUpdate(in float dt) { }
    public void Dispose() { }

    public void Update(in float dt)
    {
        if (!_runtime.IsCurrentShowcaseMap(_engine))
        {
            return;
        }

        RuntimeEntitySpawnReceiptQueue receipts = _engine.GetService(CoreServiceKeys.RuntimeEntitySpawnReceiptQueue)
            ?? throw new InvalidOperationException("Total War showcase requires RuntimeEntitySpawnReceiptQueue.");
        int receiptChannelId = ResolveReceiptChannelId();
        bool boundAny = false;
        while (receipts.TryDequeueForChannel(receiptChannelId, out RuntimeEntitySpawnReceipt receipt))
        {
            if (!_runtime.TryConsumeReceipt(receipt.ReceiptId, out TotalWarSpawnReceiptBinding binding))
            {
                throw new InvalidOperationException($"Total War showcase received unknown spawn receipt id {receipt.ReceiptId}.");
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

        _receiptChannelId = _runtime.ResolveReceiptChannelId(_engine, _runtime.ActiveConfig);
        return _receiptChannelId;
    }

    private void BindReceipt(in RuntimeEntitySpawnReceipt receipt, in TotalWarSpawnReceiptBinding binding)
    {
        if (receipt.Kind != RuntimeEntitySpawnKind.Template)
        {
            throw new InvalidOperationException($"Total War showcase expected template spawn receipt, got {receipt.Kind}.");
        }

        if (!string.Equals(receipt.TemplateId, binding.TemplateId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Total War showcase spawn receipt template mismatch: expected '{binding.TemplateId}', got '{receipt.TemplateId}'.");
        }

        Entity entity = receipt.Entity;
        if (!_engine.World.IsAlive(entity))
        {
            throw new InvalidOperationException($"Total War showcase spawn receipt id {receipt.ReceiptId} returned a dead entity.");
        }

        switch (binding.Kind)
        {
            case TotalWarSpawnReceiptKind.Soldier:
                BindSoldier(entity, in binding);
                return;
            case TotalWarSpawnReceiptKind.FormationAnchor:
                BindFormationAnchor(entity, in binding);
                return;
            default:
                throw new InvalidOperationException($"Total War showcase unsupported spawn receipt kind {binding.Kind}.");
        }
    }

    private void BindSoldier(Entity entity, in TotalWarSpawnReceiptBinding binding)
    {
        RequireComponent<MassNavigationAgentTag>(entity, binding.TemplateId);
        RequireComponent<MassNavigationControllable>(entity, binding.TemplateId);
        RequireComponent<Team>(entity, binding.TemplateId);
        RequireComponent<OrderBuffer>(entity, binding.TemplateId);
        RequireComponent<SelectionSelectableTag>(entity, binding.TemplateId);
        RequireComponent<SelectionSelectableState>(entity, binding.TemplateId);
        RequireWorldPresentationComponents(entity, binding.TemplateId);

        RequireTeam(entity, binding);

        if (_engine.World.Has<MassNavigationAgentIndex>(entity) ||
            _engine.World.Has<MassNavigationAgentProfile>(entity) ||
            _engine.World.Has<TotalWarFormationSoldier>(entity))
        {
            throw new InvalidOperationException($"Total War showcase entity from template '{binding.TemplateId}' was already bound.");
        }

        _engine.World.Add(entity, new MassNavigationAgentIndex { Value = binding.UnitIndex });
        _engine.World.Add(entity, new MassNavigationAgentProfile
        {
            Heavy = binding.Heavy,
            NavMass = binding.NavMass,
            VisualScale = binding.VisualScale,
        });
        _engine.World.Add(entity, new TotalWarFormationSoldier
        {
            FormationIndex = binding.FormationIndex,
            SlotIndex = binding.SlotIndex,
        });
        _simulation.AgentState.RegisterAgentAtIndex(entity, binding.UnitIndex, controllable: true);
        _runtime.RegisterSpawnedSoldier(entity, in binding);
    }

    private void BindFormationAnchor(Entity entity, in TotalWarSpawnReceiptBinding binding)
    {
        RequireComponent<SpatialPartitionExcluded>(entity, binding.TemplateId);
        RequireWorldPresentationComponents(entity, binding.TemplateId);

        if (_engine.World.Has<TotalWarFormationAnchor>(entity) ||
            _engine.World.Has<TotalWarFormationState>(entity) ||
            _engine.World.Has<TotalWarFormationOutline>(entity))
        {
            throw new InvalidOperationException($"Total War showcase formation anchor template '{binding.TemplateId}' was already bound.");
        }

        _runtime.RegisterSpawnedFormationAnchor(_engine, entity, in binding);
    }

    private void RequireWorldPresentationComponents(Entity entity, string templateId)
    {
        RequireComponent<WorldPositionCm>(entity, templateId);
        RequireComponent<PreviousWorldPositionCm>(entity, templateId);
        RequireComponent<FacingDirection>(entity, templateId);
        RequireComponent<VisualTransform>(entity, templateId);
        RequireComponent<CullState>(entity, templateId);
        RequireComponent<PresentationStableId>(entity, templateId);
    }

    private void RequireTeam(Entity entity, in TotalWarSpawnReceiptBinding binding)
    {
        ref readonly Team team = ref _engine.World.Get<Team>(entity);
        if (team.Id != binding.TeamId)
        {
            throw new InvalidOperationException(
                $"Total War showcase template '{binding.TemplateId}' has Team.Id={team.Id}; expected {binding.TeamId}.");
        }
    }

    private void RequireComponent<T>(Entity entity, string templateId)
    {
        if (!_engine.World.Has<T>(entity))
        {
            throw new InvalidOperationException($"Total War showcase template '{templateId}' must author component {typeof(T).Name}.");
        }
    }
}

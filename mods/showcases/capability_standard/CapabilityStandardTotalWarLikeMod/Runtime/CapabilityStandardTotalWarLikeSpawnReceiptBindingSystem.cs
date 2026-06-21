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

namespace CapabilityStandardTotalWarLikeMod.Runtime;

internal sealed class CapabilityStandardTotalWarLikeSpawnReceiptBindingSystem : ISystem<float>
{
    private readonly GameEngine _engine;
    private readonly CapabilityStandardTotalWarLikeRuntime _runtime;
    private readonly MassNavigationSimulationRuntime _simulation;
    private int _receiptChannelId;

    public CapabilityStandardTotalWarLikeSpawnReceiptBindingSystem(
        GameEngine engine,
        CapabilityStandardTotalWarLikeRuntime runtime,
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
            if (!_runtime.TryConsumeReceipt(receipt.ReceiptId, out CapabilityStandardTotalWarLikeSpawnReceiptBinding binding))
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

    private void BindReceipt(in RuntimeEntitySpawnReceipt receipt, in CapabilityStandardTotalWarLikeSpawnReceiptBinding binding)
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
            case CapabilityStandardTotalWarLikeSpawnReceiptKind.Soldier:
                BindSoldier(entity, in binding);
                return;
            case CapabilityStandardTotalWarLikeSpawnReceiptKind.FormationAgent:
                BindFormationAgent(entity, in binding);
                return;
            case CapabilityStandardTotalWarLikeSpawnReceiptKind.ObstacleOverlay:
                BindObstacleOverlay(entity, in binding);
                return;
            default:
                throw new InvalidOperationException($"Total War showcase unsupported spawn receipt kind {binding.Kind}.");
        }
    }

    private void BindSoldier(Entity entity, in CapabilityStandardTotalWarLikeSpawnReceiptBinding binding)
    {
        RequireComponent<MassNavigationAgentTag>(entity, binding.TemplateId);
        RequireWorldPresentationComponents(entity, binding.TemplateId);

        RejectComponent<Team>(entity, binding.TemplateId);
        RejectComponent<MassNavigationControllable>(entity, binding.TemplateId);
        RejectComponent<OrderBuffer>(entity, binding.TemplateId);
        RejectComponent<SelectionSelectableTag>(entity, binding.TemplateId);
        RejectComponent<SelectionSelectableState>(entity, binding.TemplateId);
        RejectComponent<AttributeBuffer>(entity, binding.TemplateId);

        if (_engine.World.Has<MassNavigationAgentIndex>(entity) ||
            _engine.World.Has<MassNavigationAgentProfile>(entity) ||
            _engine.World.Has<CapabilityStandardTotalWarLikeFormationSoldier>(entity))
        {
            throw new InvalidOperationException($"Total War showcase entity from template '{binding.TemplateId}' was already bound.");
        }

        _simulation.BindSpawnedAgent(
            _engine.World,
            entity,
            binding.MassNavAgentIndex,
            controllable: false);
        _engine.World.Add(entity, new CapabilityStandardTotalWarLikeFormationSoldier
        {
            FormationIndex = binding.FormationIndex,
            SlotIndex = binding.SlotIndex,
        });
        _runtime.RegisterSpawnedSoldier(entity, in binding);
    }

    private void BindFormationAgent(Entity entity, in CapabilityStandardTotalWarLikeSpawnReceiptBinding binding)
    {
        RequireComponent<MassNavigationAgentTag>(entity, binding.TemplateId);
        RequireComponent<MassNavigationControllable>(entity, binding.TemplateId);
        RequireComponent<OrderBuffer>(entity, binding.TemplateId);
        RequireComponent<SelectionSelectableTag>(entity, binding.TemplateId);
        RequireComponent<SelectionSelectableState>(entity, binding.TemplateId);
        RequireComponent<AttributeBuffer>(entity, binding.TemplateId);
        RejectComponent<PlayerOwner>(entity, binding.TemplateId);
        RequireWorldPresentationComponents(entity, binding.TemplateId);

        if (_engine.World.Has<MassNavigationAgentIndex>(entity) ||
            _engine.World.Has<MassNavigationAgentProfile>(entity) ||
            _engine.World.Has<CapabilityStandardTotalWarLikeFormationAgent>(entity) ||
            _engine.World.Has<CapabilityStandardTotalWarLikeFormationState>(entity) ||
            _engine.World.Has<CapabilityStandardTotalWarLikeFormationOutline>(entity))
        {
            throw new InvalidOperationException($"Total War showcase formation agent template '{binding.TemplateId}' was already bound.");
        }

        _simulation.BindSpawnedAgent(
            _engine.World,
            entity,
            binding.MassNavAgentIndex,
            controllable: true);
        _runtime.RegisterSpawnedFormationAgent(_engine, entity, in binding);
    }

    private void BindObstacleOverlay(Entity entity, in CapabilityStandardTotalWarLikeSpawnReceiptBinding binding)
    {
        RequireWorldPresentationComponents(entity, binding.TemplateId);
        if (_engine.World.Has<CapabilityStandardTotalWarLikeObstacleOverlay>(entity))
        {
            throw new InvalidOperationException($"Total War showcase template '{binding.TemplateId}' must not author component {nameof(CapabilityStandardTotalWarLikeObstacleOverlay)}; obstacle overlay values come from CapabilityStandardTotalWarLikeConfig.");
        }

        CapabilityStandardTotalWarLikeObstacleOverlay configured = _runtime.ActiveConfig.ObstacleOverlay.ToComponent(binding.ObstacleRadiusCm);
        UpsertComponent(_engine.World, entity, configured);
        _runtime.RegisterSpawnedObstacleOverlay(entity, in binding);
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

    private void RequireComponent<T>(Entity entity, string templateId)
    {
        if (!_engine.World.Has<T>(entity))
        {
            throw new InvalidOperationException($"Total War showcase template '{templateId}' must author component {typeof(T).Name}.");
        }
    }

    private void RejectComponent<T>(Entity entity, string templateId)
    {
        if (_engine.World.Has<T>(entity))
        {
            throw new InvalidOperationException($"Total War showcase template '{templateId}' must not author component {typeof(T).Name}.");
        }
    }

    private static void UpsertComponent<T>(World world, Entity entity, T component)
    {
        if (world.Has<T>(entity))
        {
            world.Set(entity, component);
        }
        else
        {
            world.Add(entity, component);
        }
    }
}

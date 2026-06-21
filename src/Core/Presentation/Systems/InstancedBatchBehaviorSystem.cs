using System;
using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Presentation.Instancing;
using Ludots.Core.Presentation.Performers;

namespace Ludots.Core.Presentation.Systems
{
    public sealed class InstancedBatchBehaviorSystem : BaseSystem<World, float>
    {
        private readonly PerformerDefinitionRegistry _definitions;
        private readonly PerformerEntityRuntime _runtime;
        private readonly InstancedBatchAssetRegistry _batchAssets;
        private readonly InstancedBatchOperationBuffer _operations;
        private readonly PresentationEventStream _events;
        private readonly PresentationOwnerChangeBuffer _ownerChanges;

        public InstancedBatchBehaviorSystem(
            World world,
            PerformerDefinitionRegistry definitions,
            PerformerEntityRuntime runtime,
            InstancedBatchAssetRegistry batchAssets,
            InstancedBatchOperationBuffer operations,
            PresentationEventStream events,
            PresentationOwnerChangeBuffer ownerChanges)
            : base(world)
        {
            _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            _batchAssets = batchAssets ?? throw new ArgumentNullException(nameof(batchAssets));
            _operations = operations ?? throw new ArgumentNullException(nameof(operations));
            _events = events ?? throw new ArgumentNullException(nameof(events));
            _ownerChanges = ownerChanges ?? throw new ArgumentNullException(nameof(ownerChanges));
        }

        public override void Update(in float dt)
        {
            EmitOwnerChangeOperations();
            EmitEventOperations();
        }

        private void EmitOwnerChangeOperations()
        {
            ReadOnlySpan<PresentationOwnerChange> changes = _ownerChanges.GetSpan();
            for (int i = 0; i < changes.Length; i++)
            {
                ref readonly PresentationOwnerChange change = ref changes[i];
                if (change.Kind != PresentationOwnerChangeKind.Attribute)
                {
                    continue;
                }

                float value = ResolveOwnerAttributeValue(change.Owner, change.KeyId);
                EmitForOwner(
                    change.Owner,
                    InstancedBatchSourceKind.Attribute,
                    change.KeyId,
                    value);
            }
        }

        private void EmitEventOperations()
        {
            ReadOnlySpan<PresentationEvent> events = _events.GetSpan();
            for (int i = 0; i < events.Length; i++)
            {
                ref readonly PresentationEvent evt = ref events[i];
                if (evt.Kind == PresentationEventKind.AttributeValueChanged)
                {
                    continue;
                }

                InstancedBatchSourceKind sourceKind = IsGasPresentationEvent(evt.Kind)
                    ? InstancedBatchSourceKind.GasEvent
                    : InstancedBatchSourceKind.PresentationEvent;
                EmitForOwner(
                    evt.Source,
                    sourceKind,
                    evt.KeyId,
                    evt.Magnitude,
                    evt.Kind);
            }
        }

        private void EmitForOwner(
            Entity owner,
            InstancedBatchSourceKind sourceKind,
            int sourceKeyId,
            float sourceValue,
            PresentationEventKind sourceEventKind = PresentationEventKind.None)
        {
            if (!_runtime.TryGetActiveByOwner(owner, out PerformerEntityRuntime.OwnerPerformerBucket performers))
            {
                return;
            }

            for (int performerIndex = 0; performerIndex < performers.Count; performerIndex++)
            {
                Entity performer = performers.GetAt(performerIndex);
                if (!World.IsAlive(performer) || !World.Has<PerformerState>(performer))
                {
                    continue;
                }

                ref readonly PerformerState state = ref World.Get<PerformerState>(performer);
                if (!_definitions.TryGet(state.DefId, out PerformerDefinition definition) ||
                    !definition.HasInstancedBatchBindings)
                {
                    continue;
                }

                InstancedBatchBinding[] bindings = definition.InstancedBatches;
                for (int bindingIndex = 0; bindingIndex < bindings.Length; bindingIndex++)
                {
                    int batchAssetId = bindings[bindingIndex].BatchAssetId;
                    if (!_batchAssets.TryGet(batchAssetId, out InstancedBatchAsset asset))
                    {
                        continue;
                    }

                    EmitMatchingBindings(
                        performer,
                        in state,
                        asset,
                        sourceKind,
                        sourceKeyId,
                        sourceValue,
                        sourceEventKind);
                }
            }
        }

        private void EmitMatchingBindings(
            Entity performer,
            in PerformerState state,
            InstancedBatchAsset asset,
            InstancedBatchSourceKind sourceKind,
            int sourceKeyId,
            float sourceValue,
            PresentationEventKind sourceEventKind)
        {
            InstancedBatchBehaviorBinding[] behaviors = asset.Behaviors;
            for (int i = 0; i < behaviors.Length; i++)
            {
                ref readonly InstancedBatchBehaviorBinding binding = ref behaviors[i];
                if (binding.SourceKind != sourceKind ||
                    !SourceKeyMatches(in binding, sourceKeyId) ||
                    binding.SourceEventKind != sourceEventKind)
                {
                    continue;
                }

                if (!binding.HasCompiledAddress)
                {
                    throw new InvalidOperationException(
                        $"Instanced batch asset '{asset.Key}' behavior '{binding.Key}' has not been compiled to a stable address.");
                }

                float mapped = MapValue(sourceValue, in binding);
                _operations.Add(new InstancedBatchOperation(
                    binding.OperationKind,
                    asset.Id,
                    state.StableId,
                    state.OwnerEntity,
                    performer,
                    binding.Address,
                    binding.CustomDataSlot,
                    new Vector4(mapped, 0f, 0f, 0f),
                    binding.TargetPayloadId,
                    ResolveOperationState(binding.OperationKind, mapped),
                    binding.Coalescing,
                    binding.Lifecycle));
            }
        }

        private float ResolveOwnerAttributeValue(Entity owner, int attributeId)
        {
            if (!World.IsAlive(owner) || !World.Has<AttributeBuffer>(owner))
            {
                return 0f;
            }

            ref AttributeBuffer attributes = ref World.Get<AttributeBuffer>(owner);
            return attributes.GetCurrent(attributeId);
        }

        private static bool IsGasPresentationEvent(PresentationEventKind kind)
        {
            return kind is PresentationEventKind.EffectApplied
                or PresentationEventKind.CastCommitted
                or PresentationEventKind.CastFailed;
        }

        private static bool SourceKeyMatches(in InstancedBatchBehaviorBinding binding, int eventKeyId)
        {
            if (binding.SourceKeyId == eventKeyId)
            {
                return true;
            }

            return binding.SourceKind == InstancedBatchSourceKind.PresentationEvent &&
                   binding.SourceEventKind is PresentationEventKind.PerformerCreated or PresentationEventKind.PerformerDestroyed &&
                   binding.SourceKeyId == -1;
        }

        private static float MapValue(float value, in InstancedBatchBehaviorBinding binding)
        {
            return binding.MappingKind switch
            {
                InstancedBatchValueMappingKind.Constant => binding.ConstantValue,
                InstancedBatchValueMappingKind.Linear => MapLinear(value, binding.InputMin, binding.InputMax, binding.OutputMin, binding.OutputMax),
                _ => value,
            };
        }

        private static float MapLinear(float value, float inputMin, float inputMax, float outputMin, float outputMax)
        {
            if (MathF.Abs(inputMax - inputMin) <= 0.0001f)
            {
                return outputMin;
            }

            float t = (value - inputMin) / (inputMax - inputMin);
            return outputMin + (outputMax - outputMin) * t;
        }

        private static byte ResolveOperationState(InstancedBatchOperationKind kind, float mappedValue)
        {
            return kind == InstancedBatchOperationKind.SetVisibility && mappedValue > 0f
                ? (byte)1
                : (byte)0;
        }
    }
}

using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Association;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.Items;
using Ludots.Core.Gameplay.Relationships;

namespace Ludots.Core.Gameplay.Exchange
{
    public sealed class ExchangeRuntime
    {
        private readonly World _world;
        private readonly ExchangeOperationRegistry _operations;
        private readonly ExchangeScopedOperationStore _scopedOperations;
        private readonly InventoryRuntimeService _inventory;
        private readonly EffectRequestQueue _effects;
        private readonly RelationshipRuntime _relationships;
        private readonly List<ItemConsumptionRecord> _consumed = new(16);
        private readonly List<AttributeCostRecord> _attributeCosts = new(8);
        private readonly List<CreatedItemRecord> _created = new(8);
        private readonly List<MovedItemRecord> _moved = new(8);
        private readonly List<ItemPlacementReservation> _reservations = new(8);

        public ExchangeRuntime(
            World world,
            ExchangeOperationRegistry operations,
            ExchangeScopedOperationStore scopedOperations,
            InventoryRuntimeService inventory,
            EffectRequestQueue effects,
            RelationshipRuntime relationships)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _operations = operations ?? throw new ArgumentNullException(nameof(operations));
            _scopedOperations = scopedOperations ?? throw new ArgumentNullException(nameof(scopedOperations));
            _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
            _effects = effects ?? throw new ArgumentNullException(nameof(effects));
            _relationships = relationships ?? throw new ArgumentNullException(nameof(relationships));
        }

        public ExchangeExecutionResult TryExecute(int operationId, in ExchangeExecutionContext context)
        {
            return TryExecute(new ExchangeOperationKey(operationId, context.Scope), in context);
        }

        public ExchangeExecutionResult TryExecute(ExchangeOperationKey key, in ExchangeExecutionContext context)
        {
            _consumed.Clear();
            _attributeCosts.Clear();
            _created.Clear();
            _moved.Clear();
            _reservations.Clear();

            if (!TryResolveOperation(in key, out ExchangeOperationDefinition operation))
            {
                return new ExchangeExecutionResult(ExchangeExecutionStatus.MissingOperation, key.OperationId);
            }

            ExchangeExecutionResult check = Validate(key.OperationId, operation, in context);
            if (!check.Succeeded)
            {
                return check;
            }

            for (int i = 0; i < operation.Inputs.Length; i++)
            {
                ExchangeInputDefinition input = operation.Inputs[i];
                if (!ApplyInput(input, in context))
                {
                    Rollback();
                    return new ExchangeExecutionResult(ExchangeExecutionStatus.InsufficientInput, key.OperationId, i);
                }
            }

            for (int i = 0; i < operation.Outputs.Length; i++)
            {
                ExchangeOutputDefinition output = operation.Outputs[i];
                if (output.Kind == ExchangeOutputKind.EffectRequest)
                {
                    continue;
                }

                if (!ApplyItemOutput(output, in context))
                {
                    Rollback();
                    return new ExchangeExecutionResult(ExchangeExecutionStatus.ExecutionFailed, key.OperationId, i);
                }
            }

            PublishEffects(operation, in context);
            _consumed.Clear();
            _attributeCosts.Clear();
            _created.Clear();
            _moved.Clear();
            _reservations.Clear();
            return new ExchangeExecutionResult(ExchangeExecutionStatus.Success, key.OperationId);
        }

        public ExchangeExecutionResult TryExecute(string operationId, in ExchangeExecutionContext context)
        {
            int id = _operations.GetId(operationId);
            return TryExecute(id, in context);
        }

        private bool TryResolveOperation(in ExchangeOperationKey key, out ExchangeOperationDefinition operation)
        {
            if (key.HasScope)
            {
                ScopeKey scope = key.Scope;
                if (_scopedOperations.TryGet(key.OperationId, in scope, out operation))
                {
                    return true;
                }
            }

            return _operations.TryGet(key.OperationId, out operation);
        }

        private ExchangeExecutionResult Validate(int operationId, ExchangeOperationDefinition operation, in ExchangeExecutionContext context)
        {
            _reservations.Clear();
            for (int i = 0; i < operation.Inputs.Length; i++)
            {
                ExchangeInputDefinition input = operation.Inputs[i];
                ExchangeExecutionResult inputCheck = ValidateInput(operationId, input, in context, i);
                if (!inputCheck.Succeeded)
                {
                    return inputCheck;
                }
            }

            ExchangeExecutionResult relationshipCheck = ValidateRelationships(operationId, operation, in context);
            if (!relationshipCheck.Succeeded)
            {
                return relationshipCheck;
            }

            for (int i = 0; i < operation.Outputs.Length; i++)
            {
                ExchangeOutputDefinition output = operation.Outputs[i];
                ExchangeExecutionResult result = ValidateOutput(operationId, output, in context, i);
                if (!result.Succeeded)
                {
                    return result;
                }
            }

            return new ExchangeExecutionResult(ExchangeExecutionStatus.Success, operationId);
        }

        private ExchangeExecutionResult ValidateInput(int operationId, ExchangeInputDefinition input, in ExchangeExecutionContext context, int index)
        {
            Entity actor = context.Resolve(input.Actor);
            if (!IsLiveActor(actor))
            {
                return new ExchangeExecutionResult(ExchangeExecutionStatus.MissingActor, operationId, index);
            }

            if (input.Kind == ExchangeInputKind.ItemStack)
            {
                if (_inventory.CountStackUnits(actor, input.ItemDefinitionId) < input.Quantity)
                {
                    return new ExchangeExecutionResult(ExchangeExecutionStatus.InsufficientInput, operationId, index);
                }

                return new ExchangeExecutionResult(ExchangeExecutionStatus.Success, operationId);
            }

            if (input.Kind == ExchangeInputKind.AttributeCost)
            {
                if (!_world.Has<AttributeBuffer>(actor))
                {
                    return new ExchangeExecutionResult(ExchangeExecutionStatus.InsufficientInput, operationId, index);
                }

                AttributeBuffer attributes = _world.Get<AttributeBuffer>(actor);
                if (attributes.GetCurrent(input.AttributeId) < input.Quantity)
                {
                    return new ExchangeExecutionResult(ExchangeExecutionStatus.InsufficientInput, operationId, index);
                }

                return new ExchangeExecutionResult(ExchangeExecutionStatus.Success, operationId);
            }

            return new ExchangeExecutionResult(ExchangeExecutionStatus.ExecutionFailed, operationId, index);
        }

        private ExchangeExecutionResult ValidateRelationships(int operationId, ExchangeOperationDefinition operation, in ExchangeExecutionContext context)
        {
            for (int i = 0; i < operation.RelationshipRequirements.Length; i++)
            {
                ExchangeRelationshipRequirement requirement = operation.RelationshipRequirements[i];
                if (!RelationshipPasses(requirement, in context))
                {
                    return new ExchangeExecutionResult(ExchangeExecutionStatus.RelationshipDenied, operationId, i);
                }
            }

            return new ExchangeExecutionResult(ExchangeExecutionStatus.Success, operationId);
        }

        private bool RelationshipPasses(ExchangeRelationshipRequirement requirement, in ExchangeExecutionContext context)
        {
            Entity source = context.Resolve(requirement.Source);
            Entity target = context.Resolve(requirement.Target);
            if (!_relationships.HasLink(source, target, requirement.TypeId))
            {
                return false;
            }

            if (requirement.MetricComparison != ExchangeRelationshipMetricComparison.None &&
                (!_relationships.TryGetMetric(source, target, requirement.TypeId, requirement.MetricId, out short metricValue) ||
                 !MetricPasses(metricValue, requirement)))
            {
                return false;
            }

            if (requirement.HasFlagRequirement &&
                (!_relationships.TryHasFlag(source, target, requirement.TypeId, requirement.FlagId, out bool enabled) ||
                 enabled != requirement.RequiredFlagValue))
            {
                return false;
            }

            return true;
        }

        private static bool MetricPasses(short value, ExchangeRelationshipRequirement requirement)
        {
            return requirement.MetricComparison switch
            {
                ExchangeRelationshipMetricComparison.GreaterOrEqual => value >= requirement.MinimumMetric,
                ExchangeRelationshipMetricComparison.LessOrEqual => value <= requirement.MaximumMetric,
                ExchangeRelationshipMetricComparison.RangeInclusive => value >= requirement.MinimumMetric && value <= requirement.MaximumMetric,
                _ => true
            };
        }

        private ExchangeExecutionResult ValidateOutput(int operationId, ExchangeOutputDefinition output, in ExchangeExecutionContext context, int index)
        {
            if (output.Kind == ExchangeOutputKind.EffectRequest)
            {
                return ValidateEffectOutput(operationId, output, in context, index);
            }

            Entity actor = context.Resolve(output.Actor);
            if (!IsLiveActor(actor))
            {
                return new ExchangeExecutionResult(ExchangeExecutionStatus.MissingActor, operationId, index);
            }

            if (!_inventory.TryFindOwnedContainer(actor, output.Purpose, out Entity container))
            {
                return new ExchangeExecutionResult(ExchangeExecutionStatus.MissingContainer, operationId, index);
            }

            if (output.Kind == ExchangeOutputKind.CreateItem)
            {
                if (!_inventory.CanAutoPlaceItemDefinition(container, output.ItemDefinitionId, _reservations, out ItemPlacementReservation reservation))
                {
                    return new ExchangeExecutionResult(ExchangeExecutionStatus.OutputBlocked, operationId, index);
                }

                _reservations.Add(reservation);
                return new ExchangeExecutionResult(ExchangeExecutionStatus.Success, operationId);
            }

            if (output.Kind == ExchangeOutputKind.MoveItem)
            {
                Entity fromActor = context.Resolve(output.FromActor);
                if (!IsLiveActor(fromActor))
                {
                    return new ExchangeExecutionResult(ExchangeExecutionStatus.MissingActor, operationId, index);
                }

                if (!_inventory.TryFindOwnedItem(fromActor, output.ItemDefinitionId, output.FromPurpose, out Entity item))
                {
                    return new ExchangeExecutionResult(ExchangeExecutionStatus.MissingSourceItem, operationId, index);
                }

                if (!_inventory.CanAutoPlaceItem(item, container, _reservations, out ItemPlacementReservation reservation))
                {
                    return new ExchangeExecutionResult(ExchangeExecutionStatus.OutputBlocked, operationId, index);
                }

                _reservations.Add(reservation);
                return new ExchangeExecutionResult(ExchangeExecutionStatus.Success, operationId);
            }

            return new ExchangeExecutionResult(ExchangeExecutionStatus.ExecutionFailed, operationId, index);
        }

        private ExchangeExecutionResult ValidateEffectOutput(int operationId, ExchangeOutputDefinition output, in ExchangeExecutionContext context, int index)
        {
            if (output.EffectTemplateId <= 0 ||
                !IsLiveActor(context.Resolve(output.EffectSource)) ||
                !IsLiveActor(context.Resolve(output.EffectTarget)))
            {
                return new ExchangeExecutionResult(ExchangeExecutionStatus.MissingActor, operationId, index);
            }

            return new ExchangeExecutionResult(ExchangeExecutionStatus.Success, operationId);
        }

        private bool ApplyItemOutput(ExchangeOutputDefinition output, in ExchangeExecutionContext context)
        {
            Entity actor = context.Resolve(output.Actor);
            if (!_inventory.TryFindOwnedContainer(actor, output.Purpose, out Entity container))
            {
                return false;
            }

            if (output.Kind == ExchangeOutputKind.CreateItem)
            {
                if (!_inventory.TryCreateAndPlaceItem(
                    container,
                    output.ItemDefinitionId,
                    output.Quantity,
                    output.Charges,
                    output.Durability,
                    out Entity item))
                {
                    return false;
                }

                _created.Add(new CreatedItemRecord(item));
                return true;
            }

            if (output.Kind == ExchangeOutputKind.MoveItem)
            {
                Entity fromActor = context.Resolve(output.FromActor);
                if (!_inventory.TryFindOwnedItem(fromActor, output.ItemDefinitionId, output.FromPurpose, out Entity item))
                {
                    return false;
                }

                bool hadLocation = _world.Has<ItemLocationCm>(item);
                ItemLocationCm previous = hadLocation ? _world.Get<ItemLocationCm>(item) : default;
                if (!_inventory.TryTransferItem(item, container))
                {
                    return false;
                }

                _moved.Add(new MovedItemRecord(item, hadLocation, previous));
                return true;
            }

            return false;
        }

        private bool ApplyInput(ExchangeInputDefinition input, in ExchangeExecutionContext context)
        {
            Entity actor = context.Resolve(input.Actor);
            if (input.Kind == ExchangeInputKind.ItemStack)
            {
                return _inventory.ConsumeStackUnits(actor, input.ItemDefinitionId, input.Quantity, _consumed);
            }

            if (input.Kind == ExchangeInputKind.AttributeCost)
            {
                if (!_world.Has<AttributeBuffer>(actor))
                {
                    return false;
                }

                ref AttributeBuffer attributes = ref _world.Get<AttributeBuffer>(actor);
                float previousValue = attributes.GetCurrent(input.AttributeId);
                if (previousValue < input.Quantity)
                {
                    return false;
                }

                _attributeCosts.Add(new AttributeCostRecord(actor, input.AttributeId, previousValue));
                attributes.SetCurrent(input.AttributeId, previousValue - input.Quantity);
                return true;
            }

            return false;
        }

        private void PublishEffects(ExchangeOperationDefinition operation, in ExchangeExecutionContext context)
        {
            for (int i = 0; i < operation.Outputs.Length; i++)
            {
                ExchangeOutputDefinition output = operation.Outputs[i];
                if (output.Kind != ExchangeOutputKind.EffectRequest)
                {
                    continue;
                }

                _effects.Publish(new EffectRequest
                {
                    Source = context.Resolve(output.EffectSource),
                    Target = context.Resolve(output.EffectTarget),
                    TargetContext = context.Resolve(output.EffectContext),
                    TemplateId = output.EffectTemplateId
                });
            }
        }

        private void Rollback()
        {
            for (int i = _created.Count - 1; i >= 0; i--)
            {
                Entity item = _created[i].Item;
                if (_world.IsAlive(item))
                {
                    _inventory.DestroyItemTree(item);
                }
            }

            for (int i = _moved.Count - 1; i >= 0; i--)
            {
                MovedItemRecord record = _moved[i];
                if (_world.IsAlive(record.Item))
                {
                    ItemLocationCm location = record.Location;
                    _inventory.RestoreItemLocation(record.Item, record.HadLocation, in location);
                }
            }

            _inventory.RestoreConsumedUnits(_consumed);
            for (int i = _attributeCosts.Count - 1; i >= 0; i--)
            {
                AttributeCostRecord record = _attributeCosts[i];
                if (_world.IsAlive(record.Actor) && _world.Has<AttributeBuffer>(record.Actor))
                {
                    ref AttributeBuffer attributes = ref _world.Get<AttributeBuffer>(record.Actor);
                    attributes.SetCurrent(record.AttributeId, record.PreviousValue);
                }
            }

            _consumed.Clear();
            _attributeCosts.Clear();
            _created.Clear();
            _moved.Clear();
        }

        private bool IsLiveActor(Entity entity)
        {
            return entity != Entity.Null && _world.IsAlive(entity);
        }

        private readonly struct AttributeCostRecord
        {
            public AttributeCostRecord(Entity actor, int attributeId, float previousValue)
            {
                Actor = actor;
                AttributeId = attributeId;
                PreviousValue = previousValue;
            }

            public Entity Actor { get; }

            public int AttributeId { get; }

            public float PreviousValue { get; }
        }

        private readonly struct CreatedItemRecord
        {
            public CreatedItemRecord(Entity item)
            {
                Item = item;
            }

            public Entity Item { get; }
        }

        private readonly struct MovedItemRecord
        {
            public MovedItemRecord(Entity item, bool hadLocation, ItemLocationCm location)
            {
                Item = item;
                HadLocation = hadLocation;
                Location = location;
            }

            public Entity Item { get; }

            public bool HadLocation { get; }

            public ItemLocationCm Location { get; }
        }
    }
}

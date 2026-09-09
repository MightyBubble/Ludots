using System;
using System.Collections.Generic;
using Arch.Core;

namespace Ludots.Core.Presentation.Presenters
{
    public sealed partial class PresenterEntityRuntime
    {
        private readonly Dictionary<ParamDependencyKey, ParamDependency> _paramDependencies = new();
        private readonly Dictionary<Entity, ParamDependency> _entityParamDependencies = new();

        public long ParamDependencyVisitCount { get; private set; }
        public int ParamDependencyCount => _paramDependencies.Count;

        private readonly record struct ParamDependencyKey(Entity Entity, int Key, ParamLane Lane);

        private sealed class ParamDependency
        {
            public required ParamDependencyKey Key;
            public ParamDependency? Next;
            public PresenterChildren Children;
            public bool Subscribed;
            public bool VisualConsumer;
            public uint AttachmentMask;
            public bool ActiveAttachment;
            public bool Active => VisualConsumer || ActiveAttachment || Children.Count != 0;
        }

        private void RegisterParamDependencies(Entity entity, PresenterDefinition definition)
        {
            RegisterVisualParamDependencies(entity, definition.StaticVisualFloatParamKeys, ParamLane.Float);
            RegisterVisualParamDependencies(entity, definition.StaticVisualIntParamKeys, ParamLane.Int);
            RegisterVisualParamDependencies(entity, definition.StaticVisualVectorParamKeys, ParamLane.Vector);
            RegisterVisualParamDependencies(entity, definition.MaterialSourceFloatParamKeys, ParamLane.Float);
            RegisterAttachmentParamDependencies(entity, definition.Behaviors);
            if (_world.Has<PresenterInstanceBehaviors>(entity))
                RegisterAttachmentParamDependencies(entity, _world.Get<PresenterInstanceBehaviors>(entity).Slots);
            RefreshParamDependencyActivation(entity);
        }

        private void RegisterVisualParamDependencies(Entity entity, ReadOnlySpan<int> keys, ParamLane lane)
        {
            foreach (int key in keys)
            {
                ParamDependency node = ReserveParamDependency(new(entity, key, lane));
                node.VisualConsumer = true;
                RefreshParamSubscription(node);
            }
        }

        private void RegisterAttachmentParamDependencies(Entity entity, ReadOnlySpan<BehaviorSlot> slots)
        {
            foreach (ref readonly BehaviorSlot slot in slots)
            {
                if (slot.Kind != BehaviorKind.Attachment || slot.Attachment.UpdatePolicy != AttachmentUpdatePolicy.Continuous)
                    continue;
                uint bit = 1u << slot.SlotIndex;
                ReserveParamDependency(new(entity, slot.Attachment.LocalPositionParamKey, ParamLane.Vector)).AttachmentMask |= bit;
                ReserveParamDependency(new(entity, slot.Attachment.LocalRotationParamKey, ParamLane.Vector)).AttachmentMask |= bit;
                ReserveParamDependency(new(entity, slot.Attachment.LocalScaleParamKey, ParamLane.Vector)).AttachmentMask |= bit;
            }
        }

        private ParamDependency ReserveParamDependency(ParamDependencyKey key)
        {
            if (_paramDependencies.TryGetValue(key, out ParamDependency? existing)) return existing;
            _entityParamDependencies.TryGetValue(key.Entity, out ParamDependency? first);
            var node = new ParamDependency { Key = key, Next = first };
            _paramDependencies.Add(key, node);
            _entityParamDependencies[key.Entity] = node;
            Entity parent = _world.Get<PresenterParent>(key.Entity).Parent;
            if (parent != Entity.Null)
                ReserveParamDependency(new(parent, key.Key, key.Lane));
            return node;
        }

        private void RefreshParamDependencyActivation(Entity entity)
        {
            if (!_entityParamDependencies.TryGetValue(entity, out ParamDependency? node)) return;
            uint mask = _world.Get<PresenterState>(entity).BehaviorActiveMask;
            for (; node != null; node = node.Next)
            {
                node.ActiveAttachment = (node.AttachmentMask & mask) != 0;
                RefreshParamSubscription(node);
            }
        }

        private void RefreshParamSubscription(ParamDependency node)
        {
            Entity parent = _world.Get<PresenterParent>(node.Key.Entity).Parent;
            bool subscribed = parent != Entity.Null && node.Active &&
                !HasLocalParam(node.Key.Entity, node.Key.Key, node.Key.Lane);
            if (node.Subscribed == subscribed) return;
            ParamDependency parentNode = _paramDependencies[new(parent, node.Key.Key, node.Key.Lane)];
            if (subscribed)
            {
                if (!parentNode.Children.Add(node.Key.Entity))
                    throw new InvalidOperationException($"PRESENTATION.PARAM.ERR.DependencyCapacity: entity={parent.Id}, key={node.Key.Key}, capacity={PresenterChildren.MAX_CHILDREN}.");
            }
            else if (!parentNode.Children.Remove(node.Key.Entity))
                throw new InvalidOperationException($"PRESENTATION.PARAM.ERR.DependencyMissing: entity={node.Key.Entity.Id}, key={node.Key.Key}.");
            node.Subscribed = subscribed;
            RefreshParamSubscription(parentNode);
        }

        private void RefreshParamSubscription(Entity entity, int key, ParamLane lane)
        {
            if (_paramDependencies.TryGetValue(new(entity, key, lane), out ParamDependency? node))
                RefreshParamSubscription(node);
        }

        private void NotifyParamConsumers(Entity entity, int key, ParamLane lane)
        {
            if (!_paramDependencies.TryGetValue(new(entity, key, lane), out ParamDependency? node)) return;
            for (int i = 0; i < node.Children.Count; i++)
            {
                Entity child = node.Children.Get(i);
                ParamDependency childNode = _paramDependencies[new(child, key, lane)];
                ParamDependencyVisitCount++;
                if (childNode.ActiveAttachment) RefreshAttachmentParameter(child, key, lane);
                if (childNode.VisualConsumer)
                {
                    ref PresenterState state = ref _world.Get<PresenterState>(child);
                    state.Version++;
                    MarkStaticDirtyIfVisualParamChanged(child, in state, key, lane);
                }
                NotifyParamConsumers(child, key, lane);
            }
        }

        private void ValidateAttachmentParamMutation(Entity entity, in PresenterParamMutation mutation)
        {
            if (_world.Has<PresenterAttachmentState>(entity) &&
                _world.Get<PresenterAttachmentState>(entity).Initialized && _definitions != null)
            {
                ref readonly PresenterState state = ref _world.Get<PresenterState>(entity);
                PresenterDefinition definition = _definitions.Get(state.DefId);
                if (TryGetActiveAttachment(entity, definition, state.BehaviorActiveMask, out AttachmentConfig config) &&
                    config.UpdatePolicy == AttachmentUpdatePolicy.Continuous &&
                    (config.LocalPositionParamKey == mutation.Key || config.LocalRotationParamKey == mutation.Key || config.LocalScaleParamKey == mutation.Key))
                {
                    PresenterAttachmentState attachment = _world.Get<PresenterAttachmentState>(entity);
                    PresenterAttachmentTransform.ResolveChangedParameter(_world, entity, in config, ref attachment, mutation.Key, in mutation);
                }
            }
            if (!_paramDependencies.TryGetValue(new(entity, mutation.Key, ParamLane.Vector), out ParamDependency? node)) return;
            for (int i = 0; i < node.Children.Count; i++)
                ValidateAttachmentParamMutation(node.Children.Get(i), in mutation);
        }

        private void ValidateAttachmentActivation(Entity entity, PresenterDefinition definition, uint nextMask)
        {
            if (!TryGetActiveAttachment(entity, definition, nextMask, out AttachmentConfig config)) return;
            if (config.Target == AttachmentTarget.Parent)
            {
                Entity parent = _world.Get<PresenterParent>(entity).Parent;
                if (parent == Entity.Null || !_world.IsAlive(parent))
                    throw new InvalidOperationException($"PRESENTATION.ATTACHMENT.ERR.TargetMissing: presenter={entity.Id}.");
            }
            PresenterAttachmentTransform.ValidateParameters(_world, entity, in config);
        }

        private void RemoveParamDependencies(Entity entity)
        {
            if (!_entityParamDependencies.Remove(entity, out ParamDependency? node)) return;
            for (; node != null; node = node.Next)
            {
                if (node.Children.Count != 0)
                    throw new InvalidOperationException($"PRESENTATION.PARAM.ERR.DependentsAlive: entity={entity.Id}, key={node.Key.Key}.");
                node.VisualConsumer = false;
                node.ActiveAttachment = false;
                RefreshParamSubscription(node);
                _paramDependencies.Remove(node.Key);
            }
        }
    }
}

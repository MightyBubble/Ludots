using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Arch.Core;

namespace Ludots.Core.Presentation.Presenters
{
    public sealed partial class PresenterEntityRuntime
    {
        private readonly Dictionary<ParamDependencyKey, int> _paramDependencies = new();
        private readonly Dictionary<Entity, int> _entityParamDependencies = new();
        private ParamDependencyKey[] _paramDependencyKeys = Array.Empty<ParamDependencyKey>();
        private ParamDependencyLinks[] _paramDependencyLinks = Array.Empty<ParamDependencyLinks>();
        private ParamDependencyConsumer[] _paramDependencyConsumers = Array.Empty<ParamDependencyConsumer>();
        private int _paramDependencyUsed;
        private int _paramDependencyFree;

        public long ParamDependencyVisitCount { get; private set; }
        public int ParamDependencyCount => _paramDependencies.Count;
        public long ParamDependencyStorageBytes =>
            (long)_paramDependencyKeys.Length * Unsafe.SizeOf<ParamDependencyKey>() +
            (long)_paramDependencyLinks.Length * Unsafe.SizeOf<ParamDependencyLinks>() +
            (long)_paramDependencyConsumers.Length * Unsafe.SizeOf<ParamDependencyConsumer>();

        private readonly record struct ParamDependencyKey(Entity Entity, int Key, ParamLane Lane);

        private struct ParamDependencyLinks
        {
            public int NextForEntity;
            public int Parent;
            public int FirstChild;
            public int LastChild;
            public int PreviousSibling;
            public int NextSibling;
            public int ChildCount;
        }

        private struct ParamDependencyConsumer
        {
            public uint AttachmentMask;
            public bool Subscribed;
            public bool Visual;
            public bool ActiveAttachment;
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
                int node = ReserveParamDependency(new(entity, key, lane));
                _paramDependencyConsumers[node].Visual = true;
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
                int position = ReserveParamDependency(new(entity, slot.Attachment.LocalPositionParamKey, ParamLane.Vector));
                int rotation = ReserveParamDependency(new(entity, slot.Attachment.LocalRotationParamKey, ParamLane.Vector));
                int scale = ReserveParamDependency(new(entity, slot.Attachment.LocalScaleParamKey, ParamLane.Vector));
                _paramDependencyConsumers[position].AttachmentMask |= bit;
                _paramDependencyConsumers[rotation].AttachmentMask |= bit;
                _paramDependencyConsumers[scale].AttachmentMask |= bit;
            }
        }

        private int ReserveParamDependency(ParamDependencyKey key)
        {
            if (_paramDependencies.TryGetValue(key, out int existing)) return existing;
            Entity parent = _world.Get<PresenterParent>(key.Entity).Parent;
            int parentNode = parent == Entity.Null ? 0 : ReserveParamDependency(new(parent, key.Key, key.Lane));
            int node;
            if (_paramDependencyFree != 0)
            {
                node = _paramDependencyFree;
                _paramDependencyFree = _paramDependencyLinks[node].NextForEntity;
            }
            else
            {
                node = checked(_paramDependencyUsed + 1);
                if (node >= _paramDependencyKeys.Length)
                {
                    int capacity = Math.Max(checked(node + 1), checked(_paramDependencyKeys.Length * 2));
                    Array.Resize(ref _paramDependencyKeys, capacity);
                    Array.Resize(ref _paramDependencyLinks, capacity);
                    Array.Resize(ref _paramDependencyConsumers, capacity);
                }
                _paramDependencyUsed = node;
            }
            _entityParamDependencies.TryGetValue(key.Entity, out int first);
            _paramDependencyKeys[node] = key;
            _paramDependencyLinks[node] = new ParamDependencyLinks { NextForEntity = first, Parent = parentNode };
            _paramDependencyConsumers[node] = default;
            _paramDependencies.Add(key, node);
            _entityParamDependencies[key.Entity] = node;
            return node;
        }

        private void RefreshParamDependencyActivation(Entity entity)
        {
            if (!_entityParamDependencies.TryGetValue(entity, out int node)) return;
            uint mask = _world.Get<PresenterState>(entity).BehaviorActiveMask;
            for (; node != 0; node = _paramDependencyLinks[node].NextForEntity)
            {
                ref ParamDependencyConsumer consumer = ref _paramDependencyConsumers[node];
                consumer.ActiveAttachment = (consumer.AttachmentMask & mask) != 0;
                RefreshParamSubscription(node);
            }
        }

        private void RefreshParamSubscription(int node)
        {
            ref readonly ParamDependencyKey key = ref _paramDependencyKeys[node];
            ref ParamDependencyLinks links = ref _paramDependencyLinks[node];
            ref ParamDependencyConsumer consumer = ref _paramDependencyConsumers[node];
            bool subscribed = links.Parent != 0 &&
                (consumer.Visual || consumer.ActiveAttachment || links.ChildCount != 0) &&
                !HasLocalParam(key.Entity, key.Key, key.Lane);
            if (consumer.Subscribed == subscribed) return;
            ref ParamDependencyLinks parent = ref _paramDependencyLinks[links.Parent];
            if (subscribed)
            {
                if (parent.ChildCount >= PresenterChildren.MAX_CHILDREN)
                    throw new InvalidOperationException($"PRESENTATION.PARAM.ERR.DependencyCapacity: entity={_paramDependencyKeys[links.Parent].Entity.Id}, key={key.Key}, capacity={PresenterChildren.MAX_CHILDREN}.");
                links.PreviousSibling = parent.LastChild;
                links.NextSibling = 0;
                if (parent.LastChild == 0) parent.FirstChild = node;
                else _paramDependencyLinks[parent.LastChild].NextSibling = node;
                parent.LastChild = node;
                parent.ChildCount++;
            }
            else
            {
                if (links.PreviousSibling == 0) parent.FirstChild = links.NextSibling;
                else _paramDependencyLinks[links.PreviousSibling].NextSibling = links.NextSibling;
                if (links.NextSibling == 0) parent.LastChild = links.PreviousSibling;
                else _paramDependencyLinks[links.NextSibling].PreviousSibling = links.PreviousSibling;
                links.PreviousSibling = 0;
                links.NextSibling = 0;
                parent.ChildCount--;
            }
            consumer.Subscribed = subscribed;
            RefreshParamSubscription(links.Parent);
        }

        private void RefreshParamSubscription(Entity entity, int key, ParamLane lane)
        {
            if (_paramDependencies.TryGetValue(new(entity, key, lane), out int node))
                RefreshParamSubscription(node);
        }

        private void NotifyParamConsumers(Entity entity, int key, ParamLane lane)
        {
            if (_paramDependencies.TryGetValue(new(entity, key, lane), out int node))
                NotifyParamConsumers(node);
        }

        private void NotifyParamConsumers(int node)
        {
            for (int child = _paramDependencyLinks[node].FirstChild; child != 0;
                 child = _paramDependencyLinks[child].NextSibling)
            {
                ref readonly ParamDependencyKey key = ref _paramDependencyKeys[child];
                ref readonly ParamDependencyConsumer consumer = ref _paramDependencyConsumers[child];
                ParamDependencyVisitCount++;
                if (consumer.ActiveAttachment) RefreshAttachmentParameter(key.Entity, key.Key, key.Lane);
                if (consumer.Visual)
                {
                    ref PresenterState state = ref _world.Get<PresenterState>(key.Entity);
                    state.Version++;
                    MarkStaticDirtyIfVisualParamChanged(key.Entity, in state, key.Key, key.Lane);
                }
                NotifyParamConsumers(child);
            }
        }

        private void ValidateAttachmentParamMutation(Entity entity, in PresenterParamMutation mutation)
        {
            ValidateLocalAttachmentParamMutation(entity, in mutation);
            if (_paramDependencies.TryGetValue(new(entity, mutation.Key, ParamLane.Vector), out int node))
                ValidateDescendantAttachmentParamMutation(node, in mutation);
        }

        private void ValidateDescendantAttachmentParamMutation(int node, in PresenterParamMutation mutation)
        {
            for (int child = _paramDependencyLinks[node].FirstChild; child != 0;
                 child = _paramDependencyLinks[child].NextSibling)
            {
                if (_paramDependencyConsumers[child].ActiveAttachment)
                    ValidateLocalAttachmentParamMutation(_paramDependencyKeys[child].Entity, in mutation);
                ValidateDescendantAttachmentParamMutation(child, in mutation);
            }
        }

        private void ValidateLocalAttachmentParamMutation(Entity entity, in PresenterParamMutation mutation)
        {
            if (!_world.Has<PresenterAttachmentState>(entity)) return;
            PresenterAttachmentState attachment = _world.Get<PresenterAttachmentState>(entity);
            ref readonly AttachmentConfig config = ref attachment.ActiveConfig;
            if (attachment.Initialized && attachment.Active && config.UpdatePolicy == AttachmentUpdatePolicy.Continuous &&
                (config.LocalPositionParamKey == mutation.Key || config.LocalRotationParamKey == mutation.Key || config.LocalScaleParamKey == mutation.Key))
                PresenterAttachmentTransform.ResolveChangedParameter(_world, entity, in config, ref attachment, mutation.Key, in mutation);
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
            if (!_entityParamDependencies.Remove(entity, out int node)) return;
            while (node != 0)
            {
                ref ParamDependencyLinks links = ref _paramDependencyLinks[node];
                ParamDependencyKey key = _paramDependencyKeys[node];
                if (links.ChildCount != 0)
                    throw new InvalidOperationException($"PRESENTATION.PARAM.ERR.DependentsAlive: entity={entity.Id}, key={key.Key}.");
                _paramDependencyConsumers[node].Visual = false;
                _paramDependencyConsumers[node].ActiveAttachment = false;
                RefreshParamSubscription(node);
                _paramDependencies.Remove(key);
                int next = links.NextForEntity;
                links = new ParamDependencyLinks { NextForEntity = _paramDependencyFree };
                _paramDependencyKeys[node] = default;
                _paramDependencyConsumers[node] = default;
                _paramDependencyFree = node;
                node = next;
            }
        }
    }
}

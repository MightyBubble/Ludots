using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Registry;

namespace Ludots.Core.Input.Selection
{
    public static class SelectionSetKeys
    {
        public const string Ambient = "selection.ambient";
    }

    public sealed class SelectionRuntimeConfig
    {
        public int MaxEntitiesPerSet { get; set; } = SelectionBuffer.CAPACITY;
        public int MaxSetsPerSelector { get; set; } = 8;
        public float ClickPickRadiusPixels { get; set; } = 20f;
        public float DragThresholdPixels { get; set; } = 8f;
    }

    public sealed class SelectionRuntime
    {
        private readonly World _world;
        private readonly SelectionRuntimeConfig _config;
        private readonly StringIntRegistry _setKeyRegistry;
        private readonly Dictionary<SelectionSetHandle, Entity> _namedSets = new();

        public SelectionRuntime(World world, SelectionRuntimeConfig config, StringIntRegistry setKeyRegistry)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _setKeyRegistry = setKeyRegistry ?? throw new ArgumentNullException(nameof(setKeyRegistry));
            _setKeyRegistry.Register(SelectionSetKeys.Ambient);
        }

        public SelectionRuntimeConfig Config => _config;
        public StringIntRegistry SetKeyRegistry => _setKeyRegistry;

        public int GetEffectiveMaxEntitiesPerSet()
        {
            int configured = _config.MaxEntitiesPerSet;
            if (configured <= 0)
            {
                return SelectionBuffer.CAPACITY;
            }

            return Math.Min(configured, SelectionBuffer.CAPACITY);
        }

        public bool TryGetSelectionBuffer(Entity selector, string setKey, out SelectionBuffer selection)
        {
            selection = default;
            if (!_world.IsAlive(selector))
            {
                return false;
            }

            if (IsAmbientSet(setKey))
            {
                if (!_world.Has<SelectionBuffer>(selector))
                {
                    return false;
                }

                selection = _world.Get<SelectionBuffer>(selector);
                return true;
            }

            if (!TryGetNamedSetEntity(selector, setKey, out var setEntity))
            {
                return false;
            }

            selection = _world.Get<SelectionBuffer>(setEntity);
            return true;
        }

        public bool TryGetSelectionEntity(Entity selector, string setKey, out Entity selectionEntity)
        {
            selectionEntity = default;
            if (!_world.IsAlive(selector))
            {
                return false;
            }

            if (IsAmbientSet(setKey))
            {
                if (!_world.Has<SelectionBuffer>(selector))
                {
                    return false;
                }

                selectionEntity = selector;
                return true;
            }

            return TryGetNamedSetEntity(selector, setKey, out selectionEntity);
        }

        public bool TryGetOrCreateSelectionEntity(Entity selector, string setKey, out Entity selectionEntity)
        {
            selectionEntity = default;
            if (!_world.IsAlive(selector))
            {
                return false;
            }

            if (IsAmbientSet(setKey))
            {
                EnsureAmbientComponents(selector);
                selectionEntity = selector;
                return true;
            }

            int setId = _setKeyRegistry.Register(setKey);
            var handle = new SelectionSetHandle(selector, setId);
            if (TryResolveCachedNamedSet(handle, out selectionEntity))
            {
                return true;
            }

            if (CountSets(selector) >= Math.Max(1, _config.MaxSetsPerSelector))
            {
                return false;
            }

            selectionEntity = _world.Create(
                new SelectionSetOwner { Value = selector },
                new SelectionSetId { Value = setId },
                default(SelectionBuffer));
            _namedSets[handle] = selectionEntity;
            return true;
        }

        public bool ReplaceSelection(Entity selector, string setKey, ReadOnlySpan<Entity> next)
        {
            if (!TryGetOrCreateSelectionEntity(selector, setKey, out var selectionEntity))
            {
                return false;
            }

            ref var selection = ref _world.Get<SelectionBuffer>(selectionEntity);
            selection.Clear();

            int limit = GetEffectiveMaxEntitiesPerSet();
            for (int i = 0; i < next.Length && selection.Count < limit; i++)
            {
                Entity entity = next[i];
                if (!_world.IsAlive(entity))
                {
                    continue;
                }

                selection.Add(entity);
            }

            _world.Set(selectionEntity, selection);
            return true;
        }

        public bool TryGetPrimary(Entity selector, string setKey, out Entity primary)
        {
            primary = default;
            if (!TryGetSelectionBuffer(selector, setKey, out var selection))
            {
                return false;
            }

            primary = selection.Primary;
            return _world.IsAlive(primary);
        }

        public int CopySelection(Entity selector, string setKey, Span<Entity> destination)
        {
            if (!TryGetSelectionBuffer(selector, setKey, out var selection))
            {
                return 0;
            }

            int count = Math.Min(selection.Count, destination.Length);
            int written = 0;
            for (int i = 0; i < count; i++)
            {
                Entity entity = selection.Get(i);
                if (!_world.IsAlive(entity))
                {
                    continue;
                }

                destination[written++] = entity;
            }

            return written;
        }

        public bool PruneDeadTargets(Entity selectionEntity)
        {
            if (!_world.IsAlive(selectionEntity) || !_world.Has<SelectionBuffer>(selectionEntity))
            {
                return false;
            }

            ref var selection = ref _world.Get<SelectionBuffer>(selectionEntity);
            if (selection.Count <= 0)
            {
                return false;
            }

            Span<Entity> alive = stackalloc Entity[SelectionBuffer.CAPACITY];
            int aliveCount = 0;
            bool changed = false;

            for (int i = 0; i < selection.Count; i++)
            {
                Entity entity = selection.Get(i);
                if (_world.IsAlive(entity))
                {
                    alive[aliveCount++] = entity;
                    continue;
                }

                changed = true;
            }

            if (!changed)
            {
                return false;
            }

            selection.Clear();
            for (int i = 0; i < aliveCount; i++)
            {
                selection.Add(alive[i]);
            }

            _world.Set(selectionEntity, selection);
            return true;
        }

        public bool IsAmbientSelectionEntity(Entity selector, Entity selectionEntity)
        {
            return selector == selectionEntity && _world.IsAlive(selector);
        }

        private void EnsureAmbientComponents(Entity selector)
        {
            if (!_world.Has<SelectionBuffer>(selector))
            {
                _world.Add(selector, default(SelectionBuffer));
            }

            if (!_world.Has<SelectionGroupBuffer>(selector))
            {
                _world.Add(selector, default(SelectionGroupBuffer));
            }
        }

        private int CountSets(Entity selector)
        {
            int count = _world.Has<SelectionBuffer>(selector) ? 1 : 0;
            foreach (var pair in _namedSets)
            {
                if (!pair.Key.Matches(selector))
                {
                    continue;
                }

                if (_world.IsAlive(pair.Value))
                {
                    count++;
                }
            }

            return count;
        }

        private bool TryGetNamedSetEntity(Entity selector, string setKey, out Entity setEntity)
        {
            setEntity = default;
            if (string.IsNullOrWhiteSpace(setKey) || IsAmbientSet(setKey))
            {
                return false;
            }

            if (!_setKeyRegistry.TryGetId(setKey, out int setId) || setId <= 0)
            {
                return false;
            }

            return TryResolveCachedNamedSet(new SelectionSetHandle(selector, setId), out setEntity);
        }

        private bool TryResolveCachedNamedSet(in SelectionSetHandle handle, out Entity setEntity)
        {
            setEntity = default;
            if (!_namedSets.TryGetValue(handle, out var cached))
            {
                return false;
            }

            if (!_world.IsAlive(cached) ||
                !_world.Has<SelectionSetOwner>(cached) ||
                !_world.Has<SelectionSetId>(cached))
            {
                _namedSets.Remove(handle);
                return false;
            }

            ref var owner = ref _world.Get<SelectionSetOwner>(cached);
            ref var setId = ref _world.Get<SelectionSetId>(cached);
            if (owner.Value != handle.ToEntity() || setId.Value != handle.SetId)
            {
                _namedSets.Remove(handle);
                return false;
            }

            setEntity = cached;
            return true;
        }

        private static bool IsAmbientSet(string? setKey)
        {
            return string.IsNullOrWhiteSpace(setKey) ||
                   string.Equals(setKey, SelectionSetKeys.Ambient, StringComparison.Ordinal);
        }

        private readonly struct SelectionSetHandle : IEquatable<SelectionSetHandle>
        {
            public SelectionSetHandle(Entity owner, int setId)
            {
                OwnerId = owner.Id;
                OwnerWorldId = owner.WorldId;
                OwnerVersion = owner.Version;
                SetId = setId;
            }

            public int OwnerId { get; }
            public int OwnerWorldId { get; }
            public int OwnerVersion { get; }
            public int SetId { get; }

            public Entity ToEntity()
            {
                return Ludots.Core.Gameplay.GAS.EntityUtil.Reconstruct(OwnerId, OwnerWorldId, OwnerVersion);
            }

            public bool Matches(Entity owner)
            {
                return OwnerId == owner.Id &&
                       OwnerWorldId == owner.WorldId &&
                       OwnerVersion == owner.Version;
            }

            public bool Equals(SelectionSetHandle other)
            {
                return OwnerId == other.OwnerId &&
                       OwnerWorldId == other.OwnerWorldId &&
                       OwnerVersion == other.OwnerVersion &&
                       SetId == other.SetId;
            }

            public override bool Equals(object? obj)
            {
                return obj is SelectionSetHandle other && Equals(other);
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(OwnerId, OwnerWorldId, OwnerVersion, SetId);
            }
        }
    }
}

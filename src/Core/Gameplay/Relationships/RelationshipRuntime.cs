using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Arch.Core;
using Ludots.Core.Association;
using Ludots.Core.Gameplay.Components;
using Arch.Relationships;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.Relationships.Config;

namespace Ludots.Core.Gameplay.Relationships
{
    public sealed class RelationshipRuntime
    {
        private static readonly QueryDescription RelationshipEntityQuery = new QueryDescription()
            .WithAll<RelationshipInstanceCm>();

        private readonly World _world;
        private readonly RelationshipTypeRegistry _types;
        private readonly RelationshipMetricRegistry _metrics;
        private readonly RelationshipFlagRegistry _flags;
        private readonly RelationshipBandRegistry _bands;
        private readonly RelationshipChangeBuffer _changes;
        private readonly RelationshipReverseIndex _reverseIndex;
        private Ludots.Core.Gameplay.GAS.TagOps? _tagOps;
        private OwnershipResolver? _identityOwnership;
        private int _identityOwnsTypeId = -1;
        private int _identityMemberOfTypeId = -1;
        private readonly Dictionary<RelationshipEntityKey, Entity> _entityIndex = new();
        private RelationshipTypeTemplate?[] _typeTemplates = Array.Empty<RelationshipTypeTemplate?>();

        public RelationshipRuntime(
            World world,
            RelationshipTypeRegistry types,
            RelationshipMetricRegistry metrics,
            RelationshipFlagRegistry flags,
            RelationshipBandRegistry bands,
            RelationshipChangeBuffer changes,
            RelationshipReverseIndex reverseIndex)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _types = types ?? throw new ArgumentNullException(nameof(types));
            _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
            _flags = flags ?? throw new ArgumentNullException(nameof(flags));
            _bands = bands ?? throw new ArgumentNullException(nameof(bands));
            _changes = changes ?? throw new ArgumentNullException(nameof(changes));
            _reverseIndex = reverseIndex ?? throw new ArgumentNullException(nameof(reverseIndex));
            _reverseIndex.RebuildFromWorld();
            RebuildEntityIndexFromWorld();
        }

        public void BindParticipantIdentityProjection(OwnershipResolver ownership, int ownsTypeId, int memberOfTypeId)
        {
            _identityOwnership = ownership ?? throw new ArgumentNullException(nameof(ownership));
            if (ownsTypeId < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(ownsTypeId));
            }

            if (memberOfTypeId < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(memberOfTypeId));
            }

            _identityOwnsTypeId = ownsTypeId;
            _identityMemberOfTypeId = memberOfTypeId;
        }

        private void ProjectParticipantIdentity(Entity source, Entity target, int typeId)
        {
            if (_identityOwnership == null)
            {
                return;
            }

            if (typeId == _identityOwnsTypeId)
            {
                ParticipantIdentityProjector.SyncPlayerOwner(_world, target, _identityOwnership);
                return;
            }

            if (typeId == _identityMemberOfTypeId && _world.IsAlive(target) && _world.Has<TeamIdentity>(target))
            {
                ParticipantIdentityProjector.SyncTeam(_world, source, this, _identityMemberOfTypeId);
            }
        }

        /// <summary>#1570：引擎装配期注入（tagOps 在 runtime 之后构造）；metric 写穿前必须已装。</summary>
        public void InstallTagOps(Ludots.Core.Gameplay.GAS.TagOps tagOps)
        {
            _tagOps = tagOps ?? throw new ArgumentNullException(nameof(tagOps));
        }

        public RelationshipTypeRegistry TypeRegistry => _types;
        public World World => _world;

        /// <summary>Reverse adjacency index backing incoming-edge queries.</summary>
        public RelationshipReverseIndex ReverseIndex => _reverseIndex;

        /// <summary>
        /// Bakes per-type birth templates into precompiled patches. Must run after the catalog's types
        /// are registered; materialization applies each patch once, on first entity creation only.
        /// </summary>
        public void InstallTypeTemplates(RelationshipCatalogConfig catalog)
        {
            ArgumentNullException.ThrowIfNull(catalog);

            var templates = new RelationshipTypeTemplate?[_types.Count];
            for (int i = 0; i < catalog.Types.Count; i++)
            {
                RelationshipTypeConfig type = catalog.Types[i];
                if (type.Template == null)
                {
                    continue;
                }

                int typeId = _types.GetId(type.Id);
                templates[typeId] = RelationshipTypeTemplate.Bake(_world, type.Id, type.Template, authoringContext: null);
            }

            _typeTemplates = templates;
        }

        public void RebuildEntityIndexFromWorld()
        {
            _entityIndex.Clear();
            _world.Query(in RelationshipEntityQuery, (Entity entity, ref RelationshipInstanceCm relationship) =>
            {
                ValidateMaterializedRelationship(entity, in relationship);
                RelationshipEntityKey key = new(relationship.Source, relationship.Target, relationship.TypeId);
                if (_entityIndex.TryGetValue(key, out Entity existing))
                {
                    throw new InvalidOperationException(
                        $"Duplicate relationship entity projection for {key.Source.Id}:{key.Source.WorldId}:{key.Source.Version} -> " +
                        $"{key.Target.Id}:{key.Target.WorldId}:{key.Target.Version} type {key.TypeId}: " +
                        $"{existing.Id}:{existing.WorldId}:{existing.Version} and {entity.Id}:{entity.WorldId}:{entity.Version}.");
                }

                _entityIndex[key] = entity;
            });
        }

        public bool TryResolveRelationshipEntity(Entity source, Entity target, int typeId, out Entity relationshipEntity)
        {
            relationshipEntity = Entity.Null;
            if (!IsAliveInRuntimeWorld(source) || !IsAliveInRuntimeWorld(target))
            {
                return false;
            }

            int validatedTypeId = ValidateTypeId(typeId);
            RelationshipEntityKey key = new(source, target, validatedTypeId);
            if (_entityIndex.TryGetValue(key, out Entity indexed) &&
                IsAliveInRuntimeWorld(indexed) &&
                _world.Has<RelationshipInstanceCm>(indexed) &&
                HasLink(source, target, validatedTypeId))
            {
                relationshipEntity = indexed;
                return true;
            }

            RebuildEntityIndexFromWorld();
            if (_entityIndex.TryGetValue(key, out indexed) &&
                IsAliveInRuntimeWorld(indexed) &&
                _world.Has<RelationshipInstanceCm>(indexed) &&
                HasLink(source, target, validatedTypeId))
            {
                relationshipEntity = indexed;
                return true;
            }

            return false;
        }

        public Entity MaterializeRelationshipEntity(Entity source, Entity target, int typeId)
        {
            EnsureAliveInRuntimeWorld(source, target);

            int validatedTypeId = ValidateTypeId(typeId);
            if (!HasLink(source, target, validatedTypeId))
            {
                throw new InvalidOperationException(
                    "RelationshipRuntime cannot materialize a relationship entity without an existing relationship edge. " +
                    DescribeEdgeState(source, target, validatedTypeId));
            }

            RelationshipEntityKey key = new(source, target, validatedTypeId);
            if (_entityIndex.TryGetValue(key, out Entity existing) &&
                IsAliveInRuntimeWorld(existing) &&
                _world.Has<RelationshipInstanceCm>(existing))
            {
                return existing;
            }

            Entity relationshipEntity = _world.Create(
                new RelationshipInstanceCm
                {
                    Source = source,
                    Target = target,
                    TypeId = validatedTypeId,
                    Revision = 1
                },
                default(AttributeBuffer),
                new GameplayTagContainer(),
                new TagCountContainer(),
                new DirtyFlags(),
                new ActiveEffectContainer());
            if ((uint)validatedTypeId < (uint)_typeTemplates.Length)
            {
                _typeTemplates[validatedTypeId]?.Apply(_world, relationshipEntity);
            }

            _entityIndex[key] = relationshipEntity;
            return relationshipEntity;
        }

        public bool HasLink(Entity source, Entity target)
        {
            return HasLink(source, target, RelationshipTypeRegistry.AnyTypeId);
        }

        public bool HasLink(Entity source, Entity target, int typeId)
        {
            if (!IsAliveInRuntimeWorld(source) || !IsAliveInRuntimeWorld(target))
            {
                return false;
            }

            if (!TryGetEdgeSet(source, target, out RelationshipEdgeSet set))
            {
                return false;
            }

            return typeId == RelationshipTypeRegistry.AnyTypeId
                ? set.Count > 0
                : set.HasType(ValidateTypeId(typeId));
        }

        public void EnsureLink(Entity source, Entity target, int typeId)
        {
            EnsureAliveInRuntimeWorld(source, target);

            int validatedTypeId = ValidateTypeId(typeId);
            bool hasExisting = TryGetEdgeSet(source, target, out RelationshipEdgeSet set);

            if (set.HasType(validatedTypeId))
            {
                MaterializeRelationshipEntity(source, target, validatedTypeId);
                return;
            }

            set.Set(validatedTypeId, RelationshipEdge.CreateDefault(_metrics));
            if (hasExisting)
            {
                _world.SetRelationship(source, target, set);
            }
            else
            {
                _world.AddRelationship(source, target, set);
            }

            _reverseIndex.OnLinkAdded(source, target, validatedTypeId);
            ProjectParticipantIdentity(source, target, validatedTypeId);
            Entity relationshipEntity = MaterializeRelationshipEntity(source, target, validatedTypeId);
            SeedMetricDefaults(relationshipEntity);
            _changes.TryAdd(new RelationshipChangeRecord(
                source, target, validatedTypeId, RelationshipChangeKind.LinkAdded,
                metricId: -1, oldValue: 0, newValue: 0, oldFlags: 0, newFlags: 0));
        }

        public void RemoveLink(Entity source, Entity target, int typeId)
        {
            if (!IsAliveInRuntimeWorld(source) || !IsAliveInRuntimeWorld(target))
            {
                return;
            }

            if (!TryGetEdgeSet(source, target, out RelationshipEdgeSet set))
            {
                return;
            }

            int validatedTypeId = ValidateTypeId(typeId);
            if (!set.Remove(validatedTypeId))
            {
                return;
            }

            RemoveMaterializedRelationshipEntity(source, target, validatedTypeId);

            if (set.Count == 0)
            {
                _world.RemoveRelationship<RelationshipEdgeSet>(source, target);
            }
            else
            {
                _world.SetRelationship(source, target, set);
            }

            _reverseIndex.OnLinkRemoved(source, target, validatedTypeId);
            ProjectParticipantIdentity(source, target, validatedTypeId);
            _changes.TryAdd(new RelationshipChangeRecord(
                source, target, validatedTypeId, RelationshipChangeKind.LinkRemoved,
                metricId: -1, oldValue: 0, newValue: 0, oldFlags: 0, newFlags: 0));
        }

        public bool TryGetMetric(Entity source, Entity target, int typeId, int metricId, out short value)
        {
            value = _metrics.Get(metricId).DefaultValue;
            if (!TryGetEdge(source, target, typeId, out RelationshipEdge edge))
            {
                return false;
            }

            value = edge.GetMetric(metricId);
            return true;
        }

        public bool TryGetEdge(Entity source, Entity target, int typeId, out RelationshipEdge edge)
        {
            return TryGetEdge(source, target, typeId, out edge, out _);
        }

        public short GetMetric(Entity source, Entity target, int typeId, int metricId)
        {
            return TryGetMetric(source, target, typeId, metricId, out short value)
                ? value
                : _metrics.Get(metricId).DefaultValue;
        }

        public short SetMetric(Entity source, Entity target, int typeId, int metricId, int value)
        {
            EnsureLink(source, target, typeId);
            _metrics.Get(metricId);

            int validatedTypeId = ValidateTypeId(typeId);
            RelationshipEdgeSet set = _world.GetRelationship<RelationshipEdgeSet>(source, target);
            RelationshipEdge edge = set.GetOrAdd(validatedTypeId, _metrics, out _);
            Entity relationshipEntity = MaterializeRelationshipEntity(source, target, validatedTypeId);
            bool resized = edge.EnsureMetricCapacity(_metrics);
            short oldValue = edge.GetMetric(metricId);
            short clamped = ClampToDefinition(metricId, value);
            if (oldValue == clamped)
            {
                if (resized)
                {
                    set.Set(validatedTypeId, edge);
                    _world.SetRelationship(source, target, set);
                }

                SyncBufferIfDrifted(relationshipEntity, metricId, clamped);
                return clamped;
            }

            BumpMaterializedRelationshipRevision(relationshipEntity);
            uint oldFlags = edge.Flags;

            // #1570 单轨化第一步：真相写穿边实体的 AttributeBuffer（AttributeMutationOps 带
            // 钳制/差分位/聚合/deferred trigger 全链）；RelationshipEdge 的 SoA 值降级为
            // 写后同步缓存，供热查询路径与既有图 op 读取。变更记录与事件面保持不变。
            if (_metrics.TryGetAttributeId(metricId, out int attributeId))
            {
                _tagOps ??= new Ludots.Core.Gameplay.GAS.TagOps(
                    new Ludots.Core.Gameplay.GAS.DirtyEntityQueue(1 << 22),
                    new Ludots.Core.Gameplay.GAS.TagRuleRegistry(),
                    new Ludots.Core.Gameplay.GAS.GasBudget(),
                    new Ludots.Core.Gameplay.GAS.AttributeAggregateDirtyRegistry());
                Ludots.Core.Gameplay.GAS.AttributeMutationOps.SetBase(_world, relationshipEntity, attributeId, clamped, _tagOps);
            }

            edge.SetMetric(metricId, clamped);
            edge.Version++;
            set.Set(validatedTypeId, edge);
            _world.SetRelationship(source, target, set);
            _changes.TryAdd(new RelationshipChangeRecord(source, target, validatedTypeId, RelationshipChangeKind.MetricChanged, metricId, oldValue, clamped, oldFlags, edge.Flags));
            return clamped;
        }

        public short AddMetric(Entity source, Entity target, int typeId, int metricId, int delta)
        {
            short current = GetMetric(source, target, typeId, metricId);
            return SetMetric(source, target, typeId, metricId, current + delta);
        }

        public bool HasFlag(Entity source, Entity target, int typeId, int flagId)
        {
            return TryHasFlag(source, target, typeId, flagId, out bool enabled) && enabled;
        }

        public bool TryHasFlag(Entity source, Entity target, int typeId, int flagId, out bool enabled)
        {
            enabled = false;
            if (!TryGetEdge(source, target, typeId, out RelationshipEdge edge))
            {
                return false;
            }

            enabled = (edge.Flags & _flags.GetMask(flagId)) != 0;
            return true;
        }

        public void SetFlag(Entity source, Entity target, int typeId, int flagId, bool enabled)
        {
            EnsureLink(source, target, typeId);

            int validatedTypeId = ValidateTypeId(typeId);
            RelationshipEdgeSet set = _world.GetRelationship<RelationshipEdgeSet>(source, target);
            RelationshipEdge edge = set.GetOrAdd(validatedTypeId, _metrics, out _);
            Entity relationshipEntity = MaterializeRelationshipEntity(source, target, validatedTypeId);
            bool resized = edge.EnsureMetricCapacity(_metrics);
            uint mask = _flags.GetMask(flagId);
            uint oldFlags = edge.Flags;
            uint newFlags = enabled ? oldFlags | mask : oldFlags & ~mask;
            if (oldFlags == newFlags)
            {
                if (resized)
                {
                    set.Set(validatedTypeId, edge);
                    _world.SetRelationship(source, target, set);
                }

                return;
            }

            BumpMaterializedRelationshipRevision(relationshipEntity);
            edge.Flags = newFlags;
            edge.Version++;
            set.Set(validatedTypeId, edge);
            _world.SetRelationship(source, target, set);
            _changes.TryAdd(new RelationshipChangeRecord(source, target, validatedTypeId, RelationshipChangeKind.FlagChanged, metricId: -1, oldValue: 0, newValue: 0, oldFlags, newFlags));
        }

        public int CaptureChangeCount() => _changes.Count;

        public void TruncateChanges(int count) => _changes.Truncate(count);

        public bool AreLinkEndpointsAlive(Entity source, Entity target)
            => IsAliveInRuntimeWorld(source) && IsAliveInRuntimeWorld(target);

        public void RequireLinkEndpoints(Entity source, Entity target)
            => EnsureAliveInRuntimeWorld(source, target);

        public int RequireRelationshipTypeId(int typeId) => ValidateTypeId(typeId);

        public short ClampMetric(int metricId, int value)
        {
            _metrics.Get(metricId);
            return ClampToDefinition(metricId, value);
        }

        public short MetricDefault(int metricId) => _metrics.Get(metricId).DefaultValue;

        public uint RequireFlagMask(int flagId) => _flags.GetMask(flagId);

        public RelationshipEdge CreateDefaultEdge() => RelationshipEdge.CreateDefault(_metrics);

        public bool TryCopyEdge(Entity source, Entity target, int typeId, out RelationshipEdge edge)
        {
            if (!TryGetEdge(source, target, typeId, out edge))
            {
                return false;
            }

            edge = edge.Clone();
            return true;
        }

        public int CopyLinkTypeIds(Entity source, Entity target, Span<int> destination)
        {
            if (!IsAliveInRuntimeWorld(source) || !IsAliveInRuntimeWorld(target) ||
                !TryGetEdgeSet(source, target, out RelationshipEdgeSet set))
            {
                return 0;
            }

            if (set.Count > destination.Length)
            {
                throw new InvalidOperationException(
                    $"Relationship pair {source.Id}->{target.Id} has {set.Count} types; the staging buffer holds {destination.Length}.");
            }

            for (int i = 0; i < set.Count; i++)
            {
                set.TryGetAt(i, out int typeId, out _);
                destination[i] = typeId;
            }

            return set.Count;
        }

        public void RestoreEdge(Entity source, Entity target, int typeId, bool existed, in RelationshipEdge edge)
        {
            bool now = HasLink(source, target, typeId);
            if (!existed)
            {
                if (now)
                {
                    RemoveLink(source, target, typeId);
                }

                return;
            }

            if (!now)
            {
                EnsureLink(source, target, typeId);
            }

            for (int metricId = 0; metricId < _metrics.Count; metricId++)
            {
                short want = edge.GetMetric(metricId);
                if (GetMetric(source, target, typeId, metricId) != want)
                {
                    SetMetric(source, target, typeId, metricId, want);
                }
            }

            if (!TryGetEdge(source, target, typeId, out RelationshipEdge current))
            {
                throw new InvalidOperationException(
                    $"Relationship edge {source.Id}->{target.Id} type {typeId} disappeared while restoring a committed effect write.");
            }

            if (current.Flags == edge.Flags)
            {
                return;
            }

            for (int flagId = 0; flagId < 32; flagId++)
            {
                uint mask = 1u << flagId;
                bool wantOn = (edge.Flags & mask) != 0;
                if (!TryGetEdge(source, target, typeId, out current))
                {
                    throw new InvalidOperationException(
                        $"Relationship edge {source.Id}->{target.Id} type {typeId} disappeared while restoring flags.");
                }

                bool haveOn = (current.Flags & mask) != 0;
                if (wantOn != haveOn)
                {
                    SetFlag(source, target, typeId, flagId, wantOn);
                }
            }
        }

        public bool TryGetHighestMetricTarget(Entity source, ReadOnlySpan<Entity> candidates, int typeId, int metricId, out Entity target, out short value)
        {
            target = Entity.Null;
            value = short.MinValue;
            bool found = false;

            for (int i = 0; i < candidates.Length; i++)
            {
                Entity candidate = candidates[i];
                if (!IsAliveInRuntimeWorld(candidate))
                {
                    continue;
                }

                if (!TryGetMetric(source, candidate, typeId, metricId, out short current))
                {
                    continue;
                }

                if (!found || current > value)
                {
                    target = candidate;
                    value = current;
                    found = true;
                }
            }

            return found;
        }

        public int CollectOutgoing(Entity source, Span<Entity> buffer)
        {
            return CollectOutgoing(source, RelationshipTypeRegistry.AnyTypeId, buffer);
        }

        public int CollectOutgoing(Entity source, int typeId, Span<Entity> buffer)
        {
            return CollectOutgoing(source, typeId, buffer, out _);
        }

        public int CollectOutgoing(Entity source, int typeId, Span<Entity> buffer, out int dropped)
        {
            dropped = 0;
            if (!IsAliveInRuntimeWorld(source) || !_world.Has<Relationship<RelationshipEdgeSet>>(source))
            {
                return 0;
            }

            int validatedTypeId = ValidateFilterTypeId(typeId);
            ref Relationship<RelationshipEdgeSet> relationships = ref _world.Get<Relationship<RelationshipEdgeSet>>(source);
            int count = 0;
            foreach ((Entity target, RelationshipEdgeSet set) in relationships)
            {
                if (!MatchesType(set, validatedTypeId))
                {
                    continue;
                }

                if (count < buffer.Length)
                {
                    buffer[count++] = target;
                }
                else
                {
                    dropped++;
                }
            }

            return count;
        }

        public int CollectIncoming(Entity target, Span<Entity> buffer)
        {
            return CollectIncoming(target, RelationshipTypeRegistry.AnyTypeId, buffer);
        }

        /// <summary>
        /// Collects live incoming sources straight from the reverse index. No per-source edge re-verification:
        /// <see cref="EnsureLink"/>/<see cref="RemoveLink"/> are the only edge mutation paths (M9 guardrail)
        /// and both notify the index, while entity death is covered by the index's lazy IsAlive reclamation.
        /// </summary>
        public int CollectIncoming(Entity target, int typeId, Span<Entity> buffer)
        {
            return CollectIncoming(target, typeId, buffer, out _);
        }

        public int CollectIncoming(Entity target, int typeId, Span<Entity> buffer, out int dropped)
        {
            dropped = 0;
            if (!IsAliveInRuntimeWorld(target))
            {
                return 0;
            }

            return _reverseIndex.CopyIncoming(target, ValidateFilterTypeId(typeId), buffer, out dropped);
        }

        public int CollectMutual(Entity first, Entity second, Span<Entity> buffer)
        {
            return CollectMutual(first, second, RelationshipTypeRegistry.AnyTypeId, buffer);
        }

        public int CollectMutual(Entity first, Entity second, int typeId, Span<Entity> buffer)
        {
            return CollectMutual(first, second, typeId, buffer, out _);
        }

        public int CollectMutual(Entity first, Entity second, int typeId, Span<Entity> buffer, out int dropped)
        {
            dropped = 0;
            if (!IsAliveInRuntimeWorld(first) || !IsAliveInRuntimeWorld(second) || !_world.Has<Relationship<RelationshipEdgeSet>>(first))
            {
                return 0;
            }

            int validatedTypeId = ValidateFilterTypeId(typeId);
            ref Relationship<RelationshipEdgeSet> relationships = ref _world.Get<Relationship<RelationshipEdgeSet>>(first);
            int count = 0;
            foreach ((Entity candidate, RelationshipEdgeSet set) in relationships)
            {
                if (!IsAliveInRuntimeWorld(candidate) || !MatchesType(set, validatedTypeId))
                {
                    continue;
                }

                if (!HasLink(candidate, second, validatedTypeId) || !HasLink(second, candidate, validatedTypeId))
                {
                    continue;
                }

                if (count < buffer.Length)
                {
                    buffer[count++] = candidate;
                }
                else
                {
                    dropped++;
                }
            }

            return count;
        }

        public int CollectBetweenPair(Entity source, Entity target, Span<Entity> buffer)
        {
            return CollectBetweenPair(source, target, RelationshipTypeRegistry.AnyTypeId, buffer);
        }

        public int CollectBetweenPair(Entity source, Entity target, int typeId, Span<Entity> buffer)
        {
            return CollectBetweenPair(source, target, typeId, buffer, out _);
        }

        public int CollectBetweenPair(Entity source, Entity target, int typeId, Span<Entity> buffer, out int dropped)
        {
            dropped = 0;
            if (!IsAliveInRuntimeWorld(source) || !IsAliveInRuntimeWorld(target))
            {
                return 0;
            }

            int validatedTypeId = ValidateFilterTypeId(typeId);
            int count = 0;
            if (HasLink(source, target, validatedTypeId))
            {
                if (count < buffer.Length)
                {
                    buffer[count++] = target;
                }
                else
                {
                    dropped++;
                }
            }

            if (HasLink(target, source, validatedTypeId))
            {
                if (count < buffer.Length)
                {
                    buffer[count++] = source;
                }
                else
                {
                    dropped++;
                }
            }

            return count;
        }

        private bool TryGetEdge(Entity source, Entity target, int typeId, out RelationshipEdge edge, out bool resized)
        {
            edge = default;
            resized = false;
            if (!IsAliveInRuntimeWorld(source) || !IsAliveInRuntimeWorld(target))
            {
                return false;
            }

            int validatedTypeId = ValidateTypeId(typeId);
            if (!TryGetEdgeSet(source, target, out RelationshipEdgeSet set))
            {
                return false;
            }

            if (!set.TryGet(validatedTypeId, out edge))
            {
                return false;
            }

            return true;
        }

        private bool TryGetEdgeSet(Entity source, Entity target, out RelationshipEdgeSet set)
        {
            set = default;
            ref Relationship<RelationshipEdgeSet> relationships = ref _world.TryGetRef<Relationship<RelationshipEdgeSet>>(source, out bool exists);
            return exists && relationships.TryGetValueNoAlloc(target, out set);
        }

        private void RemoveMaterializedRelationshipEntity(Entity source, Entity target, int typeId)
        {
            RelationshipEntityKey key = new(source, target, typeId);
            if (!_entityIndex.TryGetValue(key, out Entity entity))
            {
                RebuildEntityIndexFromWorld();
                if (!_entityIndex.TryGetValue(key, out entity))
                {
                    return;
                }
            }

            _entityIndex.Remove(key);
            if (IsAliveInRuntimeWorld(entity) && _world.Has<RelationshipInstanceCm>(entity))
            {
                _world.Destroy(entity);
            }
        }

        private void BumpMaterializedRelationshipRevision(Entity relationshipEntity)
        {
            if (!IsAliveInRuntimeWorld(relationshipEntity) || !_world.Has<RelationshipInstanceCm>(relationshipEntity))
            {
                return;
            }

            ref RelationshipInstanceCm relationship = ref _world.Get<RelationshipInstanceCm>(relationshipEntity);
            relationship.Revision++;
        }

        private void ValidateMaterializedRelationship(Entity entity, in RelationshipInstanceCm relationship)
        {
            if (relationship.TypeId < 0)
            {
                throw new InvalidOperationException(
                    $"Relationship entity {entity.Id}:{entity.WorldId}:{entity.Version} has invalid type id {relationship.TypeId}.");
            }

            if (!IsAliveInRuntimeWorld(relationship.Source) || !IsAliveInRuntimeWorld(relationship.Target))
            {
                throw new InvalidOperationException(
                    $"Relationship entity {entity.Id}:{entity.WorldId}:{entity.Version} references a missing source or target entity.");
            }

            if (!HasLink(relationship.Source, relationship.Target, relationship.TypeId))
            {
                throw new InvalidOperationException(
                    $"Relationship entity {entity.Id}:{entity.WorldId}:{entity.Version} has no matching relationship edge for type {relationship.TypeId}.");
            }
        }

        private int ValidateTypeId(int typeId)
        {
            _types.Get(typeId);
            return typeId;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsAliveInRuntimeWorld(Entity entity)
            => entity != Entity.Null && entity.WorldId == _world.Id && _world.IsAlive(entity);

        private void EnsureAliveInRuntimeWorld(Entity source, Entity target)
        {
            if (IsAliveInRuntimeWorld(source) && IsAliveInRuntimeWorld(target))
            {
                return;
            }

            throw new InvalidOperationException(
                $"RelationshipRuntime requires both source and target entities to be alive in World {_world.Id}; " +
                $"source={source.Id}:{source.WorldId}:{source.Version}, target={target.Id}:{target.WorldId}:{target.Version}.");
        }

        private string DescribeEdgeState(Entity source, Entity target, int typeId)
        {
            bool sourceHasRelationships = _world.Has<Relationship<RelationshipEdgeSet>>(source);
            if (!sourceHasRelationships)
            {
                return $"source={source.Id}:{source.WorldId}:{source.Version}, target={target.Id}:{target.WorldId}:{target.Version}, type={typeId}, sourceHasRelationships=false.";
            }

            ref Relationship<RelationshipEdgeSet> relationships = ref _world.Get<Relationship<RelationshipEdgeSet>>(source);
            bool hasTarget = relationships.TryGetValueNoAlloc(target, out RelationshipEdgeSet set);
            return $"source={source.Id}:{source.WorldId}:{source.Version}, target={target.Id}:{target.WorldId}:{target.Version}, type={typeId}, sourceHasRelationships=true, targetEdgeSet={hasTarget}, edgeTypeCount={(hasTarget ? set.Count : 0)}.";
        }

        private int ValidateFilterTypeId(int typeId)
        {
            if (typeId == RelationshipTypeRegistry.AnyTypeId)
            {
                return typeId;
            }

            return ValidateTypeId(typeId);
        }

        private static bool MatchesType(in RelationshipEdgeSet set, int typeId)
        {
            return typeId == RelationshipTypeRegistry.AnyTypeId
                ? set.Count > 0
                : set.HasType(typeId);
        }


        /// <summary>#1570：SoA 默认值在首建边时播种进边实体 AttributeBuffer（base 侧，raw 写
        /// 不触发 GAS 管线——出生播种不是变更）；早退路径防御性同步，消除真相/缓存漂移面。</summary>
        private void SeedMetricDefaults(Entity relationshipEntity)
        {
            if (!_world.Has<Ludots.Core.Gameplay.GAS.Components.AttributeBuffer>(relationshipEntity))
            {
                return;
            }

            for (int metricId = 0; metricId < _metrics.Count; metricId++)
            {
                if (!_metrics.TryGetAttributeId(metricId, out int attributeId))
                {
                    continue;
                }

                short defaultValue = _metrics.Get(metricId).DefaultValue;
                if (defaultValue != 0)
                {
                    _world.Get<Ludots.Core.Gameplay.GAS.Components.AttributeBuffer>(relationshipEntity).SetBase(attributeId, defaultValue);
                }
            }
        }

        private void SyncBufferIfDrifted(Entity relationshipEntity, int metricId, short value)
        {
            if (_metrics.TryGetAttributeId(metricId, out int attributeId) &&
                _world.Has<Ludots.Core.Gameplay.GAS.Components.AttributeBuffer>(relationshipEntity))
            {
                ref Ludots.Core.Gameplay.GAS.Components.AttributeBuffer buffer = ref _world.Get<Ludots.Core.Gameplay.GAS.Components.AttributeBuffer>(relationshipEntity);
                if (buffer.GetBase(attributeId) != value)
                {
                    buffer.SetBase(attributeId, value);
                }
            }
        }

        private short ClampToDefinition(int metricId, int value)
        {
            ref readonly RelationshipMetricDefinition definition = ref _metrics.Get(metricId);
            if (value < definition.MinValue)
            {
                return definition.MinValue;
            }

            if (value > definition.MaxValue)
            {
                return definition.MaxValue;
            }

            return (short)value;
        }

        private readonly struct RelationshipEntityKey : IEquatable<RelationshipEntityKey>
        {
            public RelationshipEntityKey(Entity source, Entity target, int typeId)
            {
                Source = source;
                Target = target;
                TypeId = typeId;
            }

            public Entity Source { get; }
            public Entity Target { get; }
            public int TypeId { get; }

            public bool Equals(RelationshipEntityKey other)
            {
                return Source.Equals(other.Source) &&
                       Target.Equals(other.Target) &&
                       TypeId == other.TypeId;
            }

            public override bool Equals(object? obj)
            {
                return obj is RelationshipEntityKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(Source, Target, TypeId);
            }
        }
    }
}

using System;
using Arch.Core;
using Arch.System;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Registry;

namespace Ludots.Core.Association
{
    public enum ScopeKind : byte
    {
        Self = 0,
        Explicit = 1,
        Named = 2
    }

    public enum RoleSlot : byte
    {
        None = 0,
        Source = 1,
        Target = 2,
        Context = 3,
        Actor = 4,
        Subject = 5,
        Viewer = 6,
        ScopeHost = 7,
        ScopeMembers = 8
    }

    public readonly struct ScopeKey : IEquatable<ScopeKey>
    {
        public ScopeKey(ScopeKind kind, int scopeKeyId = 0, Entity scopeHost = default)
        {
            Kind = kind;
            ScopeKeyId = scopeKeyId;
            ScopeHost = scopeHost;
        }

        public readonly ScopeKind Kind;
        public readonly int ScopeKeyId;
        public readonly Entity ScopeHost;

        public static ScopeKey Self => new(ScopeKind.Self);

        public static ScopeKey Explicit(Entity scopeHost = default) => new(ScopeKind.Explicit, 0, scopeHost);

        public static ScopeKey Named(int scopeKeyId, Entity scopeHost = default) => new(ScopeKind.Named, scopeKeyId, scopeHost);

        public bool Equals(ScopeKey other)
        {
            return Kind == other.Kind &&
                   ScopeKeyId == other.ScopeKeyId &&
                   ScopeHost == other.ScopeHost;
        }

        public override bool Equals(object? obj)
        {
            return obj is ScopeKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Kind, ScopeKeyId, ScopeHost);
        }

        public static bool operator ==(ScopeKey left, ScopeKey right) => left.Equals(right);

        public static bool operator !=(ScopeKey left, ScopeKey right) => !left.Equals(right);
    }

    public readonly struct RoleResolverContext
    {
        public RoleResolverContext(
            Entity source = default,
            Entity target = default,
            Entity context = default,
            Entity actor = default,
            Entity subject = default,
            Entity viewer = default,
            Entity explicitScopeHost = default)
        {
            Source = source;
            Target = target;
            Context = context;
            Actor = actor;
            Subject = subject;
            Viewer = viewer;
            ExplicitScopeHost = explicitScopeHost;
        }

        public readonly Entity Source;
        public readonly Entity Target;
        public readonly Entity Context;
        public readonly Entity Actor;
        public readonly Entity Subject;
        public readonly Entity Viewer;
        public readonly Entity ExplicitScopeHost;
    }

    public static class RoleResolver
    {
        public static Entity Resolve(RoleSlot slot, in RoleResolverContext context)
        {
            return slot switch
            {
                RoleSlot.Source => context.Source,
                RoleSlot.Target => context.Target,
                RoleSlot.Context => context.Context,
                RoleSlot.Actor => context.Actor,
                RoleSlot.Subject => context.Subject,
                RoleSlot.Viewer => context.Viewer,
                RoleSlot.ScopeHost => context.ExplicitScopeHost,
                _ => Entity.Null
            };
        }
    }

    public unsafe struct ScopeRefBuffer
    {
        public const int Capacity = 8;

        public fixed int ScopeKeyIds[Capacity];
        public fixed int EntityIds[Capacity];
        public fixed int EntityWorldIds[Capacity];
        public fixed int EntityVersions[Capacity];
        public int Count;

        public bool TryAdd(int scopeKeyId, Entity scopeHost)
            => TryAdd(scopeKeyId, scopeHost, out _);

        public bool TryAdd(int scopeKeyId, Entity scopeHost, out bool changed)
            => TryAdd(scopeKeyId, scopeHost, out changed, out _);

        public bool TryAdd(int scopeKeyId, Entity scopeHost, out bool changed, out Entity previousScopeHost)
        {
            changed = false;
            previousScopeHost = Entity.Null;
            if (scopeKeyId <= 0 || scopeHost == Entity.Null)
            {
                return false;
            }

            for (int i = 0; i < Count; i++)
            {
                if (ScopeKeyIds[i] != scopeKeyId)
                {
                    continue;
                }

                if (EntityIds[i] == scopeHost.Id &&
                    EntityWorldIds[i] == scopeHost.WorldId &&
                    EntityVersions[i] == scopeHost.Version)
                {
                    return true;
                }

                previousScopeHost = EntityUtil.Reconstruct(
                    EntityIds[i],
                    EntityWorldIds[i],
                    EntityVersions[i]);
                EntityIds[i] = scopeHost.Id;
                EntityWorldIds[i] = scopeHost.WorldId;
                EntityVersions[i] = scopeHost.Version;
                changed = true;
                return true;
            }

            if (Count >= Capacity)
            {
                return false;
            }

            ScopeKeyIds[Count] = scopeKeyId;
            EntityIds[Count] = scopeHost.Id;
            EntityWorldIds[Count] = scopeHost.WorldId;
            EntityVersions[Count] = scopeHost.Version;
            Count++;
            changed = true;
            return true;
        }

        public readonly bool TryGet(int scopeKeyId, out Entity scopeHost)
        {
            if (scopeKeyId <= 0)
            {
                scopeHost = Entity.Null;
                return false;
            }

            for (int i = 0; i < Count; i++)
            {
                if (ScopeKeyIds[i] == scopeKeyId)
                {
                    scopeHost = EntityUtil.Reconstruct(EntityIds[i], EntityWorldIds[i], EntityVersions[i]);
                    return true;
                }
            }

            scopeHost = Entity.Null;
            return false;
        }
    }

    public struct ScopeMembershipRevision
    {
        public uint Revision;
    }

    public struct ScopeMemberTag
    {
    }

    public unsafe struct ScopeHostAuthoring
    {
        public const int Capacity = 8;

        public fixed int ScopeNameKeyIds[Capacity];
        public fixed int HostKeyIds[Capacity];
        public int Count;

        public bool TryAdd(int scopeNameKeyId, int hostKeyId)
        {
            if (scopeNameKeyId <= 0 || hostKeyId <= 0 || Count >= Capacity)
            {
                return false;
            }

            for (int i = 0; i < Count; i++)
            {
                if (ScopeNameKeyIds[i] == scopeNameKeyId && HostKeyIds[i] == hostKeyId)
                {
                    return true;
                }
            }

            ScopeNameKeyIds[Count] = scopeNameKeyId;
            HostKeyIds[Count] = hostKeyId;
            Count++;
            return true;
        }
    }

    public unsafe struct ScopeBindingAuthoring
    {
        public const int Capacity = 8;

        public fixed int ScopeNameKeyIds[Capacity];
        public fixed int HostKeyIds[Capacity];
        public int Count;

        public bool TryAdd(int scopeNameKeyId, int hostKeyId)
        {
            if (scopeNameKeyId <= 0 || hostKeyId <= 0 || Count >= Capacity)
            {
                return false;
            }

            for (int i = 0; i < Count; i++)
            {
                if (ScopeNameKeyIds[i] == scopeNameKeyId && HostKeyIds[i] == hostKeyId)
                {
                    return true;
                }
            }

            ScopeNameKeyIds[Count] = scopeNameKeyId;
            HostKeyIds[Count] = hostKeyId;
            Count++;
            return true;
        }
    }

    public sealed class ScopeKeyRegistry
    {
        private readonly StringIntRegistry _registry = new(capacity: 32, startId: 1, invalidId: 0, comparer: StringComparer.OrdinalIgnoreCase);

        public int Register(string key) => _registry.Register(key);

        public int GetId(string key) => _registry.GetId(key);

        public bool TryGetId(string key, out int id) => _registry.TryGetId(key, out id);

        public string GetName(int id) => _registry.GetName(id);

        public void Freeze() => _registry.Freeze();
    }

    public sealed class ScopeResolver
    {
        private static readonly QueryDescription ScopeMemberQuery = new QueryDescription()
            .WithAll<ScopeRefBuffer>();

        private readonly World _world;

        public ScopeResolver(World world)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
        }

        public bool TryResolveHost(in ScopeKey scope, in RoleResolverContext context, out Entity scopeHost)
        {
            if (_world.IsAlive(scope.ScopeHost))
            {
                scopeHost = scope.ScopeHost;
                return true;
            }

            switch (scope.Kind)
            {
                case ScopeKind.Self:
                    return TryResolveFirstLive(
                        context.Subject,
                        context.Actor,
                        context.Viewer,
                        context.Source,
                        out scopeHost);
                case ScopeKind.Explicit:
                    scopeHost = context.ExplicitScopeHost;
                    return _world.IsAlive(scopeHost);
                case ScopeKind.Named:
                    if (scope.ScopeKeyId <= 0)
                    {
                        scopeHost = Entity.Null;
                        return false;
                    }

                    return TryResolveNamedHost(scope.ScopeKeyId, in context, out scopeHost);
                default:
                    scopeHost = Entity.Null;
                    return false;
            }
        }

        public unsafe int ResolveMembers(in ScopeKey scope, in RoleResolverContext context, Span<Entity> destination)
        {
            if (destination.IsEmpty ||
                !TryResolveHost(in scope, in context, out Entity scopeHost))
            {
                return 0;
            }

            if (scope.Kind != ScopeKind.Named)
            {
                destination[0] = scopeHost;
                return 1;
            }

            fixed (Entity* destinationPtr = destination)
            {
                var job = new CollectScopeMembersJob
                {
                    ScopeKeyId = scope.ScopeKeyId,
                    ScopeHost = scopeHost,
                    Destination = destinationPtr,
                    Capacity = destination.Length,
                    Count = 0
                };
                _world.InlineEntityQuery<CollectScopeMembersJob, ScopeRefBuffer>(in ScopeMemberQuery, ref job);
                return job.Count;
            }
        }

        public bool TryBindScope(Entity entity, int scopeKeyId, Entity scopeHost)
        {
            if (!_world.IsAlive(entity) || !_world.IsAlive(scopeHost) || scopeKeyId <= 0)
            {
                return false;
            }

            if (!_world.Has<ScopeRefBuffer>(entity))
            {
                return false;
            }

            ref var refs = ref _world.Get<ScopeRefBuffer>(entity);
            if (!refs.TryAdd(scopeKeyId, scopeHost, out bool changed, out Entity previousScopeHost))
            {
                return false;
            }

            if (!changed)
            {
                return true;
            }

            BumpMembershipRevision(scopeHost);
            if (_world.IsAlive(previousScopeHost) && previousScopeHost != scopeHost)
            {
                BumpMembershipRevision(previousScopeHost);
            }

            return true;
        }

        private bool TryResolveNamedHost(int scopeKeyId, in RoleResolverContext context, out Entity scopeHost)
        {
            if (TryResolveScopeHostFrom(context.Subject, scopeKeyId, out scopeHost) ||
                TryResolveScopeHostFrom(context.Actor, scopeKeyId, out scopeHost) ||
                TryResolveScopeHostFrom(context.Viewer, scopeKeyId, out scopeHost) ||
                TryResolveScopeHostFrom(context.Source, scopeKeyId, out scopeHost) ||
                TryResolveScopeHostFrom(context.Target, scopeKeyId, out scopeHost) ||
                TryResolveScopeHostFrom(context.Context, scopeKeyId, out scopeHost))
            {
                return true;
            }

            scopeHost = Entity.Null;
            return false;
        }

        private bool TryResolveScopeHostFrom(Entity entity, int scopeKeyId, out Entity scopeHost)
        {
            if (!_world.IsAlive(entity) || !_world.Has<ScopeRefBuffer>(entity))
            {
                scopeHost = Entity.Null;
                return false;
            }

            ref readonly var refs = ref _world.Get<ScopeRefBuffer>(entity);
            return refs.TryGet(scopeKeyId, out scopeHost) && _world.IsAlive(scopeHost);
        }

        private void BumpMembershipRevision(Entity scopeHost)
        {
            if (!_world.IsAlive(scopeHost) || !_world.Has<ScopeMembershipRevision>(scopeHost))
            {
                return;
            }

            ref var revision = ref _world.Get<ScopeMembershipRevision>(scopeHost);
            revision.Revision++;
        }

        private bool TryResolveFirstLive(Entity first, Entity second, Entity third, Entity fourth, out Entity entity)
        {
            if (_world.IsAlive(first))
            {
                entity = first;
                return true;
            }

            if (_world.IsAlive(second))
            {
                entity = second;
                return true;
            }

            if (_world.IsAlive(third))
            {
                entity = third;
                return true;
            }

            if (_world.IsAlive(fourth))
            {
                entity = fourth;
                return true;
            }

            entity = Entity.Null;
            return false;
        }

        private unsafe struct CollectScopeMembersJob : IForEachWithEntity<ScopeRefBuffer>
        {
            public int ScopeKeyId;
            public Entity ScopeHost;
            public Entity* Destination;
            public int Capacity;
            public int Count;

            public void Update(Entity entity, ref ScopeRefBuffer refs)
            {
                if (Count >= Capacity ||
                    !refs.TryGet(ScopeKeyId, out Entity host) ||
                    host != ScopeHost)
                {
                    return;
                }

                Destination[Count++] = entity;
            }
        }

    }
}

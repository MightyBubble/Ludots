using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Arch.Core;
using Arch.System;
using Ludots.Core.Gameplay.GAS.Components;

namespace Ludots.Core.Gameplay.GAS.Systems
{
    public sealed class BlackboardEntityRefLifetimeSystem : ISystem<float>
    {
        private static readonly ConditionalWeakTable<World, BlackboardEntityRefLifetimeSystem> Live = new();

        private readonly World _world;
        private readonly QueryDescription _owners = new QueryDescription().WithAll<BlackboardEntityBuffer>();
        private Entity[] _pending = new Entity[256];
        private int _pendingCount;

        public BlackboardEntityRefLifetimeSystem(World world)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            if (Live.TryGetValue(world, out _))
            {
                throw new InvalidOperationException(
                    $"BlackboardEntityRefLifetimeSystem is already registered for world {world.Id}.");
            }

            Live.Add(world, this);
            _world.SubscribeEntityDestroyed(OnEntityDestroyed);
        }

        public static void FlushWorld(World world)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            if (Live.TryGetValue(world, out BlackboardEntityRefLifetimeSystem? system))
            {
                system.Flush();
            }
        }

        public void Initialize() { }
        public void BeforeUpdate(in float dt) { }

        public void Update(in float dt) => Flush();

        public void AfterUpdate(in float dt) { }
        public void Dispose() { }

        public void Flush()
        {
            while (_pendingCount > 0)
            {
                int queued = _pendingCount;
                // Array.Sort(IComparer) builds a Comparison delegate on every call.
                MemoryExtensions.Sort(_pending.AsSpan(0, queued), HandleComparison);
                int unique = DedupeSorted(_pending, queued);
                var scrub = new Scrub(_pending, unique);
                _world.InlineEntityQuery<Scrub, BlackboardEntityBuffer>(in _owners, ref scrub);
                int grown = _pendingCount;
                if (grown == queued)
                {
                    _pendingCount = 0;
                    return;
                }

                int extra = grown - queued;
                Array.Copy(_pending, queued, _pending, 0, extra);
                _pendingCount = extra;
            }
        }

        private void OnEntityDestroyed(in Entity entity)
        {
            // Cleanup walks every blackboard once per flush. Scanning inside each destroy is
            // deaths × blackboards. The key stays: a missing key fails the next read.
            if (_pendingCount == _pending.Length)
            {
                Array.Resize(ref _pending, _pending.Length * 2);
            }

            _pending[_pendingCount++] = entity;
        }

        private static int DedupeSorted(Entity[] items, int count)
        {
            if (count <= 1)
            {
                return count;
            }

            int write = 1;
            for (int i = 1; i < count; i++)
            {
                if (items[i] != items[write - 1])
                {
                    items[write++] = items[i];
                }
            }

            return write;
        }

        private readonly struct Scrub : IForEachWithEntity<BlackboardEntityBuffer>
        {
            private readonly Entity[] _dead;
            private readonly int _count;

            public Scrub(Entity[] dead, int count)
            {
                _dead = dead;
                _count = count;
            }

            public void Update(Entity entity, ref BlackboardEntityBuffer buffer) => Clear(_dead, _count, ref buffer);

            private static unsafe void Clear(Entity[] dead, int deadCount, ref BlackboardEntityBuffer buffer)
            {
                int count = buffer.Count;
                for (int i = 0; i < count; i++)
                {
                    Entity slot = EntityUtil.Reconstruct(buffer.EntityIds[i], buffer.WorldIds[i], buffer.Versions[i]);
                    if (!Contains(dead, deadCount, slot))
                    {
                        continue;
                    }

                    buffer.EntityIds[i] = Entity.Null.Id;
                    buffer.WorldIds[i] = Entity.Null.WorldId;
                    buffer.Versions[i] = Entity.Null.Version;
                }
            }

            private static bool Contains(Entity[] sorted, int count, Entity candidate)
            {
                int lo = 0;
                int hi = count - 1;
                while (lo <= hi)
                {
                    int mid = lo + ((hi - lo) >> 1);
                    int cmp = EntityHandleOrder.Instance.Compare(sorted[mid], candidate);
                    if (cmp == 0)
                    {
                        return true;
                    }

                    if (cmp < 0)
                    {
                        lo = mid + 1;
                    }
                    else
                    {
                        hi = mid - 1;
                    }
                }

                return false;
            }
        }

        // Cached once. A fresh Comparison inside Sort would allocate on every flush.
        private static readonly Comparison<Entity> HandleComparison = CompareHandles;

        private static int CompareHandles(Entity x, Entity y) => EntityHandleOrder.Instance.Compare(x, y);

        // Id, then world, then version. Entity.CompareTo is not a total order once versions differ.
        private sealed class EntityHandleOrder : IComparer<Entity>
        {
            public static readonly EntityHandleOrder Instance = new();

            public int Compare(Entity x, Entity y)
            {
                int id = x.Id.CompareTo(y.Id);
                if (id != 0)
                {
                    return id;
                }

                int world = x.WorldId.CompareTo(y.WorldId);
                if (world != 0)
                {
                    return world;
                }

                return x.Version.CompareTo(y.Version);
            }
        }
    }
}

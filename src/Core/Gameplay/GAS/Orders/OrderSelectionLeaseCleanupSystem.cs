using System.Collections.Generic;
using Arch.Core;
using Arch.System;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Input.Selection;

namespace Ludots.Core.Gameplay.GAS.Orders
{
    public sealed class OrderSelectionLeaseCleanupSystem : ISystem<float>
    {
        private static readonly QueryDescription OrderBufferQuery = new QueryDescription().WithAll<OrderBuffer>();
        private static readonly QueryDescription LeaseQuery = new QueryDescription().WithAll<SelectionLeaseOwnerTag, SelectionLeaseContainer>();

        private readonly World _world;
        private readonly OrderQueue _orders;
        private readonly HashSet<Entity> _liveContainers = new();
        private readonly List<Entity> _staleLeaseOwners = new();

        public OrderSelectionLeaseCleanupSystem(World world, OrderQueue orders)
        {
            _world = world;
            _orders = orders;
        }

        public void Initialize()
        {
        }

        public void BeforeUpdate(in float dt)
        {
        }

        public void Update(in float dt)
        {
            _liveContainers.Clear();
            _staleLeaseOwners.Clear();

            _orders.CollectSelectionContainers(_liveContainers);
            CollectOrderBufferContainers();

            _world.Query(in LeaseQuery, (Entity owner, ref SelectionLeaseOwnerTag _, ref SelectionLeaseContainer lease) =>
            {
                if (lease.Value == Entity.Null ||
                    !_world.IsAlive(lease.Value) ||
                    !_liveContainers.Contains(lease.Value))
                {
                    _staleLeaseOwners.Add(owner);
                }
            });

            for (int i = 0; i < _staleLeaseOwners.Count; i++)
            {
                Entity owner = _staleLeaseOwners[i];
                if (_world.IsAlive(owner))
                {
                    _world.Destroy(owner);
                }
            }
        }

        public void AfterUpdate(in float dt)
        {
        }

        public void Dispose()
        {
        }

        private void CollectOrderBufferContainers()
        {
            _world.Query(in OrderBufferQuery, (Entity _, ref OrderBuffer orders) =>
            {
                AddContainer(orders.ActiveOrder.Order.Args.Selection.Container, orders.HasActive);
                AddContainer(orders.PendingOrder.Order.Args.Selection.Container, orders.HasPending);

                for (int i = 0; i < orders.QueuedCount; i++)
                {
                    AddContainer(orders.GetQueued(i).Order.Args.Selection.Container, include: true);
                }
            });
        }

        private void AddContainer(Entity container, bool include)
        {
            if (include && container != Entity.Null)
            {
                _liveContainers.Add(container);
            }
        }
    }
}

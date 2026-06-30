using System.Numerics;
using Arch.Core;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Map.Hex;

namespace Ludots.Core.Gameplay.GAS.Orders
{
    public readonly struct RallyTargetSnapshot
    {
        public RallyTargetSnapshot(
            RallyTargetKind kind,
            Vector3 worldPositionCm,
            Entity targetEntity,
            int hexQ,
            int hexR)
        {
            Kind = kind;
            WorldPositionCm = worldPositionCm;
            TargetEntity = targetEntity;
            HexQ = hexQ;
            HexR = hexR;
        }

        public RallyTargetKind Kind { get; }
        public Vector3 WorldPositionCm { get; }
        public Entity TargetEntity { get; }
        public int HexQ { get; }
        public int HexR { get; }

        public bool HasTarget => Kind != RallyTargetKind.None;
    }

    public static class RallyBlackboardOps
    {
        public static bool TryRead(World world, Entity producer, out RallyTargetSnapshot snapshot)
        {
            snapshot = default;
            if (!world.IsAlive(producer))
            {
                return false;
            }

            if (!world.TryGet(producer, out BlackboardIntBuffer ints) ||
                !ints.TryGet(OrderBlackboardKeys.Rally_TargetKind, out int kindValue))
            {
                return false;
            }

            var kind = (RallyTargetKind)kindValue;
            if (kind == RallyTargetKind.None)
            {
                return false;
            }

            Vector3 worldPositionCm = default;
            if (world.TryGet(producer, out BlackboardSpatialBuffer spatial) &&
                spatial.TryGetPoint(OrderBlackboardKeys.Rally_TargetPosition, out worldPositionCm))
            {
            }

            Entity targetEntity = Entity.Null;
            if (world.TryGet(producer, out BlackboardEntityBuffer entities) &&
                entities.TryGet(OrderBlackboardKeys.Rally_TargetEntity, out targetEntity))
            {
            }

            ints.TryGet(OrderBlackboardKeys.Rally_HexQ, out int hexQ);
            ints.TryGet(OrderBlackboardKeys.Rally_HexR, out int hexR);

            snapshot = new RallyTargetSnapshot(kind, worldPositionCm, targetEntity, hexQ, hexR);
            return true;
        }

        public static void Clear(World world, Entity producer)
        {
            if (!world.IsAlive(producer))
            {
                return;
            }

            if (world.TryGet(producer, out BlackboardIntBuffer ints))
            {
                ints.Remove(OrderBlackboardKeys.Rally_TargetKind);
                ints.Remove(OrderBlackboardKeys.Rally_HexQ);
                ints.Remove(OrderBlackboardKeys.Rally_HexR);
            }

            if (world.TryGet(producer, out BlackboardSpatialBuffer spatial))
            {
                spatial.ClearPoints(OrderBlackboardKeys.Rally_TargetPosition);
            }

            if (world.TryGet(producer, out BlackboardEntityBuffer entities))
            {
                entities.Remove(OrderBlackboardKeys.Rally_TargetEntity);
            }
        }

        public static void CommitFromOrder(World world, Entity producer, in Order order)
        {
            if (!world.IsAlive(producer))
            {
                return;
            }

            Clear(world, producer);

            if (order.Target != Entity.Null && world.IsAlive(order.Target))
            {
                SetEntity(world, producer, order.Target);
                return;
            }

            if (order.Args.Spatial.Kind == OrderSpatialKind.Hex)
            {
                SetHex(world, producer, order.Args.Spatial.A0, order.Args.Spatial.A1);
                return;
            }

            if (order.Args.Spatial.Mode == OrderCollectionMode.Single)
            {
                SetPoint(world, producer, order.Args.Spatial.WorldCm);
            }
        }

        public static void SetPoint(World world, Entity producer, Vector3 worldPositionCm)
        {
            EnsureBlackboardBuffers(world, producer);
            ref var ints = ref world.Get<BlackboardIntBuffer>(producer);
            ref var spatial = ref world.Get<BlackboardSpatialBuffer>(producer);
            ref var entities = ref world.Get<BlackboardEntityBuffer>(producer);

            ints.Set(OrderBlackboardKeys.Rally_TargetKind, (int)RallyTargetKind.Point);
            ints.Remove(OrderBlackboardKeys.Rally_HexQ);
            ints.Remove(OrderBlackboardKeys.Rally_HexR);
            entities.Remove(OrderBlackboardKeys.Rally_TargetEntity);
            spatial.ClearPoints(OrderBlackboardKeys.Rally_TargetPosition);
            spatial.SetPoint(OrderBlackboardKeys.Rally_TargetPosition, worldPositionCm);
        }

        public static void SetHex(World world, Entity producer, int hexQ, int hexR)
        {
            EnsureBlackboardBuffers(world, producer);
            ref var ints = ref world.Get<BlackboardIntBuffer>(producer);
            ref var spatial = ref world.Get<BlackboardSpatialBuffer>(producer);
            ref var entities = ref world.Get<BlackboardEntityBuffer>(producer);

            Vector3 worldPositionCm = new HexCoordinates(hexQ, hexR).ToWorldPositionCm();

            ints.Set(OrderBlackboardKeys.Rally_TargetKind, (int)RallyTargetKind.HexCell);
            ints.Set(OrderBlackboardKeys.Rally_HexQ, hexQ);
            ints.Set(OrderBlackboardKeys.Rally_HexR, hexR);
            entities.Remove(OrderBlackboardKeys.Rally_TargetEntity);
            spatial.ClearPoints(OrderBlackboardKeys.Rally_TargetPosition);
            spatial.SetPoint(OrderBlackboardKeys.Rally_TargetPosition, worldPositionCm);
        }

        public static void SetEntity(World world, Entity producer, Entity targetEntity)
        {
            EnsureBlackboardBuffers(world, producer);
            ref var ints = ref world.Get<BlackboardIntBuffer>(producer);
            ref var spatial = ref world.Get<BlackboardSpatialBuffer>(producer);
            ref var entities = ref world.Get<BlackboardEntityBuffer>(producer);

            ints.Set(OrderBlackboardKeys.Rally_TargetKind, (int)RallyTargetKind.Entity);
            ints.Remove(OrderBlackboardKeys.Rally_HexQ);
            ints.Remove(OrderBlackboardKeys.Rally_HexR);
            entities.Set(OrderBlackboardKeys.Rally_TargetEntity, targetEntity);
            spatial.ClearPoints(OrderBlackboardKeys.Rally_TargetPosition);
        }

        public static bool TryResolveWorldPositionCm(in RallyTargetSnapshot rally, out Vector3 worldPositionCm)
        {
            switch (rally.Kind)
            {
                case RallyTargetKind.Point:
                case RallyTargetKind.HexCell:
                    worldPositionCm = rally.WorldPositionCm;
                    return true;
                case RallyTargetKind.Entity:
                    worldPositionCm = default;
                    return false;
                default:
                    worldPositionCm = default;
                    return false;
            }
        }

        private static void EnsureBlackboardBuffers(World world, Entity producer)
        {
            if (!world.Has<BlackboardIntBuffer>(producer))
            {
                world.Add(producer, new BlackboardIntBuffer());
            }

            if (!world.Has<BlackboardSpatialBuffer>(producer))
            {
                world.Add(producer, new BlackboardSpatialBuffer());
            }

            if (!world.Has<BlackboardEntityBuffer>(producer))
            {
                world.Add(producer, new BlackboardEntityBuffer());
            }
        }
    }
}

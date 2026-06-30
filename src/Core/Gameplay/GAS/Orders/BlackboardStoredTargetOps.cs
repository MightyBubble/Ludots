using System.Numerics;
using Arch.Core;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Map.Hex;

namespace Ludots.Core.Gameplay.GAS.Orders
{
    public readonly struct BlackboardStoredTargetSnapshot
    {
        public BlackboardStoredTargetSnapshot(
            BlackboardStoredTargetKind kind,
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

        public BlackboardStoredTargetKind Kind { get; }
        public Vector3 WorldPositionCm { get; }
        public Entity TargetEntity { get; }
        public int HexQ { get; }
        public int HexR { get; }

        public bool HasTarget => Kind != BlackboardStoredTargetKind.None;
    }

    public static class BlackboardStoredTargetOps
    {
        public static bool TryRead(
            World world,
            Entity host,
            in BlackboardStoredTargetKeys keys,
            out BlackboardStoredTargetSnapshot snapshot)
        {
            snapshot = default;
            if (!world.IsAlive(host) || !keys.IsConfigured)
            {
                return false;
            }

            if (!world.TryGet(host, out BlackboardIntBuffer ints) ||
                !ints.TryGet(keys.TargetKindKey, out int kindValue))
            {
                return false;
            }

            var kind = (BlackboardStoredTargetKind)kindValue;
            if (kind == BlackboardStoredTargetKind.None)
            {
                return false;
            }

            Vector3 worldPositionCm = default;
            bool hasWorldPosition = world.TryGet(host, out BlackboardSpatialBuffer spatial) &&
                                    spatial.TryGetPoint(keys.TargetPositionKey, out worldPositionCm);

            Entity targetEntity = Entity.Null;
            bool hasTargetEntity = world.TryGet(host, out BlackboardEntityBuffer entities) &&
                                   entities.TryGet(keys.TargetEntityKey, out targetEntity) &&
                                   targetEntity != Entity.Null &&
                                   world.IsAlive(targetEntity);

            int hexQ = 0;
            int hexR = 0;
            bool hasHexCell = ints.TryGet(keys.HexQKey, out hexQ) && ints.TryGet(keys.HexRKey, out hexR);

            switch (kind)
            {
                case BlackboardStoredTargetKind.Entity:
                    if (!hasTargetEntity)
                    {
                        return false;
                    }
                    break;
                case BlackboardStoredTargetKind.Point:
                    if (!hasWorldPosition)
                    {
                        return false;
                    }
                    break;
                case BlackboardStoredTargetKind.HexCell:
                    if (!hasHexCell)
                    {
                        return false;
                    }
                    if (!hasWorldPosition)
                    {
                        worldPositionCm = new HexCoordinates(hexQ, hexR).ToWorldPositionCm();
                    }
                    break;
                default:
                    return false;
            }

            snapshot = new BlackboardStoredTargetSnapshot(kind, worldPositionCm, targetEntity, hexQ, hexR);
            return true;
        }

        public static void Clear(World world, Entity host, in BlackboardStoredTargetKeys keys)
        {
            if (!world.IsAlive(host) || !keys.IsConfigured)
            {
                return;
            }

            if (world.TryGet(host, out BlackboardIntBuffer ints))
            {
                ints.Remove(keys.TargetKindKey);
                ints.Remove(keys.HexQKey);
                ints.Remove(keys.HexRKey);
            }

            if (world.TryGet(host, out BlackboardSpatialBuffer spatial))
            {
                spatial.ClearPoints(keys.TargetPositionKey);
            }

            if (world.TryGet(host, out BlackboardEntityBuffer entities))
            {
                entities.Remove(keys.TargetEntityKey);
            }
        }

        public static void CommitFromOrder(
            World world,
            Entity host,
            in Order order,
            in BlackboardStoredTargetKeys keys)
        {
            if (!world.IsAlive(host) || !keys.IsConfigured)
            {
                return;
            }

            Clear(world, host, in keys);

            if (order.Target != Entity.Null && world.IsAlive(order.Target))
            {
                SetEntity(world, host, order.Target, in keys);
                return;
            }

            if (order.Args.Spatial.Kind == OrderSpatialKind.Hex)
            {
                SetHex(world, host, order.Args.Spatial.A0, order.Args.Spatial.A1, in keys);
                return;
            }

            if (order.Args.Spatial.Mode == OrderCollectionMode.Single)
            {
                SetPoint(world, host, order.Args.Spatial.WorldCm, in keys);
            }
        }

        public static void SetPoint(
            World world,
            Entity host,
            Vector3 worldPositionCm,
            in BlackboardStoredTargetKeys keys)
        {
            EnsureBlackboardBuffers(world, host);
            ref var ints = ref world.Get<BlackboardIntBuffer>(host);
            ref var spatial = ref world.Get<BlackboardSpatialBuffer>(host);
            ref var entities = ref world.Get<BlackboardEntityBuffer>(host);

            ints.Set(keys.TargetKindKey, (int)BlackboardStoredTargetKind.Point);
            ints.Remove(keys.HexQKey);
            ints.Remove(keys.HexRKey);
            entities.Remove(keys.TargetEntityKey);
            spatial.ClearPoints(keys.TargetPositionKey);
            spatial.SetPoint(keys.TargetPositionKey, worldPositionCm);
        }

        public static void SetHex(
            World world,
            Entity host,
            int hexQ,
            int hexR,
            in BlackboardStoredTargetKeys keys)
        {
            EnsureBlackboardBuffers(world, host);
            ref var ints = ref world.Get<BlackboardIntBuffer>(host);
            ref var spatial = ref world.Get<BlackboardSpatialBuffer>(host);
            ref var entities = ref world.Get<BlackboardEntityBuffer>(host);

            Vector3 worldPositionCm = new HexCoordinates(hexQ, hexR).ToWorldPositionCm();

            ints.Set(keys.TargetKindKey, (int)BlackboardStoredTargetKind.HexCell);
            ints.Set(keys.HexQKey, hexQ);
            ints.Set(keys.HexRKey, hexR);
            entities.Remove(keys.TargetEntityKey);
            spatial.ClearPoints(keys.TargetPositionKey);
            spatial.SetPoint(keys.TargetPositionKey, worldPositionCm);
        }

        public static void SetEntity(
            World world,
            Entity host,
            Entity targetEntity,
            in BlackboardStoredTargetKeys keys)
        {
            EnsureBlackboardBuffers(world, host);
            ref var ints = ref world.Get<BlackboardIntBuffer>(host);
            ref var spatial = ref world.Get<BlackboardSpatialBuffer>(host);
            ref var entities = ref world.Get<BlackboardEntityBuffer>(host);

            ints.Set(keys.TargetKindKey, (int)BlackboardStoredTargetKind.Entity);
            ints.Remove(keys.HexQKey);
            ints.Remove(keys.HexRKey);
            entities.Set(keys.TargetEntityKey, targetEntity);
            spatial.ClearPoints(keys.TargetPositionKey);
        }

        public static bool TryResolveWorldPositionCm(
            in BlackboardStoredTargetSnapshot snapshot,
            out Vector3 worldPositionCm)
        {
            switch (snapshot.Kind)
            {
                case BlackboardStoredTargetKind.Point:
                case BlackboardStoredTargetKind.HexCell:
                    worldPositionCm = snapshot.WorldPositionCm;
                    return true;
                default:
                    worldPositionCm = default;
                    return false;
            }
        }

        private static void EnsureBlackboardBuffers(World world, Entity host)
        {
            if (!world.Has<BlackboardIntBuffer>(host))
            {
                world.Add(host, new BlackboardIntBuffer());
            }

            if (!world.Has<BlackboardSpatialBuffer>(host))
            {
                world.Add(host, new BlackboardSpatialBuffer());
            }

            if (!world.Has<BlackboardEntityBuffer>(host))
            {
                world.Add(host, new BlackboardEntityBuffer());
            }
        }
    }
}

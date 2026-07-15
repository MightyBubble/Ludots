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
        public const string CapacityExceededError = "GAS.STORED_TARGET.ERR.BlackboardCapacityExceeded";

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
            PrepareBuffers(world, host, in keys, out BlackboardIntBuffer ints, out BlackboardSpatialBuffer spatial, out BlackboardEntityBuffer entities);
            ClearPrepared(ref ints, ref spatial, ref entities, in keys);
            CommitPrepared(world, host, in ints, in spatial, in entities);
        }

        public static void CommitFromOrder(
            World world,
            Entity host,
            in Order order,
            in BlackboardStoredTargetKeys keys)
        {
            PrepareBuffers(world, host, in keys, out BlackboardIntBuffer ints, out BlackboardSpatialBuffer spatial, out BlackboardEntityBuffer entities);
            ClearPrepared(ref ints, ref spatial, ref entities, in keys);

            bool hasEntityTarget = order.Target != Entity.Null && order.Target != default(Entity);
            bool hasSpatialTarget = order.Args.Spatial.Kind == OrderSpatialKind.Hex ||
                                    order.Args.Spatial.Mode == OrderCollectionMode.Single;
            if (hasEntityTarget && hasSpatialTarget)
            {
                throw new InvalidOperationException(
                    $"GAS.STORED_TARGET.ERR.AmbiguousOrderTarget: host={host.Id}, target={order.Target.Id}, spatialKind={order.Args.Spatial.Kind}, spatialMode={order.Args.Spatial.Mode}.");
            }

            if (hasEntityTarget)
            {
                if (!world.IsAlive(order.Target))
                {
                    throw new InvalidOperationException(
                        $"GAS.STORED_TARGET.ERR.InvalidTargetEntity: host={host.Id}, target={order.Target.Id}.");
                }
                SetEntityPrepared(host, ref ints, ref spatial, ref entities, order.Target, in keys);
            }
            else if (order.Args.Spatial.Kind == OrderSpatialKind.Hex)
            {
                SetHexPrepared(host, ref ints, ref spatial, ref entities, order.Args.Spatial.A0, order.Args.Spatial.A1, in keys);
            }
            else if (order.Args.Spatial.Mode == OrderCollectionMode.Single)
            {
                SetPointPrepared(host, ref ints, ref spatial, ref entities, order.Args.Spatial.WorldCm, in keys);
            }

            CommitPrepared(world, host, in ints, in spatial, in entities);
        }

        public static void SetPoint(
            World world,
            Entity host,
            Vector3 worldPositionCm,
            in BlackboardStoredTargetKeys keys)
        {
            PrepareBuffers(world, host, in keys, out BlackboardIntBuffer ints, out BlackboardSpatialBuffer spatial, out BlackboardEntityBuffer entities);
            ClearPrepared(ref ints, ref spatial, ref entities, in keys);
            SetPointPrepared(host, ref ints, ref spatial, ref entities, worldPositionCm, in keys);
            CommitPrepared(world, host, in ints, in spatial, in entities);
        }

        private static void SetPointPrepared(
            Entity host,
            ref BlackboardIntBuffer ints,
            ref BlackboardSpatialBuffer spatial,
            ref BlackboardEntityBuffer entities,
            Vector3 worldPositionCm,
            in BlackboardStoredTargetKeys keys)
        {
            RequireIntCapacity(host, ref ints, keys.TargetKindKey);
            RequireSpatialCapacity(host, ref spatial, keys.TargetPositionKey);
            ints.Set(keys.TargetKindKey, (int)BlackboardStoredTargetKind.Point);
            spatial.SetPoint(keys.TargetPositionKey, worldPositionCm);
        }

        public static void SetHex(
            World world,
            Entity host,
            int hexQ,
            int hexR,
            in BlackboardStoredTargetKeys keys)
        {
            PrepareBuffers(world, host, in keys, out BlackboardIntBuffer ints, out BlackboardSpatialBuffer spatial, out BlackboardEntityBuffer entities);
            ClearPrepared(ref ints, ref spatial, ref entities, in keys);
            SetHexPrepared(host, ref ints, ref spatial, ref entities, hexQ, hexR, in keys);
            CommitPrepared(world, host, in ints, in spatial, in entities);
        }

        private static void SetHexPrepared(
            Entity host,
            ref BlackboardIntBuffer ints,
            ref BlackboardSpatialBuffer spatial,
            ref BlackboardEntityBuffer entities,
            int hexQ,
            int hexR,
            in BlackboardStoredTargetKeys keys)
        {
            Vector3 worldPositionCm = new HexCoordinates(hexQ, hexR).ToWorldPositionCm();

            RequireIntCapacity(host, ref ints, keys.TargetKindKey);
            ints.Set(keys.TargetKindKey, (int)BlackboardStoredTargetKind.HexCell);
            RequireIntCapacity(host, ref ints, keys.HexQKey);
            ints.Set(keys.HexQKey, hexQ);
            RequireIntCapacity(host, ref ints, keys.HexRKey);
            ints.Set(keys.HexRKey, hexR);
            RequireSpatialCapacity(host, ref spatial, keys.TargetPositionKey);
            spatial.SetPoint(keys.TargetPositionKey, worldPositionCm);
        }

        public static void SetEntity(
            World world,
            Entity host,
            Entity targetEntity,
            in BlackboardStoredTargetKeys keys)
        {
            if (targetEntity == Entity.Null || !world.IsAlive(targetEntity))
            {
                throw new InvalidOperationException(
                    $"GAS.STORED_TARGET.ERR.InvalidTargetEntity: host={host.Id}, target={targetEntity.Id}.");
            }

            PrepareBuffers(world, host, in keys, out BlackboardIntBuffer ints, out BlackboardSpatialBuffer spatial, out BlackboardEntityBuffer entities);
            ClearPrepared(ref ints, ref spatial, ref entities, in keys);
            SetEntityPrepared(host, ref ints, ref spatial, ref entities, targetEntity, in keys);
            CommitPrepared(world, host, in ints, in spatial, in entities);
        }

        private static void SetEntityPrepared(
            Entity host,
            ref BlackboardIntBuffer ints,
            ref BlackboardSpatialBuffer spatial,
            ref BlackboardEntityBuffer entities,
            Entity targetEntity,
            in BlackboardStoredTargetKeys keys)
        {
            RequireIntCapacity(host, ref ints, keys.TargetKindKey);
            RequireEntityCapacity(host, ref entities, keys.TargetEntityKey);
            ints.Set(keys.TargetKindKey, (int)BlackboardStoredTargetKind.Entity);
            entities.Set(keys.TargetEntityKey, targetEntity);
        }

        private static void PrepareBuffers(
            World world,
            Entity host,
            in BlackboardStoredTargetKeys keys,
            out BlackboardIntBuffer ints,
            out BlackboardSpatialBuffer spatial,
            out BlackboardEntityBuffer entities)
        {
            if (!world.IsAlive(host))
            {
                throw new InvalidOperationException($"GAS.STORED_TARGET.ERR.InvalidHost: host={host.Id}.");
            }
            if (!keys.IsConfigured)
            {
                throw new InvalidOperationException($"GAS.STORED_TARGET.ERR.KeysNotConfigured: host={host.Id}.");
            }

            OrderBlackboardStateInstaller.RequireInstalled(world, host);
            ints = world.Get<BlackboardIntBuffer>(host);
            spatial = world.Get<BlackboardSpatialBuffer>(host);
            entities = world.Get<BlackboardEntityBuffer>(host);
        }

        private static void ClearPrepared(
            ref BlackboardIntBuffer ints,
            ref BlackboardSpatialBuffer spatial,
            ref BlackboardEntityBuffer entities,
            in BlackboardStoredTargetKeys keys)
        {
            ints.Remove(keys.TargetKindKey);
            ints.Remove(keys.HexQKey);
            ints.Remove(keys.HexRKey);
            spatial.RemoveEntry(keys.TargetPositionKey);
            entities.Remove(keys.TargetEntityKey);
        }

        private static void CommitPrepared(
            World world,
            Entity host,
            in BlackboardIntBuffer ints,
            in BlackboardSpatialBuffer spatial,
            in BlackboardEntityBuffer entities)
        {
            world.Get<BlackboardIntBuffer>(host) = ints;
            world.Get<BlackboardSpatialBuffer>(host) = spatial;
            world.Get<BlackboardEntityBuffer>(host) = entities;
        }

        private static void RequireIntCapacity(Entity host, ref BlackboardIntBuffer buffer, int key)
        {
            if (!buffer.TryGet(key, out _) && buffer.Count >= GasConstants.MAX_BLACKBOARD_ENTRIES)
            {
                ThrowCapacityExceeded(host, nameof(BlackboardIntBuffer), key);
            }
        }

        private static void RequireSpatialCapacity(Entity host, ref BlackboardSpatialBuffer buffer, int key)
        {
            if (!buffer.HasKey(key) && buffer.EntryCount >= BlackboardSpatialBuffer.MAX_ENTRIES)
            {
                ThrowCapacityExceeded(host, nameof(BlackboardSpatialBuffer), key);
            }
        }

        private static void RequireEntityCapacity(Entity host, ref BlackboardEntityBuffer buffer, int key)
        {
            if (!buffer.HasKey(key) && buffer.Count >= BlackboardEntityBuffer.MAX_ENTRIES)
            {
                ThrowCapacityExceeded(host, nameof(BlackboardEntityBuffer), key);
            }
        }

        private static void ThrowCapacityExceeded(Entity host, string buffer, int key)
        {
            throw new InvalidOperationException(
                $"{CapacityExceededError}: host={host.Id}, buffer={buffer}, key={key}.");
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

    }
}

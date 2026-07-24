using System;
using Arch.Core;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Networking.Commands;
using Ludots.Core.Networking.Protocol;
using Ludots.Core.Networking.Replication;

namespace Ludots.Core.Networking.Runtime
{
    public enum ReplicatedClientCommandSubmitResult : byte
    {
        Submitted = 0,
        EmptyBatch = 1,
        NotConnected = 2,
        SnapshotUnavailable = 3,
        BatchCapacityExceeded = 4,
        SchemaNotExposed = 5,
        ActorNotReplicated = 6,
        TargetNotReplicated = 7,
        TargetShapeMismatch = 8,
        TargetPositionInvalid = 9,
        ArgumentNotAllowed = 10,
        SequenceExhausted = 11,
        TransportRejected = 12,
    }

    /// <summary>
    /// Platform-neutral local order submission boundary for a replicated client. The port owns
    /// session sequence and acknowledgement cursors; callers submit only semantic orders.
    /// </summary>
    public interface IReplicatedClientCommandPort
    {
        ReplicatedClientCommandSubmitResult Submit(in Order order);

        ReplicatedClientCommandSubmitResult Submit(ReadOnlySpan<Order> orders);
    }

    public sealed class ReplicatedClientCommandPort : IReplicatedClientCommandPort
    {
        private readonly World _world;
        private readonly ReplicatedClientNetworkRuntime _runtime;
        private readonly NetworkCommandSchemaRegistry _schemas;
        private readonly NetworkCommandWireEntry[] _entries;
        private ulong _boundSessionEpoch;
        private ulong _nextBatchSequence = 1;

        public ReplicatedClientCommandPort(
            World world,
            ReplicatedClientNetworkRuntime runtime,
            NetworkCommandSchemaRegistry schemas,
            int maxActorsPerBatch)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            _schemas = schemas ?? throw new ArgumentNullException(nameof(schemas));
            if (!schemas.IsFrozen)
            {
                throw new InvalidOperationException("Replicated client command schemas must be frozen before submission composition.");
            }

            if (maxActorsPerBatch <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxActorsPerBatch));
            }

            _entries = new NetworkCommandWireEntry[maxActorsPerBatch];
        }

        public ReplicatedClientCommandSubmitResult Submit(in Order order)
        {
            Span<Order> orders = stackalloc Order[1];
            orders[0] = order;
            return Submit(orders);
        }

        public ReplicatedClientCommandSubmitResult Submit(ReadOnlySpan<Order> orders)
        {
            if (orders.IsEmpty)
            {
                return ReplicatedClientCommandSubmitResult.EmptyBatch;
            }

            if (_runtime.State != ReplicatedClientConnectionState.Connected ||
                _runtime.SessionEpoch.IsEmpty)
            {
                return ReplicatedClientCommandSubmitResult.NotConnected;
            }

            if (_runtime.LastCommittedTick == 0)
            {
                return ReplicatedClientCommandSubmitResult.SnapshotUnavailable;
            }

            if (orders.Length > _entries.Length || orders.Length > ushort.MaxValue)
            {
                return ReplicatedClientCommandSubmitResult.BatchCapacityExceeded;
            }

            ulong sessionEpoch = _runtime.SessionEpoch.Value;
            if (_boundSessionEpoch != sessionEpoch)
            {
                _boundSessionEpoch = sessionEpoch;
                _nextBatchSequence = 1;
            }

            if (_nextBatchSequence == 0)
            {
                return ReplicatedClientCommandSubmitResult.SequenceExhausted;
            }

            for (int i = 0; i < orders.Length; i++)
            {
                ReplicatedClientCommandSubmitResult materialized = TryMaterialize(
                    in orders[i],
                    out _entries[i]);
                if (materialized != ReplicatedClientCommandSubmitResult.Submitted)
                {
                    return materialized;
                }
            }

            uint acknowledgedTick = _runtime.LastCommittedTick;
            int targetTick = acknowledgedTick >= int.MaxValue
                ? int.MaxValue
                : checked((int)acknowledgedTick + 1);
            var header = new NetworkCommandBatchHeader(
                sessionEpoch,
                _nextBatchSequence,
                targetTick,
                checked((int)Math.Min(acknowledgedTick, int.MaxValue)),
                checked((ushort)orders.Length));
            if (!_runtime.TrySubmitCommand(in header, _entries.AsSpan(0, orders.Length)))
            {
                return ReplicatedClientCommandSubmitResult.TransportRejected;
            }

            _nextBatchSequence = _nextBatchSequence == ulong.MaxValue
                ? 0
                : _nextBatchSequence + 1;
            return ReplicatedClientCommandSubmitResult.Submitted;
        }

        private ReplicatedClientCommandSubmitResult TryMaterialize(
            in Order order,
            out NetworkCommandWireEntry entry)
        {
            entry = default;
            if (!_schemas.TryGet(order.OrderTypeId, out NetworkCommandSchema schema))
            {
                return ReplicatedClientCommandSubmitResult.SchemaNotExposed;
            }

            if (!TryGetReplicatedHandle(order.Actor, out NetworkEntityHandle actor))
            {
                return ReplicatedClientCommandSubmitResult.ActorNotReplicated;
            }

            if ((!schema.AllowArg0 && order.Args.I0 != 0) ||
                (!schema.AllowArg1 && order.Args.I1 != 0))
            {
                return ReplicatedClientCommandSubmitResult.ArgumentNotAllowed;
            }

            ReplicatedClientCommandSubmitResult targetResult = TryCreateTarget(
                in order,
                in schema,
                out NetworkCommandTargetPayload target);
            if (targetResult != ReplicatedClientCommandSubmitResult.Submitted)
            {
                return targetResult;
            }

            entry = new NetworkCommandWireEntry(actor, order.OrderTypeId, in target);
            return ReplicatedClientCommandSubmitResult.Submitted;
        }

        private ReplicatedClientCommandSubmitResult TryCreateTarget(
            in Order order,
            in NetworkCommandSchema schema,
            out NetworkCommandTargetPayload target)
        {
            target = default;
            bool requiresPosition = schema.TargetKind is NetworkCommandTargetKind.WorldPositionCm or
                NetworkCommandTargetKind.WorldPositionAndEntity;
            bool requiresEntity = schema.TargetKind is NetworkCommandTargetKind.NetworkEntity or
                NetworkCommandTargetKind.WorldPositionAndEntity;

            int x = 0;
            int y = 0;
            int z = 0;
            if (requiresPosition)
            {
                if (order.Args.Spatial.Kind != OrderSpatialKind.WorldCm ||
                    order.Args.Spatial.Mode != OrderCollectionMode.Single)
                {
                    return ReplicatedClientCommandSubmitResult.TargetShapeMismatch;
                }

                if (!TryQuantize(order.Args.Spatial.WorldCm.X, out x) ||
                    !TryQuantize(order.Args.Spatial.WorldCm.Y, out y) ||
                    !TryQuantize(order.Args.Spatial.WorldCm.Z, out z))
                {
                    return ReplicatedClientCommandSubmitResult.TargetPositionInvalid;
                }
            }
            else if (order.Args.Spatial.Kind != OrderSpatialKind.None)
            {
                return ReplicatedClientCommandSubmitResult.TargetShapeMismatch;
            }

            NetworkEntityHandle targetEntity = default;
            if (requiresEntity)
            {
                if (!TryGetReplicatedHandle(order.Target, out targetEntity))
                {
                    return ReplicatedClientCommandSubmitResult.TargetNotReplicated;
                }
            }
            else if (order.Target != Entity.Null)
            {
                return ReplicatedClientCommandSubmitResult.TargetShapeMismatch;
            }

            target = new NetworkCommandTargetPayload(
                schema.TargetKind,
                x,
                y,
                z,
                targetEntity.Slot,
                targetEntity.Generation,
                schema.AllowArg0 ? order.Args.I0 : 0,
                schema.AllowArg1 ? order.Args.I1 : 0);
            return ReplicatedClientCommandSubmitResult.Submitted;
        }

        private bool TryGetReplicatedHandle(Entity entity, out NetworkEntityHandle handle)
        {
            if (entity != Entity.Null &&
                _world.IsAlive(entity) &&
                _world.TryGet(entity, out ReplicationMirrorIdentity identity) &&
                identity.Handle.IsValid)
            {
                handle = identity.Handle;
                return true;
            }

            handle = default;
            return false;
        }

        private static bool TryQuantize(float value, out int quantized)
        {
            if (!float.IsFinite(value) || value < int.MinValue || value > int.MaxValue)
            {
                quantized = 0;
                return false;
            }

            quantized = checked((int)MathF.Round(value));
            return true;
        }
    }
}

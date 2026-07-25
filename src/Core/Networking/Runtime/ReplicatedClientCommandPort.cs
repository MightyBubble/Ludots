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
        None = 0,
        Submitted = 1,
        EmptyBatch = 2,
        NotConnected = 3,
        SnapshotUnavailable = 4,
        BatchCapacityExceeded = 5,
        SchemaNotExposed = 6,
        ActorNotReplicated = 7,
        TargetNotReplicated = 8,
        TargetShapeMismatch = 9,
        TargetPositionInvalid = 10,
        ArgumentNotAllowed = 11,
        SequenceExhausted = 12,
        TransportRejected = 13,
        MixedSubmitModes = 14,
        SubmitModeNotAllowed = 15,
    }

    /// <summary>
    /// Platform-neutral local order submission boundary for a replicated client. The port owns
    /// session sequence and acknowledgement cursors; callers submit only semantic orders.
    /// </summary>
    public interface IReplicatedClientCommandPort
    {
        ulong SubmissionRevision { get; }

        ulong LastSubmittedBatchSequence { get; }

        ReplicatedClientCommandSubmitResult LastSubmitResult { get; }

        ReplicatedClientCommandSubmitResult Submit(in Order order);

        ReplicatedClientCommandSubmitResult Submit(ReadOnlySpan<Order> orders);
    }

    public sealed class ReplicatedClientCommandPort : IReplicatedClientCommandPort
    {
        private readonly World _world;
        private readonly ReplicatedClientNetworkRuntime _runtime;
        private readonly NetworkCommandSchemaRegistry _schemas;
        private readonly NetworkCommandWireEntry[] _entries;
        private ReplicatedClientCommandStreamIdentity _boundIdentity;
        private ulong _boundCommandStreamRevision;
        private ulong _nextBatchSequence = ReplicatedClientCommandStreamIdentity.FirstBatchSequence;

        public ulong SubmissionRevision { get; private set; }

        public ulong LastSubmittedBatchSequence { get; private set; }

        public ReplicatedClientCommandSubmitResult LastSubmitResult { get; private set; }

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
                return Complete(ReplicatedClientCommandSubmitResult.EmptyBatch);
            }

            for (int i = 0; i < orders.Length; i++)
            {
                OrderEntityReferenceContract.Validate(in orders[i], nameof(ReplicatedClientCommandPort));
            }

            if (_runtime.State != ReplicatedClientConnectionState.Connected ||
                _runtime.SessionEpoch.IsEmpty)
            {
                return Complete(ReplicatedClientCommandSubmitResult.NotConnected);
            }

            if (_runtime.LastCommittedTick == 0 || _runtime.IsAwaitingFullSnapshot)
            {
                return Complete(ReplicatedClientCommandSubmitResult.SnapshotUnavailable);
            }

            if (orders.Length > _entries.Length || orders.Length > ushort.MaxValue)
            {
                return Complete(ReplicatedClientCommandSubmitResult.BatchCapacityExceeded);
            }

            OrderSubmitMode submitMode = orders[0].SubmitMode;
            if ((uint)submitMode > (uint)OrderSubmitMode.PersistentQueued)
            {
                return Complete(ReplicatedClientCommandSubmitResult.SubmitModeNotAllowed);
            }

            ReplicatedClientCommandStreamIdentity identity = _runtime.CommandStreamIdentity;
            if (!identity.IsValid)
            {
                return Complete(ReplicatedClientCommandSubmitResult.NotConnected);
            }

            ulong commandStreamRevision = _runtime.CommandStreamRevision;
            ulong authoritativeNextSequence = _runtime.NextClientBatchSequence;
            if (commandStreamRevision == 0 || authoritativeNextSequence == 0)
            {
                return Complete(ReplicatedClientCommandSubmitResult.NotConnected);
            }

            if (_boundIdentity != identity || _boundCommandStreamRevision != commandStreamRevision)
            {
                _boundIdentity = identity;
                _boundCommandStreamRevision = commandStreamRevision;
                _nextBatchSequence = authoritativeNextSequence;
            }

            if (_nextBatchSequence is 0 or ulong.MaxValue)
            {
                return Complete(ReplicatedClientCommandSubmitResult.SequenceExhausted);
            }

            for (int i = 0; i < orders.Length; i++)
            {
                if (orders[i].SubmitMode != submitMode)
                {
                    return Complete(ReplicatedClientCommandSubmitResult.MixedSubmitModes);
                }
                ReplicatedClientCommandSubmitResult materialized = TryMaterialize(
                    in orders[i],
                    out _entries[i]);
                if (materialized != ReplicatedClientCommandSubmitResult.Submitted)
                {
                    return Complete(materialized);
                }
            }

            uint acknowledgedTick = _runtime.LastCommittedTick;
            uint estimatedTargetTick = _runtime.EstimatedCommandTargetTick;
            int targetTick = estimatedTargetTick >= int.MaxValue
                ? int.MaxValue
                : checked((int)estimatedTargetTick);
            var header = new NetworkCommandBatchHeader(
                identity.SessionEpoch.Value,
                _nextBatchSequence,
                targetTick,
                checked((int)Math.Min(acknowledgedTick, int.MaxValue)),
                checked((ushort)orders.Length),
                submitMode);
            if (!_runtime.TrySubmitCommand(in header, _entries.AsSpan(0, orders.Length)))
            {
                return Complete(ReplicatedClientCommandSubmitResult.TransportRejected);
            }

            ulong submittedSequence = _nextBatchSequence;
            _nextBatchSequence++;
            return Complete(ReplicatedClientCommandSubmitResult.Submitted, submittedSequence);
        }

        private ReplicatedClientCommandSubmitResult Complete(
            ReplicatedClientCommandSubmitResult result,
            ulong submittedBatchSequence = 0)
        {
            SubmissionRevision = checked(SubmissionRevision + 1);
            LastSubmitResult = result;
            LastSubmittedBatchSequence = submittedBatchSequence;
            return result;
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

            if (!schema.AllowsSubmitMode(order.SubmitMode))
            {
                return ReplicatedClientCommandSubmitResult.SubmitModeNotAllowed;
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

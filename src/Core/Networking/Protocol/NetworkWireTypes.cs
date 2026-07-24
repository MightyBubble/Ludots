using System;

namespace Ludots.Core.Networking.Protocol
{
    /// <summary>
    /// Fixed binary envelope kinds for the platform-neutral authoritative networking wire.
    /// </summary>
    public enum NetworkWireKind : byte
    {
        SessionHandshakeRequest = 1,
        SessionHandshakeResponse = 2,
        CommandBatch = 3,
        CommandAdmissionResult = 4,
        ReplicationPacket = 5,
        SnapshotAcknowledgement = 6,
        ResyncRequired = 7,
        SnapshotFragment = 8,
        CommandFragment = 9,
        FixedInputBatch = 10,
        FixedInputAcknowledgement = 11,
    }

    /// <summary>
    /// Explicit encode/decode outcomes. Malformed input never throws on the hot path.
    /// </summary>
    public enum NetworkWireCodecStatus : byte
    {
        Success = 0,
        BufferTooSmall = 1,
        MalformedLength = 2,
        UnknownKind = 3,
        UnknownVersion = 4,
        UnknownSchema = 5,
        Overflow = 6,
        InvalidEnum = 7,
        TrailingBytes = 8,
        CapacityExhausted = 9,
        InvalidHandle = 10,
        InvalidInput = 11,
    }

    public enum NetworkCommandTargetKind : byte
    {
        None = 0,
        WorldPositionCm = 1,
        NetworkEntity = 2,
        WorldPositionAndEntity = 3,
    }

    public enum NetworkResyncReason : byte
    {
        BaselineUnavailable = 1,
        BaselineExpired = 2,
        SnapshotGap = 3,
        ExplicitServerRequest = 4,
    }

    /// <summary>
    /// Explicit outcomes for fixed-capacity snapshot fragment reassembly.
    /// Rejected fragments never mutate assembler state.
    /// </summary>
    public enum SnapshotReassemblyStatus : byte
    {
        Incomplete = 0,
        Completed = 1,
        CapacityExceeded = 2,
        InvalidFragment = 3,
        Duplicate = 4,
        StaleOrOutOfOrder = 5,
        MixedMetadata = 6,
    }

    /// <summary>
    /// Lifecycle phase of <see cref="SnapshotFragmentReassembler"/>.
    /// Completed payloads require an explicit <c>Reset</c> before a new snapshot may begin.
    /// </summary>
    public enum SnapshotReassemblyPhase : byte
    {
        Empty = 0,
        Assembling = 1,
        Completed = 2,
    }

    /// <summary>
    /// Explicit outcomes for fixed-capacity command-batch fragment reassembly.
    /// Rejected fragments never mutate assembler state.
    /// </summary>
    public enum CommandReassemblyStatus : byte
    {
        Incomplete = 0,
        Completed = 1,
        CapacityExceeded = 2,
        InvalidFragment = 3,
        Duplicate = 4,
        StaleOrOutOfOrder = 5,
        MixedMetadata = 6,
    }

    /// <summary>
    /// Lifecycle phase of <see cref="CommandFragmentReassembler"/>.
    /// Completed payloads require an explicit <c>Reset</c> before a new batch may begin.
    /// </summary>
    public enum CommandReassemblyPhase : byte
    {
        Empty = 0,
        Assembling = 1,
        Completed = 2,
    }

    /// <summary>
    /// Fixed 8-byte little-endian envelope preceding every networking datagram payload.
    /// Layout: magic u32 | version u8 | kind u8 | payloadLength u16.
    /// </summary>
    public readonly struct NetworkWireEnvelope
    {
        public const uint Magic = 0x504E444C; // "LDNP"
        public const byte CurrentVersion = 1;
        public const int SizeInBytes = 8;

        public NetworkWireEnvelope(byte version, NetworkWireKind kind, ushort payloadLength)
        {
            Version = version;
            Kind = kind;
            PayloadLength = payloadLength;
        }

        public byte Version { get; }
        public NetworkWireKind Kind { get; }
        public ushort PayloadLength { get; }

        public int TotalLength => SizeInBytes + PayloadLength;
    }

    public readonly struct NetworkSnapshotAcknowledgement
    {
        public const int SizeInBytes = 8 + 8 + 4;

        public NetworkSnapshotAcknowledgement(ulong sessionEpoch, ulong snapshotId, uint committedTick)
        {
            SessionEpoch = sessionEpoch;
            SnapshotId = snapshotId;
            CommittedTick = committedTick;
        }

        public ulong SessionEpoch { get; }
        public ulong SnapshotId { get; }
        public uint CommittedTick { get; }
    }

    /// <summary>
    /// Fixed little-endian header for one snapshot datagram fragment.
    /// Layout (28 bytes): sessionEpoch u64 | snapshotId u64 | fragmentIndex u16 | fragmentCount u16 |
    /// totalPayloadLength u32 | fragmentPayloadLength u16 | reserved u16 (must be 0).
    /// </summary>
    public readonly struct NetworkSnapshotFragmentHeader
    {
        public const int SizeInBytes = 8 + 8 + 2 + 2 + 4 + 2 + 2;

        public NetworkSnapshotFragmentHeader(
            ulong sessionEpoch,
            ulong snapshotId,
            ushort fragmentIndex,
            ushort fragmentCount,
            uint totalPayloadLength,
            ushort fragmentPayloadLength)
        {
            SessionEpoch = sessionEpoch;
            SnapshotId = snapshotId;
            FragmentIndex = fragmentIndex;
            FragmentCount = fragmentCount;
            TotalPayloadLength = totalPayloadLength;
            FragmentPayloadLength = fragmentPayloadLength;
        }

        public ulong SessionEpoch { get; }
        public ulong SnapshotId { get; }
        public ushort FragmentIndex { get; }
        public ushort FragmentCount { get; }
        public uint TotalPayloadLength { get; }
        public ushort FragmentPayloadLength { get; }
    }

    /// <summary>
    /// Fixed little-endian header for one command-batch datagram fragment.
    /// Target tick stays inside the reassembled <see cref="NetworkCommandBatchHeader"/> payload, not here.
    /// Layout (28 bytes): sessionEpoch u64 (nonzero) | clientBatchSequence u64 (nonzero) |
    /// fragmentIndex u16 | fragmentCount u16 | totalPayloadLength u32 | fragmentPayloadLength u16 |
    /// reserved u16 (must be 0).
    /// </summary>
    public readonly struct NetworkCommandFragmentHeader
    {
        public const int SizeInBytes = 8 + 8 + 2 + 2 + 4 + 2 + 2;

        public NetworkCommandFragmentHeader(
            ulong sessionEpoch,
            ulong clientBatchSequence,
            ushort fragmentIndex,
            ushort fragmentCount,
            uint totalPayloadLength,
            ushort fragmentPayloadLength)
        {
            SessionEpoch = sessionEpoch;
            ClientBatchSequence = clientBatchSequence;
            FragmentIndex = fragmentIndex;
            FragmentCount = fragmentCount;
            TotalPayloadLength = totalPayloadLength;
            FragmentPayloadLength = fragmentPayloadLength;
        }

        public ulong SessionEpoch { get; }
        public ulong ClientBatchSequence { get; }
        public ushort FragmentIndex { get; }
        public ushort FragmentCount { get; }
        public uint TotalPayloadLength { get; }
        public ushort FragmentPayloadLength { get; }
    }

    public readonly struct NetworkResyncRequired
    {
        public const int SizeInBytes = 8 + 1 + 3 + 4 + 8;

        public NetworkResyncRequired(
            ulong sessionEpoch,
            NetworkResyncReason reason,
            uint latestCommittedTick,
            ulong latestSnapshotId)
        {
            SessionEpoch = sessionEpoch;
            Reason = reason;
            LatestCommittedTick = latestCommittedTick;
            LatestSnapshotId = latestSnapshotId;
        }

        public ulong SessionEpoch { get; }
        public NetworkResyncReason Reason { get; }
        public uint LatestCommittedTick { get; }
        public ulong LatestSnapshotId { get; }
    }

    /// <summary>
    /// Fixed-size quantized command target. Never carries Arch Entity ids or client-trusted PlayerId.
    /// Layout (32 bytes): kind u8 | reserved 3 | x/y/z i32 | targetSlot i32 | targetGeneration u32 | arg0 i32 | arg1 i32.
    /// </summary>
    public readonly struct NetworkCommandTargetPayload
    {
        public const int SizeInBytes = 32;

        public NetworkCommandTargetPayload(
            NetworkCommandTargetKind kind,
            int positionXCm,
            int positionYCm,
            int positionZCm,
            int targetSlot,
            uint targetGeneration,
            int arg0,
            int arg1)
        {
            Kind = kind;
            PositionXCm = positionXCm;
            PositionYCm = positionYCm;
            PositionZCm = positionZCm;
            TargetSlot = targetSlot;
            TargetGeneration = targetGeneration;
            Arg0 = arg0;
            Arg1 = arg1;
        }

        public NetworkCommandTargetKind Kind { get; }
        public int PositionXCm { get; }
        public int PositionYCm { get; }
        public int PositionZCm { get; }
        public int TargetSlot { get; }
        public uint TargetGeneration { get; }
        public int Arg0 { get; }
        public int Arg1 { get; }

        public static NetworkCommandTargetPayload None => default;

        public static NetworkCommandTargetPayload FromWorldPositionCm(int x, int y, int z) =>
            new(NetworkCommandTargetKind.WorldPositionCm, x, y, z, 0, 0, 0, 0);

        public static NetworkCommandTargetPayload FromNetworkEntity(int slot, uint generation) =>
            new(NetworkCommandTargetKind.NetworkEntity, 0, 0, 0, slot, generation, 0, 0);

        public bool TryGetTargetEntity(out Replication.NetworkEntityHandle handle)
        {
            if (Kind is NetworkCommandTargetKind.NetworkEntity or NetworkCommandTargetKind.WorldPositionAndEntity)
            {
                return Replication.NetworkEntityHandle.TryCreate(TargetSlot, TargetGeneration, out handle);
            }

            handle = default;
            return false;
        }
    }

    /// <summary>
    /// One semantic command entry on the wire. Actor is always a NetworkEntityHandle.
    /// </summary>
    public readonly struct NetworkCommandWireEntry
    {
        // actor slot i32 + generation u32 + orderTypeId i32 + fixed target payload
        public const int SizeInBytes = 4 + 4 + 4 + NetworkCommandTargetPayload.SizeInBytes;

        public NetworkCommandWireEntry(
            Replication.NetworkEntityHandle actor,
            int orderTypeId,
            in NetworkCommandTargetPayload target)
        {
            Actor = actor;
            OrderTypeId = orderTypeId;
            Target = target;
        }

        public Replication.NetworkEntityHandle Actor { get; }
        public int OrderTypeId { get; }
        public NetworkCommandTargetPayload Target { get; }
    }

    public readonly struct NetworkCommandBatchHeader
    {
        public const int SizeInBytes = 8 + 8 + 4 + 4 + 2 + 2;

        public NetworkCommandBatchHeader(
            ulong sessionEpoch,
            ulong clientBatchSequence,
            int targetTick,
            int acknowledgedCommittedTick,
            ushort entryCount)
        {
            SessionEpoch = sessionEpoch;
            ClientBatchSequence = clientBatchSequence;
            TargetTick = targetTick;
            AcknowledgedCommittedTick = acknowledgedCommittedTick;
            EntryCount = entryCount;
        }

        public ulong SessionEpoch { get; }
        public ulong ClientBatchSequence { get; }
        public int TargetTick { get; }
        public int AcknowledgedCommittedTick { get; }
        public ushort EntryCount { get; }
    }

    /// <summary>
    /// Fixed little-endian header for one unfragmented fixed-input batch datagram body.
    /// Layout (20 bytes): sessionEpoch u64 | schemaId u16 | framePayloadBytes u16 |
    /// acknowledgedCommittedTick u32 | frameCount u16 | reserved u16 (must be 0).
    /// Each frame is targetTick u32 plus exactly <see cref="FramePayloadBytes"/> bytes.
    /// </summary>
    public readonly struct NetworkFixedInputBatchHeader
    {
        public const int SizeInBytes = 8 + 2 + 2 + 4 + 2 + 2;

        public NetworkFixedInputBatchHeader(
            ulong sessionEpoch,
            ushort schemaId,
            ushort framePayloadBytes,
            uint acknowledgedCommittedTick,
            ushort frameCount)
        {
            SessionEpoch = sessionEpoch;
            SchemaId = schemaId;
            FramePayloadBytes = framePayloadBytes;
            AcknowledgedCommittedTick = acknowledgedCommittedTick;
            FrameCount = frameCount;
        }

        public ulong SessionEpoch { get; }
        public ushort SchemaId { get; }
        public ushort FramePayloadBytes { get; }
        public uint AcknowledgedCommittedTick { get; }
        public ushort FrameCount { get; }
    }

    /// <summary>
    /// Fixed 32-byte little-endian fixed-input acknowledgement body.
    /// Layout: sessionEpoch u64 | schemaId u16 | reserved u16 (must be 0) |
    /// committedThroughTick u32 | latestReceivedTick u32 | receivedMask u64 |
    /// latestMissingInputTick u32.
    /// </summary>
    /// <remarks>
    /// ACK-mask invariant (SSOT, enforced by <see cref="FixedInputWireCodec"/>):
    /// bit <c>i</c> of <see cref="ReceivedMask"/> means tick
    /// <c>LatestReceivedTick - i</c> is present in the receiver ring.
    /// Therefore:
    /// <list type="bullet">
    /// <item>when <see cref="LatestReceivedTick"/> is 0, <see cref="ReceivedMask"/> must be 0;</item>
    /// <item>when <see cref="LatestReceivedTick"/> is nonzero, bit 0 must be set
    /// (the latest received tick itself is present);</item>
    /// <item>when <see cref="LatestReceivedTick"/> is 1..63, bits at index &gt;= LatestReceivedTick
    /// must be zero (they would name tick 0 or a negative tick); latest=64 with
    /// <c>ulong.MaxValue</c> is valid.</item>
    /// </list>
    /// Runtime contract: fixed-input ACK is built/sent only after the authoritative frame commit.
    /// Wire rule: <see cref="LatestMissingInputTick"/> is 0 or &lt;= <see cref="CommittedThroughTick"/>.
    /// <see cref="LatestReceivedTick"/> may still exceed CommittedThroughTick when future input arrived.
    /// Tick fields are domain-aligned with <c>AuthoritativeSimulationTickState</c>
    /// (<c>&lt;= int.MaxValue</c>). Target input frames separately forbid tick 0.
    /// <see cref="CommittedThroughTick"/> may exceed <see cref="LatestReceivedTick"/>
    /// after deadline misses.
    /// </remarks>
    public readonly struct NetworkFixedInputAcknowledgement
    {
        public const int SizeInBytes = 8 + 2 + 2 + 4 + 4 + 8 + 4;

        public NetworkFixedInputAcknowledgement(
            ulong sessionEpoch,
            ushort schemaId,
            uint committedThroughTick,
            uint latestReceivedTick,
            ulong receivedMask,
            uint latestMissingInputTick)
        {
            SessionEpoch = sessionEpoch;
            SchemaId = schemaId;
            CommittedThroughTick = committedThroughTick;
            LatestReceivedTick = latestReceivedTick;
            ReceivedMask = receivedMask;
            LatestMissingInputTick = latestMissingInputTick;
        }

        public ulong SessionEpoch { get; }
        public ushort SchemaId { get; }
        public uint CommittedThroughTick { get; }
        public uint LatestReceivedTick { get; }
        public ulong ReceivedMask { get; }
        public uint LatestMissingInputTick { get; }
    }

    /// <summary>
    /// Explicit per-frame outcomes for authoritative fixed-input admission.
    /// </summary>
    public enum FixedInputAdmissionDisposition : byte
    {
        Accepted = 1,
        AcceptedOutOfOrder = 2,
        Duplicate = 3,
        Conflict = 4,
        RejectedAtExecutionCutoff = 5,
        Late = 6,
        TooFarFuture = 7,
        InvalidSeatGeneration = 8,
        EpochMismatch = 9,
        SchemaMismatch = 10,
        PayloadMismatch = 11,
        RingWrap = 12,
        BatchRejected = 13,
        ReservedNonZero = 14,
        InvalidFrameOrder = 15,
        /// <summary>
        /// Target tick is 0 or exceeds <see cref="int.MaxValue"/> (AuthoritativeSimulationTickState domain).
        /// Hard batch reject; no frames are applied.
        /// </summary>
        TickOutOfRange = 16,
    }

    /// <summary>
    /// Explicit lookup outcomes. Missing input at an executing tick is never fabricated.
    /// </summary>
    public enum FixedInputLookupResult : byte
    {
        Present = 1,
        Missing = 2,
        MissingAtDeadline = 3,
        InvalidSeat = 4,
        /// <summary>
        /// Lookup tick is 0 or exceeds <see cref="int.MaxValue"/> (AuthoritativeSimulationTickState domain).
        /// Distinct from <see cref="Missing"/>; never silently remapped.
        /// </summary>
        InvalidTick = 5,
    }

    public enum FixedInputBatchAdmissionStatus : byte
    {
        Success = 0,
        Rejected = 1,
    }

    public enum FixedInputAckApplyStatus : byte
    {
        Applied = 0,
        RejectedRegression = 1,
        EpochMismatch = 2,
        SchemaMismatch = 3,
        InvalidInput = 4,
    }

    public enum FixedInputOutboxEnqueueStatus : byte
    {
        Enqueued = 0,
        CapacityExceeded = 1,
        TickNotIncreasing = 2,
        PayloadMismatch = 3,
        InvalidInput = 4,
    }

    /// <summary>
    /// Explicit outcomes for client fixed-input outbox batch building.
    /// <see cref="NoData"/> means nothing needs sending; it is not a successful encode path.
    /// </summary>
    public enum FixedInputBatchBuildStatus : byte
    {
        Built = 0,
        NoData = 1,
        InvalidInput = 2,
        CapacityExceeded = 3,
    }
}

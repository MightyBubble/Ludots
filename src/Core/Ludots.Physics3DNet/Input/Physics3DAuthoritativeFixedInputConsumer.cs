using System.Buffers.Binary;
using System.Numerics;
using Arch.System;
using Ludots.Core.Networking.FixedInput;
using Ludots.Core.Networking.Protocol;
using Ludots.Core.Networking.Session;
using Ludots.Core.Networking.Simulation;
using Ludots.Core.Physics3D;
using Ludots.Core.Physics3DNet.Bridge;

namespace Ludots.Core.Physics3DNet.Input;

public readonly struct Physics3DFixedInputFrame
{
    public Physics3DFixedInputFrame(Vector2 movement)
    {
        Movement = movement;
    }

    public Vector2 Movement { get; }
}

public static class Physics3DFixedInputFrameCodec
{
    public const ushort PayloadBytes = 8;

    public static bool TryEncode(Vector2 movement, Span<byte> destination)
    {
        if (destination.Length < PayloadBytes ||
            !float.IsFinite(movement.X) ||
            !float.IsFinite(movement.Y) ||
            movement.LengthSquared() > 1.0001f)
        {
            return false;
        }

        destination[..PayloadBytes].Clear();
        BinaryPrimitives.WriteInt16LittleEndian(destination, QuantizeAxis(movement.X));
        BinaryPrimitives.WriteInt16LittleEndian(destination[2..], QuantizeAxis(movement.Y));
        return true;
    }

    public static bool TryDecode(ReadOnlySpan<byte> payload, out Physics3DFixedInputFrame frame)
    {
        frame = default;
        if (payload.Length != PayloadBytes || payload[4] != 0 || payload[5] != 0 || payload[6] != 0 || payload[7] != 0)
        {
            return false;
        }

        Vector2 movement = new(
            BinaryPrimitives.ReadInt16LittleEndian(payload) / (float)short.MaxValue,
            BinaryPrimitives.ReadInt16LittleEndian(payload[2..]) / (float)short.MaxValue);
        if (movement.LengthSquared() > 1.0002f)
        {
            return false;
        }

        frame = new Physics3DFixedInputFrame(movement);
        return true;
    }

    private static short QuantizeAxis(float value) =>
        (short)MathF.Round(value * short.MaxValue, MidpointRounding.AwayFromZero);
}

public interface IPhysics3DAuthoritativeFixedInputSource
{
    int SeatCapacity { get; }
    ushort SchemaId { get; }
    int FramePayloadBytes { get; }

    void EnsureReady();

    FixedInputSeatActivationState GetSeatActivationState(
        in SessionSeatBinding seat,
        out uint activationTick);

    FixedInputLookupResult TryRead(
        in SessionSeatBinding seat,
        uint tick,
        Span<byte> destination,
        out int bytesWritten);
}

public sealed class Physics3DAuthoritativeFixedInputIngressSource : IPhysics3DAuthoritativeFixedInputSource
{
    private readonly AuthoritativeFixedInputIngress _ingress;

    public Physics3DAuthoritativeFixedInputIngressSource(AuthoritativeFixedInputIngress ingress)
    {
        _ingress = ingress ?? throw new ArgumentNullException(nameof(ingress));
    }

    public int SeatCapacity => _ingress.SeatCapacity;
    public ushort SchemaId => _ingress.Config.SchemaId;
    public int FramePayloadBytes => _ingress.FramePayloadBytes;

    public void EnsureReady() { }

    public FixedInputSeatActivationState GetSeatActivationState(
        in SessionSeatBinding seat,
        out uint activationTick) =>
        _ingress.GetSeatActivationState(in seat, out activationTick);

    public FixedInputLookupResult TryRead(
        in SessionSeatBinding seat,
        uint tick,
        Span<byte> destination,
        out int bytesWritten) =>
        _ingress.TryGet(in seat, tick, destination, out bytesWritten);
}

public sealed class Physics3DLazyAuthoritativeFixedInputSource : IPhysics3DAuthoritativeFixedInputSource
{
    private readonly Func<AuthoritativeFixedInputIngress?> _resolver;
    private AuthoritativeFixedInputIngress? _ingress;

    public Physics3DLazyAuthoritativeFixedInputSource(
        int seatCapacity,
        ushort schemaId,
        int framePayloadBytes,
        Func<AuthoritativeFixedInputIngress?> resolver)
    {
        if (seatCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(seatCapacity));
        }

        if (schemaId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(schemaId));
        }

        if (framePayloadBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(framePayloadBytes));
        }

        SeatCapacity = seatCapacity;
        SchemaId = schemaId;
        FramePayloadBytes = framePayloadBytes;
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
    }

    public int SeatCapacity { get; }
    public ushort SchemaId { get; }
    public int FramePayloadBytes { get; }
    public bool IsBound => _ingress != null;

    public void EnsureReady()
    {
        AuthoritativeFixedInputIngress resolved = _resolver() ??
            throw new InvalidOperationException(
                "Physics3D authoritative fixed input was not published before the first authoritative fixed step.");
        if (_ingress != null)
        {
            if (!ReferenceEquals(_ingress, resolved))
            {
                throw new InvalidOperationException(
                    "Physics3D authoritative fixed input changed after its first successful binding.");
            }

            return;
        }

        if (resolved.SeatCapacity != SeatCapacity ||
            resolved.Config.SchemaId != SchemaId ||
            resolved.FramePayloadBytes != FramePayloadBytes)
        {
            throw new InvalidOperationException(
                "Physics3D authoritative fixed-input capacity, schema, or payload contract differs from network configuration.");
        }

        _ingress = resolved;
    }

    public FixedInputSeatActivationState GetSeatActivationState(
        in SessionSeatBinding seat,
        out uint activationTick)
    {
        AuthoritativeFixedInputIngress ingress = _ingress ??
            throw new InvalidOperationException("Physics3D authoritative fixed input has not been bound.");
        return ingress.GetSeatActivationState(in seat, out activationTick);
    }

    public FixedInputLookupResult TryRead(
        in SessionSeatBinding seat,
        uint tick,
        Span<byte> destination,
        out int bytesWritten)
    {
        AuthoritativeFixedInputIngress ingress = _ingress ??
            throw new InvalidOperationException("Physics3D authoritative fixed input has not been bound.");
        return ingress.TryGet(in seat, tick, destination, out bytesWritten);
    }
}

public enum Physics3DFixedInputConsumeResult : byte
{
    Success = 0,
    InvalidTick = 1,
    CapacityContractViolated = 2,
    Missing = 3,
    MissingAtDeadline = 4,
    InvalidSeat = 5,
    InvalidPayload = 6,
    PlayerUnavailable = 7,
}

public sealed class Physics3DAuthoritativeFixedInputConsumer
{
    private readonly IPhysics3DAuthoritativeFixedInputSource _source;
    private readonly Physics3DNetworkPlayerLifecycle _players;
    private readonly IPhysics3DWorld _physics;
    private readonly Physics3DNetworkMovementConfig _movement;
    private readonly SessionSeatBinding[] _seatScratch;
    private readonly Physics3DBodyId[] _bodyScratch;
    private readonly float[] _movementX;
    private readonly float[] _movementZ;
    private readonly byte[] _payloadScratch;

    public Physics3DAuthoritativeFixedInputConsumer(
        IPhysics3DAuthoritativeFixedInputSource source,
        Physics3DNetworkPlayerLifecycle players,
        IPhysics3DWorld physics,
        Physics3DNetworkMovementConfig movement)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _players = players ?? throw new ArgumentNullException(nameof(players));
        _physics = physics ?? throw new ArgumentNullException(nameof(physics));
        _movement = movement ?? throw new ArgumentNullException(nameof(movement));
        movement.Validate();
        if (source.SeatCapacity != players.SeatCapacity ||
            source.SchemaId != movement.SchemaId ||
            source.FramePayloadBytes != Physics3DFixedInputFrameCodec.PayloadBytes)
        {
            throw new ArgumentException("Physics3D fixed-input source, player seats, schema, and payload contracts must match.");
        }

        _seatScratch = new SessionSeatBinding[source.SeatCapacity];
        _bodyScratch = new Physics3DBodyId[source.SeatCapacity];
        _movementX = new float[source.SeatCapacity];
        _movementZ = new float[source.SeatCapacity];
        _payloadScratch = new byte[source.FramePayloadBytes];
    }

    public Physics3DFixedInputConsumeResult TryConsume(uint executingTick)
    {
        if (executingTick == 0 || executingTick > int.MaxValue)
        {
            return Physics3DFixedInputConsumeResult.InvalidTick;
        }

        _source.EnsureReady();

        if (!_players.TryCopyConnectedSeats(_seatScratch, out int seatCount))
        {
            return Physics3DFixedInputConsumeResult.CapacityContractViolated;
        }

        int activeCount = 0;
        for (int index = 0; index < seatCount; index++)
        {
            SessionSeatBinding seat = _seatScratch[index];
            FixedInputSeatActivationState activation = _source.GetSeatActivationState(
                in seat,
                out uint activationTick);
            if (activation == FixedInputSeatActivationState.InvalidSeat)
            {
                return Physics3DFixedInputConsumeResult.InvalidSeat;
            }

            if (activation == FixedInputSeatActivationState.AwaitingFirstInput || executingTick < activationTick)
            {
                continue;
            }

            FixedInputLookupResult lookup = _source.TryRead(
                in seat,
                executingTick,
                _payloadScratch,
                out int bytesWritten);
            if (lookup != FixedInputLookupResult.Present)
            {
                return lookup switch
                {
                    FixedInputLookupResult.Missing => Physics3DFixedInputConsumeResult.Missing,
                    FixedInputLookupResult.MissingAtDeadline => Physics3DFixedInputConsumeResult.MissingAtDeadline,
                    FixedInputLookupResult.InvalidSeat => Physics3DFixedInputConsumeResult.InvalidSeat,
                    FixedInputLookupResult.InvalidTick => Physics3DFixedInputConsumeResult.InvalidTick,
                    _ => Physics3DFixedInputConsumeResult.InvalidPayload,
                };
            }

            if (bytesWritten != Physics3DFixedInputFrameCodec.PayloadBytes ||
                !Physics3DFixedInputFrameCodec.TryDecode(_payloadScratch, out Physics3DFixedInputFrame frame))
            {
                return Physics3DFixedInputConsumeResult.InvalidPayload;
            }

            if (!_players.TryGetBody(in seat, out Physics3DBodyId body) || !_physics.ContainsBody(body))
            {
                return Physics3DFixedInputConsumeResult.PlayerUnavailable;
            }

            _bodyScratch[activeCount] = body;
            _movementX[activeCount] = frame.Movement.X;
            _movementZ[activeCount] = frame.Movement.Y;
            activeCount++;
        }

        for (int index = 0; index < activeCount; index++)
        {
            Physics3DBodyId body = _bodyScratch[index];
            Physics3DBodyState state = _physics.GetBodyState(body);
            Vector2 desired = new(_movementX[index], _movementZ[index]);
            desired *= _movement.MaximumSpeedCmPerSecond;
            Vector2 current = new(state.LinearVelocityCmPerSecond.X, state.LinearVelocityCmPerSecond.Z);
            Vector2 acceleration = (desired - current) * _movement.VelocityResponsePerSecond;
            float accelerationLengthSquared = acceleration.LengthSquared();
            float maximumAcceleration = _movement.MaximumAccelerationCmPerSecondSquared;
            if (accelerationLengthSquared > maximumAcceleration * maximumAcceleration)
            {
                acceleration = Vector2.Normalize(acceleration) * maximumAcceleration;
            }

            _physics.EnqueueAcceleration(body, new Vector3(acceleration.X, 0f, acceleration.Y));
        }

        return Physics3DFixedInputConsumeResult.Success;
    }
}

public sealed class Physics3DAuthoritativeFixedInputException : InvalidOperationException
{
    public Physics3DAuthoritativeFixedInputException(uint tick, Physics3DFixedInputConsumeResult result)
        : base($"Physics3D fixed input failed at authoritative tick {tick}: {result}.")
    {
        Tick = tick;
        Result = result;
    }

    public uint Tick { get; }
    public Physics3DFixedInputConsumeResult Result { get; }
}

public sealed class Physics3DAuthoritativeFixedInputSystem : ISystem<float>
{
    private readonly AuthoritativeSimulationTickState _ticks;
    private readonly Physics3DAuthoritativeFixedInputConsumer _consumer;

    public Physics3DAuthoritativeFixedInputSystem(
        AuthoritativeSimulationTickState ticks,
        Physics3DAuthoritativeFixedInputConsumer consumer)
    {
        _ticks = ticks ?? throw new ArgumentNullException(nameof(ticks));
        _consumer = consumer ?? throw new ArgumentNullException(nameof(consumer));
    }

    public void Initialize() { }
    public void BeforeUpdate(in float t) { }
    public void AfterUpdate(in float t) { }
    public void Dispose() { }

    public void Update(in float t)
    {
        if (!_ticks.IsExecuting || _ticks.ExecutingTick <= 0)
        {
            throw new InvalidOperationException("Physics3D fixed input must run inside the authoritative executing tick.");
        }

        uint tick = checked((uint)_ticks.ExecutingTick);
        Physics3DFixedInputConsumeResult result = _consumer.TryConsume(tick);
        if (result != Physics3DFixedInputConsumeResult.Success)
        {
            throw new Physics3DAuthoritativeFixedInputException(tick, result);
        }
    }
}

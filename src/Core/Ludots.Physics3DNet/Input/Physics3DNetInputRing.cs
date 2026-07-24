using System;

namespace Ludots.Core.Physics3DNet;

public enum Physics3DNetInputArrivalDisposition : byte
{
    Accepted = 1,
    Duplicate = 2,
    Late = 3,
    TooFarFuture = 4,
    AcceptedOutOfOrder = 5,
    Conflict = 6
}

public enum Physics3DNetInputLookupResult : byte
{
    Present = 1,
    Missing = 2,
    UnregisteredPlayer = 3
}

/// <summary>
/// Result of the pre-execute input gate. Distinct from post-commit frame acknowledgement.
/// </summary>
public enum Physics3DNetInputExecuteGateResultKind : byte
{
    /// <summary>Validate-only: every registered player has input for the tick.</summary>
    InputsComplete = 1,

    /// <summary>Gate success: inputs complete and <see cref="Physics3DNetTickLifecycle.BeginExecute"/> started.</summary>
    BeganExecute = 2,

    /// <summary>Gate failure: one or more registered players are missing input; execution did not start.</summary>
    MissingInputs = 3
}

public readonly struct Physics3DNetQuantizedAxes2
{
    public Physics3DNetQuantizedAxes2(short x, short y)
    {
        X = x;
        Y = y;
    }

    public short X { get; }
    public short Y { get; }
}

public readonly struct Physics3DNetInputSubmit
{
    public Physics3DNetInputSubmit(
        long tick,
        int networkPlayerId,
        int generation,
        uint sequence,
        Physics3DNetQuantizedAxes2 moveAxes,
        Physics3DNetQuantizedAxes2 lookAxes,
        uint buttons)
    {
        Physics3DNetValidation.RequirePositiveTick(tick, nameof(tick));
        Physics3DNetValidation.RequireNonNegativeId(networkPlayerId, nameof(networkPlayerId));
        Physics3DNetValidation.RequirePositiveGeneration(generation, nameof(generation));

        Tick = tick;
        NetworkPlayerId = networkPlayerId;
        Generation = generation;
        Sequence = sequence;
        MoveAxes = moveAxes;
        LookAxes = lookAxes;
        Buttons = buttons;
    }

    public long Tick { get; }
    public int NetworkPlayerId { get; }
    public int Generation { get; }
    public uint Sequence { get; }
    public Physics3DNetQuantizedAxes2 MoveAxes { get; }
    public Physics3DNetQuantizedAxes2 LookAxes { get; }
    public uint Buttons { get; }
}

public readonly struct Physics3DNetInputArrivalResult
{
    public Physics3DNetInputArrivalResult(Physics3DNetInputArrivalDisposition disposition, long confirmationTick)
    {
        Disposition = disposition;
        ConfirmationTick = confirmationTick;
    }

    public Physics3DNetInputArrivalDisposition Disposition { get; }
    public long ConfirmationTick { get; }

    public bool Accepted =>
        Disposition is Physics3DNetInputArrivalDisposition.Accepted
            or Physics3DNetInputArrivalDisposition.AcceptedOutOfOrder;
}

public readonly struct Physics3DNetInputFrameView
{
    public Physics3DNetInputFrameView(
        long tick,
        int networkPlayerId,
        int generation,
        uint sequence,
        Physics3DNetQuantizedAxes2 moveAxes,
        Physics3DNetQuantizedAxes2 lookAxes,
        uint buttons,
        long confirmationTick)
    {
        Physics3DNetValidation.RequirePositiveTick(tick, nameof(tick));
        Physics3DNetValidation.RequireNonNegativeId(networkPlayerId, nameof(networkPlayerId));
        Physics3DNetValidation.RequirePositiveGeneration(generation, nameof(generation));
        if (confirmationTick < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(confirmationTick), confirmationTick, "Confirmation tick cannot be negative.");
        }

        Tick = tick;
        NetworkPlayerId = networkPlayerId;
        Generation = generation;
        Sequence = sequence;
        MoveAxes = moveAxes;
        LookAxes = lookAxes;
        Buttons = buttons;
        ConfirmationTick = confirmationTick;
    }

    public long Tick { get; }
    public int NetworkPlayerId { get; }
    public int Generation { get; }
    public uint Sequence { get; }
    public Physics3DNetQuantizedAxes2 MoveAxes { get; }
    public Physics3DNetQuantizedAxes2 LookAxes { get; }
    public uint Buttons { get; }
    public long ConfirmationTick { get; }
}

public readonly struct Physics3DNetInputExecuteGateResult
{
    public Physics3DNetInputExecuteGateResult(Physics3DNetInputExecuteGateResultKind kind, int missingCount)
    {
        Kind = kind;
        MissingCount = missingCount;
    }

    public Physics3DNetInputExecuteGateResultKind Kind { get; }
    public int MissingCount { get; }
}

/// <summary>
/// Fixed-capacity SoA input ring for up to 150 players.
/// Late/future windows read <see cref="Physics3DNetTickLifecycle.CommittedTick"/>; this type never owns authority.
/// Missing frames are reported explicitly; neutral input is never invented.
/// UDP reorder into an empty in-window cell is accepted as AcceptedOutOfOrder.
/// </summary>
public sealed class Physics3DNetInputRing
{
    private readonly Physics3DNetTickLifecycle _lifecycle;
    private readonly int _playerCapacity;
    private readonly int _historyTicks;
    private readonly int _maxFutureTicks;
    private readonly int _slotCapacity;

    private readonly long[] _tick;
    private readonly int[] _networkPlayerId;
    private readonly int[] _generation;
    private readonly uint[] _sequence;
    private readonly short[] _moveX;
    private readonly short[] _moveY;
    private readonly short[] _lookX;
    private readonly short[] _lookY;
    private readonly uint[] _buttons;
    private readonly Physics3DNetInputArrivalDisposition[] _disposition;
    private readonly long[] _confirmationTick;
    private readonly bool[] _occupied;

    private readonly int[] _playerNetworkIdBySlot;
    private readonly int[] _playerGenerationBySlot;
    private readonly bool[] _playerRegistered;
    private readonly uint[] _lastAcceptedSequenceBySlot;
    private readonly long[] _lastAcceptedTickBySlot;
    private readonly bool[] _hasAcceptedBySlot;

    public Physics3DNetInputRing(Physics3DNetConfig config, Physics3DNetTickLifecycle lifecycle)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(lifecycle);
        config.Validate();

        _lifecycle = lifecycle;
        _playerCapacity = config.PlayerCapacity;
        _historyTicks = config.InputHistoryTicksPerPlayer;
        _maxFutureTicks = config.MaxFutureInputTicks;
        _slotCapacity = checked(_playerCapacity * _historyTicks);

        _tick = new long[_slotCapacity];
        _networkPlayerId = new int[_slotCapacity];
        _generation = new int[_slotCapacity];
        _sequence = new uint[_slotCapacity];
        _moveX = new short[_slotCapacity];
        _moveY = new short[_slotCapacity];
        _lookX = new short[_slotCapacity];
        _lookY = new short[_slotCapacity];
        _buttons = new uint[_slotCapacity];
        _disposition = new Physics3DNetInputArrivalDisposition[_slotCapacity];
        _confirmationTick = new long[_slotCapacity];
        _occupied = new bool[_slotCapacity];

        _playerNetworkIdBySlot = new int[_playerCapacity];
        _playerGenerationBySlot = new int[_playerCapacity];
        _playerRegistered = new bool[_playerCapacity];
        _lastAcceptedSequenceBySlot = new uint[_playerCapacity];
        _lastAcceptedTickBySlot = new long[_playerCapacity];
        _hasAcceptedBySlot = new bool[_playerCapacity];
    }

    public Physics3DNetTickLifecycle Lifecycle => _lifecycle;
    public int PlayerCapacity => _playerCapacity;
    public int HistoryTicksPerPlayer => _historyTicks;
    public int SlotCapacity => _slotCapacity;

    public int AcceptedCount { get; private set; }
    public int AcceptedOutOfOrderCount { get; private set; }
    public int DuplicateCount { get; private set; }
    public int LateCount { get; private set; }
    public int TooFarFutureCount { get; private set; }
    public int ConflictCount { get; private set; }
    public int MissingLookupCount { get; private set; }

    public int RegisterPlayer(int networkPlayerId, int generation, int playerSlot)
    {
        if ((uint)playerSlot >= (uint)_playerCapacity)
        {
            throw new Physics3DNetCapacityExceededException("input player slots", _playerCapacity, tick: 0);
        }

        Physics3DNetValidation.RequireNonNegativeId(networkPlayerId, nameof(networkPlayerId));
        Physics3DNetValidation.RequirePositiveGeneration(generation, nameof(generation));

        if (_playerRegistered[playerSlot])
        {
            if (_playerNetworkIdBySlot[playerSlot] == networkPlayerId
                && _playerGenerationBySlot[playerSlot] == generation)
            {
                return playerSlot;
            }

            throw new InvalidOperationException(
                $"Player slot {playerSlot} already registered to network id {_playerNetworkIdBySlot[playerSlot]} generation {_playerGenerationBySlot[playerSlot]}. Unregister before reuse.");
        }

        for (int i = 0; i < _playerCapacity; i++)
        {
            if (_playerRegistered[i] && _playerNetworkIdBySlot[i] == networkPlayerId)
            {
                throw new InvalidOperationException(
                    $"Network player id {networkPlayerId} is already registered in slot {i}. Player ids must be unique.");
            }
        }

        _playerRegistered[playerSlot] = true;
        _playerNetworkIdBySlot[playerSlot] = networkPlayerId;
        _playerGenerationBySlot[playerSlot] = generation;
        _hasAcceptedBySlot[playerSlot] = false;
        _lastAcceptedSequenceBySlot[playerSlot] = 0;
        _lastAcceptedTickBySlot[playerSlot] = 0;
        return playerSlot;
    }

    public void UnregisterPlayer(int playerSlot)
    {
        if ((uint)playerSlot >= (uint)_playerCapacity)
        {
            throw new ArgumentOutOfRangeException(nameof(playerSlot), playerSlot, "Player slot out of range.");
        }

        if (!_playerRegistered[playerSlot])
        {
            throw new InvalidOperationException($"Player slot {playerSlot} is not registered.");
        }

        int baseIndex = playerSlot * _historyTicks;
        for (int local = 0; local < _historyTicks; local++)
        {
            ClearHistoryCell(baseIndex + local);
        }

        _playerRegistered[playerSlot] = false;
        _playerNetworkIdBySlot[playerSlot] = 0;
        _playerGenerationBySlot[playerSlot] = 0;
        _hasAcceptedBySlot[playerSlot] = false;
        _lastAcceptedSequenceBySlot[playerSlot] = 0;
        _lastAcceptedTickBySlot[playerSlot] = 0;
    }

    public bool TryFindPlayerSlot(int networkPlayerId, int generation, out int playerSlot)
    {
        for (int i = 0; i < _playerCapacity; i++)
        {
            if (_playerRegistered[i]
                && _playerNetworkIdBySlot[i] == networkPlayerId
                && _playerGenerationBySlot[i] == generation)
            {
                playerSlot = i;
                return true;
            }
        }

        playerSlot = -1;
        return false;
    }

    public Physics3DNetInputArrivalResult Submit(in Physics3DNetInputSubmit submit)
    {
        if (!TryFindPlayerSlot(submit.NetworkPlayerId, submit.Generation, out int playerSlot))
        {
            throw new InvalidOperationException(
                $"Player network id {submit.NetworkPlayerId} generation {submit.Generation} is not registered.");
        }

        long committedTick = _lifecycle.CommittedTick;
        long earliestAllowed = committedTick + 1;
        if (submit.Tick < earliestAllowed)
        {
            LateCount++;
            return new Physics3DNetInputArrivalResult(Physics3DNetInputArrivalDisposition.Late, confirmationTick: 0);
        }

        long latestAllowed = committedTick + _maxFutureTicks;
        if (submit.Tick > latestAllowed)
        {
            TooFarFutureCount++;
            return new Physics3DNetInputArrivalResult(Physics3DNetInputArrivalDisposition.TooFarFuture, confirmationTick: 0);
        }

        int slot = SlotIndex(playerSlot, submit.Tick);
        if (_occupied[slot] && _tick[slot] == submit.Tick)
        {
            if (_sequence[slot] == submit.Sequence && PayloadEquals(slot, submit))
            {
                DuplicateCount++;
                return new Physics3DNetInputArrivalResult(
                    Physics3DNetInputArrivalDisposition.Duplicate,
                    _confirmationTick[slot]);
            }

            ConflictCount++;
            return new Physics3DNetInputArrivalResult(
                Physics3DNetInputArrivalDisposition.Conflict,
                _confirmationTick[slot]);
        }

        // Empty in-window cell accepts UDP reorder even when a newer tick arrived first.
        bool outOfOrder = _hasAcceptedBySlot[playerSlot]
            && submit.Tick < _lastAcceptedTickBySlot[playerSlot];

        Physics3DNetInputArrivalDisposition disposition = outOfOrder
            ? Physics3DNetInputArrivalDisposition.AcceptedOutOfOrder
            : Physics3DNetInputArrivalDisposition.Accepted;

        _tick[slot] = submit.Tick;
        _networkPlayerId[slot] = submit.NetworkPlayerId;
        _generation[slot] = submit.Generation;
        _sequence[slot] = submit.Sequence;
        _moveX[slot] = submit.MoveAxes.X;
        _moveY[slot] = submit.MoveAxes.Y;
        _lookX[slot] = submit.LookAxes.X;
        _lookY[slot] = submit.LookAxes.Y;
        _buttons[slot] = submit.Buttons;
        _disposition[slot] = disposition;
        _confirmationTick[slot] = 0;
        _occupied[slot] = true;

        if (!_hasAcceptedBySlot[playerSlot]
            || submit.Tick > _lastAcceptedTickBySlot[playerSlot]
            || (submit.Tick == _lastAcceptedTickBySlot[playerSlot]
                && submit.Sequence > _lastAcceptedSequenceBySlot[playerSlot]))
        {
            _lastAcceptedSequenceBySlot[playerSlot] = submit.Sequence;
            _lastAcceptedTickBySlot[playerSlot] = submit.Tick;
        }

        _hasAcceptedBySlot[playerSlot] = true;

        if (disposition == Physics3DNetInputArrivalDisposition.AcceptedOutOfOrder)
        {
            AcceptedOutOfOrderCount++;
        }
        else
        {
            AcceptedCount++;
        }

        return new Physics3DNetInputArrivalResult(disposition, confirmationTick: 0);
    }

    public Physics3DNetInputLookupResult TryGet(int playerSlot, long tick, out Physics3DNetInputFrameView frame)
    {
        frame = default;
        if ((uint)playerSlot >= (uint)_playerCapacity || !_playerRegistered[playerSlot])
        {
            return Physics3DNetInputLookupResult.UnregisteredPlayer;
        }

        int slot = SlotIndex(playerSlot, tick);
        if (!_occupied[slot] || _tick[slot] != tick)
        {
            MissingLookupCount++;
            return Physics3DNetInputLookupResult.Missing;
        }

        frame = new Physics3DNetInputFrameView(
            _tick[slot],
            _networkPlayerId[slot],
            _generation[slot],
            _sequence[slot],
            new Physics3DNetQuantizedAxes2(_moveX[slot], _moveY[slot]),
            new Physics3DNetQuantizedAxes2(_lookX[slot], _lookY[slot]),
            _buttons[slot],
            _confirmationTick[slot]);
        return Physics3DNetInputLookupResult.Present;
    }

    /// <summary>
    /// Phase 1a (pre-execute, validate-only): atomically reports missing inputs without starting execution
    /// and without mutating confirmation metadata or lifecycle authority.
    /// </summary>
    public bool TryValidateInputsForExecute(
        long tick,
        Span<int> missingPlayerSlotsDestination,
        out Physics3DNetInputExecuteGateResult result)
    {
        Physics3DNetValidation.RequirePositiveTick(tick, nameof(tick));

        int missingCount = CountMissingInputs(tick);
        if (missingCount > missingPlayerSlotsDestination.Length)
        {
            throw new Physics3DNetCapacityExceededException(
                "execute-gate missing-player destination",
                missingPlayerSlotsDestination.Length,
                tick);
        }

        if (missingCount > 0)
        {
            WriteMissingSlots(tick, missingPlayerSlotsDestination, missingCount);
            result = new Physics3DNetInputExecuteGateResult(
                Physics3DNetInputExecuteGateResultKind.MissingInputs,
                missingCount);
            return false;
        }

        result = new Physics3DNetInputExecuteGateResult(
            Physics3DNetInputExecuteGateResultKind.InputsComplete,
            missingCount: 0);
        return true;
    }

    /// <summary>
    /// Phase 1b (pre-execute gate): atomically validates complete inputs, then starts
    /// <see cref="Physics3DNetTickLifecycle.BeginExecute"/>. On missing inputs, BeginExecute is not called.
    /// </summary>
    public bool TryBeginAuthoritativeExecute(
        long tick,
        Span<int> missingPlayerSlotsDestination,
        out Physics3DNetInputExecuteGateResult result)
    {
        if (!TryValidateInputsForExecute(tick, missingPlayerSlotsDestination, out result))
        {
            return false;
        }

        _lifecycle.BeginExecute(tick);
        result = new Physics3DNetInputExecuteGateResult(
            Physics3DNetInputExecuteGateResultKind.BeganExecute,
            missingCount: 0);
        return true;
    }

    /// <summary>
    /// Strict Phase 1 gate. Throws <see cref="Physics3DNetMissingInputException"/> without starting execution
    /// when inputs are missing.
    /// </summary>
    public void BeginAuthoritativeExecute(long tick)
    {
        Physics3DNetValidation.RequirePositiveTick(tick, nameof(tick));
        int missingCount = CountMissingInputs(tick);
        if (missingCount > 0)
        {
            throw new Physics3DNetMissingInputException(tick, missingCount);
        }

        _lifecycle.BeginExecute(tick);
    }

    /// <summary>
    /// Phase 2 (post-commit acknowledgement): marks frame confirmation metadata only.
    /// Requires the shared lifecycle to have already committed <paramref name="tick"/>.
    /// Never advances ExecutingTick / CommittedTick / SnapshotTick.
    /// </summary>
    public void AcknowledgeInputFramesAfterCommit(long tick)
    {
        Physics3DNetValidation.RequirePositiveTick(tick, nameof(tick));

        if (_lifecycle.IsExecuting)
        {
            throw new InvalidOperationException(
                $"Cannot acknowledge input frames for tick {tick} while ExecutingTick {_lifecycle.ExecutingTick} is still open. Commit the lifecycle first.");
        }

        if (tick > _lifecycle.CommittedTick)
        {
            throw new InvalidOperationException(
                $"Cannot acknowledge input frames for tick {tick} before lifecycle Commit. CommittedTick is {_lifecycle.CommittedTick}.");
        }

        int missingCount = CountMissingInputs(tick);
        if (missingCount > 0)
        {
            throw new Physics3DNetMissingInputException(tick, missingCount);
        }

        for (int playerSlot = 0; playerSlot < _playerCapacity; playerSlot++)
        {
            if (!_playerRegistered[playerSlot])
            {
                continue;
            }

            int slot = SlotIndex(playerSlot, tick);
            _confirmationTick[slot] = tick;
        }
    }

    private int CountMissingInputs(long tick)
    {
        int missingCount = 0;
        for (int playerSlot = 0; playerSlot < _playerCapacity; playerSlot++)
        {
            if (!_playerRegistered[playerSlot])
            {
                continue;
            }

            int slot = SlotIndex(playerSlot, tick);
            if (!_occupied[slot] || _tick[slot] != tick)
            {
                missingCount++;
            }
        }

        return missingCount;
    }

    private void WriteMissingSlots(long tick, Span<int> destination, int missingCount)
    {
        int write = 0;
        for (int playerSlot = 0; playerSlot < _playerCapacity; playerSlot++)
        {
            if (!_playerRegistered[playerSlot])
            {
                continue;
            }

            int slot = SlotIndex(playerSlot, tick);
            if (!_occupied[slot] || _tick[slot] != tick)
            {
                destination[write++] = playerSlot;
            }
        }

        if (write != missingCount)
        {
            throw new InvalidOperationException(
                $"Missing-input scan became inconsistent for tick {tick}: expected {missingCount}, wrote {write}.");
        }
    }

    public int CountRegisteredPlayers()
    {
        int count = 0;
        for (int i = 0; i < _playerCapacity; i++)
        {
            if (_playerRegistered[i])
            {
                count++;
            }
        }

        return count;
    }

    private bool PayloadEquals(int slot, in Physics3DNetInputSubmit submit) =>
        _networkPlayerId[slot] == submit.NetworkPlayerId
        && _generation[slot] == submit.Generation
        && _moveX[slot] == submit.MoveAxes.X
        && _moveY[slot] == submit.MoveAxes.Y
        && _lookX[slot] == submit.LookAxes.X
        && _lookY[slot] == submit.LookAxes.Y
        && _buttons[slot] == submit.Buttons;

    private void ClearHistoryCell(int slot)
    {
        _tick[slot] = 0;
        _networkPlayerId[slot] = 0;
        _generation[slot] = 0;
        _sequence[slot] = 0;
        _moveX[slot] = 0;
        _moveY[slot] = 0;
        _lookX[slot] = 0;
        _lookY[slot] = 0;
        _buttons[slot] = 0;
        _disposition[slot] = 0;
        _confirmationTick[slot] = 0;
        _occupied[slot] = false;
    }

    private int SlotIndex(int playerSlot, long tick)
    {
        int tickIndex = (int)(tick % _historyTicks);
        if (tickIndex < 0)
        {
            tickIndex += _historyTicks;
        }

        return (playerSlot * _historyTicks) + tickIndex;
    }
}

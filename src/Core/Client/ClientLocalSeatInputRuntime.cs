using System;
using System.Collections.Generic;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Scripting;

namespace Ludots.Core.Client
{
    /// <summary>
    /// One seat's input interpretation channel: a handler over the shared backend with its own
    /// active context list and action states, plus the accumulator/reader pair that freezes the
    /// seat's authoritative input snapshot per logic tick. Two seats' action spaces never merge,
    /// so per-seat axis reads cannot overwrite each other.
    /// </summary>
    public sealed class ClientLocalSeatInputChannel
    {
        private readonly ControlSchemeRuntime _schemes;
        private string[] _activeSchemeContexts = Array.Empty<string>();

        internal ClientLocalSeatInputChannel(
            string seatId,
            PlayerInputHandler handler,
            AuthoritativeInputAccumulator accumulator,
            FrozenInputActionReader reader,
            ControlSchemeRuntime schemes)
        {
            SeatId = seatId;
            Handler = handler;
            Accumulator = accumulator;
            Reader = reader;
            _schemes = schemes;
        }

        public string SeatId { get; }
        public PlayerInputHandler Handler { get; }
        public AuthoritativeInputAccumulator Accumulator { get; }
        public FrozenInputActionReader Reader { get; }

        /// <summary>Scheme activated on this channel; 0 = no scheme activated for this seat.</summary>
        public int ActiveSchemeId { get; internal set; }

        /// <summary>Bumped on every channel scheme switch so consumers can cache per-seat bindings.</summary>
        public uint SchemeRevision { get; internal set; }

        /// <summary>
        /// The channel scheme's axis move declaration, resolved from the shared control scheme
        /// catalog. Returns false when the seat has no activated scheme or that scheme declares
        /// no axis move.
        /// </summary>
        public bool TryGetActiveAxisMove(out ControlSchemeAxisMoveBinding axisMove)
        {
            axisMove = default;
            if (ActiveSchemeId == 0 ||
                !_schemes.TryGetSchemeActivation(ActiveSchemeId, out ControlSchemeActivation activation) ||
                !activation.HasAxisMove)
            {
                return false;
            }

            axisMove = activation.AxisMove;
            return true;
        }

        internal void ActivateScheme(int schemeId, in ControlSchemeActivation activation)
        {
            for (int i = 0; i < _activeSchemeContexts.Length; i++)
            {
                Handler.PopContext(_activeSchemeContexts[i]);
            }

            for (int i = 0; i < activation.InputContexts.Length; i++)
            {
                Handler.PushContext(activation.InputContexts[i]);
            }

            _activeSchemeContexts = activation.InputContexts;
            ActiveSchemeId = schemeId;
            SchemeRevision++;
        }
    }

    /// <summary>
    /// Per-seat input interpretation stacks for the client-local seat table. The sole seat keeps
    /// the engine-global chain (adapter-bound handler, global accumulator/reader under
    /// <see cref="CoreServiceKeys.AuthoritativeInput"/>, global
    /// <see cref="ControlSchemeRuntime"/> activation) as its interpretation stack — with exactly
    /// one seat this runtime holds no channels and changes nothing. With multiple seats every
    /// seat gets its own channel; scheme compilation and the allowed-set stay in the single
    /// control scheme catalog, only activation state is per seat. A seat's declared
    /// <c>controlSchemeId</c> activates on its own channel at seat publish time; hot switches go
    /// through <see cref="TrySwitchSeatScheme"/> and write back to that seat only.
    /// </summary>
    public sealed class ClientLocalSeatInputRuntime
    {
        private readonly IDictionary<string, object> _globals;
        private readonly ControlSchemeRuntime _schemes;
        private readonly InputConfigRoot _inputConfig;
        private readonly IReadOnlyList<string> _startupInputContexts;
        private readonly Dictionary<string, ClientLocalSeatInputChannel> _channels = new(StringComparer.Ordinal);
        private readonly List<ClientLocalSeatInputChannel> _orderedChannels = new();
        private EmptyInputBackend? _emptyBackend;

        public ClientLocalSeatInputRuntime(
            IDictionary<string, object> globals,
            ControlSchemeRuntime schemes,
            InputConfigRoot inputConfig,
            IReadOnlyList<string>? startupInputContexts = null)
        {
            _globals = globals ?? throw new ArgumentNullException(nameof(globals));
            _schemes = schemes ?? throw new ArgumentNullException(nameof(schemes));
            _inputConfig = inputConfig ?? throw new ArgumentNullException(nameof(inputConfig));
            _startupInputContexts = startupInputContexts ?? Array.Empty<string>();
        }

        public int ChannelCount => _orderedChannels.Count;

        public bool TryGetChannel(string seatId, out ClientLocalSeatInputChannel channel)
        {
            channel = null!;
            return !string.IsNullOrWhiteSpace(seatId) && _channels.TryGetValue(seatId.Trim(), out channel!);
        }

        /// <summary>
        /// Rebuilds the channel table from the current seat table. Sole-seat (and zero-seat)
        /// tables release every channel: the sole seat's interpretation stack is the global
        /// chain and this runtime must not shadow it. For multi-seat tables each seat gets a
        /// channel; seats declaring a controlSchemeId activate it on their own channel with the
        /// same fail-fast semantics as the sole-seat activation chain (unknown or refused scheme
        /// is a configuration error, not a silent fallback).
        /// </summary>
        public void PublishSeats(ClientLocalSeatRegistry seats)
        {
            ArgumentNullException.ThrowIfNull(seats);
            _channels.Clear();
            _orderedChannels.Clear();
            if (seats.Count <= 1)
            {
                return;
            }

            IInputBackend backend = ResolveChannelBackend();
            IReadOnlyList<string> ids = seats.SeatIds;
            for (int i = 0; i < ids.Count; i++)
            {
                ClientLocalSeat seat = seats.Require(ids[i]);
                var handler = new PlayerInputHandler(backend, _inputConfig);
                for (int c = 0; c < _startupInputContexts.Count; c++)
                {
                    handler.PushContext(_startupInputContexts[c]);
                }

                var channel = new ClientLocalSeatInputChannel(
                    seat.SeatId,
                    handler,
                    new AuthoritativeInputAccumulator(),
                    new FrozenInputActionReader(),
                    _schemes);
                _channels.Add(seat.SeatId, channel);
                _orderedChannels.Add(channel);

                if (!string.IsNullOrWhiteSpace(seat.ControlSchemeId))
                {
                    ActivateDeclaredScheme(seat, channel);
                }
            }
        }

        /// <summary>
        /// Per visual frame: update every channel's handler and capture the frame into the
        /// channel accumulator. Mirrors the global InputRuntimeSystem pump (UI capture blocks
        /// channel handlers the same way); the sole-seat global chain is not touched here.
        /// </summary>
        public void UpdateVisualFrame()
        {
            if (_orderedChannels.Count == 0)
            {
                return;
            }

            bool uiCaptured =
                _globals.TryGetValue(CoreServiceKeys.UiCaptured.Name, out object? capturedObj) &&
                capturedObj is bool captured &&
                captured;
            for (int i = 0; i < _orderedChannels.Count; i++)
            {
                ClientLocalSeatInputChannel channel = _orderedChannels[i];
                channel.Handler.InputBlocked = uiCaptured;
                channel.Handler.Update();
                channel.Accumulator.CaptureVisualFrame(channel.Handler);
            }
        }

        /// <summary>
        /// Per logic tick freeze of every channel snapshot, driven from the authoritative input
        /// snapshot system so replay isolation discards channel live input on the same tick the
        /// global snapshot is replaced or isolated.
        /// </summary>
        public void FreezeSnapshots(bool discardLiveInput)
        {
            for (int i = 0; i < _orderedChannels.Count; i++)
            {
                ClientLocalSeatInputChannel channel = _orderedChannels[i];
                if (discardLiveInput)
                {
                    channel.Accumulator.DiscardLiveInput();
                    channel.Reader.ClearSnapshot();
                }
                else
                {
                    channel.Accumulator.BuildTickSnapshot(channel.Reader);
                }
            }
        }

        /// <summary>
        /// Hot-switch a seat's scheme and write the choice back to that seat's
        /// <see cref="ClientLocalSeat.ControlSchemeId"/>. The sole seat's stack is the global
        /// runtime, so its switch keeps explicit-user semantics (preference store write); a
        /// multi-seat channel switches runtime-only on its own handler — the preference store is
        /// client-global and belongs to no individual seat. Returns false when the scheme is
        /// unknown, not installed, or refused by the mod allowed-set.
        /// </summary>
        public bool TrySwitchSeatScheme(ClientLocalSeatRegistry seats, string seatId, string schemeId)
        {
            ArgumentNullException.ThrowIfNull(seats);
            ClientLocalSeat seat = seats.Require(seatId);
            if (string.IsNullOrWhiteSpace(schemeId))
            {
                return false;
            }

            string trimmed = schemeId.Trim();
            if (!_schemes.SchemeIdRegistry.TryGetId(trimmed, out int compiledSchemeId))
            {
                return false;
            }

            if (seats.Count == 1)
            {
                if (!_schemes.TrySwitch(compiledSchemeId))
                {
                    return false;
                }

                seat.ControlSchemeId = trimmed;
                return true;
            }

            if (!TryGetChannel(seat.SeatId, out ClientLocalSeatInputChannel channel) ||
                !_schemes.IsAllowed(compiledSchemeId) ||
                !_schemes.TryGetSchemeActivation(compiledSchemeId, out ControlSchemeActivation activation))
            {
                return false;
            }

            channel.ActivateScheme(compiledSchemeId, activation);
            seat.ControlSchemeId = trimmed;
            return true;
        }

        private void ActivateDeclaredScheme(ClientLocalSeat seat, ClientLocalSeatInputChannel channel)
        {
            string schemeId = seat.ControlSchemeId!.Trim();
            if (!_schemes.SchemeIdRegistry.TryGetId(schemeId, out int compiledSchemeId))
            {
                throw new InvalidOperationException(
                    $"ClientLocalSeat '{seat.SeatId}' declares control scheme '{schemeId}' which is not installed.");
            }

            if (!_schemes.IsAllowed(compiledSchemeId))
            {
                throw new InvalidOperationException(
                    $"ClientLocalSeat '{seat.SeatId}' declares control scheme '{schemeId}' which the mod allowed-set refuses.");
            }

            if (!_schemes.TryGetSchemeActivation(compiledSchemeId, out ControlSchemeActivation activation))
            {
                throw new InvalidOperationException(
                    $"ClientLocalSeat '{seat.SeatId}' declares control scheme '{schemeId}' which is not installed.");
            }

            channel.ActivateScheme(compiledSchemeId, activation);
        }

        private IInputBackend ResolveChannelBackend()
        {
            if (_globals.TryGetValue(CoreServiceKeys.InputBackend.Name, out object? backendObj) &&
                backendObj is IInputBackend backend)
            {
                return backend;
            }

            _emptyBackend ??= new EmptyInputBackend();
            return _emptyBackend;
        }
    }
}

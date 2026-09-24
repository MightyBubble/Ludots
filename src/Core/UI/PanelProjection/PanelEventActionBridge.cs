using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Ludots.Core.Client;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Scripting;
using Ludots.Core.UI.PanelActivation;

namespace Ludots.Core.UI.PanelProjection
{
    /// <summary>
    /// Production wiring between retained panel buttons and the semantic action pipeline:
    /// an admitted, payload-validated panel event injects the action edge into the firing
    /// seat's input channel — the same convergence layer device input and bots enter
    /// through. The action id is the event id (one vocabulary); context-owned mounts gate
    /// consumption exactly like device actions, so an unbound context simply leaves the
    /// click inert instead of needing a capture flag. Audience admission and strict payload
    /// validation run before anything reaches gameplay; a missing seat channel or an
    /// undeclared action id is a named failure, never a silent drop.
    /// </summary>
    public sealed class PanelEventActionBridge
    {
        private const int ReleaseHoldTicks = 2;

        private readonly UiPanelActivationStore _activation;
        private readonly Func<ClientLocalSeatInputRuntime?> _seatInput;
        private readonly Func<ClientLocalSeatRegistry?> _seats;
        private readonly Func<PlayerInputHandler?> _globalHandler;
        private readonly Action<string>? _onRejected;
        /// <summary>Companion custom-event prefix: payload-carrying fires also dispatch "panel.&lt;eventId&gt;" in-tick.</summary>
        public const string CompanionEventPrefix = "panel.";

        private readonly Dictionary<string, PanelEventDispatcher> _dispatchers = new(StringComparer.Ordinal);
        private readonly List<PendingRelease> _pendingReleases = new();
        private readonly List<PendingCompanionEvent> _pendingCompanions = new();

        public PanelEventActionBridge(
            UiPanelActivationStore activation,
            Func<ClientLocalSeatInputRuntime?> seatInput,
            Func<ClientLocalSeatRegistry?> seats,
            Func<PlayerInputHandler?> globalHandler,
            Action<string>? onRejected = null)
        {
            _activation = activation ?? throw new ArgumentNullException(nameof(activation));
            _seatInput = seatInput ?? throw new ArgumentNullException(nameof(seatInput));
            _seats = seats;
            _globalHandler = globalHandler;
            _onRejected = onRejected;
        }

        /// <summary>Last refusal reason surfaced for diagnostics and tests (null after an admitted fire).</summary>
        public string? LastRefusalReason { get; private set; }

        public PanelEventFireResult FireFromSeat(PanelTemplate template, string eventId, JsonObject payload, string seatId)
        {
            ArgumentNullException.ThrowIfNull(template);
            if (string.IsNullOrWhiteSpace(eventId))
            {
                throw new ArgumentException("Panel event id is required.", nameof(eventId));
            }
            ArgumentNullException.ThrowIfNull(payload);
            if (string.IsNullOrWhiteSpace(seatId))
            {
                throw new ArgumentException("Panel events attribute to a seat.", nameof(seatId));
            }

            PanelEventDispatcher dispatcher = GetDispatcher(template);
            PanelEventFireResult result = dispatcher.FireFromSeat(eventId, payload, seatId);
            if (!result.Admitted)
            {
                LastRefusalReason = result.Reason;
                _onRejected?.Invoke(result.Reason ?? $"Panel event '{eventId}' was refused.");
                return result;
            }

            LastRefusalReason = null;
            InjectActionEdge(seatId, eventId);
            if (result.Payload is { Count: > 0 })
            {
                _pendingCompanions.Add(new PendingCompanionEvent(eventId, result.Payload));
            }

            return result;
        }

        /// <summary>
        /// Releases held synthetic presses. Held for a couple of engine updates so the press
        /// edge is observed exactly once by the authoritative snapshot, then released so the
        /// action does not stay down.
        /// </summary>
        public void Update()
        {
            for (int i = _pendingReleases.Count - 1; i >= 0; i--)
            {
                PendingRelease entry = _pendingReleases[i];
                if (entry.RemainingTicks <= 1)
                {
                    _pendingReleases.RemoveAt(i);
                    entry.Handler.InjectButtonRelease(entry.ActionId);
                }
                else
                {
                    _pendingReleases[i] = entry with { RemainingTicks = entry.RemainingTicks - 1 };
                }
            }
        }

        private void InjectActionEdge(string seatId, string actionId)
        {
            PlayerInputHandler? handler = null;
            ClientLocalSeatInputRuntime? seatInput = _seatInput();
            if (seatInput != null && seatInput.TryGetChannel(seatId, out ClientLocalSeatInputChannel channel))
            {
                handler = channel.Handler;
            }
            else
            {
                // Sole-seat clients keep the global handler/snapshot chain (no per-seat channels);
                // only the sole seat may fall back to it — anything else is an attribution bug.
                // Policy twin: TriggerGraphActionBindingSystem (read side) routes multi-seat
                // per channel and sole-seat through the global reader — keep the two in lockstep.
                ClientLocalSeatRegistry? seats = _seats();
                PlayerInputHandler? global = _globalHandler();
                if (seats != null && global != null && seats.Count == 1 && seats.SeatIds[0] == seatId)
                {
                    handler = global;
                }
            }

            if (handler == null)
            {
                throw new InvalidOperationException(
                    $"PANEL.EVENT.ERR.SeatChannelMissing: panel event action '{actionId}' cannot attribute to " +
                    $"seat '{seatId}' — the seat has no input channel.");
            }

            if (!handler.HasAction(actionId))
            {
                throw new InvalidOperationException(
                    $"PANEL.EVENT.ERR.ActionNotDeclared: panel event '{actionId}' is not a declared input action — " +
                    "panel event ids must be registered in the input config action catalog (eventId 即 action id).");
            }

            handler.InjectButtonPress(actionId);
            _pendingReleases.Add(new PendingRelease(handler, actionId, ReleaseHoldTicks));
        }

        private PanelEventDispatcher GetDispatcher(PanelTemplate template)
        {
            if (!_dispatchers.TryGetValue(template.Id, out PanelEventDispatcher? dispatcher))
            {
                // The sink is the injection point itself: admission already ran in FireFromSeat,
                // validated payload flows into the semantic action edge below.
                dispatcher = new PanelEventDispatcher(template, static (_, _) => { }, _activation);
                _dispatchers.Add(template.Id, dispatcher);
            }

            return dispatcher;
        }

        /// <summary>
        /// Drains queued companion events inside the engine tick (EventDispatch phase) —
        /// never at presentation time. Each fires the custom event "panel.&lt;eventId&gt;"
        /// with the validated payload fields set on the context; the mod declares the event
        /// in Events/custom_events.json with a matching schema, and context profiles mount
        /// the consuming graph by event. Undeclared events fail named at dispatch.
        /// Returns the number of events dispatched.
        /// </summary>
        public int DispatchPendingCompanions(
            Scripting.TriggerManager triggers,
            Gameplay.MapTriggers.CustomEventNameRegistry customEvents,
            Map.MapId mapId,
            Func<ScriptContext> createContext)
        {
            ArgumentNullException.ThrowIfNull(triggers);
            ArgumentNullException.ThrowIfNull(customEvents);
            ArgumentNullException.ThrowIfNull(createContext);
            if (_pendingCompanions.Count == 0)
            {
                return 0;
            }

            int dispatched = 0;
            foreach (PendingCompanionEvent entry in _pendingCompanions)
            {
                ScriptContext context = createContext();
                foreach (KeyValuePair<string, object?> field in entry.Payload)
                {
                    context.Set(field.Key, field.Value);
                }

                triggers.FireMapCustomEvent(mapId, CompanionEventPrefix + entry.EventId, context, customEvents);
                dispatched++;
            }

            _pendingCompanions.Clear();
            return dispatched;
        }

        private readonly record struct PendingCompanionEvent(string EventId, IReadOnlyDictionary<string, object?> Payload);

        private readonly record struct PendingRelease(PlayerInputHandler Handler, string ActionId, int RemainingTicks);
    }
}

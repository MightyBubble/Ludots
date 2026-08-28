using System;
using System.Collections.Generic;
using Arch.Core;
using Arch.System;
using Ludots.Core.Client;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Scripting;

namespace Ludots.Core.Input.Systems
{
    /// <summary>Diff operation of one projected input context command.</summary>
    public enum InputContextProjectionOp : byte
    {
        Push = 1,
        Pop = 2,
    }

    /// <summary>
    /// One per-seat projection decision: which input context the seat's handler must activate or
    /// release. The command stream is the projection's whole output shape — per-seat handler
    /// routing only has to consume it, never re-derive it.
    /// </summary>
    public readonly record struct InputContextProjectionCommand(string SeatId, string ContextId, InputContextProjectionOp Op);

    /// <summary>
    /// Local input context projection (#1306): derives, per client local seat, the input context
    /// set the seat's possessed representative's <see cref="InteractionMode"/> demands, diffs it
    /// against what this projection has applied to the seat's handler, and emits
    /// (seatId, contextId, op) commands onto the existing <see cref="PlayerInputHandler"/> IMC
    /// stack. Sparse default: a rep without the component projects no mode contexts (the active
    /// control scheme's resident contexts stay untouched). Pure derivation — mode writes come from
    /// graph ops; this system never mutates entity state. Seats without a bound handler keep
    /// re-emitting their diff until one consumes it, mirroring the interaction context bridge's
    /// null-handler tolerance.
    /// </summary>
    public sealed class InputContextProjectionSystem : ISystem<float>
    {
        private readonly World _world;
        private readonly Dictionary<string, object> _globals;
        private readonly InteractionModeMap _modeMap;
        private readonly Func<string, PlayerInputHandler?> _handlerBySeat;
        private readonly Dictionary<string, List<string>> _appliedBySeat = new(StringComparer.Ordinal);
        private readonly List<InputContextProjectionCommand> _commands = new();
        private readonly List<InteractionModeContextBinding> _desired = new();

        public InputContextProjectionSystem(
            World world,
            Dictionary<string, object> globals,
            InteractionModeMap modeMap,
            Func<string, PlayerInputHandler?> handlerBySeat)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _globals = globals ?? throw new ArgumentNullException(nameof(globals));
            _modeMap = modeMap ?? throw new ArgumentNullException(nameof(modeMap));
            _handlerBySeat = handlerBySeat ?? throw new ArgumentNullException(nameof(handlerBySeat));
        }

        /// <summary>Commands emitted by the most recent update (empty until the first tick).</summary>
        public IReadOnlyList<InputContextProjectionCommand> LastCommands => _commands;

        public void Initialize() { }

        public void Update(in float dt)
        {
            _commands.Clear();
            if (!_globals.TryGetValue(CoreServiceKeys.ClientLocalSeatRegistry.Name, out object? seatsObj) ||
                seatsObj is not ClientLocalSeatRegistry seats)
            {
                return;
            }

            IReadOnlyList<string> seatIds = seats.SeatIds;
            RetireVanishedSeats(seatIds);
            for (int i = 0; i < seatIds.Count; i++)
            {
                ClientLocalSeat seat = seats.Require(seatIds[i]);
                ProjectSeat(seat.SeatId, desiredCount: ResolveDesiredContexts(seat));
            }
        }

        public void BeforeUpdate(in float dt) { }
        public void AfterUpdate(in float dt) { }
        public void Dispose() { }

        /// <summary>
        /// Fills <see cref="_desired"/> with the mode's context bindings sorted by descending
        /// priority (deterministic command order); returns the count. Entities without the sparse
        /// component yield zero. A mode id unknown to the installed map fails fast by id and name —
        /// the only component writer is the graph op, so this is a save/config drift signal.
        /// </summary>
        private int ResolveDesiredContexts(ClientLocalSeat seat)
        {
            _desired.Clear();
            if (!seat.HasPossession || !_world.IsAlive(seat.PossessedRep))
            {
                return 0;
            }

            if (!_world.TryGet<InteractionMode>(seat.PossessedRep, out InteractionMode mode))
            {
                return 0;
            }

            if (!_modeMap.TryGetContexts(mode.ModeId, out IReadOnlyList<InteractionModeContextBinding> contexts))
            {
                throw InvalidModeId(seat, mode.ModeId);
            }

            for (int i = 0; i < contexts.Count; i++)
            {
                _desired.Add(contexts[i]);
            }

            _desired.Sort((a, b) => b.Priority.CompareTo(a.Priority));
            return _desired.Count;
        }

        private InvalidOperationException InvalidModeId(ClientLocalSeat seat, int modeId)
        {
            string name = _modeMap.ModeIdRegistry.GetName(modeId);
            return new InvalidOperationException(
                $"Seat '{seat.SeatId}' possesses an entity carrying interaction mode id {modeId} which is not installed" +
                (string.IsNullOrEmpty(name) ? "." : $" ('{name}')."));
        }

        private void ProjectSeat(string seatId, int desiredCount)
        {
            PlayerInputHandler? handler = _handlerBySeat(seatId);
            List<string> applied = Applied(seatId);
            int commandBase = _commands.Count;

            for (int i = applied.Count - 1; i >= 0; i--)
            {
                if (!ContainsDesired(applied[i], desiredCount))
                {
                    _commands.Add(new InputContextProjectionCommand(seatId, applied[i], InputContextProjectionOp.Pop));
                }
            }

            for (int i = 0; i < desiredCount; i++)
            {
                if (!applied.Contains(_desired[i].ContextId))
                {
                    _commands.Add(new InputContextProjectionCommand(seatId, _desired[i].ContextId, InputContextProjectionOp.Push));
                }
            }

            if (handler == null)
            {
                return;
            }

            ApplyRange(commandBase);
            applied.Clear();
            for (int i = 0; i < desiredCount; i++)
            {
                applied.Add(_desired[i].ContextId);
            }
        }

        private void RetireVanishedSeats(IReadOnlyList<string> seatIds)
        {
            List<string>? vanished = null;
            foreach (KeyValuePair<string, List<string>> pair in _appliedBySeat)
            {
                bool present = false;
                for (int i = 0; i < seatIds.Count; i++)
                {
                    if (string.Equals(seatIds[i], pair.Key, StringComparison.Ordinal))
                    {
                        present = true;
                        break;
                    }
                }

                if (!present)
                {
                    vanished ??= new List<string>();
                    vanished.Add(pair.Key);
                }
            }

            if (vanished == null)
            {
                return;
            }

            for (int i = 0; i < vanished.Count; i++)
            {
                int commandBase = _commands.Count;
                foreach (string contextId in _appliedBySeat[vanished[i]])
                {
                    _commands.Add(new InputContextProjectionCommand(vanished[i], contextId, InputContextProjectionOp.Pop));
                }

                ApplyRange(commandBase);
                _appliedBySeat.Remove(vanished[i]);
            }
        }

        private void ApplyRange(int fromIndex)
        {
            for (int i = fromIndex; i < _commands.Count; i++)
            {
                InputContextProjectionCommand command = _commands[i];
                PlayerInputHandler? handler = _handlerBySeat(command.SeatId);
                if (handler == null)
                {
                    continue;
                }

                if (!handler.HasContext(command.ContextId))
                {
                    throw new InvalidOperationException(
                        $"Interaction mode projection requested input context '{command.ContextId}' for seat '{command.SeatId}', but the seat's PlayerInputHandler config does not define it.");
                }

                if (command.Op == InputContextProjectionOp.Push)
                {
                    handler.PushContext(command.ContextId);
                }
                else
                {
                    handler.PopContext(command.ContextId);
                }
            }
        }

        private bool ContainsDesired(string contextId, int desiredCount)
        {
            for (int i = 0; i < desiredCount; i++)
            {
                if (string.Equals(_desired[i].ContextId, contextId, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private List<string> Applied(string seatId)
        {
            if (!_appliedBySeat.TryGetValue(seatId, out List<string>? applied))
            {
                applied = new List<string>();
                _appliedBySeat[seatId] = applied;
            }

            return applied;
        }
    }
}

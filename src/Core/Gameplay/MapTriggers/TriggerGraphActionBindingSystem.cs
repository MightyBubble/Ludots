using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Client;
using Ludots.Core.Input.CommandSources;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Map;
using Ludots.Core.Persistence;
using Ludots.Core.Scripting;

namespace Ludots.Core.Gameplay.MapTriggers
{
    /// <summary>
    /// Dispatches TriggerGraph mounts that bind a semantic input action directly.
    /// Button actions fire on their configured press or release. Axis2D
    /// <c>firesOn: change</c> actions and the reserved PointerMoved action share one
    /// value-change path: each mount samples the watched vector once when it is
    /// present, then again only when that vector changes. Each dispatch stamps the
    /// shared InputAction payload and executes the mount.
    /// </summary>
    public sealed class TriggerGraphActionBindingSystem : Arch.System.ISystem<float>
    {
        private readonly Func<MapSession?> _currentSession;
        private readonly TriggerManager _triggerManager;
        private readonly Func<ScriptContext> _createContext;
        private readonly Func<IInputActionReader?> _globalReader;
        private readonly Func<AuthoritativePointerButtonSnapshot?> _pointerButtons;
        private readonly TriggerGraphActionBindingIndex _bindings;
        private readonly Dictionary<string, string> _firesOnByAction;
        private readonly Func<ClientLocalSeatRegistry?> _seats;
        private readonly Func<ClientLocalSeatInputRuntime?> _seatInput;
        private readonly Action<Entity> _reconcileContext;
        private readonly List<string> _actionSnapshot = new(32);
        private readonly List<TriggerGraphMountTrigger> _mountSnapshot = new(16);
        private readonly List<InputBindingIntFire> _intFires = new(4);

        public TriggerGraphActionBindingSystem(
            Func<MapSession?> currentSession,
            TriggerManager triggerManager,
            Func<ScriptContext> createContext,
            Func<IInputActionReader?> globalReader,
            Func<AuthoritativePointerButtonSnapshot?> pointerButtons,
            TriggerGraphActionBindingIndex bindings,
            InputConfigRoot inputConfig,
            Func<ClientLocalSeatRegistry?> seats,
            Func<ClientLocalSeatInputRuntime?> seatInput,
            Action<Entity> reconcileContext)
        {
            _currentSession = currentSession ?? throw new ArgumentNullException(nameof(currentSession));
            _triggerManager = triggerManager ?? throw new ArgumentNullException(nameof(triggerManager));
            _createContext = createContext ?? throw new ArgumentNullException(nameof(createContext));
            _globalReader = globalReader ?? throw new ArgumentNullException(nameof(globalReader));
            _pointerButtons = pointerButtons ?? throw new ArgumentNullException(nameof(pointerButtons));
            _bindings = bindings ?? throw new ArgumentNullException(nameof(bindings));
            _seats = seats ?? throw new ArgumentNullException(nameof(seats));
            _seatInput = seatInput ?? throw new ArgumentNullException(nameof(seatInput));
            _reconcileContext = reconcileContext ?? throw new ArgumentNullException(nameof(reconcileContext));
            _firesOnByAction = BuildFiresOnLookup(inputConfig);
        }

        public void Initialize() { }
        public void BeforeUpdate(in float dt) { }
        public void AfterUpdate(in float dt) { }
        public void Dispose() { }

        public void Update(in float dt)
        {
            if (_bindings.ActionIds.Count == 0)
            {
                return;
            }

            _bindings.CopyKnownActionIds(_actionSnapshot);

            MapSession? session = _currentSession();
            if (session == null)
            {
                return;
            }

            ClientLocalSeatRegistry? seats = _seats();
            ClientLocalSeatInputRuntime? seatInput = _seatInput();
            if (seats != null && seatInput != null && seats.Count > 1)
            {
                DispatchPerSeat(session, seats, seatInput);
                return;
            }

            IInputActionReader? input = _globalReader();
            if (input == null)
            {
                return;
            }

            Entity? possessedRep = null;
            if (seats?.Count == 1)
            {
                if (!seats.TryGetSolePossessedRep(out Entity rep)) return;
                possessedRep = rep;
            }
            DispatchReader(session, input, possessedRep);
        }

        private void DispatchPerSeat(
            MapSession session,
            ClientLocalSeatRegistry seats,
            ClientLocalSeatInputRuntime seatInput)
        {
            IReadOnlyList<string> seatIds = seats.SeatIds;
            for (int s = 0; s < seatIds.Count; s++)
            {
                ClientLocalSeat seat = seats.Require(seatIds[s]);
                if (!seat.HasPossession ||
                    !seatInput.TryGetChannel(seat.SeatId, out ClientLocalSeatInputChannel channel))
                {
                    continue;
                }

                DispatchReader(session, channel.Reader, seat.PossessedRep);
            }
        }

        private void DispatchReader(
            MapSession session,
            IInputActionReader input,
            Entity? possessedRep)
        {
            // Both edges with a held final state close the old gesture before opening
            // its successor. Value changes observe the reconciled context after both edges.
            for (int phase = 0; phase < 4; phase++)
            foreach (string actionId in _actionSnapshot)
            {
                if (!_bindings.TryGetMounts(actionId, out var mounts)) continue;
                bool valueChange = IsValueChange(actionId);
                if (DispatchPhase(input, actionId, valueChange) != phase) continue;

                System.Numerics.Vector2 actionValue = default;
                System.Numerics.Vector2 pointer = default;
                if (valueChange)
                {
                    string watchedId = actionId == ReservedInputActionIds.PointerMoved
                        ? ReservedInputActionIds.PointerPos
                        : actionId;
                    if (!input.TryReadPresentVector2(watchedId, out actionValue)) continue;
                    if (actionId == ReservedInputActionIds.PointerMoved)
                    {
                        pointer = actionValue;
                    }
                    else if (!input.TryReadPresentVector2(ReservedInputActionIds.PointerPos, out pointer))
                    {
                        pointer = default;
                    }
                }
                else if (!DispatchButtonEdges(session, input, possessedRep, actionId, mounts))
                {
                    continue;
                }

                if (!valueChange)
                {
                    continue;
                }

                int modifiers = ReadHeldModifiers(input);
                CopyMounts(mounts);
                for (int i = 0; i < _mountSnapshot.Count; i++)
                {
                    TriggerGraphMountTrigger mount = _mountSnapshot[i];
                    Entity rep = mount.Scope;
                    if (!mount.IsActionRegistered || rep == Entity.Null || rep == default ||
                        (possessedRep.HasValue && rep != possessedRep.Value)) continue;
                    if (valueChange && !mount.ConsumeActionValueSample(actionValue)) continue;
                    Dispatch(session, mount, actionId, rep, pointer, modifiers, valueChange, actionValue, bindingInt: null);
                }
            }
        }

        /// <summary>
        /// Integer bindings dispatch once per matching edge and suppress the aggregate
        /// edge. The aggregate is the 0-to-nonzero transition of the summed value, so
        /// a second slot pressed while another is held would otherwise never fire, and
        /// firing both would double the first slot.
        /// </summary>
        private bool DispatchButtonEdges(
            MapSession session,
            IInputActionReader input,
            Entity? possessedRep,
            string actionId,
            IReadOnlyList<TriggerGraphMountTrigger> mounts)
        {
            bool release = IsRelease(actionId);
            _intFires.Clear();
            input.CopyBindingIntFires(actionId, _intFires);
            int matched = 0;
            for (int f = 0; f < _intFires.Count; f++)
            {
                InputBindingIntFire fire = _intFires[f];
                if (release ? !fire.Released : !fire.Pressed)
                {
                    continue;
                }

                matched++;
                if (!TryResolveEventPointer(actionId, release, out System.Numerics.Vector2 pointer))
                {
                    continue;
                }

                DispatchMounts(session, input, possessedRep, mounts, actionId, pointer, stampActionValue: false, default, fire.Value);
            }

            if (matched > 0)
            {
                return true;
            }

            if (!FiredThisTick(input, actionId) ||
                !TryResolveEventPointer(actionId, release, out System.Numerics.Vector2 aggregatePointer))
            {
                return true;
            }

            DispatchMounts(session, input, possessedRep, mounts, actionId, aggregatePointer, stampActionValue: false, default, bindingInt: null);
            return true;
        }

        private void DispatchMounts(
            MapSession session,
            IInputActionReader input,
            Entity? possessedRep,
            IReadOnlyList<TriggerGraphMountTrigger> mounts,
            string actionId,
            System.Numerics.Vector2 pointer,
            bool stampActionValue,
            System.Numerics.Vector2 actionValue,
            int? bindingInt)
        {
            int modifiers = ReadHeldModifiers(input);
            CopyMounts(mounts);
            for (int i = 0; i < _mountSnapshot.Count; i++)
            {
                TriggerGraphMountTrigger mount = _mountSnapshot[i];
                Entity rep = mount.Scope;
                if (!mount.IsActionRegistered || rep == Entity.Null || rep == default ||
                    (possessedRep.HasValue && rep != possessedRep.Value)) continue;
                if (stampActionValue && !mount.ConsumeActionValueSample(actionValue)) continue;
                Dispatch(session, mount, actionId, rep, pointer, modifiers, stampActionValue, actionValue, bindingInt);
            }
        }

        private void Dispatch(
            MapSession session,
            TriggerGraphMountTrigger mount,
            string actionId,
            Entity rep,
            System.Numerics.Vector2 pointer,
            int modifiers,
            bool stampActionValue,
            System.Numerics.Vector2 actionValue,
            int? bindingInt)
        {
            ScriptContext context = _createContext();
            context.Set(CoreServiceKeys.MapId, session.MapId);
            context.Set(CoreServiceKeys.MapSession, session);
            context.Set(MapTriggerEventPayloadKeys.Rep, rep);
            context.Set(MapTriggerEventPayloadKeys.Action, actionId);
            context.Set(MapTriggerEventPayloadKeys.PointerScreenX, pointer.X);
            context.Set(MapTriggerEventPayloadKeys.PointerScreenY, pointer.Y);
            context.Set(MapTriggerEventPayloadKeys.Modifiers, modifiers);
            if (stampActionValue)
            {
                context.Set(MapTriggerEventPayloadKeys.ActionValueX, actionValue.X);
                context.Set(MapTriggerEventPayloadKeys.ActionValueY, actionValue.Y);
            }

            if (bindingInt.HasValue)
            {
                context.Set(MapTriggerEventPayloadKeys.BindingInt, bindingInt.Value);
            }

            _triggerManager.DispatchMountedTrigger(mount, context);
            _reconcileContext(rep);
        }

        private int DispatchPhase(IInputActionReader input, string actionId, bool valueChange)
        {
            if (valueChange) return 3;
            if (!IsRelease(actionId)) return 1;
            return input.IsDown(actionId) && input.PressedThisFrame(actionId) ? 0 : 2;
        }

        private void CopyMounts(IReadOnlyList<TriggerGraphMountTrigger> mounts)
        {
            _mountSnapshot.Clear();
            for (int i = 0; i < mounts.Count; i++) _mountSnapshot.Add(mounts[i]);
        }

        private bool FiredThisTick(IInputActionReader input, string actionId)
        {
            return IsRelease(actionId)
                ? input.ReleasedThisFrame(actionId)
                : input.PressedThisFrame(actionId);
        }

        private bool IsValueChange(string actionId)
        {
            if (actionId == ReservedInputActionIds.PointerMoved) return true;
            return RequireFiresOn(actionId) == InputActionDef.FiresOnChange;
        }

        private bool IsRelease(string actionId)
        {
            return RequireFiresOn(actionId) == InputActionDef.FiresOnRelease;
        }

        private string RequireFiresOn(string actionId)
        {
            if (!_firesOnByAction.TryGetValue(actionId, out string? firesOn))
            {
                throw new InvalidOperationException(
                    $"LUDOTS_INPUT_ACTION_FIRES_ON_MISSING: TriggerGraph binds action '{actionId}' but the input config does not define it.");
            }

            if (firesOn != InputActionDef.FiresOnPress &&
                firesOn != InputActionDef.FiresOnRelease &&
                firesOn != InputActionDef.FiresOnChange)
            {
                throw new InvalidOperationException(
                    $"LUDOTS_INPUT_ACTION_FIRES_ON_INVALID: TriggerGraph binds action '{actionId}' but its firesOn is '{firesOn}'.");
            }

            return firesOn;
        }

        private bool TryResolveEventPointer(string actionId, bool release, out System.Numerics.Vector2 pointer)
        {
            AuthoritativePointerButtonSnapshot? buttons = _pointerButtons();
            if (buttons == null || !buttons.TryGetState(actionId, out PointerButtonState state))
            {
                pointer = default;
                return false;
            }

            if (release)
            {
                if (state.HasReleasePointer)
                {
                    pointer = state.ReleasePointer;
                    return true;
                }
            }
            else if (state.HasPressPointer)
            {
                pointer = state.PressPointer;
                return true;
            }

            pointer = state.Pointer;
            return true;
        }

        private static int ReadHeldModifiers(IInputActionReader input)
        {
            int modifiers = InputActionFiredModifiers.None;
            if (input.IsDown(CommandSourceModifierActionIds.Additive))
            {
                modifiers |= InputActionFiredModifiers.Queue;
            }

            if (input.IsDown(CommandSourceModifierActionIds.Toggle))
            {
                modifiers |= InputActionFiredModifiers.Precision;
            }

            if (input.IsDown(CommandSourceModifierActionIds.Subtract))
            {
                modifiers |= InputActionFiredModifiers.Subtract;
            }

            return modifiers;
        }

        private static Dictionary<string, string> BuildFiresOnLookup(InputConfigRoot inputConfig)
        {
            ArgumentNullException.ThrowIfNull(inputConfig);
            var lookup = new Dictionary<string, string>(StringComparer.Ordinal);
            List<InputActionDef> actions = inputConfig.Actions ?? new List<InputActionDef>();
            for (int i = 0; i < actions.Count; i++)
            {
                InputActionDef action = actions[i]
                    ?? throw new InvalidOperationException($"Input config actions[{i}] is null.");
                if (string.IsNullOrWhiteSpace(action.Id))
                {
                    throw new InvalidOperationException($"Input config actions[{i}] has an empty id.");
                }

                lookup[action.Id] = action.FiresOn ?? string.Empty;
            }

            return lookup;
        }
    }
}

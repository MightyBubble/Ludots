using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Client;
using Ludots.Core.Input.CommandSources;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Map;
using Ludots.Core.Scripting;

namespace Ludots.Core.Gameplay.MapTriggers
{
    /// <summary>
    /// Dispatches TriggerGraph mounts that bind a semantic input action directly:
    /// when the action's configured moment (press started / release completed) fires,
    /// stamps the shared InputAction payload (rep from the mount subject, pointer
    /// window pixels, held modifiers) and executes the mount. Replaces the retired
    /// retired input-action bridge.
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

        public TriggerGraphActionBindingSystem(
            Func<MapSession?> currentSession,
            TriggerManager triggerManager,
            Func<ScriptContext> createContext,
            Func<IInputActionReader?> globalReader,
            Func<AuthoritativePointerButtonSnapshot?> pointerButtons,
            TriggerGraphActionBindingIndex bindings,
            InputConfigRoot inputConfig,
            Func<ClientLocalSeatRegistry?> seats,
            Func<ClientLocalSeatInputRuntime?> seatInput)
        {
            _currentSession = currentSession ?? throw new ArgumentNullException(nameof(currentSession));
            _triggerManager = triggerManager ?? throw new ArgumentNullException(nameof(triggerManager));
            _createContext = createContext ?? throw new ArgumentNullException(nameof(createContext));
            _globalReader = globalReader ?? throw new ArgumentNullException(nameof(globalReader));
            _pointerButtons = pointerButtons ?? throw new ArgumentNullException(nameof(pointerButtons));
            _bindings = bindings ?? throw new ArgumentNullException(nameof(bindings));
            _seats = seats ?? throw new ArgumentNullException(nameof(seats));
            _seatInput = seatInput ?? throw new ArgumentNullException(nameof(seatInput));
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

            foreach (string actionId in _bindings.MountedActionIds)
            {
                if (!FiredThisTick(input, actionId))
                {
                    continue;
                }

                if (!_bindings.TryGetMounts(actionId, out IReadOnlyList<TriggerGraphMountTrigger> mounts))
                {
                    continue;
                }

                if (!TryResolveEventPointer(actionId, IsRelease(actionId), out System.Numerics.Vector2 pointer))
                {
                    continue;
                }

                int modifiers = ReadHeldModifiers(input);
                for (int i = 0; i < mounts.Count; i++)
                {
                    TriggerGraphMountTrigger mount = mounts[i];
                    Entity rep = mount.Scope;
                    if (rep == Entity.Null || rep == default)
                    {
                        continue;
                    }

                    Dispatch(session, mount, actionId, rep, pointer, modifiers);
                }
            }
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

                IInputActionReader input = channel.Reader;
                Entity rep = seat.PossessedRep;
                foreach (string actionId in _bindings.MountedActionIds)
                {
                    if (!FiredThisTick(input, actionId))
                    {
                        continue;
                    }

                    if (!_bindings.TryGetMounts(actionId, out IReadOnlyList<TriggerGraphMountTrigger> mounts))
                    {
                        continue;
                    }

                    if (!TryResolveEventPointer(actionId, IsRelease(actionId), out System.Numerics.Vector2 pointer))
                    {
                        continue;
                    }

                    int modifiers = ReadHeldModifiers(input);
                    for (int i = 0; i < mounts.Count; i++)
                    {
                        TriggerGraphMountTrigger mount = mounts[i];
                        if (mount.Scope != rep)
                        {
                            continue;
                        }

                        Dispatch(session, mount, actionId, rep, pointer, modifiers);
                    }
                }
            }
        }

        private void Dispatch(
            MapSession session,
            TriggerGraphMountTrigger mount,
            string actionId,
            Entity rep,
            System.Numerics.Vector2 pointer,
            int modifiers)
        {
            // #region agent log
            try { System.IO.File.AppendAllText("/opt/cursor/logs/debug.log", System.Text.Json.JsonSerializer.Serialize(new { hypothesisId = "H1_H3", location = "TriggerGraphActionBindingSystem.Dispatch", message = "dispatch action with pointer payload", data = new { actionId, pointerX = pointer.X, pointerY = pointer.Y, modifiers, repId = rep.Id, mapId = session.MapId.Value }, timestamp = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() }) + "\n"); } catch { }
            // #endregion
            ScriptContext context = _createContext();
            context.Set(CoreServiceKeys.MapId, session.MapId);
            context.Set(CoreServiceKeys.MapSession, session);
            context.Set(MapTriggerEventPayloadKeys.Rep, rep);
            context.Set(MapTriggerEventPayloadKeys.Action, actionId);
            context.Set(MapTriggerEventPayloadKeys.PointerScreenX, pointer.X);
            context.Set(MapTriggerEventPayloadKeys.PointerScreenY, pointer.Y);
            context.Set(MapTriggerEventPayloadKeys.Modifiers, modifiers);
            _triggerManager.DispatchMountedTrigger(mount, context);
        }

        private bool FiredThisTick(IInputActionReader input, string actionId)
        {
            return IsRelease(actionId)
                ? input.ReleasedThisFrame(actionId)
                : input.PressedThisFrame(actionId);
        }

        private bool IsRelease(string actionId)
        {
            if (!_firesOnByAction.TryGetValue(actionId, out string? firesOn))
            {
                throw new InvalidOperationException(
                    $"LUDOTS_INPUT_ACTION_FIRES_ON_MISSING: TriggerGraph binds action '{actionId}' but the input config does not define it.");
            }

            return firesOn == InputActionDef.FiresOnRelease;
        }

        private bool TryResolveEventPointer(string actionId, bool release, out System.Numerics.Vector2 pointer)
        {
            AuthoritativePointerButtonSnapshot? buttons = _pointerButtons();
            if (buttons == null || !buttons.TryGetState(actionId, out PointerButtonState state))
            {
                // #region agent log
                try { System.IO.File.AppendAllText("/opt/cursor/logs/debug.log", System.Text.Json.JsonSerializer.Serialize(new { hypothesisId = "H1", location = "TriggerGraphActionBindingSystem.TryResolveEventPointer", message = "pointer state missing for action", data = new { actionId, release, buttonsNull = buttons == null }, timestamp = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() }) + "\n"); } catch { }
                // #endregion
                pointer = default;
                return false;
            }

            if (release)
            {
                if (state.HasReleasePointer)
                {
                    pointer = state.ReleasePointer;
                    // #region agent log
                    try { System.IO.File.AppendAllText("/opt/cursor/logs/debug.log", System.Text.Json.JsonSerializer.Serialize(new { hypothesisId = "H1", location = "TriggerGraphActionBindingSystem.TryResolveEventPointer", message = "resolved release pointer", data = new { actionId, pointerX = pointer.X, pointerY = pointer.Y, source = "ReleasePointer" }, timestamp = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() }) + "\n"); } catch { }
                    // #endregion
                    return true;
                }
            }
            else if (state.HasPressPointer)
            {
                pointer = state.PressPointer;
                // #region agent log
                try { System.IO.File.AppendAllText("/opt/cursor/logs/debug.log", System.Text.Json.JsonSerializer.Serialize(new { hypothesisId = "H1", location = "TriggerGraphActionBindingSystem.TryResolveEventPointer", message = "resolved press pointer", data = new { actionId, pointerX = pointer.X, pointerY = pointer.Y, source = "PressPointer", livePointerX = state.Pointer.X, livePointerY = state.Pointer.Y }, timestamp = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() }) + "\n"); } catch { }
                // #endregion
                return true;
            }

            pointer = state.Pointer;
            // #region agent log
            try { System.IO.File.AppendAllText("/opt/cursor/logs/debug.log", System.Text.Json.JsonSerializer.Serialize(new { hypothesisId = "H1", location = "TriggerGraphActionBindingSystem.TryResolveEventPointer", message = "fallback live pointer", data = new { actionId, release, pointerX = pointer.X, pointerY = pointer.Y, hasPress = state.HasPressPointer, hasRelease = state.HasReleasePointer }, timestamp = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() }) + "\n"); } catch { }
            // #endregion
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

                string firesOn = string.IsNullOrWhiteSpace(action.FiresOn)
                    ? InputActionDef.FiresOnPress
                    : action.FiresOn;
                if (firesOn != InputActionDef.FiresOnPress && firesOn != InputActionDef.FiresOnRelease)
                {
                    throw new InvalidOperationException(
                        $"LUDOTS_INPUT_ACTION_FIRES_ON_INVALID: action '{action.Id}' firesOn must be '{InputActionDef.FiresOnPress}' or '{InputActionDef.FiresOnRelease}' (got '{action.FiresOn}').");
                }

                lookup[action.Id] = firesOn;
            }

            return lookup;
        }
    }
}

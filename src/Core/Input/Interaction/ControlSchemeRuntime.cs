using System;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Registry;

namespace Ludots.Core.Input.Interaction
{
    /// <summary>
    /// Control scheme catalog and hot-switch runtime (RFC-0065 INT-5, §5.11, DEC-15). Schemes are
    /// declared in <c>Input/control_schemes.json</c> and compiled at install time: axis move
    /// <c>orderTypeKey</c> references resolve against <see cref="OrderTypeRegistry"/>, default command
    /// intent ids must be installed <see cref="CommandIntentProfileRegistry"/> profiles and register
    /// into the <see cref="InteractionContextStack.CommandIntentProfileIdRegistry"/> id space, so
    /// <see cref="ActiveDefaultCommandIntentId"/> is directly comparable with
    /// <see cref="InteractionContextFrame.CommandIntentProfileId"/> (the space
    /// <see cref="CommandIntentArbiter"/> resolves in). Switching pops the previous scheme's IMC
    /// contexts off the <see cref="PlayerInputHandler"/> and pushes the new ones; non-default frames
    /// on the stack are untouched, and the default frame's intent reference reads the new scheme
    /// immediately through the arbiter. The handler is resolved through a provider per switch and
    /// may be null (headless engine / handler bound later by the adapter) — in that case only the
    /// intent default and preference bookkeeping happen.
    /// </summary>
    public sealed class ControlSchemeRuntime
    {
        private readonly StringIntRegistry _schemeIds;
        private readonly InteractionContextStack _stack;
        private readonly CommandIntentProfileRegistry _commandIntents;
        private readonly OrderTypeRegistry _orderTypes;
        private readonly Func<PlayerInputHandler> _handlerProvider;
        private readonly ClientCastPreferenceStore _preferences;

        private CompiledScheme[] _schemes = new CompiledScheme[8];
        private bool _allowAll = true;
        private bool[] _allowed = Array.Empty<bool>();
        private int _activeSchemeId;
        private int _activeDefaultCommandIntentId;

        public ControlSchemeRuntime(
            StringIntRegistry schemeIdRegistry,
            InteractionContextStack stack,
            CommandIntentProfileRegistry commandIntents,
            OrderTypeRegistry orderTypes,
            Func<PlayerInputHandler> handlerProvider = null,
            ClientCastPreferenceStore preferences = null)
        {
            _schemeIds = schemeIdRegistry ?? throw new ArgumentNullException(nameof(schemeIdRegistry));
            _stack = stack ?? throw new ArgumentNullException(nameof(stack));
            _commandIntents = commandIntents ?? throw new ArgumentNullException(nameof(commandIntents));
            _orderTypes = orderTypes ?? throw new ArgumentNullException(nameof(orderTypes));
            _handlerProvider = handlerProvider;
            _preferences = preferences;
        }

        /// <summary>Scheme id space; players/mods reference schemes by these ids.</summary>
        public StringIntRegistry SchemeIdRegistry => _schemeIds;

        /// <summary>Active scheme id; 0 = no scheme has been activated yet.</summary>
        public int ActiveSchemeId => _activeSchemeId;

        /// <summary>
        /// The active scheme's default command intent, in the
        /// <see cref="InteractionContextStack.CommandIntentProfileIdRegistry"/> id space (DEC-14:
        /// consumed only for the default frame). 0 when no scheme is active — pointer commands then
        /// do not route (no fallback).
        /// </summary>
        public int ActiveDefaultCommandIntentId => _activeDefaultCommandIntentId;

        /// <summary>Bumped on every successful switch.</summary>
        public uint Revision { get; private set; }

        /// <summary>
        /// The active scheme's axis move declaration with the order type key pre-resolved at
        /// <see cref="Install"/> (consumers never re-parse strings per tick). Returns false when no
        /// scheme is active or the active scheme declares no axis move — the topology fact that the
        /// current scheme has no axis movement, not a fallback.
        /// </summary>
        public bool TryGetActiveAxisMove(out ControlSchemeAxisMoveBinding axisMove)
        {
            if (_activeSchemeId == 0 || !_schemes[_activeSchemeId].HasAxisMove)
            {
                axisMove = default;
                return false;
            }

            axisMove = _schemes[_activeSchemeId].AxisMove;
            return true;
        }

        /// <summary>
        /// Compile and install every scheme in the config. Fails fast on duplicate installs and on
        /// <c>defaults.commandIntentId</c> references that are not installed command intent profiles.
        /// </summary>
        public void Install(ControlSchemesConfig config)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            ControlSchemeConfigLoader.Validate(config, nameof(ControlSchemesConfig));
            for (int i = 0; i < config.Schemes.Count; i++)
            {
                InstallScheme(config.Schemes[i]);
            }

            if (config.AllowedSchemes != null && config.AllowedSchemes.Count > 0)
            {
                _allowAll = false;
                for (int i = 0; i < config.AllowedSchemes.Count; i++)
                {
                    int schemeId = _schemeIds.GetId(config.AllowedSchemes[i]);
                    EnsureAllowedCapacity(schemeId);
                    _allowed[schemeId] = true;
                }
            }
        }

        /// <summary>True when the scheme id has been compiled and can activate.</summary>
        public bool IsInstalled(int schemeId)
        {
            return schemeId > 0 && schemeId < _schemes.Length && _schemes[schemeId] != null;
        }

        /// <summary>True when the mod-declared allowed set permits switching to the scheme.</summary>
        public bool IsAllowed(int schemeId)
        {
            return _allowAll || (schemeId < _allowed.Length && _allowed[schemeId]);
        }

        /// <summary>
        /// Hot-switch to a scheme: pop the previous scheme's IMC contexts, push the new scheme's,
        /// record the active default command intent, and persist the choice into the preference
        /// store. Returns false when the scheme is not installed or the mod allowed-set refuses it
        /// (settings UI shows the refusal); switching to the already-active scheme is a no-op
        /// success. Tolerates a null input handler (headless).
        /// </summary>
        public bool TrySwitch(int schemeId)
        {
            if (!IsInstalled(schemeId) || !IsAllowed(schemeId))
            {
                return false;
            }

            if (schemeId == _activeSchemeId)
            {
                return true;
            }

            PlayerInputHandler handler = _handlerProvider?.Invoke();
            if (handler != null)
            {
                if (_activeSchemeId != 0)
                {
                    string[] previousContexts = _schemes[_activeSchemeId].InputContexts;
                    for (int i = 0; i < previousContexts.Length; i++)
                    {
                        handler.PopContext(previousContexts[i]);
                    }
                }

                string[] nextContexts = _schemes[schemeId].InputContexts;
                for (int i = 0; i < nextContexts.Length; i++)
                {
                    handler.PushContext(nextContexts[i]);
                }
            }

            _activeSchemeId = schemeId;
            _activeDefaultCommandIntentId = _schemes[schemeId].DefaultCommandIntentId;
            _preferences?.SetActiveScheme(_schemeIds.GetName(schemeId));
            Revision++;
            return true;
        }

        private void InstallScheme(ControlSchemeDefinition definition)
        {
            int schemeId = _schemeIds.Register(definition.Id);
            if (schemeId < _schemes.Length && _schemes[schemeId] != null)
            {
                throw new InvalidOperationException($"Control scheme '{definition.Id}' is already installed.");
            }

            string commandIntentId = definition.Defaults.CommandIntentId;
            if (!_commandIntents.ProfileIdRegistry.TryGetId(commandIntentId, out int intentRegistryId) ||
                !_commandIntents.IsInstalled(intentRegistryId))
            {
                throw new InvalidOperationException(
                    $"Control scheme '{definition.Id}' defaults.commandIntentId references command intent profile " +
                    $"'{commandIntentId}' which is not installed.");
            }

            var contexts = new string[definition.InputContexts.Count];
            for (int i = 0; i < contexts.Length; i++)
            {
                contexts[i] = definition.InputContexts[i].Trim();
            }

            var scheme = new CompiledScheme
            {
                InputContexts = contexts,
                DefaultCommandIntentId = _stack.CommandIntentProfileIdRegistry.Register(commandIntentId),
            };

            if (definition.AxisMove != null)
            {
                if (!_orderTypes.TryGetId(definition.AxisMove.OrderTypeKey, out int orderTypeId))
                {
                    throw new InvalidOperationException(
                        $"Control scheme '{definition.Id}' axisMove.orderTypeKey references unknown order type " +
                        $"'{definition.AxisMove.OrderTypeKey}'.");
                }

                scheme.HasAxisMove = true;
                scheme.AxisMove = new ControlSchemeAxisMoveBinding(
                    definition.AxisMove.ActionId,
                    orderTypeId,
                    definition.AxisMove.ThrottleTicks,
                    definition.AxisMove.StepDistanceCm);
            }

            if (schemeId >= _schemes.Length)
            {
                int next = _schemes.Length;
                while (next <= schemeId)
                {
                    next *= 2;
                }

                Array.Resize(ref _schemes, next);
            }

            _schemes[schemeId] = scheme;
        }

        private void EnsureAllowedCapacity(int schemeId)
        {
            if (schemeId >= _allowed.Length)
            {
                Array.Resize(ref _allowed, Math.Max(schemeId + 1, Math.Max(8, _allowed.Length * 2)));
            }
        }

        private sealed class CompiledScheme
        {
            public string[] InputContexts = Array.Empty<string>();
            public int DefaultCommandIntentId;
            public bool HasAxisMove;
            public ControlSchemeAxisMoveBinding AxisMove;
        }
    }

    /// <summary>
    /// Compiled axis move declaration of the active control scheme: the raw Axis2D action id plus
    /// the order parameters, with <c>orderTypeKey</c> already resolved to its
    /// <c>OrderTypeRegistry</c> id at install time.
    /// </summary>
    public readonly struct ControlSchemeAxisMoveBinding
    {
        public readonly string ActionId;
        public readonly int OrderTypeId;
        public readonly int ThrottleTicks;
        public readonly int StepDistanceCm;

        public ControlSchemeAxisMoveBinding(string actionId, int orderTypeId, int throttleTicks, int stepDistanceCm)
        {
            ActionId = actionId;
            OrderTypeId = orderTypeId;
            ThrottleTicks = throttleTicks;
            StepDistanceCm = stepDistanceCm;
        }
    }
}

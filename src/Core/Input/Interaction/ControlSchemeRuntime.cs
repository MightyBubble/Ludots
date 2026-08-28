using System;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Registry;

namespace Ludots.Core.Input.Interaction
{
    /// <summary>
    /// Control scheme catalog and hot-switch runtime (RFC-0065 INT-5, Section 5.11, DEC-15). Schemes are
    /// declared in <c>Input/control_schemes.json</c> and compiled at install time: axis move
    /// <c>orderTypeKey</c> references resolve against <see cref="OrderTypeRegistry"/>, default command
    /// intent ids must be installed <see cref="CommandIntentProfileRegistry"/> profiles and register
    /// into the <see cref="InteractionContextStack.CommandIntentProfileIdRegistry"/> id space, while
    /// default dispatch profile ids must be installed <see cref="CastDispatchProfileRegistry"/> profiles, so
    /// <see cref="ActiveDefaultCommandIntentId"/> is directly comparable with
    /// <see cref="InteractionContextFrame.CommandIntentProfileId"/> (the space
    /// <see cref="CommandIntentArbiter"/> resolves in). Switching pops the previous scheme's IMC
    /// contexts off the <see cref="PlayerInputHandler"/> and pushes the new ones; non-default frames
    /// on the stack are untouched, and the default frame's intent reference reads the new scheme
    /// immediately through the arbiter. The handler is resolved through a provider per switch and
    /// may be null (headless engine / handler bound later by the adapter); in that case only the
    /// intent default and preference bookkeeping happen.
    /// </summary>
    public sealed class ControlSchemeRuntime
    {
        private readonly StringIntRegistry _schemeIds;
        private readonly InteractionContextStack _stack;
        private readonly CommandIntentProfileRegistry _commandIntents;
        private readonly CastDispatchProfileRegistry _castDispatchProfiles;
        private readonly OrderTypeRegistry _orderTypes;
        private readonly InputConfigRoot _inputConfig;
        private readonly Func<PlayerInputHandler> _handlerProvider;
        private readonly ClientCastPreferenceStore _preferences;

        private CompiledScheme[] _schemes = new CompiledScheme[8];
        private bool _allowAll = true;
        private bool[] _allowed = Array.Empty<bool>();
        private int _activeSchemeId;
        private int _activeDefaultCommandIntentId;
        private int _activeDefaultCastDispatchProfileId;

        public ControlSchemeRuntime(
            StringIntRegistry schemeIdRegistry,
            InteractionContextStack stack,
            CommandIntentProfileRegistry commandIntents,
            CastDispatchProfileRegistry castDispatchProfiles,
            OrderTypeRegistry orderTypes,
            Func<PlayerInputHandler> handlerProvider = null,
            ClientCastPreferenceStore preferences = null,
            InputConfigRoot inputConfig = null)
        {
            _schemeIds = schemeIdRegistry ?? throw new ArgumentNullException(nameof(schemeIdRegistry));
            _stack = stack ?? throw new ArgumentNullException(nameof(stack));
            _commandIntents = commandIntents ?? throw new ArgumentNullException(nameof(commandIntents));
            _castDispatchProfiles = castDispatchProfiles ?? throw new ArgumentNullException(nameof(castDispatchProfiles));
            _orderTypes = orderTypes ?? throw new ArgumentNullException(nameof(orderTypes));
            _inputConfig = inputConfig;
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
        /// consumed only for the default frame). 0 when no scheme is active; pointer commands then
        /// do not route.
        /// </summary>
        public int ActiveDefaultCommandIntentId => _activeDefaultCommandIntentId;

        /// <summary>
        /// The active scheme's default cast-dispatch profile, in
        /// <see cref="CastDispatchProfileRegistry.ProfileIdRegistry"/> id space. 0 when no scheme
        /// is active; routed pointer commands then fail fast instead of inventing a Core default.
        /// </summary>
        public int ActiveDefaultCastDispatchProfileId => _activeDefaultCastDispatchProfileId;

        /// <summary>Bumped on every successful switch.</summary>
        public uint Revision { get; private set; }

        /// <summary>
        /// The active scheme's axis move declaration with the order type key pre-resolved at
        /// <see cref="Install"/>. Returns false when no scheme is active or the active scheme
        /// declares no axis move: the current scheme has no axis movement.
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
        /// Read-only view of an installed scheme's activation payload (the contexts to push on
        /// an input handler plus the scheme-owned defaults). Per-seat activation stacks mirror
        /// this data without recompiling or re-registering the catalog; the global active
        /// scheme state above stays untouched by those readers.
        /// </summary>
        public bool TryGetSchemeActivation(int schemeId, out ControlSchemeActivation activation)
        {
            activation = default;
            if (!IsInstalled(schemeId))
            {
                return false;
            }

            CompiledScheme scheme = _schemes[schemeId];
            activation = new ControlSchemeActivation(
                scheme.InputContexts,
                scheme.DefaultCommandIntentId,
                scheme.DefaultCastDispatchProfileId,
                scheme.HasAxisMove,
                scheme.AxisMove);
            return true;
        }

        /// <summary>
        /// Compile and install every scheme in the config. Fails fast on duplicate installs and on
        /// <c>defaults.commandIntentId</c> references that are not installed command intent profiles.
        /// After installation, activates the persisted active scheme when present; otherwise it
        /// activates the first allowed declaration so production startup has a scheme-owned intent
        /// default without test-only calls.
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

            ActivateInitialScheme(config);
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
            if (!TrySwitchCore(schemeId, out bool switched))
            {
                return false;
            }

            if (switched)
            {
                _preferences?.SetActiveScheme(_schemeIds.GetName(schemeId));
            }

            return true;
        }

        /// <summary>
        /// Runtime-only hot-switch: identical switching semantics to <see cref="TrySwitch"/>
        /// (installed/allowed-set refusal, IMC context swap, no-op success on the already-active
        /// scheme) but never writes the preference store — the caller's activation truth is
        /// transient per scope (e.g. a seat's declared scheme for this map entry), while the
        /// persisted preference stays the player's own choice. Explicit user switches keep using
        /// <see cref="TrySwitch"/>.
        /// </summary>
        public bool TrySwitchRuntimeOnly(int schemeId)
        {
            return TrySwitchCore(schemeId, out _);
        }

        private bool TrySwitchCore(int schemeId, out bool switched)
        {
            switched = false;
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
            _activeDefaultCastDispatchProfileId = _schemes[schemeId].DefaultCastDispatchProfileId;
            Revision++;
            switched = true;
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

            string castDispatchProfileId = definition.Defaults.CastDispatchProfileId;
            if (!_castDispatchProfiles.ProfileIdRegistry.TryGetId(castDispatchProfileId, out int dispatchRegistryId) ||
                !_castDispatchProfiles.IsInstalled(dispatchRegistryId))
            {
                throw new InvalidOperationException(
                    $"Control scheme '{definition.Id}' defaults.castDispatchProfileId references cast dispatch profile " +
                    $"'{castDispatchProfileId}' which is not installed.");
            }

            var contexts = new string[definition.InputContexts.Count];
            for (int i = 0; i < contexts.Length; i++)
            {
                contexts[i] = definition.InputContexts[i].Trim();
            }

            ValidateInputContexts(definition, contexts);

            var scheme = new CompiledScheme
            {
                InputContexts = contexts,
                DefaultCommandIntentId = _stack.CommandIntentProfileIdRegistry.Register(commandIntentId),
                DefaultCastDispatchProfileId = dispatchRegistryId,
            };

            if (definition.AxisMove != null)
            {
                ValidateAxisMoveAction(definition);

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

        /// <summary>
        /// Same fail-fast contract as the intent/dispatch reference checks, applied to the
        /// scheme's IMC context ids: <see cref="PlayerInputHandler.PushContext(string)"/> is a
        /// silent no-op for unknown contexts, so a typo here would otherwise drop the scheme's
        /// bindings without any signal. Skipped when no input config is available (nothing to
        /// validate against), mirroring <see cref="ValidateAxisMoveAction"/>.
        /// </summary>
        private void ValidateInputContexts(ControlSchemeDefinition definition, string[] contexts)
        {
            if (_inputConfig?.Contexts == null || contexts.Length == 0)
            {
                return;
            }

            for (int i = 0; i < contexts.Length; i++)
            {
                string contextId = contexts[i];
                bool declared = false;
                for (int c = 0; c < _inputConfig.Contexts.Count; c++)
                {
                    InputContextDef context = _inputConfig.Contexts[c];
                    if (context != null && string.Equals(context.Id, contextId, StringComparison.Ordinal))
                    {
                        declared = true;
                        break;
                    }
                }

                if (!declared)
                {
                    throw new InvalidOperationException(
                        $"Control scheme '{definition.Id}' inputContexts references unknown input context '{contextId}'.");
                }
            }
        }

        private void ValidateAxisMoveAction(ControlSchemeDefinition definition)
        {
            if (_inputConfig?.Actions == null)
            {
                return;
            }

            string actionId = definition.AxisMove.ActionId;
            for (int i = 0; i < _inputConfig.Actions.Count; i++)
            {
                InputActionDef action = _inputConfig.Actions[i];
                if (action == null || !string.Equals(action.Id, actionId, StringComparison.Ordinal))
                {
                    continue;
                }

                if (action.Type != InputActionType.Axis2D)
                {
                    throw new InvalidOperationException(
                        $"Control scheme '{definition.Id}' axisMove.actionId references input action '{actionId}' " +
                        $"with type '{action.Type}'; axisMove requires an Axis2D action.");
                }

                return;
            }

            throw new InvalidOperationException(
                $"Control scheme '{definition.Id}' axisMove.actionId references unknown input action '{actionId}'.");
        }

        private void EnsureAllowedCapacity(int schemeId)
        {
            if (schemeId >= _allowed.Length)
            {
                Array.Resize(ref _allowed, Math.Max(schemeId + 1, Math.Max(8, _allowed.Length * 2)));
            }
        }

        private void ActivateInitialScheme(ControlSchemesConfig config)
        {
            if (_activeSchemeId != 0 || config.Schemes.Count == 0)
            {
                return;
            }

            if (_preferences != null && !string.IsNullOrWhiteSpace(_preferences.ActiveSchemeId))
            {
                string preferredScheme = _preferences.ActiveSchemeId.Trim();
                if (!_schemeIds.TryGetId(preferredScheme, out int preferredSchemeId))
                {
                    throw new InvalidOperationException(
                        $"Persisted control scheme '{preferredScheme}' is not installed.");
                }

                if (!TrySwitch(preferredSchemeId))
                {
                    throw new InvalidOperationException(
                        $"Persisted control scheme '{preferredScheme}' is not allowed.");
                }

                return;
            }

            for (int i = 0; i < config.Schemes.Count; i++)
            {
                int schemeId = _schemeIds.GetId(config.Schemes[i].Id);
                if (IsAllowed(schemeId))
                {
                    if (!TrySwitch(schemeId))
                    {
                        throw new InvalidOperationException(
                            $"Initial control scheme '{config.Schemes[i].Id}' is installed but could not activate.");
                    }

                    return;
                }
            }

            throw new InvalidOperationException("Control scheme config leaves no installed scheme allowed for startup.");
        }

        private sealed class CompiledScheme
        {
            public string[] InputContexts = Array.Empty<string>();
            public int DefaultCommandIntentId;
            public int DefaultCastDispatchProfileId;
            public bool HasAxisMove;
            public ControlSchemeAxisMoveBinding AxisMove;
        }
    }

    /// <summary>
    /// Compiled axis move declaration of the active control scheme: the raw Axis2D action id plus
    /// the order parameters, with <c>orderTypeKey</c> resolved to an <c>OrderTypeRegistry</c> id.
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

    /// <summary>
    /// Compiled activation payload of one installed scheme: the IMC contexts a switch must
    /// push (and the previous scheme's pop), plus the scheme-owned default intent / cast
    /// dispatch ids and axis move declaration. Read through
    /// <see cref="ControlSchemeRuntime.TryGetSchemeActivation"/> by per-seat activation
    /// stacks; the ids live in the same id spaces the runtime itself consumes.
    /// </summary>
    public readonly struct ControlSchemeActivation
    {
        public readonly string[] InputContexts;
        public readonly int DefaultCommandIntentId;
        public readonly int DefaultCastDispatchProfileId;
        public readonly bool HasAxisMove;
        public readonly ControlSchemeAxisMoveBinding AxisMove;

        public ControlSchemeActivation(
            string[] inputContexts,
            int defaultCommandIntentId,
            int defaultCastDispatchProfileId,
            bool hasAxisMove,
            ControlSchemeAxisMoveBinding axisMove)
        {
            InputContexts = inputContexts;
            DefaultCommandIntentId = defaultCommandIntentId;
            DefaultCastDispatchProfileId = defaultCastDispatchProfileId;
            HasAxisMove = hasAxisMove;
            AxisMove = axisMove;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Text;
using Ludots.Core.Input.Config;

namespace Ludots.Core.Input.Runtime
{
    public class PlayerInputHandler : IInputActionReader
    {
        // Interaction judging thresholds (visual-frame cadence). These are the fallback
        // defaults when a binding's Interactions parameters omit the named override.
        public const float TapMaxTravelPixels = 6f;
        public const float DragThresholdPixels = 8f;
        public const float HoldDefaultDurationSeconds = 0.5f;
        public const int MultiTapDefaultTapCount = 2;
        public const float MultiTapDefaultWindowSeconds = 0.5f;

        private readonly IInputBackend _backend;
        private readonly List<CompiledContext> _activeContexts = new();
        private readonly Dictionary<string, CompiledContext> _contextsById = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _actionIndices = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Vector3> _injections = new(StringComparer.Ordinal);
        private readonly HashSet<string> _seenBindingContributions = new(StringComparer.Ordinal);
        private readonly InputActionInstance[] _actionStates;
        private readonly Vector3[] _tempValues;
        private Vector2 _mousePosition;
        private Vector2 _mouseDelta;
        private bool _hasMousePosition;

        public bool InputBlocked { get; set; } = false;
        public long UpdateRevision { get; private set; }

        public PlayerInputHandler(IInputBackend backend, InputConfigRoot config)
        {
            _backend = backend;
            if (config == null) throw new ArgumentNullException(nameof(config));
            InputConfigPipelineLoader.Validate(config, "PlayerInputHandler config");

            _actionStates = new InputActionInstance[config.Actions.Count];
            _tempValues = new Vector3[config.Actions.Count];
            for (int i = 0; i < config.Actions.Count; i++)
            {
                var actionDef = config.Actions[i];
                _actionIndices[actionDef.Id] = i;
                _actionStates[i] = new InputActionInstance(actionDef);
            }

            for (int i = 0; i < config.Contexts.Count; i++)
            {
                var context = config.Contexts[i];
                _contextsById[context.Id] = CompileContext(context);
            }

            _seenBindingContributions.EnsureCapacity(CountTopLevelBindings(config));
        }

        public void PushContext(string contextId)
        {
            if (!_contextsById.TryGetValue(contextId, out var context))
            {
                return;
            }

            if (_activeContexts.Contains(context))
            {
                return;
            }

            _activeContexts.Add(context);
            _activeContexts.Sort((a, b) => b.Priority.CompareTo(a.Priority));
        }

        public void PopContext(string contextId)
        {
            _activeContexts.RemoveAll(c => string.Equals(c.Id, contextId, StringComparison.Ordinal));
        }

        public bool HasAction(string actionId)
        {
            if (string.IsNullOrWhiteSpace(actionId)) return false;
            return _actionIndices.ContainsKey(actionId);
        }

        public bool HasContext(string contextId)
        {
            if (string.IsNullOrWhiteSpace(contextId)) return false;
            return _contextsById.ContainsKey(contextId);
        }

        public T ReadAction<T>(string actionId) where T : struct
        {
            if (_actionIndices.TryGetValue(actionId, out int actionIndex))
            {
                return _actionStates[actionIndex].ReadValue<T>();
            }
            return default;
        }

        public bool IsDown(string actionId)
        {
            return _actionIndices.TryGetValue(actionId, out int actionIndex) && _actionStates[actionIndex].Triggered;
        }

        public bool PressedThisFrame(string actionId)
        {
            return _actionIndices.TryGetValue(actionId, out int actionIndex) && _actionStates[actionIndex].PressedThisFrame;
        }

        public bool ReleasedThisFrame(string actionId)
        {
            return _actionIndices.TryGetValue(actionId, out int actionIndex) && _actionStates[actionIndex].ReleasedThisFrame;
        }

        public bool SuppressActionThisFrame(string actionId)
        {
            if (!_actionIndices.TryGetValue(actionId, out int actionIndex))
            {
                return false;
            }

            _actionStates[actionIndex].SuppressThisFrame();
            return true;
        }

        public void CaptureFrame(AuthoritativeInputAccumulator accumulator)
        {
            if (accumulator == null) throw new ArgumentNullException(nameof(accumulator));

            for (int i = 0; i < _actionStates.Length; i++)
            {
                var state = _actionStates[i];
                accumulator.CaptureAction(
                    state.Definition.Id,
                    state.Value,
                    state.Triggered,
                    state.PressedThisFrame,
                    state.ReleasedThisFrame);
            }
        }

        /// <summary>
        /// Inject action value for next Update() tick.
        /// Primarily for automation and deterministic test driving.
        /// </summary>
        public void InjectAction(string actionId, Vector3 value)
        {
            _injections[actionId] = value;
        }

        public void InjectButtonPress(string actionId) => InjectAction(actionId, Vector3.One);

        public void InjectButtonRelease(string actionId) => _injections.Remove(actionId);

        /// <summary>Whether a synthetic injection is currently held for this action (true after press, false after release).</summary>
        public bool IsInjectionActive(string actionId) => _injections.ContainsKey(actionId);

        public void Update(float deltaTime)
        {
            UpdateRevision++;
            RefreshPointerState();

            if (InputBlocked)
            {
                // Suppressed frames read as up without erasing gesture history: wiping the
                // previous-frame press/release memory here fabricates a fresh press when the
                // block lifts while the button is still held, and re-anchoring interaction
                // judges mid-gesture re-measures travel from the re-anchor so long drags
                // misjudge as taps.
                for (int i = 0; i < _actionStates.Length; i++)
                {
                    _actionStates[i].SuppressThisFrame();
                }

                return;
            }

            Array.Clear(_tempValues, 0, _tempValues.Length);
            _seenBindingContributions.Clear();

            for (int contextIndex = 0; contextIndex < _activeContexts.Count; contextIndex++)
            {
                var context = _activeContexts[contextIndex];
                var bindings = context.Bindings;
                for (int bindingIndex = 0; bindingIndex < bindings.Length; bindingIndex++)
                {
                    ref readonly var binding = ref bindings[bindingIndex];
                    if (binding.ActionIndex < 0)
                    {
                        continue;
                    }

                    if (!_seenBindingContributions.Add(binding.ContributionKey))
                    {
                        continue;
                    }

                    Vector3 value = ResolveBindingValue(in binding);
                    if (binding.Interactions.Length > 0)
                    {
                        value = EvaluateBindingInteractions(bindings[bindingIndex], value, deltaTime, _mousePosition)
                            ? Vector3.One
                            : Vector3.Zero;
                    }

                    _tempValues[binding.ActionIndex] += value;
                }
            }

            foreach (var (actionId, value) in _injections)
            {
                if (_actionIndices.TryGetValue(actionId, out int actionIndex))
                {
                    _tempValues[actionIndex] = value;
                }
            }
            _injections.Clear();

            for (int i = 0; i < _actionStates.Length; i++)
            {
                _actionStates[i].Update(_tempValues[i]);
            }
        }

        private static void ResetInteractionStates(CompiledContext context)
        {
            var bindings = context.Bindings;
            for (int bindingIndex = 0; bindingIndex < bindings.Length; bindingIndex++)
            {
                ResetInteractionStates(bindings[bindingIndex]);
            }
        }

        private static void ResetInteractionStates(CompiledBinding binding)
        {
            ResetInteractionStates(binding.Interactions);
            var parts = binding.CompositeParts;
            for (int partIndex = 0; partIndex < parts.Length; partIndex++)
            {
                ResetInteractionStates(parts[partIndex]);
            }
        }

        private static void ResetInteractionStates(CompiledInteraction[] interactions)
        {
            for (int i = 0; i < interactions.Length; i++)
            {
                interactions[i].Reset();
            }
        }

        private void ResetInteractionStates()
        {
            for (int contextIndex = 0; contextIndex < _activeContexts.Count; contextIndex++)
            {
                ResetInteractionStates(_activeContexts[contextIndex]);
            }
        }

        /// <summary>
        /// Steps every configured interaction of one binding against the raw press state
        /// and this frame's pointer; true on exactly the frame one of the time-sequence
        /// judges completes. The gated contribution is a one-frame pulse, so the action's
        /// PressedThisFrame detectors report the completion as pressed-this-frame and it
        /// folds into the tick snapshot like any other action press.
        /// </summary>
        private static bool EvaluateBindingInteractions(
            CompiledBinding binding,
            Vector3 rawValue,
            float deltaTime,
            Vector2 pointer)
        {
            bool pressed = rawValue.LengthSquared() > 0.25f;
            bool completed = false;
            var interactions = binding.Interactions;
            for (int i = 0; i < interactions.Length; i++)
            {
                if (StepInteraction(interactions[i], pressed, pointer, deltaTime))
                {
                    completed = true;
                }
            }

            return completed;
        }

        private static bool StepInteraction(CompiledInteraction interaction, bool pressed, Vector2 pointer, float deltaTime)
        {
            switch (interaction.Kind)
            {
                case InteractionKind.Tap:
                    if (pressed)
                    {
                        if (!interaction.Held)
                        {
                            interaction.Held = true;
                            interaction.PressPosition = pointer;
                        }

                        return false;
                    }

                    if (interaction.Held)
                    {
                        interaction.Held = false;
                        return PointerTravelPixels(pointer, interaction.PressPosition) <= interaction.MaxTravelPixels;
                    }

                    return false;

                case InteractionKind.Drag:
                    if (pressed)
                    {
                        if (!interaction.Held)
                        {
                            interaction.Held = true;
                            interaction.PressPosition = pointer;
                        }

                        return false;
                    }

                    if (interaction.Held)
                    {
                        interaction.Held = false;
                        float travel = PointerTravelPixels(pointer, interaction.PressPosition);
                        return travel >= interaction.ThresholdPixels || travel > interaction.MaxTravelPixels;
                    }

                    return false;

                case InteractionKind.Hold:
                    if (!pressed)
                    {
                        interaction.Held = false;
                        interaction.HeldSeconds = 0f;
                        interaction.HoldFired = false;
                        return false;
                    }

                    if (!interaction.Held)
                    {
                        interaction.Held = true;
                        interaction.PressPosition = pointer;
                        interaction.HeldSeconds = 0f;
                        interaction.HoldFired = false;
                    }

                    interaction.HeldSeconds += deltaTime;
                    if (!interaction.HoldFired && interaction.HeldSeconds >= interaction.DurationSeconds)
                    {
                        interaction.HoldFired = true;
                        return true;
                    }

                    return false;

                case InteractionKind.MultiTap:
                    interaction.SecondsSinceTap += deltaTime;
                    if (pressed)
                    {
                        if (!interaction.Held)
                        {
                            interaction.Held = true;
                            interaction.PressPosition = pointer;
                        }

                        return false;
                    }

                    if (interaction.Held)
                    {
                        interaction.Held = false;
                        if (PointerTravelPixels(pointer, interaction.PressPosition) > TapMaxTravelPixels)
                        {
                            interaction.TapsCompleted = 0;
                            return false;
                        }

                        interaction.TapsCompleted = interaction.SecondsSinceTap > interaction.TapWindowSeconds
                            ? 1
                            : interaction.TapsCompleted + 1;
                        interaction.SecondsSinceTap = 0f;
                        if (interaction.TapsCompleted >= interaction.TapCount)
                        {
                            interaction.TapsCompleted = 0;
                            return true;
                        }
                    }

                    return false;

                default:
                    return false;
            }
        }

        private static float PointerTravelPixels(Vector2 current, Vector2 press)
        {
            float dx = current.X - press.X;
            float dy = current.Y - press.Y;
            return MathF.Sqrt((dx * dx) + (dy * dy));
        }

        private CompiledContext CompileContext(InputContextDef context)
        {
            var bindings = context.Bindings ?? new List<InputBindingDef>();
            var compiledBindings = new CompiledBinding[bindings.Count];
            for (int i = 0; i < bindings.Count; i++)
            {
                compiledBindings[i] = CompileBinding(bindings[i]);
            }

            ValidateInteractionTravelPartition(compiledBindings, context.Id);

            return new CompiledContext
            {
                Id = context.Id,
                Priority = context.Priority,
                Bindings = compiledBindings,
            };
        }

        /// <summary>
        /// Tap judges partition the release-travel axis from below (complete at travel ≤
        /// MaxTravelPixels) and Drag judges from above (complete at travel ≥ ThresholdPixels
        /// or, via the gap-fold arm, travel > MaxTravelPixels). Bindings of one context judge
        /// the same gesture stream, so a Tap slop reaching into a Drag completion set would
        /// fire two actions for one release — fail closed at compile instead.
        /// </summary>
        private static void ValidateInteractionTravelPartition(CompiledBinding[] bindings, string contextId)
        {
            for (int i = 0; i < bindings.Length; i++)
            {
                CompiledInteraction[] dragInteractions = bindings[i].Interactions;
                for (int d = 0; d < dragInteractions.Length; d++)
                {
                    if (dragInteractions[d].Kind != InteractionKind.Drag)
                    {
                        continue;
                    }

                    float threshold = dragInteractions[d].ThresholdPixels;
                    float dragSlop = dragInteractions[d].MaxTravelPixels;
                    for (int j = 0; j < bindings.Length; j++)
                    {
                        if (!string.Equals(bindings[j].Path, bindings[i].Path, StringComparison.Ordinal))
                        {
                            continue;
                        }

                        CompiledInteraction[] tapInteractions = bindings[j].Interactions;
                        for (int t = 0; t < tapInteractions.Length; t++)
                        {
                            if (tapInteractions[t].Kind != InteractionKind.Tap)
                            {
                                continue;
                            }

                            float tapSlop = tapInteractions[t].MaxTravelPixels;
                            bool overlaps = threshold <= dragSlop
                                ? tapSlop >= threshold
                                : tapSlop > dragSlop;
                            if (overlaps)
                            {
                                throw new InvalidOperationException(
                                    $"LUDOTS_INPUT_INTERACTION_TRAVEL_OVERLAP: context '{contextId}' path '{bindings[i].Path}' declares Tap (MaxTravelPixels={tapSlop}) and Drag (ThresholdPixels={threshold}, MaxTravelPixels={dragSlop}) whose travel windows overlap; one release would complete both. Keep the Tap slop strictly below the Drag completion floor.");
                            }
                        }
                    }
                }
            }
        }

        private CompiledBinding CompileBinding(InputBindingDef binding)
        {
            // A binding whose action id is not declared in the actions directory compiles to
            // -1 (skip) — never default(0): Dictionary.TryGetValue writes default(int)=0 into
            // the out param on miss, which would silently alias the first declared action's
            // slot (engine-reserved ids like PointerMoved are intentionally undeclared).
            int actionIndex = -1;
            if (!string.IsNullOrWhiteSpace(binding.ActionId) &&
                _actionIndices.TryGetValue(binding.ActionId, out int resolved))
            {
                actionIndex = resolved;
            }

            var compiled = new CompiledBinding
            {
                ActionIndex = actionIndex,
                Path = binding.Path ?? string.Empty,
                Processors = CompileProcessors(binding.Processors),
            };

            if (!string.IsNullOrEmpty(binding.CompositeType))
            {
                var parts = binding.CompositeParts ?? new List<InputBindingDef>();
                compiled.CompositeParts = new CompiledBinding[parts.Count];
                for (int i = 0; i < parts.Count; i++)
                {
                    compiled.CompositeParts[i] = CompileBinding(parts[i]);
                }

                compiled.SourceKind = binding.CompositeType switch
                {
                    string type when string.Equals(type, "Vector2", StringComparison.OrdinalIgnoreCase) =>
                        BindingSourceKind.CompositeVector2,
                    string type when string.Equals(type, "ButtonChord", StringComparison.OrdinalIgnoreCase) ||
                                     string.Equals(type, "Chord", StringComparison.OrdinalIgnoreCase) =>
                        BindingSourceKind.CompositeButtonChord,
                    _ => BindingSourceKind.Unsupported
                };
                compiled.Interactions = CompileInteractions(binding.Interactions, binding.Path, compiled.SourceKind);
                compiled.ContributionKey = BuildContributionKey(compiled);
                return compiled;
            }

            if (compiled.Path.StartsWith("<Mouse>/Pos", StringComparison.Ordinal))
            {
                compiled.SourceKind = BindingSourceKind.MousePosition;
            }
            else if (compiled.Path.StartsWith("<Mouse>/Delta", StringComparison.Ordinal))
            {
                compiled.SourceKind = BindingSourceKind.MouseDelta;
            }
            else if (compiled.Path.StartsWith("<Mouse>/Scroll", StringComparison.Ordinal))
            {
                compiled.SourceKind = BindingSourceKind.MouseScroll;
            }
            else if (compiled.Path.StartsWith("<Keyboard>", StringComparison.Ordinal) || compiled.Path.StartsWith("<Mouse>", StringComparison.Ordinal))
            {
                compiled.SourceKind = BindingSourceKind.Button;
            }
            else
            {
                compiled.SourceKind = BindingSourceKind.Unsupported;
            }

            compiled.Interactions = CompileInteractions(binding.Interactions, binding.Path, compiled.SourceKind);
            compiled.ContributionKey = BuildContributionKey(compiled);
            return compiled;
        }

        private static string BuildContributionKey(CompiledBinding binding)
        {
            var sb = new StringBuilder();
            AppendContributionKey(sb, binding);
            return sb.ToString();
        }

        private static void AppendContributionKey(StringBuilder sb, CompiledBinding binding)
        {
            sb.Append(binding.ActionIndex)
                .Append('|')
                .Append((byte)binding.SourceKind)
                .Append('|')
                .Append(binding.Path)
                .Append('|');

            var processors = binding.Processors;
            for (int i = 0; i < processors.Length; i++)
            {
                sb.Append((byte)processors[i].Kind)
                    .Append(':')
                    .Append(processors[i].Scalar.ToString("R", CultureInfo.InvariantCulture))
                    .Append(':')
                    .Append(processors[i].AxisMask)
                    .Append(',');
            }

            AppendInteractions(sb, binding.Interactions);

            var parts = binding.CompositeParts;
            if (parts.Length == 0)
            {
                return;
            }

            sb.Append('[');
            for (int i = 0; i < parts.Length; i++)
            {
                if (i > 0)
                {
                    sb.Append(';');
                }

                AppendContributionKey(sb, parts[i]);
            }

            sb.Append(']');
        }

        private static void AppendInteractions(StringBuilder sb, CompiledInteraction[] interactions)
        {
            for (int i = 0; i < interactions.Length; i++)
            {
                sb.Append('#')
                    .Append((byte)interactions[i].Kind)
                    .Append(':')
                    .Append(interactions[i].DurationSeconds.ToString("R", CultureInfo.InvariantCulture))
                    .Append(':')
                    .Append(interactions[i].TapCount)
                    .Append(':')
                    .Append(interactions[i].TapWindowSeconds.ToString("R", CultureInfo.InvariantCulture))
                    .Append(':')
                    .Append(interactions[i].MaxTravelPixels.ToString("R", CultureInfo.InvariantCulture))
                    .Append(':')
                    .Append(interactions[i].ThresholdPixels.ToString("R", CultureInfo.InvariantCulture))
                    .Append(',');
            }
        }

        private static int CountTopLevelBindings(InputConfigRoot config)
        {
            int count = 0;
            for (int i = 0; i < config.Contexts.Count; i++)
            {
                count += config.Contexts[i].Bindings?.Count ?? 0;
            }

            return count;
        }

        private static CompiledProcessor[] CompileProcessors(List<InputModifierDef> processorDefs)
        {
            if (processorDefs == null || processorDefs.Count == 0)
            {
                return Array.Empty<CompiledProcessor>();
            }

            var compiled = new CompiledProcessor[processorDefs.Count];
            for (int i = 0; i < processorDefs.Count; i++)
            {
                var def = processorDefs[i];
                compiled[i] = def.Type switch
                {
                    "Normalize" => new CompiledProcessor(ProcessorKind.Normalize, 0f),
                    "Deadzone" => new CompiledProcessor(ProcessorKind.Deadzone, RequireParameter(def.Parameters, "Min", def.Type)),
                    "Scale" => new CompiledProcessor(ProcessorKind.Scale, RequireParameter(def.Parameters, "Factor", def.Type)),
                    "Invert" => new CompiledProcessor(ProcessorKind.Invert, 0f, GetAxisMask(def.Parameters)),
                    _ => new CompiledProcessor(ProcessorKind.Unknown, 0f)
                };
            }

            return compiled;
        }

        /// <summary>
        /// Compiles a binding's Interactions: time-sequence judges (Tap/Hold/Drag/MultiTap)
        /// that turn the raw button press into a completion pulse. Only button-like sources
        /// carry them — a time sequence over an axis or pointer stream has no press/release
        /// to judge, so those fail closed at compile instead of silently passing through.
        /// Parameter surface: Hold reads DurationSeconds; MultiTap reads TapCount and
        /// TapWindowSeconds; Tap reads MaxTravelPixels (release-travel slop, default
        /// <see cref="TapMaxTravelPixels"/>); Drag reads ThresholdPixels (deliberate-drag
        /// floor, default <see cref="DragThresholdPixels"/>) and MaxTravelPixels (gap-fold
        /// slop, defaulting to the threshold so the fold arm is inert until authored).
        /// </summary>
        private static CompiledInteraction[] CompileInteractions(List<InputModifierDef> interactionDefs, string? path, BindingSourceKind sourceKind)
        {
            if (interactionDefs == null || interactionDefs.Count == 0)
            {
                return Array.Empty<CompiledInteraction>();
            }

            if (sourceKind != BindingSourceKind.Button && sourceKind != BindingSourceKind.CompositeButtonChord)
            {
                throw new InvalidOperationException(
                    $"LUDOTS_INPUT_INTERACTION_UNSUPPORTED_SOURCE: binding '{path}' declares Interactions on a non-button source ({sourceKind}); time-sequence judging needs a button press.");
            }

            var compiled = new CompiledInteraction[interactionDefs.Count];
            for (int i = 0; i < interactionDefs.Count; i++)
            {
                InputModifierDef def = interactionDefs[i];
                switch (def.Type)
                {
                    case "Tap":
                        compiled[i] = new CompiledInteraction(
                            InteractionKind.Tap,
                            0f,
                            0,
                            0f,
                            RequireFiniteParameter(
                                OptionalParameter(def.Parameters, "MaxTravelPixels", TapMaxTravelPixels),
                                path,
                                def.Type,
                                "MaxTravelPixels"),
                            0f);
                        break;
                    case "Drag":
                        float thresholdPixels = RequireFiniteParameter(
                            OptionalParameter(def.Parameters, "ThresholdPixels", DragThresholdPixels),
                            path,
                            def.Type,
                            "ThresholdPixels");
                        compiled[i] = new CompiledInteraction(
                            InteractionKind.Drag,
                            0f,
                            0,
                            0f,
                            RequireFiniteParameter(
                                OptionalParameter(def.Parameters, "MaxTravelPixels", thresholdPixels),
                                path,
                                def.Type,
                                "MaxTravelPixels"),
                            thresholdPixels);
                        break;
                    case "Hold":
                        compiled[i] = new CompiledInteraction(
                            InteractionKind.Hold,
                            OptionalParameter(def.Parameters, "DurationSeconds", HoldDefaultDurationSeconds),
                            0,
                            0f,
                            0f,
                            0f);
                        break;
                    case "MultiTap":
                        compiled[i] = new CompiledInteraction(
                            InteractionKind.MultiTap,
                            0f,
                            (int)OptionalParameter(def.Parameters, "TapCount", MultiTapDefaultTapCount),
                            OptionalParameter(def.Parameters, "TapWindowSeconds", MultiTapDefaultWindowSeconds),
                            0f,
                            0f);
                        break;
                    default:
                        throw new InvalidOperationException(
                            $"LUDOTS_INPUT_INTERACTION_UNKNOWN: binding '{path}' declares unknown interaction '{def.Type}'; supported: Tap, Hold, Drag, MultiTap.");
                }
            }

            return compiled;
        }

        private static float OptionalParameter(IReadOnlyList<InputParameterDef> parameters, string name, float fallback)
        {
            if (parameters != null)
            {
                for (int i = 0; i < parameters.Count; i++)
                {
                    var parameter = parameters[i];
                    if (parameter != null && string.Equals(parameter.Name, name, StringComparison.Ordinal))
                    {
                        return parameter.Value;
                    }
                }
            }

            return fallback;
        }

        private static float RequireFiniteParameter(float value, string? path, string interactionType, string parameterName)
        {
            if (!float.IsFinite(value) || value < 0f)
            {
                throw new InvalidOperationException(
                    $"LUDOTS_INPUT_INTERACTION_INVALID_PARAMETER: binding '{path}' interaction '{interactionType}' parameter '{parameterName}' must be a finite non-negative pixel count (got {value}).");
            }

            return value;
        }

        private static float RequireParameter(IReadOnlyList<InputParameterDef> parameters, string name, string processorType)
        {
            if (parameters != null)
            {
                for (int i = 0; i < parameters.Count; i++)
                {
                    var parameter = parameters[i];
                    if (parameter != null && string.Equals(parameter.Name, name, StringComparison.Ordinal))
                    {
                        return parameter.Value;
                    }
                }
            }

            throw new InvalidOperationException(
                $"LUDOTS_INPUT_PROCESSOR_PARAMETER_REQUIRED: input processor '{processorType}' must explicitly define parameter '{name}'.");
        }

        private static byte GetAxisMask(IReadOnlyList<InputParameterDef> parameters)
        {
            if (parameters == null || parameters.Count == 0)
            {
                return 0b111;
            }

            byte mask = 0;
            for (int i = 0; i < parameters.Count; i++)
            {
                var parameter = parameters[i];
                if (parameter == null || parameter.Value == 0f)
                {
                    continue;
                }

                if (string.Equals(parameter.Name, "X", StringComparison.OrdinalIgnoreCase))
                {
                    mask |= 0b001;
                }
                else if (string.Equals(parameter.Name, "Y", StringComparison.OrdinalIgnoreCase))
                {
                    mask |= 0b010;
                }
                else if (string.Equals(parameter.Name, "Z", StringComparison.OrdinalIgnoreCase))
                {
                    mask |= 0b100;
                }
            }

            return mask == 0 ? (byte)0b111 : mask;
        }

        private Vector3 ResolveBindingValue(in CompiledBinding binding)
        {
            Vector3 rawValue = ReadRawBindingValue(in binding);
            var processors = binding.Processors;
            for (int i = 0; i < processors.Length; i++)
            {
                rawValue = ApplyProcessor(rawValue, in processors[i]);
            }

            return rawValue;
        }

        private Vector3 ReadRawBindingValue(in CompiledBinding binding)
        {
            switch (binding.SourceKind)
            {
                case BindingSourceKind.MousePosition:
                    return new Vector3(_mousePosition.X, _mousePosition.Y, 0f);
                case BindingSourceKind.MouseDelta:
                    return new Vector3(_mouseDelta.X, _mouseDelta.Y, 0f);
                case BindingSourceKind.MouseScroll:
                    return new Vector3(_backend.GetMouseWheel(), 0f, 0f);
                case BindingSourceKind.Button:
                    return _backend.GetButton(binding.Path) ? Vector3.One : Vector3.Zero;
                case BindingSourceKind.CompositeVector2:
                {
                    float up = ReadCompositeScalar(binding.CompositeParts, 0);
                    float down = ReadCompositeScalar(binding.CompositeParts, 1);
                    float left = ReadCompositeScalar(binding.CompositeParts, 2);
                    float right = ReadCompositeScalar(binding.CompositeParts, 3);
                    return new Vector3(right - left, up - down, 0f);
                }
                case BindingSourceKind.CompositeButtonChord:
                {
                    if (binding.CompositeParts.Length == 0)
                    {
                        return Vector3.Zero;
                    }

                    for (int index = 0; index < binding.CompositeParts.Length; index++)
                    {
                        if (ResolveBindingValue(in binding.CompositeParts[index]).LengthSquared() <= 0.25f)
                        {
                            return Vector3.Zero;
                        }
                    }

                    return Vector3.One;
                }
                default:
                    return Vector3.Zero;
            }
        }

        private void RefreshPointerState()
        {
            var currentMousePosition = _backend.GetMousePosition();
            if (!float.IsFinite(currentMousePosition.X) || !float.IsFinite(currentMousePosition.Y))
            {
                _mouseDelta = Vector2.Zero;
                _mousePosition = new Vector2(-1f, -1f);
                _hasMousePosition = false;
                return;
            }

            _mouseDelta = _hasMousePosition
                ? currentMousePosition - _mousePosition
                : Vector2.Zero;
            _mousePosition = currentMousePosition;
            _hasMousePosition = true;
        }

        private float ReadCompositeScalar(CompiledBinding[] parts, int index)
        {
            if ((uint)index >= (uint)parts.Length)
            {
                return 0f;
            }

            return ResolveBindingValue(in parts[index]).X;
        }

        private static Vector3 ApplyProcessor(Vector3 value, in CompiledProcessor processor)
        {
            switch (processor.Kind)
            {
                case ProcessorKind.Normalize:
                    if (value.LengthSquared() > 1f)
                    {
                        return Vector3.Normalize(value);
                    }
                    return value;
                case ProcessorKind.Deadzone:
                    return value.Length() < processor.Scalar ? Vector3.Zero : value;
                case ProcessorKind.Scale:
                    return value * processor.Scalar;
                case ProcessorKind.Invert:
                    if ((processor.AxisMask & 0b001) != 0)
                    {
                        value.X = -value.X;
                    }
                    if ((processor.AxisMask & 0b010) != 0)
                    {
                        value.Y = -value.Y;
                    }
                    if ((processor.AxisMask & 0b100) != 0)
                    {
                        value.Z = -value.Z;
                    }
                    return value;
                default:
                    return value;
            }
        }

        private sealed class CompiledContext
        {
            public string Id { get; init; } = string.Empty;
            public int Priority { get; init; }
            public CompiledBinding[] Bindings { get; init; } = Array.Empty<CompiledBinding>();
        }

        private sealed class CompiledBinding
        {
            public int ActionIndex { get; init; }
            public string Path { get; init; } = string.Empty;
            public BindingSourceKind SourceKind { get; set; }
            public string ContributionKey { get; set; } = string.Empty;
            public CompiledBinding[] CompositeParts { get; set; } = Array.Empty<CompiledBinding>();
            public CompiledProcessor[] Processors { get; init; } = Array.Empty<CompiledProcessor>();
            public CompiledInteraction[] Interactions { get; set; } = Array.Empty<CompiledInteraction>();
        }

        private enum InteractionKind : byte
        {
            Tap = 1,
            Hold = 2,
            Drag = 3,
            MultiTap = 4,
        }

        /// <summary>
        /// One compiled time-sequence judge plus its live press-tracking state. State lives
        /// on the compiled binding instance for the handler's lifetime; the machine is
        /// stepped once per visual frame with the frame's pointer position and duration.
        /// </summary>
        private sealed class CompiledInteraction
        {
            public readonly InteractionKind Kind;

            // Hold parameters
            public readonly float DurationSeconds;

            // MultiTap parameters
            public readonly int TapCount;
            public readonly float TapWindowSeconds;

            // Tap / Drag travel parameters (pixels):
            // Tap completes on release within MaxTravelPixels (the "same position" slop).
            // Drag completes on release at or beyond ThresholdPixels (deliberate drag), or —
            // the gap-fold arm — beyond MaxTravelPixels. Defaulting MaxTravelPixels to the
            // threshold keeps the fold inert, so an unconfigured Drag behaves exactly as the
            // threshold alone; authoring MaxTravelPixels (typically equal to the tap slop of
            // the sibling Tap binding on the same path) pins the (slop, threshold) travel
            // interval to the Drag judge, closing the legacy (6, 8) dead zone where neither
            // judge completed and gesture consumers hung.
            public readonly float MaxTravelPixels;
            public readonly float ThresholdPixels;

            // Live press tracking
            public bool Held;
            public Vector2 PressPosition;
            public float HeldSeconds;
            public bool HoldFired;
            public float SecondsSinceTap;
            public int TapsCompleted;

            public CompiledInteraction(
                InteractionKind kind,
                float durationSeconds,
                int tapCount,
                float tapWindowSeconds,
                float maxTravelPixels,
                float thresholdPixels)
            {
                Kind = kind;
                DurationSeconds = durationSeconds;
                TapCount = tapCount;
                TapWindowSeconds = tapWindowSeconds;
                MaxTravelPixels = maxTravelPixels;
                ThresholdPixels = thresholdPixels;
            }

            public void Reset()
            {
                Held = false;
                PressPosition = default;
                HeldSeconds = 0f;
                HoldFired = false;
                SecondsSinceTap = 0f;
                TapsCompleted = 0;
            }
        }

        private readonly record struct CompiledProcessor(ProcessorKind Kind, float Scalar, byte AxisMask = 0);

        private enum BindingSourceKind : byte
        {
            Unsupported = 0,
            MousePosition = 1,
            MouseDelta = 2,
            MouseScroll = 3,
            Button = 4,
            CompositeVector2 = 5,
            CompositeButtonChord = 6,
        }

        private enum ProcessorKind : byte
        {
            Unknown = 0,
            Normalize = 1,
            Deadzone = 2,
            Scale = 3,
            Invert = 4,
        }
    }
}

using System;
using System.Collections.Generic;
using Ludots.Core.Input.Config;
using Ludots.Core.Registry;

namespace Ludots.Core.Input.Interaction
{
    /// <summary>
    /// Compiled <c>Input/interaction_modes.json</c> catalog (#1306): mode id → activated IMC
    /// input context set. Read-only lookup consumed by <c>InputContextProjectionSystem</c> and the
    /// <c>SetInteractionMode</c> graph op; unknown mode ids fail fast by name.
    /// </summary>
    public sealed class InteractionModeMap
    {
        private readonly StringIntRegistry _modeIds;
        private readonly List<InteractionModeContextBinding[]> _contextsByModeId = new();
        private int _normalModeId;

        public InteractionModeMap(StringIntRegistry modeIdRegistry)
        {
            _modeIds = modeIdRegistry ?? throw new ArgumentNullException(nameof(modeIdRegistry));
        }

        /// <summary>Mode id space; graph ops and saves reference modes through these ids.</summary>
        public StringIntRegistry ModeIdRegistry => _modeIds;

        /// <summary>Registered id of the reserved sparse default mode.</summary>
        public int NormalModeId => _normalModeId;

        /// <summary>
        /// Compile every declared mode. Fails fast on duplicate mode ids, a missing or contexted
        /// reserved normal mode, and context entries whose id is undefined in the input config or
        /// whose priority drifts from the IMC context definition (the handler owns stack order).
        /// A null input config skips cross-file validation (test harness contract).
        /// </summary>
        public void Install(InteractionModesConfig config, InputConfigRoot inputConfig)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            if (config.Modes == null)
            {
                throw new InvalidOperationException("Interaction mode config must explicitly define modes.");
            }

            bool hasNormal = false;
            for (int i = 0; i < config.Modes.Count; i++)
            {
                InteractionModeDefinition definition = config.Modes[i]
                    ?? throw new InvalidOperationException($"Interaction mode config modes[{i}] must be an object.");
                string path = $"interaction modes[{i}]";
                RequireTrimmedNonEmpty(definition.Id, $"{path}.id");

                if (definition.Contexts == null)
                {
                    throw new InvalidOperationException($"{path}.contexts must be explicitly declared (empty array allowed).");
                }

                bool isNormal = string.Equals(definition.Id, InteractionModeIds.Normal, StringComparison.Ordinal);
                if (isNormal)
                {
                    if (hasNormal)
                    {
                        throw new InvalidOperationException($"{path}.id duplicates the reserved mode '{InteractionModeIds.Normal}'.");
                    }

                    if (definition.Contexts.Count > 0)
                    {
                        throw new InvalidOperationException(
                            $"{path}: the reserved mode '{InteractionModeIds.Normal}' is the sparse default (no component) and must declare no input contexts.");
                    }

                    hasNormal = true;
                }

                int modeId = _modeIds.Register(definition.Id);
                if (modeId < _contextsByModeId.Count && _contextsByModeId[modeId] != null)
                {
                    throw new InvalidOperationException($"Interaction mode '{definition.Id}' is already installed.");
                }

                while (_contextsByModeId.Count <= modeId)
                {
                    _contextsByModeId.Add(null);
                }

                _contextsByModeId[modeId] = CompileContexts(definition, path, inputConfig);
                if (isNormal)
                {
                    _normalModeId = modeId;
                }
            }

            if (!hasNormal)
            {
                throw new InvalidOperationException(
                    $"Interaction mode config must declare the reserved mode '{InteractionModeIds.Normal}'.");
            }
        }

        /// <summary>True when the registered mode id is the reserved sparse default.</summary>
        public bool IsNormalMode(int modeId)
        {
            return modeId == _normalModeId;
        }

        /// <summary>Context set activated while the mode is held; empty for the sparse default.</summary>
        public bool TryGetContexts(int modeId, out IReadOnlyList<InteractionModeContextBinding> contexts)
        {
            if (modeId > 0 && modeId < _contextsByModeId.Count && _contextsByModeId[modeId] != null)
            {
                contexts = _contextsByModeId[modeId];
                return true;
            }

            contexts = Array.Empty<InteractionModeContextBinding>();
            return false;
        }

        private static InteractionModeContextBinding[] CompileContexts(
            InteractionModeDefinition definition,
            string path,
            InputConfigRoot inputConfig)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var compiled = new InteractionModeContextBinding[definition.Contexts.Count];
            for (int i = 0; i < definition.Contexts.Count; i++)
            {
                InteractionModeContextRef reference = definition.Contexts[i]
                    ?? throw new InvalidOperationException($"{path}.contexts[{i}] must be an object.");
                string entryPath = $"{path}.contexts[{i}]";
                RequireTrimmedNonEmpty(reference.ContextId, $"{entryPath}.contextId");
                if (!seen.Add(reference.ContextId))
                {
                    throw new InvalidOperationException($"{entryPath}.contextId duplicates context '{reference.ContextId}'.");
                }

                if (inputConfig != null)
                {
                    if (!ContainsContext(inputConfig, reference.ContextId, out int declaredPriority))
                    {
                        throw new InvalidOperationException(
                            $"{entryPath}.contextId references input context '{reference.ContextId}' which is not defined in the input config.");
                    }

                    if (declaredPriority != reference.Priority)
                    {
                        throw new InvalidOperationException(
                            $"{entryPath}: priority {reference.Priority} drifts from input context '{reference.ContextId}' priority {declaredPriority}; the input config owns the IMC stack order.");
                    }
                }

                compiled[i] = new InteractionModeContextBinding(reference.ContextId, reference.Priority);
            }

            return compiled;
        }

        private static bool ContainsContext(InputConfigRoot inputConfig, string contextId, out int priority)
        {
            for (int i = 0; i < inputConfig.Contexts.Count; i++)
            {
                if (string.Equals(inputConfig.Contexts[i].Id, contextId, StringComparison.Ordinal))
                {
                    priority = inputConfig.Contexts[i].Priority;
                    return true;
                }
            }

            priority = 0;
            return false;
        }

        private static void RequireTrimmedNonEmpty(string value, string path)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException($"{path} must be a non-empty string.");
            }

            if (!string.Equals(value, value.Trim(), StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"{path} must not contain leading or trailing whitespace.");
            }
        }
    }
}

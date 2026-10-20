using System;
using System.Collections.Generic;

namespace Ludots.Core.Input.Config
{
    /// <summary>
    /// Maps an action id, plus an integer when that action's bindings carry
    /// <c>intValue</c>, to the key label of one binding. The integer is required
    /// exactly in that case. Key text comes from the binding path inside the contexts
    /// the caller names.
    /// </summary>
    public static class InputBindingKeyLookup
    {
        public static void RequireSlotBinding(InputConfigRoot config, string actionId, int? bindingInt, string where)
        {
            ArgumentNullException.ThrowIfNull(config);
            if (!TryFindAction(config, actionId))
            {
                throw new InvalidOperationException(
                    $"LUDOTS_INPUT_BINDING_KEY_ACTION_UNKNOWN: {where} action '{actionId}' is not a declared input action.");
            }

            CollectBindingInts(config, actionId, out bool anyInt, out HashSet<int>? ints);
            if (bindingInt.HasValue)
            {
                if (!anyInt)
                {
                    throw new InvalidOperationException(
                        $"LUDOTS_INPUT_BINDING_KEY_INT_UNEXPECTED: {where} gives int {bindingInt.Value} but action '{actionId}' bindings carry no intValue.");
                }

                if (ints == null || !ints.Contains(bindingInt.Value))
                {
                    throw new InvalidOperationException(
                        $"LUDOTS_INPUT_BINDING_KEY_INT_UNBOUND: {where} int {bindingInt.Value} is not carried by any binding of action '{actionId}'.");
                }

                return;
            }

            if (anyInt)
            {
                throw new InvalidOperationException(
                    $"LUDOTS_INPUT_BINDING_KEY_INT_REQUIRED: {where} omits int but action '{actionId}' bindings carry intValue.");
            }
        }

        public static string Resolve(
            InputConfigRoot config,
            IReadOnlyList<string> contextIds,
            string scopeName,
            string actionId,
            int? bindingInt)
        {
            ArgumentNullException.ThrowIfNull(config);
            if (contextIds == null || contextIds.Count == 0)
            {
                throw Absent(scopeName, actionId, bindingInt);
            }

            int bestPriority = int.MinValue;
            bool found = false;
            bool bestAmbiguous = false;
            string? path = null;
            string? contextId = null;
            for (int i = 0; i < contextIds.Count; i++)
            {
                string candidateId = contextIds[i];
                InputContextDef context = FindContext(config, candidateId, scopeName);
                if (!TryMatch(context, actionId, bindingInt, out string matchedPath, out bool ambiguous))
                {
                    continue;
                }

                int priority = context.Priority;
                if (found && priority <= bestPriority)
                {
                    continue;
                }

                found = true;
                bestPriority = priority;
                bestAmbiguous = ambiguous;
                path = matchedPath;
                contextId = candidateId;
            }

            if (!found)
            {
                throw Absent(scopeName, actionId, bindingInt);
            }

            if (bestAmbiguous)
            {
                throw new InvalidOperationException(
                    $"LUDOTS_INPUT_BINDING_KEY_AMBIGUOUS: {scopeName} context '{contextId}' binds action '{actionId}' {IntText(bindingInt)} to more than one path.");
            }

            return LabelFromPath(path!, scopeName, contextId!, actionId);
        }

        public static string LabelFromPath(string path, string scopeName, string contextId, string actionId)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new InvalidOperationException(
                    $"LUDOTS_INPUT_BINDING_KEY_PATH_EMPTY: {scopeName} context '{contextId}' action '{actionId}' has an empty binding path.");
            }

            int slash = path.LastIndexOf('/');
            string token = slash >= 0 && slash + 1 < path.Length ? path[(slash + 1)..] : path;
            return token.Length == 1 ? token.ToUpperInvariant() : token;
        }

        private static InvalidOperationException Absent(string scopeName, string actionId, int? bindingInt)
        {
            return new InvalidOperationException(
                $"LUDOTS_INPUT_BINDING_KEY_ABSENT: {scopeName} has no binding for action '{actionId}' {IntText(bindingInt)}.");
        }

        private static string IntText(int? bindingInt)
        {
            return bindingInt.HasValue ? $"with int {bindingInt.Value}" : "with no intValue";
        }

        private static bool TryFindAction(InputConfigRoot config, string actionId)
        {
            IReadOnlyList<InputActionDef>? actions = config.Actions;
            if (actions == null)
            {
                return false;
            }

            for (int i = 0; i < actions.Count; i++)
            {
                InputActionDef? action = actions[i];
                if (action != null && string.Equals(action.Id, actionId, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static void CollectBindingInts(InputConfigRoot config, string actionId, out bool anyInt, out HashSet<int>? ints)
        {
            anyInt = false;
            ints = null;
            IReadOnlyList<InputContextDef>? contexts = config.Contexts;
            if (contexts == null)
            {
                return;
            }

            for (int c = 0; c < contexts.Count; c++)
            {
                List<InputBindingDef>? bindings = contexts[c]?.Bindings;
                if (bindings == null)
                {
                    continue;
                }

                for (int b = 0; b < bindings.Count; b++)
                {
                    InputBindingDef? binding = bindings[b];
                    if (!IsPlainBinding(binding, actionId))
                    {
                        continue;
                    }

                    if (binding!.IntValue is int value)
                    {
                        anyInt = true;
                        ints ??= new HashSet<int>();
                        ints.Add(value);
                    }
                }
            }
        }

        private static InputContextDef FindContext(InputConfigRoot config, string contextId, string scopeName)
        {
            IReadOnlyList<InputContextDef>? contexts = config.Contexts;
            if (contexts != null)
            {
                for (int i = 0; i < contexts.Count; i++)
                {
                    InputContextDef? context = contexts[i];
                    if (context != null && string.Equals(context.Id, contextId, StringComparison.Ordinal))
                    {
                        return context;
                    }
                }
            }

            throw new InvalidOperationException(
                $"LUDOTS_INPUT_BINDING_KEY_CONTEXT_UNKNOWN: {scopeName} names input context '{contextId}' that is not declared.");
        }

        private static bool TryMatch(InputContextDef context, string actionId, int? bindingInt, out string path, out bool ambiguous)
        {
            path = string.Empty;
            ambiguous = false;
            List<InputBindingDef>? bindings = context.Bindings;
            if (bindings == null)
            {
                return false;
            }

            bool found = false;
            for (int i = 0; i < bindings.Count; i++)
            {
                InputBindingDef? binding = bindings[i];
                if (!IsPlainBinding(binding, actionId) || binding!.IntValue != bindingInt)
                {
                    continue;
                }

                if (found && !string.Equals(path, binding.Path, StringComparison.Ordinal))
                {
                    ambiguous = true;
                    return true;
                }

                found = true;
                path = binding.Path ?? string.Empty;
            }

            return found;
        }

        private static bool IsPlainBinding(InputBindingDef? binding, string actionId)
        {
            return binding != null
                && string.IsNullOrWhiteSpace(binding.CompositeType)
                && string.Equals(binding.ActionId, actionId, StringComparison.Ordinal);
        }
    }
}

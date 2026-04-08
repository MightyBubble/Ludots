using System;
using System.Collections.Generic;
using Ludots.Core.Scripting;

namespace Ludots.Core.Input.Interaction
{
    public static class InteractionActionBindingsResolver
    {
        public static InteractionActionBindings Require(IReadOnlyDictionary<string, object> globals, string consumerName)
        {
            if (globals == null) throw new ArgumentNullException(nameof(globals));
            if (string.IsNullOrWhiteSpace(consumerName)) throw new ArgumentException("Consumer name is required.", nameof(consumerName));

            if (globals.TryGetValue(CoreServiceKeys.InteractionActionBindings.Name, out var obj) &&
                obj is InteractionActionBindings bindings)
            {
                return bindings;
            }

            throw new InvalidOperationException(
                $"{consumerName} requires {CoreServiceKeys.InteractionActionBindings.Name} to be registered as the single source of truth for interaction action ids.");
        }
    }
}

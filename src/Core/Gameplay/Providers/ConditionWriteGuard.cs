using System;
using System.Collections.Generic;
using Arch.Core;

namespace Ludots.Core.Gameplay.Providers
{
    public static class ConditionWriteGuard
    {
        public static bool EvaluateReadOnly(
            IConditionProvider condition,
            ProviderExecutionContext context,
            IReadOnlyDictionary<string, object?> parameters)
        {
            ArgumentNullException.ThrowIfNull(condition);
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(parameters);

            int before = CountAlive(context.World);
            bool result = condition.Evaluate(context, parameters);
            int after = CountAlive(context.World);
            if (after != before)
            {
                throw new InvalidOperationException(
                    $"{ProviderFailureCodes.ConditionWriteDetected}: condition provider mutated world entity count from {before} to {after}.");
            }

            return result;
        }

        private static int CountAlive(World world)
        {
            int count = 0;
            var query = new QueryDescription();
            world.Query(in query, (Entity _) => { count++; });
            return count;
        }
    }
}

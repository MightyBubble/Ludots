using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Spatial;

namespace Ludots.Core.Gameplay.GAS
{
    /// <summary>
    /// Runtime services and scratch state exposed to builtin phase handlers.
    /// Lets builtin handlers execute real gameplay work without pushing special-cases
    /// back out into the outer effect systems.
    /// </summary>
    public sealed class BuiltinHandlerExecutionContext
    {
        public ISpatialQueryService? SpatialQueries { get; set; }
        public RootBudgetTable? FanOutBudget { get; set; }
        public List<FanOutCommand>? FanOutCommands { get; set; }
        public Entity[]? ResolverBuffer { get; set; }

        public int ResolvedCandidateCount { get; private set; }
        public int DroppedCount { get; private set; }

        public void ResetPerEffect()
        {
            ResolvedCandidateCount = 0;
            DroppedCount = 0;
        }

        public void SetResolvedCandidateCount(int count)
        {
            ResolvedCandidateCount = Math.Max(0, count);
        }

        public void ClearResolvedCandidates()
        {
            ResolvedCandidateCount = 0;
        }

        public void AddDropped(int dropped)
        {
            if (dropped > 0)
            {
                DroppedCount += dropped;
            }
        }
    }

    internal static class BuiltinHandlerRuntimeScope
    {
        [ThreadStatic]
        private static BuiltinHandlerExecutionContext? _current;

        public static BuiltinHandlerExecutionContext? Current => _current;

        public static Scope Push(BuiltinHandlerExecutionContext? context)
        {
            var previous = _current;
            _current = context;
            return new Scope(previous);
        }

        internal readonly struct Scope : IDisposable
        {
            private readonly BuiltinHandlerExecutionContext? _previous;

            public Scope(BuiltinHandlerExecutionContext? previous)
            {
                _previous = previous;
            }

            public void Dispose()
            {
                _current = _previous;
            }
        }
    }
}

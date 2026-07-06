using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Gameplay.Exchange;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Gameplay.Lifecycle;
using Ludots.Core.Gameplay.Progression;
using Ludots.Core.Spatial;
using Ludots.Core.Vision;

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
        public RuntimeEntitySpawnQueue? SpawnRequests { get; set; }
        public RuntimeEntityLifecycleQueue? LifecycleRequests { get; set; }
        public EntityLifecycleRuntimeServices? LifecycleServices { get; set; }
        public LifecycleTransactionState? LifecycleTransaction { get; set; }
        public ExchangeRuntime? Exchange { get; set; }
        public RelationshipRuntime? Relationships { get; set; }
        public ProgressionRequirementEvaluator? ProgressionEvaluator { get; set; }
        public KnowledgeAreaRevealRuntime? KnowledgeAreaReveal { get; set; }
        public OrderTypeRegistry? OrderTypeRegistry { get; set; }
        public OrderRuleRegistry? OrderRuleRegistry { get; set; }
        public int CurrentStep { get; set; }
        public int StepRateHz { get; set; }

        public int ResolvedCandidateCount { get; private set; }
        public int DroppedCount { get; private set; }
        public bool HasExchangeResult { get; private set; }
        public ExchangeExecutionResult LastExchangeResult { get; private set; }
        public int AttributeDeltaId { get; private set; } = -1;
        public float AttributeDelta { get; private set; }
        public bool HasAttributeDelta => AttributeDeltaId >= 0 && AttributeDelta != 0f;

        public void ResetPerEffect()
        {
            ResolvedCandidateCount = 0;
            DroppedCount = 0;
            HasExchangeResult = false;
            LastExchangeResult = default;
            AttributeDeltaId = -1;
            AttributeDelta = 0f;
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

        public void RecordExchangeResult(ExchangeExecutionResult result)
        {
            HasExchangeResult = true;
            LastExchangeResult = result;
        }

        public void RecordAttributeDelta(int attributeId, float delta)
        {
            if (attributeId < 0 || delta == 0f)
            {
                return;
            }

            if (AttributeDeltaId == attributeId)
            {
                AttributeDelta += delta;
                return;
            }

            if (AttributeDeltaId < 0)
            {
                AttributeDeltaId = attributeId;
                AttributeDelta = delta;
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

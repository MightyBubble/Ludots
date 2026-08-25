using System;
using System.Collections.Generic;
using Arch.Core;

namespace Ludots.Core.Gameplay.Activities
{
    public static class ActivitySignalFailures
    {
        public const string UnknownSourceKey = "signal.unknown_source_key";
        public const string Malformed = "signal.malformed";
        public const string NoSubscription = "signal.no_subscription";
        public const string MatchConditionFailed = "subscription.match_condition_failed";
        public const string LifecycleKeyNotSource = "lifecycle_key_not_source";
    }

    public sealed class ActivitySignal
    {
        public ActivitySignal(
            string sourceKey,
            string signalId,
            long occurredAt,
            Entity scopeRef,
            IReadOnlyList<Entity>? objectRefs,
            IReadOnlyDictionary<string, object?>? parameters)
        {
            SourceKey = sourceKey ?? throw new ArgumentNullException(nameof(sourceKey));
            SignalId = signalId ?? throw new ArgumentNullException(nameof(signalId));
            OccurredAt = occurredAt;
            ScopeRef = scopeRef;
            ObjectRefs = objectRefs;
            Parameters = parameters;
        }

        public string SourceKey { get; }
        public string SignalId { get; }
        public long OccurredAt { get; }
        public Entity ScopeRef { get; }
        public IReadOnlyList<Entity>? ObjectRefs { get; }
        public IReadOnlyDictionary<string, object?>? Parameters { get; }
    }

    public readonly record struct ActivitySignalMatchResult(
        string ActivityId,
        Entity Instance,
        string? RejectionCode)
    {
        public bool Accepted => RejectionCode is null;
    }

    public sealed class ActivitySignalIntakeResult
    {
        public ActivitySignalIntakeResult(
            bool isIdempotentDrop,
            IReadOnlyList<ActivitySignalMatchResult> matches)
        {
            IsIdempotentDrop = isIdempotentDrop;
            Matches = matches ?? throw new ArgumentNullException(nameof(matches));
        }

        public bool IsIdempotentDrop { get; }
        public IReadOnlyList<ActivitySignalMatchResult> Matches { get; }
        public bool MatchedAnyDefinition => Matches.Count > 0;
    }
}

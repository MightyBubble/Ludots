using System;
using System.Collections.Generic;

namespace Ludots.Core.Gameplay.Providers
{
    public sealed class ProviderGapEntry
    {
        public ProviderGapEntry(
            string key,
            ProviderKind kind,
            string expectedSemantics,
            string targetDomain,
            string blockedContentScope,
            string reviewStatus)
        {
            if (!ProviderKey.TryParse(key, out ProviderKey parsed, out string failureCode, out string reason))
            {
                throw new InvalidOperationException($"{failureCode}: {reason}");
            }

            if (string.IsNullOrWhiteSpace(expectedSemantics))
            {
                throw new ArgumentException("expectedSemantics is required.", nameof(expectedSemantics));
            }

            Key = parsed.Value;
            Kind = kind;
            ExpectedSemantics = expectedSemantics;
            TargetDomain = string.IsNullOrWhiteSpace(targetDomain) ? parsed.Domain : targetDomain;
            BlockedContentScope = blockedContentScope ?? string.Empty;
            ReviewStatus = string.IsNullOrWhiteSpace(reviewStatus)
                ? ProviderFailureCodes.NeedsProviderRegistration
                : reviewStatus;
        }

        public string Key { get; }
        public ProviderKind Kind { get; }
        public string ExpectedSemantics { get; }
        public string TargetDomain { get; }
        public string BlockedContentScope { get; }
        public string ReviewStatus { get; }
    }

    public sealed class ProviderGapCatalog
    {
        private readonly Dictionary<string, ProviderGapEntry> _entries = new(StringComparer.Ordinal);

        public IReadOnlyCollection<ProviderGapEntry> Entries => _entries.Values;

        public void RegisterGap(ProviderGapEntry entry)
        {
            ArgumentNullException.ThrowIfNull(entry);
            if (!_entries.TryAdd(entry.Key, entry))
            {
                throw new InvalidOperationException(
                    $"{ProviderFailureCodes.DuplicateProviderKey}: gap entry '{entry.Key}' already exists.");
            }
        }

        public bool Contains(string key) =>
            !string.IsNullOrWhiteSpace(key) && _entries.ContainsKey(key);

        public bool TryGet(string key, out ProviderGapEntry entry)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                entry = null!;
                return false;
            }

            return _entries.TryGetValue(key, out entry!);
        }

        public bool TryResolve(string key, out ProviderGapEntry resolved)
        {
            if (string.IsNullOrWhiteSpace(key) || !_entries.TryGetValue(key, out resolved!))
            {
                resolved = null!;
                return false;
            }

            _entries.Remove(key);
            return true;
        }

        public void RegisterDefaultY5kProgramGaps()
        {
            RegisterGap(new ProviderGapEntry(
                "population.appoint_governor",
                ProviderKind.Effect,
                "Appoint a settlement governor on the single local seat and activate population-hero relation chain.",
                "population",
                "governance / governor appoint paths",
                ProviderFailureCodes.NeedsProviderRegistration));

            RegisterGap(new ProviderGapEntry(
                "task.create",
                ProviderKind.Effect,
                "Create a TaskInstance from a TaskDefinition for cross-system interoperability.",
                "task",
                "activity options that spawn follow-up tasks",
                ProviderFailureCodes.NeedsProviderRegistration));

            RegisterGap(new ProviderGapEntry(
                "task.state_changed",
                ProviderKind.Source,
                "Emit task lifecycle state changes as Source signals.",
                "task",
                "activity subscriptions and UI projections",
                ProviderFailureCodes.NeedsProviderRegistration));

            RegisterGap(new ProviderGapEntry(
                "combat.siege_invest",
                ProviderKind.Effect,
                "Dedicated siege invest order (not combat.attack_target).",
                "combat",
                "siege command paths",
                ProviderFailureCodes.NeedsProviderRegistration));

            RegisterGap(new ProviderGapEntry(
                "combat.siege_lift",
                ProviderKind.Effect,
                "Dedicated siege lift order.",
                "combat",
                "siege command paths",
                ProviderFailureCodes.NeedsProviderRegistration));

            RegisterGap(new ProviderGapEntry(
                "combat.siege_accept_surrender",
                ProviderKind.Effect,
                "Dedicated accept-surrender order.",
                "combat",
                "siege command paths",
                ProviderFailureCodes.NeedsProviderRegistration));

            RegisterGap(new ProviderGapEntry(
                "city_control.commit_troops_takeover",
                ProviderKind.Effect,
                "Commit field troops to complete ownership transfer after breach.",
                "city_control",
                "takeover paths",
                ProviderFailureCodes.NeedsProviderRegistration));
        }
    }
}

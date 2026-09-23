using System;
using System.Collections.Generic;

namespace Ludots.Core.Gameplay.MapTriggers
{
    /// <summary>
    /// One mod-contributed mount resolved for a specific map, with the arbitration
    /// keys that decide its dispatch order: Priority, then the owning mod's
    /// dependency-order rank, then the owner's declaration order.
    /// </summary>
    public readonly struct ResolvedTriggerGraphMount
    {
        public ResolvedTriggerGraphMount(
            TriggerGraphMount mount,
            string? ownerModId,
            int modRank,
            int declarationIndex)
        {
            Mount = mount;
            OwnerModId = ownerModId;
            ModRank = modRank;
            DeclarationIndex = declarationIndex;
        }

        public TriggerGraphMount Mount { get; }
        public string? OwnerModId { get; }
        public int ModRank { get; }
        public int DeclarationIndex { get; }
    }

    /// <summary>
    /// Mod-contributed TriggerGraph mount table (GAS/map_trigger_mounts.json family).
    /// Owns the deterministic arbitration contract for the same event on the same map:
    /// Priority ascending, then mod dependency order (upstream first), then declaration
    /// order. Replace ("replaces" targeting a base mount key) is the only explicit graph
    /// override; any collision without it fails closed.
    /// </summary>
    public sealed class TriggerGraphMountTable
    {
        private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> EmptyClosure
            = new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal);

        private readonly Dictionary<string, int> _modRank = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _declarationCount = new(StringComparer.Ordinal);
        private readonly List<RegisteredMount> _mounts = new();
        private IReadOnlyDictionary<string, IReadOnlySet<string>> _dependencyClosure = EmptyClosure;

        public void SetModOrder(
            IReadOnlyList<string> modIds,
            IReadOnlyDictionary<string, IReadOnlySet<string>> dependencyClosure)
        {
            _modRank.Clear();
            if (modIds != null)
            {
                for (int i = 0; i < modIds.Count; i++)
                {
                    if (!string.IsNullOrWhiteSpace(modIds[i]))
                    {
                        _modRank[modIds[i]] = i + 1;
                    }
                }
            }

            _dependencyClosure = dependencyClosure ?? EmptyClosure;
        }

        public void Clear()
        {
            _modRank.Clear();
            _declarationCount.Clear();
            _mounts.Clear();
            _dependencyClosure = EmptyClosure;
        }

        /// <summary>Registers one mod's family mounts in file order; declaration order is per-mod sequential.</summary>
        public void AddMounts(string modId, IReadOnlyList<TriggerGraphMount> mounts)
        {
            if (string.IsNullOrWhiteSpace(modId))
            {
                throw new ArgumentException("Mount owner mod id is required.", nameof(modId));
            }

            if (mounts == null || mounts.Count == 0)
            {
                return;
            }

            int rank = _modRank.TryGetValue(modId, out int known) ? known : 0;
            int next = _declarationCount.TryGetValue(modId, out int count) ? count : 0;
            for (int i = 0; i < mounts.Count; i++)
            {
                _mounts.Add(new RegisteredMount(modId, rank, next + i, mounts[i]));
            }

            _declarationCount[modId] = next + mounts.Count;
        }

        /// <summary>
        /// Resolves the mount list for one map: the map's own mounts (rank 0, declaration
        /// order = authoring order) followed by applicable mod mounts, with Replace applied
        /// and the deterministic arbitration sort applied. Fails closed on: a replaces
        /// target that does not exist on this map, a diamond (two mods replacing the same
        /// base with no dependency order), a duplicate mount key, and the same graph
        /// mounted twice without an explicit replaces relationship.
        /// </summary>
        public IReadOnlyList<ResolvedTriggerGraphMount> ResolveForMap(
            string mapId,
            IReadOnlyList<TriggerGraphMount> mapOwnedMounts)
        {
            var candidates = new List<ResolvedTriggerGraphMount>();
            var keyByCandidate = new List<string?>((mapOwnedMounts?.Count ?? 0) + _mounts.Count);
            AddMapOwnedCandidates(candidates, keyByCandidate, mapId, mapOwnedMounts);
            AddApplicableModCandidates(candidates, keyByCandidate, mapId);

            ApplyReplaces(candidates, keyByCandidate, mapId);
            RejectDuplicateKeys(candidates, keyByCandidate, mapId);
            RejectDuplicateGraphMounts(candidates, mapId);

            var sorted = new List<ResolvedTriggerGraphMount>(candidates.Count);
            for (int i = 0; i < candidates.Count; i++)
            {
                sorted.Add(candidates[i]);
            }

            sorted.Sort(CompareArbitration);
            return sorted;
        }

        private void AddMapOwnedCandidates(
            List<ResolvedTriggerGraphMount> candidates,
            List<string?> keyByCandidate,
            string mapId,
            IReadOnlyList<TriggerGraphMount> mapOwnedMounts)
        {
            if (mapOwnedMounts == null)
            {
                return;
            }

            for (int i = 0; i < mapOwnedMounts.Count; i++)
            {
                TriggerGraphMount mount = mapOwnedMounts[i];
                if (!mount.Enabled || mount.Domain == TriggerGraphMountDomain.Entity)
                {
                    keyByCandidate.Add(null);
                    continue;
                }

                candidates.Add(new ResolvedTriggerGraphMount(mount, null, 0, i));
                keyByCandidate.Add(mount.Id.Length == 0 ? null : $"{mapId}.{mount.Id}");
            }
        }

        private void AddApplicableModCandidates(
            List<ResolvedTriggerGraphMount> candidates,
            List<string?> keyByCandidate,
            string mapId)
        {
            for (int i = 0; i < _mounts.Count; i++)
            {
                RegisteredMount registered = _mounts[i];
                TriggerGraphMount mount = registered.Mount;
                if (!mount.Enabled)
                {
                    continue;
                }

                if (mount.Domain == TriggerGraphMountDomain.Map)
                {
                    if (!string.Equals(mount.MapFilter, mapId, StringComparison.Ordinal))
                    {
                        continue;
                    }
                }
                else if (mount.Domain == TriggerGraphMountDomain.Mod)
                {
                    if (mount.MapFilter.Length > 0 && !string.Equals(mount.MapFilter, mapId, StringComparison.Ordinal))
                    {
                        continue;
                    }
                }
                else
                {
                    continue;
                }

                candidates.Add(new ResolvedTriggerGraphMount(
                    mount,
                    registered.ModId,
                    registered.Rank,
                    registered.DeclarationIndex));
                keyByCandidate.Add($"{registered.ModId}.{mount.Id}");
            }
        }

        private void ApplyReplaces(
            List<ResolvedTriggerGraphMount> candidates,
            List<string?> keyByCandidate,
            string mapId)
        {
            var replacersByTarget = new List<(string Target, List<int> Replacers)>();
            var targetIndex = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < candidates.Count; i++)
            {
                ResolvedTriggerGraphMount candidate = candidates[i];
                if (candidate.Mount.Replaces.Length == 0)
                {
                    continue;
                }

                if (!targetIndex.TryGetValue(candidate.Mount.Replaces, out int groupIndex))
                {
                    groupIndex = replacersByTarget.Count;
                    targetIndex[candidate.Mount.Replaces] = groupIndex;
                    replacersByTarget.Add((candidate.Mount.Replaces, new List<int>()));
                }

                replacersByTarget[groupIndex].Replacers.Add(i);
            }

            var dropped = new bool[candidates.Count];
            for (int g = 0; g < replacersByTarget.Count; g++)
            {
                string target = replacersByTarget[g].Target;
                List<int> replacers = replacersByTarget[g].Replacers;
                if (replacers.Count == 1)
                {
                    ApplySingleReplace(candidates, keyByCandidate, mapId, replacers[0], target, dropped);
                    continue;
                }

                int winner = ArbitrateReplacers(candidates, replacers, target, mapId, dropped);
                ApplySingleReplace(candidates, keyByCandidate, mapId, winner, target, dropped);
                for (int r = 0; r < replacers.Count; r++)
                {
                    if (replacers[r] != winner)
                    {
                        dropped[replacers[r]] = true;
                    }
                }
            }

            if (dropped.Length == 0)
            {
                return;
            }

            for (int i = candidates.Count - 1; i >= 0; i--)
            {
                if (dropped[i])
                {
                    candidates.RemoveAt(i);
                    keyByCandidate.RemoveAt(i);
                }
            }
        }

        private void ApplySingleReplace(
            List<ResolvedTriggerGraphMount> candidates,
            List<string?> keyByCandidate,
            string mapId,
            int replacerIndex,
            string target,
            bool[] dropped)
        {
            ResolvedTriggerGraphMount replacer = candidates[replacerIndex];
            if (string.Equals(keyByCandidate[replacerIndex], target, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Map '{mapId}' mount '{Describe(candidates[replacerIndex])}' cannot replace itself.");
            }

            int baseIndex = FindCandidateByKey(candidates, keyByCandidate, target);
            if (baseIndex < 0 || dropped[baseIndex] || baseIndex == replacerIndex)
            {
                throw new InvalidOperationException(
                    $"Map '{mapId}' mount '{Describe(replacer)}' declares replaces '{target}' but no such mount is mounted on this map.");
            }

            dropped[baseIndex] = true;
        }

        private int ArbitrateReplacers(
            List<ResolvedTriggerGraphMount> candidates,
            List<int> replacers,
            string target,
            string mapId,
            bool[] dropped)
        {
            int winner = replacers[0];
            for (int i = 1; i < replacers.Count; i++)
            {
                int challenger = replacers[i];
                if (IsDownstream(candidates[challenger], candidates[winner]))
                {
                    winner = challenger;
                }
                else if (!IsDownstream(candidates[winner], candidates[challenger]))
                {
                    throw new InvalidOperationException(
                        $"Map '{mapId}' mounts '{Describe(candidates[winner])}' and '{Describe(candidates[challenger])}' both replace "
                        + $"'{target}' with no dependency order between their mods; the override is ambiguous and fails closed.");
                }
            }

            return winner;
        }

        private bool IsDownstream(ResolvedTriggerGraphMount candidate, ResolvedTriggerGraphMount incumbent)
        {
            string? candidateMod = candidate.OwnerModId;
            string? incumbentMod = incumbent.OwnerModId;
            if (candidateMod == null || incumbentMod == null)
            {
                return false;
            }

            if (_dependencyClosure.TryGetValue(candidateMod, out IReadOnlySet<string>? closure))
            {
                return closure.Contains(incumbentMod);
            }

            return false;
        }

        private static int FindCandidateByKey(
            List<ResolvedTriggerGraphMount> candidates,
            List<string?> keyByCandidate,
            string target)
        {
            for (int i = 0; i < keyByCandidate.Count; i++)
            {
                if (string.Equals(keyByCandidate[i], target, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }

        private static void RejectDuplicateKeys(
            List<ResolvedTriggerGraphMount> candidates,
            List<string?> keyByCandidate,
            string mapId)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < candidates.Count; i++)
            {
                string? key = keyByCandidate[i];
                if (key == null)
                {
                    continue;
                }

                if (!seen.Add(key))
                {
                    throw new InvalidOperationException(
                        $"Map '{mapId}' has duplicate mount key '{key}'; a mount key must be unique on a map.");
                }
            }
        }

        private static void RejectDuplicateGraphMounts(
            List<ResolvedTriggerGraphMount> candidates,
            string mapId)
        {
            var graphs = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < candidates.Count; i++)
            {
                string graph = candidates[i].Mount.Graph;
                if (!graphs.Add(graph))
                {
                    throw new InvalidOperationException(
                        $"Map '{mapId}' mounts graph '{graph}' more than once without an explicit replaces relationship; "
                        + "declare 'replaces' to override a mounted graph.");
                }
            }
        }

        private static int CompareArbitration(ResolvedTriggerGraphMount a, ResolvedTriggerGraphMount b)
        {
            int byPriority = a.Mount.Priority.CompareTo(b.Mount.Priority);
            if (byPriority != 0)
            {
                return byPriority;
            }

            int byRank = a.ModRank.CompareTo(b.ModRank);
            if (byRank != 0)
            {
                return byRank;
            }

            return a.DeclarationIndex.CompareTo(b.DeclarationIndex);
        }

        private static string Describe(ResolvedTriggerGraphMount candidate)
        {
            string owner = candidate.OwnerModId ?? "map";
            return $"{owner}.{candidate.Mount.Id} (graph '{candidate.Mount.Graph}')";
        }

        private readonly struct RegisteredMount
        {
            public RegisteredMount(string modId, int rank, int declarationIndex, TriggerGraphMount mount)
            {
                ModId = modId;
                Rank = rank;
                DeclarationIndex = declarationIndex;
                Mount = mount;
            }

            public string ModId { get; }
            public int Rank { get; }
            public int DeclarationIndex { get; }
            public TriggerGraphMount Mount { get; }
        }
    }
}

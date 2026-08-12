using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Registry;

namespace Ludots.Core.Input.Interaction
{
    /// <summary>
    /// FilterProfile registry and evaluator (RFC-0065 CTX-4, DEC-8). Profiles are declared in
    /// <c>Input/filter_profiles.json</c> and compiled at install time: expand kinds resolve against a
    /// registered expander table (association providers are injected, e.g. by the control plane),
    /// tag names resolve to ids, and unknown expand kinds / anchor kinds / frozen-registry-unknown tags
    /// throw. Evaluation is steady-state allocation free: the association expansion is cached in a
    /// pooled set and invalidated by the expander's revision source.
    /// </summary>
    public sealed class FilterProfileRegistry
    {
        private readonly StringIntRegistry _profileIds;
        private readonly World _world;
        private readonly TagOps _tagOps;
        private readonly Dictionary<string, int> _expanderIndexByKind = new(StringComparer.Ordinal);
        private readonly List<ExpanderEntry> _expanders = new();
        private readonly HashSet<string> _anchorKinds = new(StringComparer.Ordinal);

        private CompiledProfile[] _profiles = new CompiledProfile[8];

        public FilterProfileRegistry(StringIntRegistry profileIdRegistry, World world, TagOps tagOps)
        {
            _profileIds = profileIdRegistry ?? throw new ArgumentNullException(nameof(profileIdRegistry));
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _tagOps = tagOps ?? throw new ArgumentNullException(nameof(tagOps));
            _anchorKinds.Add(FilterAnchorKinds.LocalPlayerRep);
        }

        /// <summary>Profile id space shared with <see cref="InteractionContextStack.FilterProfileIdRegistry"/>.</summary>
        public StringIntRegistry ProfileIdRegistry => _profileIds;

        /// <summary>
        /// Register an association expander kind (DEC-8: providers are injected, not owned).
        /// <see cref="FilterAssociationExpandKinds.None"/> is built in and cannot be re-registered.
        /// </summary>
        public void RegisterExpander(string kind, FilterAssociationExpander expander, Func<uint> revisionSource)
        {
            if (string.IsNullOrWhiteSpace(kind))
            {
                throw new ArgumentException("Expander kind is required.", nameof(kind));
            }

            kind = kind.Trim();
            if (string.Equals(kind, FilterAssociationExpandKinds.None, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Expand kind '{FilterAssociationExpandKinds.None}' is built in and cannot be registered.");
            }

            if (_expanderIndexByKind.ContainsKey(kind))
            {
                throw new InvalidOperationException($"Expand kind '{kind}' is already registered.");
            }

            _expanders.Add(new ExpanderEntry(
                expander ?? throw new ArgumentNullException(nameof(expander)),
                revisionSource ?? throw new ArgumentNullException(nameof(revisionSource))));
            _expanderIndexByKind.Add(kind, _expanders.Count - 1);
        }

        /// <summary>Register an additional anchor kind accepted by profile declarations.</summary>
        public void RegisterAnchorKind(string kind)
        {
            if (string.IsNullOrWhiteSpace(kind))
            {
                throw new ArgumentException("Anchor kind is required.", nameof(kind));
            }

            _anchorKinds.Add(kind.Trim());
        }

        /// <summary>
        /// Compile and install every profile in the config. Fails fast on unknown anchor kinds,
        /// unknown expand kinds, unresolvable tag names, and duplicate installs.
        /// </summary>
        public void Install(FilterProfilesConfig config)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            FilterProfileConfigLoader.Validate(config, nameof(FilterProfilesConfig));
            for (int i = 0; i < config.Profiles.Count; i++)
            {
                InstallProfile(config.Profiles[i]);
            }
        }

        /// <summary>True when the profile id has been compiled and can be evaluated.</summary>
        public bool IsInstalled(int profileId)
        {
            return profileId > 0 && profileId < _profiles.Length && _profiles[profileId] != null;
        }

        /// <summary>
        /// Evaluate a profile against raw cast hits: intersect with the cached association expansion of
        /// <paramref name="anchorRep"/> (skipped for expand kind <see cref="FilterAssociationExpandKinds.None"/>),
        /// then apply the any-of exclude/include tag rules. Returns the number of survivors written to
        /// <paramref name="filtered"/>, preserving raw order. Steady-state allocation free.
        /// </summary>
        public int Evaluate(int profileId, Entity anchorRep, ReadOnlySpan<Entity> raw, Span<Entity> filtered)
        {
            if (!IsInstalled(profileId))
            {
                throw new InvalidOperationException(
                    $"Filter profile id {profileId} ('{_profileIds.GetName(profileId)}') is not installed.");
            }

            if (filtered.Length < raw.Length)
            {
                throw new ArgumentException("Filtered buffer must cover every raw entity.", nameof(filtered));
            }

            CompiledProfile profile = _profiles[profileId];
            bool useAssociation = profile.ExpanderIndex >= 0;
            if (useAssociation)
            {
                if (anchorRep == Entity.Null)
                {
                    throw new ArgumentException("Anchor rep is required for association-filtering profiles.", nameof(anchorRep));
                }

                EnsureExpansion(profile, anchorRep);
            }

            int written = 0;
            for (int i = 0; i < raw.Length; i++)
            {
                Entity candidate = raw[i];
                if (candidate == Entity.Null)
                {
                    continue;
                }

                if (useAssociation && !profile.SetContains(candidate))
                {
                    continue;
                }

                if (profile.HasExclude || profile.HasInclude)
                {
                    bool hasTags = _world.IsAlive(candidate) && _world.Has<GameplayTagContainer>(candidate);
                    if (profile.HasExclude && hasTags)
                    {
                        ref GameplayTagContainer tags = ref _world.Get<GameplayTagContainer>(candidate);
                        if (_tagOps.Intersects(ref tags, in profile.ExcludeMask, TagSense.Effective))
                        {
                            continue;
                        }
                    }

                    if (profile.HasInclude)
                    {
                        if (!hasTags)
                        {
                            continue;
                        }

                        ref GameplayTagContainer tags = ref _world.Get<GameplayTagContainer>(candidate);
                        if (!_tagOps.Intersects(ref tags, in profile.IncludeMask, TagSense.Effective))
                        {
                            continue;
                        }
                    }
                }

                filtered[written++] = candidate;
            }

            return written;
        }

        private void InstallProfile(FilterProfileDefinition definition)
        {
            if (!_anchorKinds.Contains(definition.AssociationQuery.Anchor))
            {
                throw new InvalidOperationException(
                    $"Filter profile '{definition.Id}' declares unknown anchor kind '{definition.AssociationQuery.Anchor}'.");
            }

            string expandKind = definition.AssociationQuery.Expand;
            int expanderIndex;
            if (string.Equals(expandKind, FilterAssociationExpandKinds.None, StringComparison.Ordinal))
            {
                expanderIndex = -1;
            }
            else if (!_expanderIndexByKind.TryGetValue(expandKind, out expanderIndex))
            {
                throw new InvalidOperationException(
                    $"Filter profile '{definition.Id}' declares unknown expand kind '{expandKind}'.");
            }

            int profileId = _profileIds.Register(definition.Id);
            if (profileId < _profiles.Length && _profiles[profileId] != null)
            {
                throw new InvalidOperationException($"Filter profile '{definition.Id}' is already installed.");
            }

            var profile = new CompiledProfile { ExpanderIndex = expanderIndex };
            profile.HasExclude = TryBuildMask(definition.Id, definition.Exclude, ref profile.ExcludeMask);
            profile.HasInclude = TryBuildMask(definition.Id, definition.Include, ref profile.IncludeMask);

            if (profileId >= _profiles.Length)
            {
                int next = _profiles.Length;
                while (next <= profileId)
                {
                    next *= 2;
                }

                Array.Resize(ref _profiles, next);
            }

            _profiles[profileId] = profile;
        }

        private static bool TryBuildMask(string profileId, FilterProfileTagRule rule, ref GameplayTagBitSet mask)
        {
            if (rule?.AnyTags == null || rule.AnyTags.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < rule.AnyTags.Count; i++)
            {
                mask.AddTag(ResolveTagId(profileId, rule.AnyTags[i]));
            }

            return true;
        }

        private static int ResolveTagId(string profileId, string tagName)
        {
            int id = TagRegistry.GetId(tagName);
            if (id != TagRegistry.InvalidId)
            {
                return id;
            }

            if (TagRegistry.IsFrozen)
            {
                throw new InvalidOperationException(
                    $"Filter profile '{profileId}' references unknown tag '{tagName}' (tag registry is frozen).");
            }

            // Load-time declaration into the shared tag id space (same precedent as
            // RelationshipCatalogRuntime / PerformerDefinitionConfigLoader).
            return TagRegistry.Register(tagName);
        }

        private void EnsureExpansion(CompiledProfile profile, Entity anchorRep)
        {
            ExpanderEntry entry = _expanders[profile.ExpanderIndex];
            uint revision = entry.RevisionSource();
            if (profile.CacheValid && profile.CacheAnchor == anchorRep && profile.CacheRevision == revision)
            {
                return;
            }

            int count;
            while (true)
            {
                count = entry.Expander(anchorRep, profile.ExpandScratch);
                if (count < profile.ExpandScratch.Length)
                {
                    break;
                }

                profile.ExpandScratch = new Entity[profile.ExpandScratch.Length * 2];
            }

            profile.RebuildSet(profile.ExpandScratch.AsSpan(0, count));
            profile.CacheAnchor = anchorRep;
            profile.CacheRevision = revision;
            profile.CacheValid = true;
        }

        private readonly struct ExpanderEntry
        {
            public ExpanderEntry(FilterAssociationExpander expander, Func<uint> revisionSource)
            {
                Expander = expander;
                RevisionSource = revisionSource;
            }

            public readonly FilterAssociationExpander Expander;
            public readonly Func<uint> RevisionSource;
        }

        /// <summary>
        /// Compiled profile with a pooled open-addressed membership set; arrays grow on demand
        /// and are reused across re-expansions (steady-state zero allocation).
        /// </summary>
        private sealed class CompiledProfile
        {
            public int ExpanderIndex = -1;
            public GameplayTagBitSet ExcludeMask;
            public GameplayTagBitSet IncludeMask;
            public bool HasExclude;
            public bool HasInclude;

            public Entity CacheAnchor;
            public uint CacheRevision;
            public bool CacheValid;
            public Entity[] ExpandScratch = new Entity[64];

            private Entity[] _slots = new Entity[128];
            private bool[] _used = new bool[128];

            public void RebuildSet(ReadOnlySpan<Entity> members)
            {
                int required = Math.Max(4, members.Length * 2);
                if (_slots.Length < required)
                {
                    int next = _slots.Length;
                    while (next < required)
                    {
                        next *= 2;
                    }

                    _slots = new Entity[next];
                    _used = new bool[next];
                }
                else
                {
                    Array.Clear(_used, 0, _used.Length);
                }

                int mask = _slots.Length - 1;
                for (int i = 0; i < members.Length; i++)
                {
                    Entity member = members[i];
                    int slot = member.Id & mask;
                    while (_used[slot])
                    {
                        if (_slots[slot] == member)
                        {
                            break;
                        }

                        slot = (slot + 1) & mask;
                    }

                    _slots[slot] = member;
                    _used[slot] = true;
                }
            }

            public bool SetContains(Entity candidate)
            {
                int mask = _slots.Length - 1;
                int slot = candidate.Id & mask;
                while (_used[slot])
                {
                    if (_slots[slot] == candidate)
                    {
                        return true;
                    }

                    slot = (slot + 1) & mask;
                }

                return false;
            }
        }
    }
}

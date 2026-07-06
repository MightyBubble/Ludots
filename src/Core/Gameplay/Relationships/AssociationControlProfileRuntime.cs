using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Relationships.Config;

namespace Ludots.Core.Gameplay.Relationships
{
    /// <summary>
    /// Generic "predicate → Controls-edge grant/revoke" rule engine (RFC-0065 §5.4 / CTRL-4b, DEC-4).
    /// Profiles are pure data: <c>when</c> (tag / relationship predicate combinators) grants an edge between
    /// two roles, <c>revokeWhen</c> removes an edge this engine granted. All strings (tag names, edge names)
    /// are resolved to ids at load time; steady-state evaluation performs zero string comparisons and zero
    /// allocations. Candidates are the domain reps carrying <see cref="PlayerIdentity"/>, evaluated pairwise
    /// (an O(players²) set). Edges granted by profiles carry the <c>Granted</c> relationship flag so revoke
    /// never touches manually created edges. Re-evaluation is gated by the relationship reverse-index revision
    /// and a tag-bit snapshot of the candidate reps projected onto the tags the profile predicates actually
    /// reference (plus their rule disable masks, since predicates read the effective sense): an unchanged tick
    /// or a change to an unreferenced tag does no predicate work.
    /// </summary>
    public sealed class AssociationControlProfileRuntime
    {
        /// <summary>Relationship flag marking edges granted by this engine (vs. manually created edges).</summary>
        public const string GrantedFlagName = "Granted";

        private static readonly QueryDescription PlayerRepQuery = new QueryDescription().WithAll<PlayerIdentity>();
        private static readonly Comparison<Entity> EntityIdOrder = static (a, b) => a.Id.CompareTo(b.Id);
        private const int TagWordCount = 4;

        private readonly World _world;
        private readonly RelationshipRuntime _relationships;
        private readonly TagOps _tagOps;
        private readonly int _grantedFlagId;
        private readonly CompiledProfile[] _profiles;
        private readonly int[] _predicateTagIds;

        private readonly List<Entity> _candidates = new(8);
        private readonly ulong[] _relevantTagMask = new ulong[TagWordCount];
        private ulong[] _tagSnapshot = new ulong[8 * TagWordCount];
        private uint _lastTopologyRevision;
        private bool _hasEvaluated;

        private AssociationControlProfileRuntime(
            World world,
            RelationshipRuntime relationships,
            TagOps tagOps,
            int grantedFlagId,
            CompiledProfile[] profiles,
            int[] predicateTagIds)
        {
            _world = world;
            _relationships = relationships;
            _tagOps = tagOps;
            _grantedFlagId = grantedFlagId;
            _profiles = profiles;
            _predicateTagIds = predicateTagIds;
        }

        /// <summary>Number of registered profiles; zero profiles means the runtime is a no-op.</summary>
        public int ProfileCount => _profiles.Length;

        /// <summary>Completed full evaluation passes; unchanged ticks must not advance this (revision gate).</summary>
        public int EvaluationPassCount { get; private set; }

        /// <summary>Compiles the profile catalog: every tag / edge / role string becomes an id here.</summary>
        public static AssociationControlProfileRuntime Create(
            World world,
            RelationshipRuntime relationships,
            TagOps tagOps,
            RelationshipTypeRegistry relationshipTypes,
            AssociationControlProfileCatalogConfig catalog,
            int grantedFlagId)
        {
            ArgumentNullException.ThrowIfNull(world);
            ArgumentNullException.ThrowIfNull(relationships);
            ArgumentNullException.ThrowIfNull(tagOps);
            ArgumentNullException.ThrowIfNull(relationshipTypes);
            ArgumentNullException.ThrowIfNull(catalog);
            if (grantedFlagId < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(grantedFlagId));
            }

            var compiled = new CompiledProfile[catalog.Profiles.Count];
            for (int i = 0; i < catalog.Profiles.Count; i++)
            {
                compiled[i] = CompileProfile(catalog.Profiles[i], relationshipTypes);
            }

            return new AssociationControlProfileRuntime(
                world,
                relationships,
                tagOps,
                grantedFlagId,
                compiled,
                CollectPredicateTagIds(compiled));
        }

        private static int[] CollectPredicateTagIds(CompiledProfile[] profiles)
        {
            var tagIds = new HashSet<int>();
            for (int p = 0; p < profiles.Length; p++)
            {
                ConditionNode[] nodes = profiles[p].Nodes;
                for (int n = 0; n < nodes.Length; n++)
                {
                    if (nodes[n].Kind != ConditionKind.Tag)
                    {
                        continue;
                    }

                    if ((uint)nodes[n].Id > GameplayTagContainer.MAX_TAG_ID)
                    {
                        throw new InvalidOperationException(
                            $"Control profile '{profiles[p].Id}' references tag id {nodes[n].Id} outside the tag container range.");
                    }

                    tagIds.Add(nodes[n].Id);
                }
            }

            var result = new int[tagIds.Count];
            tagIds.CopyTo(result);
            Array.Sort(result);
            return result;
        }

        /// <summary>
        /// Re-evaluates all profiles over all candidate pairs when — and only when — the relationship topology
        /// revision or a candidate rep's tag bits changed since the last pass.
        /// </summary>
        public void Update()
        {
            if (_profiles.Length == 0)
            {
                return;
            }

            uint revision = _relationships.ReverseIndex.Revision;
            bool topologyChanged = !_hasEvaluated || revision != _lastTopologyRevision;
            if (topologyChanged)
            {
                RebuildCandidates();
            }

            RefreshRelevantTagMask();
            bool tagsChanged = RefreshTagSnapshot();
            if (_hasEvaluated && !topologyChanged && !tagsChanged)
            {
                return;
            }

            EvaluatePairs();
            _hasEvaluated = true;
            // Read the post-mutation revision so self-inflicted edge changes do not retrigger a pass.
            _lastTopologyRevision = _relationships.ReverseIndex.Revision;
            EvaluationPassCount++;
        }

        private void RebuildCandidates()
        {
            _candidates.Clear();
            var job = new CollectRepsJob { Candidates = _candidates };
            _world.InlineEntityQuery<CollectRepsJob, PlayerIdentity>(in PlayerRepQuery, ref job);
            _candidates.Sort(EntityIdOrder);

            int required = _candidates.Count * TagWordCount;
            if (_tagSnapshot.Length < required)
            {
                Array.Resize(ref _tagSnapshot, Math.Max(required, _tagSnapshot.Length * 2));
            }

            // Invalidate the snapshot so the refresh below re-seeds it for the new candidate layout.
            Array.Clear(_tagSnapshot, 0, required);
        }

        /// <summary>
        /// Rebuilds the bitset of tag ids the snapshot cares about: every tag id a profile predicate
        /// references, widened by that tag's rule disable mask (the effective sense reads those bits).
        /// Recomputed per tick because tag rules may be registered after this runtime is constructed.
        /// </summary>
        private void RefreshRelevantTagMask()
        {
            Span<ulong> mask = stackalloc ulong[TagWordCount];
            for (int i = 0; i < _predicateTagIds.Length; i++)
            {
                int tagId = _predicateTagIds[i];
                mask[tagId >> 6] |= 1UL << (tagId & 63);
                if (_tagOps.Rules.HasRule(tagId))
                {
                    ref readonly TagRuleCompiled rule = ref _tagOps.Rules.Get(tagId);
                    if (rule.DisabledIfAny != 0)
                    {
                        OrBits(in rule.DisabledIfMask, mask);
                    }
                }
            }

            for (int w = 0; w < TagWordCount; w++)
            {
                _relevantTagMask[w] = mask[w];
            }
        }

        private static unsafe void OrBits(in GameplayTagContainer container, Span<ulong> mask)
        {
            fixed (ulong* bits = container.Bits)
            {
                for (int w = 0; w < TagWordCount; w++)
                {
                    mask[w] |= bits[w];
                }
            }
        }

        private unsafe bool RefreshTagSnapshot()
        {
            bool changed = false;
            for (int i = 0; i < _candidates.Count; i++)
            {
                Entity candidate = _candidates[i];
                int baseIndex = i * TagWordCount;
                if (!_world.IsAlive(candidate) || !_world.Has<GameplayTagContainer>(candidate))
                {
                    for (int w = 0; w < TagWordCount; w++)
                    {
                        changed |= _tagSnapshot[baseIndex + w] != 0;
                        _tagSnapshot[baseIndex + w] = 0;
                    }

                    continue;
                }

                ref GameplayTagContainer tags = ref _world.Get<GameplayTagContainer>(candidate);
                fixed (ulong* bits = tags.Bits)
                {
                    for (int w = 0; w < TagWordCount; w++)
                    {
                        ulong masked = bits[w] & _relevantTagMask[w];
                        changed |= _tagSnapshot[baseIndex + w] != masked;
                        _tagSnapshot[baseIndex + w] = masked;
                    }
                }
            }

            return changed;
        }

        private void EvaluatePairs()
        {
            for (int from = 0; from < _candidates.Count; from++)
            {
                Entity fromRep = _candidates[from];
                if (!_world.IsAlive(fromRep))
                {
                    continue;
                }

                for (int to = 0; to < _candidates.Count; to++)
                {
                    if (to == from)
                    {
                        continue;
                    }

                    Entity toRep = _candidates[to];
                    if (!_world.IsAlive(toRep))
                    {
                        continue;
                    }

                    for (int p = 0; p < _profiles.Length; p++)
                    {
                        EvaluateProfile(_profiles[p], fromRep, toRep);
                    }
                }
            }
        }

        private void EvaluateProfile(CompiledProfile profile, Entity fromRep, Entity toRep)
        {
            bool exists = _relationships.HasLink(fromRep, toRep, profile.EdgeTypeId);
            if (Evaluate(profile, profile.WhenRoot, fromRep, toRep))
            {
                if (!exists)
                {
                    _relationships.EnsureLink(fromRep, toRep, profile.EdgeTypeId);
                    _relationships.SetFlag(fromRep, toRep, profile.EdgeTypeId, _grantedFlagId, enabled: true);
                }

                // An existing edge without the Granted flag is a manual edge; the profile never claims it.
                return;
            }

            if (profile.RevokeRoot >= 0 &&
                exists &&
                _relationships.HasFlag(fromRep, toRep, profile.EdgeTypeId, _grantedFlagId) &&
                Evaluate(profile, profile.RevokeRoot, fromRep, toRep))
            {
                _relationships.RemoveLink(fromRep, toRep, profile.EdgeTypeId);
            }
        }

        private bool Evaluate(CompiledProfile profile, int nodeIndex, Entity fromRep, Entity toRep)
        {
            ref readonly ConditionNode node = ref profile.Nodes[nodeIndex];
            switch (node.Kind)
            {
                case ConditionKind.All:
                    for (int i = 0; i < node.ChildCount; i++)
                    {
                        if (!Evaluate(profile, profile.Children[node.ChildStart + i], fromRep, toRep))
                        {
                            return false;
                        }
                    }

                    return true;
                case ConditionKind.Any:
                    for (int i = 0; i < node.ChildCount; i++)
                    {
                        if (Evaluate(profile, profile.Children[node.ChildStart + i], fromRep, toRep))
                        {
                            return true;
                        }
                    }

                    return false;
                case ConditionKind.Not:
                    return !Evaluate(profile, profile.Children[node.ChildStart], fromRep, toRep);
                case ConditionKind.Relationship:
                {
                    Entity a = node.RoleA == 0 ? fromRep : toRep;
                    Entity b = node.RoleB == 0 ? fromRep : toRep;
                    if (_relationships.HasLink(a, b, node.Id))
                    {
                        return true;
                    }

                    return node.SymmetricRelationship && _relationships.HasLink(b, a, node.Id);
                }

                case ConditionKind.Tag:
                {
                    Entity target = node.RoleA == 0 ? fromRep : toRep;
                    if (!_world.Has<GameplayTagContainer>(target))
                    {
                        return false;
                    }

                    ref GameplayTagContainer tags = ref _world.Get<GameplayTagContainer>(target);
                    return _tagOps.HasTag(ref tags, node.Id, TagSense.Effective);
                }

                default:
                    throw new InvalidOperationException($"Unsupported condition kind '{node.Kind}'.");
            }
        }

        private static CompiledProfile CompileProfile(
            AssociationControlProfileConfig config,
            RelationshipTypeRegistry relationshipTypes)
        {
            if (config == null || string.IsNullOrWhiteSpace(config.Id))
            {
                throw new InvalidOperationException("Association control profile requires a non-empty id.");
            }

            AssociationControlGrantConfig grant = config.Grant
                ?? throw new InvalidOperationException($"Control profile '{config.Id}' requires a grant declaration.");
            if (string.IsNullOrWhiteSpace(grant.EdgeType))
            {
                throw new InvalidOperationException($"Control profile '{config.Id}' grant requires a non-empty edgeType.");
            }

            if (string.IsNullOrWhiteSpace(grant.From) || string.IsNullOrWhiteSpace(grant.To))
            {
                throw new InvalidOperationException($"Control profile '{config.Id}' grant requires both from and to roles.");
            }

            if (string.Equals(grant.From, grant.To, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Control profile '{config.Id}' grant roles must be distinct.");
            }

            if (config.When == null)
            {
                throw new InvalidOperationException($"Control profile '{config.Id}' requires a when predicate.");
            }

            var builder = new ProfileBuilder(config.Id, grant.From, grant.To, relationshipTypes);
            int whenRoot = builder.Compile(config.When);
            int revokeRoot = config.RevokeWhen != null ? builder.Compile(config.RevokeWhen) : -1;
            return new CompiledProfile(
                config.Id,
                relationshipTypes.GetId(grant.EdgeType),
                builder.Nodes.ToArray(),
                builder.Children.ToArray(),
                whenRoot,
                revokeRoot);
        }

        private struct CollectRepsJob : Arch.Core.IForEachWithEntity<PlayerIdentity>
        {
            public List<Entity> Candidates;

            public void Update(Entity entity, ref PlayerIdentity identity)
            {
                Candidates.Add(entity);
            }
        }

        private enum ConditionKind : byte
        {
            All,
            Any,
            Not,
            Relationship,
            Tag,
        }

        private readonly struct ConditionNode
        {
            public ConditionNode(ConditionKind kind, int childStart, int childCount, int id, byte roleA, byte roleB, bool symmetricRelationship)
            {
                Kind = kind;
                ChildStart = childStart;
                ChildCount = childCount;
                Id = id;
                RoleA = roleA;
                RoleB = roleB;
                SymmetricRelationship = symmetricRelationship;
            }

            public ConditionKind Kind { get; }
            public int ChildStart { get; }
            public int ChildCount { get; }

            /// <summary>Relationship type id or tag id depending on <see cref="Kind"/>.</summary>
            public int Id { get; }
            public byte RoleA { get; }
            public byte RoleB { get; }
            public bool SymmetricRelationship { get; }
        }

        private sealed class CompiledProfile
        {
            public CompiledProfile(string id, int edgeTypeId, ConditionNode[] nodes, int[] children, int whenRoot, int revokeRoot)
            {
                Id = id;
                EdgeTypeId = edgeTypeId;
                Nodes = nodes;
                Children = children;
                WhenRoot = whenRoot;
                RevokeRoot = revokeRoot;
            }

            public string Id { get; }
            public int EdgeTypeId { get; }
            public ConditionNode[] Nodes { get; }
            public int[] Children { get; }
            public int WhenRoot { get; }
            public int RevokeRoot { get; }
        }

        private sealed class ProfileBuilder
        {
            private readonly string _profileId;
            private readonly string _fromRole;
            private readonly string _toRole;
            private readonly RelationshipTypeRegistry _relationshipTypes;

            public ProfileBuilder(string profileId, string fromRole, string toRole, RelationshipTypeRegistry relationshipTypes)
            {
                _profileId = profileId;
                _fromRole = fromRole;
                _toRole = toRole;
                _relationshipTypes = relationshipTypes;
            }

            public List<ConditionNode> Nodes { get; } = new();
            public List<int> Children { get; } = new();

            public int Compile(AssociationControlConditionConfig condition)
            {
                int declared =
                    (condition.All != null ? 1 : 0) +
                    (condition.Any != null ? 1 : 0) +
                    (condition.Not != null ? 1 : 0) +
                    (condition.Relationship != null ? 1 : 0) +
                    (condition.Tag != null ? 1 : 0);
                if (declared != 1)
                {
                    throw new InvalidOperationException(
                        $"Control profile '{_profileId}' condition must declare exactly one of all/any/not/relationship/tag.");
                }

                if (condition.All != null)
                {
                    return CompileCombinator(ConditionKind.All, condition.All);
                }

                if (condition.Any != null)
                {
                    return CompileCombinator(ConditionKind.Any, condition.Any);
                }

                if (condition.Not != null)
                {
                    int child = Compile(condition.Not);
                    int start = Children.Count;
                    Children.Add(child);
                    Nodes.Add(new ConditionNode(ConditionKind.Not, start, 1, id: 0, roleA: 0, roleB: 0, symmetricRelationship: false));
                    return Nodes.Count - 1;
                }

                if (condition.Relationship != null)
                {
                    if (condition.Between is not { Count: 2 })
                    {
                        throw new InvalidOperationException(
                            $"Control profile '{_profileId}' relationship predicate requires between with exactly two roles.");
                    }

                    int typeId = _relationshipTypes.GetId(condition.Relationship);
                    bool symmetric = _relationshipTypes.Get(typeId).IsSymmetric;
                    Nodes.Add(new ConditionNode(
                        ConditionKind.Relationship,
                        childStart: 0,
                        childCount: 0,
                        typeId,
                        ResolveRole(condition.Between[0]),
                        ResolveRole(condition.Between[1]),
                        symmetric));
                    return Nodes.Count - 1;
                }

                if (string.IsNullOrWhiteSpace(condition.On))
                {
                    throw new InvalidOperationException(
                        $"Control profile '{_profileId}' tag predicate requires an on role.");
                }

                Nodes.Add(new ConditionNode(
                    ConditionKind.Tag,
                    childStart: 0,
                    childCount: 0,
                    ResolveTagId(condition.Tag!),
                    ResolveRole(condition.On),
                    roleB: 0,
                    symmetricRelationship: false));
                return Nodes.Count - 1;
            }

            private int CompileCombinator(ConditionKind kind, List<AssociationControlConditionConfig> children)
            {
                if (children.Count == 0)
                {
                    throw new InvalidOperationException(
                        $"Control profile '{_profileId}' {kind} combinator requires at least one child condition.");
                }

                Span<int> compiled = children.Count <= 16 ? stackalloc int[children.Count] : new int[children.Count];
                for (int i = 0; i < children.Count; i++)
                {
                    compiled[i] = Compile(children[i]
                        ?? throw new InvalidOperationException($"Control profile '{_profileId}' {kind} combinator has a null child."));
                }

                int start = Children.Count;
                for (int i = 0; i < compiled.Length; i++)
                {
                    Children.Add(compiled[i]);
                }

                Nodes.Add(new ConditionNode(kind, start, compiled.Length, id: 0, roleA: 0, roleB: 0, symmetricRelationship: false));
                return Nodes.Count - 1;
            }

            private byte ResolveRole(string role)
            {
                if (string.Equals(role, _fromRole, StringComparison.Ordinal))
                {
                    return 0;
                }

                if (string.Equals(role, _toRole, StringComparison.Ordinal))
                {
                    return 1;
                }

                throw new InvalidOperationException(
                    $"Control profile '{_profileId}' references unknown role '{role}' (declared roles: '{_fromRole}', '{_toRole}').");
            }

            private int ResolveTagId(string tag)
            {
                if (string.IsNullOrWhiteSpace(tag))
                {
                    throw new InvalidOperationException($"Control profile '{_profileId}' tag predicate requires a non-empty tag.");
                }

                int tagId = TagRegistry.GetId(tag);
                if (tagId > 0)
                {
                    return tagId;
                }

                if (TagRegistry.IsFrozen)
                {
                    throw new InvalidOperationException(
                        $"Control profile '{_profileId}' references tag '{tag}' which is unknown to the frozen TagRegistry.");
                }

                return TagRegistry.Register(tag);
            }
        }
    }
}

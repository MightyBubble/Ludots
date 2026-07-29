using System;
using Arch.Core;
using Ludots.Core.Association;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.Progression.Components;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Mathematics;
using Ludots.Core.NodeLibraries.GASGraph;

namespace Ludots.Core.Gameplay.Progression
{
    public sealed class ProgressionRequirementEvaluator
    {
        private const int MaxResolvedScopeMembers = 256;

        private readonly World _world;
        private readonly ProgressionRequirementRegistry _requirements;
        private readonly ScopeKeyRegistry _scopeKeys;
        private readonly ScopeResolver _scopeResolver;
        private readonly GraphProgramRegistry? _graphPrograms;
        private readonly IGraphRuntimeApi? _graphApi;
        private readonly TagOps _tagOps;

        public ProgressionRequirementEvaluator(
            World world,
            ProgressionRequirementRegistry requirements,
            ScopeKeyRegistry scopeKeys,
            GraphProgramRegistry? graphPrograms = null,
            IGraphRuntimeApi? graphApi = null,
            TagOps? tagOps = null,
            ScopeResolver? scopeResolver = null)
        {
            _world = world;
            _requirements = requirements ?? throw new ArgumentNullException(nameof(requirements));
            _scopeKeys = scopeKeys ?? throw new ArgumentNullException(nameof(scopeKeys));
            _scopeResolver = scopeResolver ?? new ScopeResolver(world, scopeKeys);
            _graphPrograms = graphPrograms;
            _graphApi = graphApi;
            _tagOps = tagOps ?? throw new InvalidOperationException(TagOps.MissingTagOpsError);
        }

        public ScopeKeyRegistry ScopeKeys => _scopeKeys;

        public bool Evaluate(int requirementId, in RoleResolverContext context)
        {
            if (requirementId <= 0)
            {
                return true;
            }

            if (!_requirements.TryGet(requirementId, out var definition))
            {
                throw new InvalidOperationException($"Progression requirement {requirementId} is not registered.");
            }

            if (definition.Nodes.Length == 0)
            {
                return true;
            }

            return EvaluateNode(definition, 0, in context);
        }

        public uint ComputeRevision(int requirementId, in RoleResolverContext context)
        {
            if (requirementId <= 0)
            {
                return 0;
            }

            if (!_requirements.TryGet(requirementId, out var definition))
            {
                throw new InvalidOperationException($"Progression requirement {requirementId} is not registered.");
            }

            uint revision = 2166136261u;
            revision = HashCombine(revision, (uint)requirementId);

            ProgressionRequirementNode[] nodes = definition.Nodes;
            for (int i = 0; i < nodes.Length; i++)
            {
                revision = HashNodeRevision(revision, in nodes[i], in context);
            }

            return revision;
        }

        public uint ComputeScopeRevision(int requirementId, in RoleResolverContext context)
        {
            if (requirementId <= 0)
            {
                return 0;
            }

            if (!_requirements.TryGet(requirementId, out var definition))
            {
                throw new InvalidOperationException($"Progression requirement {requirementId} is not registered.");
            }

            uint revision = 2166136261u;
            revision = HashCombine(revision, (uint)requirementId);

            ProgressionRequirementNode[] nodes = definition.Nodes;
            for (int i = 0; i < nodes.Length; i++)
            {
                revision = HashNodeScopeRevision(revision, in nodes[i], in context);
            }

            return revision;
        }

        public bool RequiresExplicitScope(int requirementId)
        {
            if (requirementId <= 0)
            {
                return false;
            }

            if (!_requirements.TryGet(requirementId, out var definition))
            {
                throw new InvalidOperationException($"Progression requirement {requirementId} is not registered.");
            }

            ProgressionRequirementNode[] nodes = definition.Nodes;
            for (int i = 0; i < nodes.Length; i++)
            {
                if (nodes[i].Scope.Kind == ScopeKind.Explicit)
                {
                    return true;
                }
            }

            return false;
        }

        public bool UsesGraphValidation(int requirementId)
        {
            if (requirementId <= 0)
            {
                return false;
            }

            if (!_requirements.TryGet(requirementId, out var definition))
            {
                throw new InvalidOperationException($"Progression requirement {requirementId} is not registered.");
            }

            ProgressionRequirementNode[] nodes = definition.Nodes;
            for (int i = 0; i < nodes.Length; i++)
            {
                if (nodes[i].Kind == ProgressionRequirementNodeKind.GraphValidation)
                {
                    return true;
                }
            }

            return false;
        }

        public bool TryComplete(Entity scopeHost, int progressionId)
            => TryApply(scopeHost, progressionId, ProgressionLevelChange.Complete);

        public bool TryApply(Entity scopeHost, int progressionId, ProgressionLevelChange change)
        {
            if (!_world.IsAlive(scopeHost) || progressionId <= 0)
            {
                return false;
            }

            ref var state = ref _world.TryGetRef<ProgressionStateBuffer>(scopeHost, out bool hasState);
            if (!hasState)
            {
                return false;
            }

            return ApplyChange(ref state, progressionId, in change);
        }

        public bool TryComplete(Entity actor, ScopeKey scope, int progressionId)
            => TryApply(actor, scope, progressionId, ProgressionLevelChange.Complete);

        public bool TryApply(Entity actor, ScopeKey scope, int progressionId, ProgressionLevelChange change)
        {
            var context = new RoleResolverContext(actor: actor, subject: actor);
            return TryApply(in context, scope, progressionId, in change);
        }

        public bool TryComplete(in RoleResolverContext context, ScopeKey scope, int progressionId)
            => TryApply(in context, scope, progressionId, ProgressionLevelChange.Complete);

        public bool TryApply(
            in RoleResolverContext context,
            ScopeKey scope,
            int progressionId,
            in ProgressionLevelChange change)
        {
            if (!TryResolveScopeHostInternal(in scope, in context, out Entity scopeHost))
            {
                return false;
            }

            return TryApply(scopeHost, progressionId, change);
        }

        public bool TryResolveScopeHost(ScopeKey scope, in RoleResolverContext context, out Entity scopeHost)
        {
            return TryResolveScopeHostInternal(in scope, in context, out scopeHost);
        }

        public bool TryGetScopeProgressionRevision(in ScopeKey scope, in RoleResolverContext context, out uint revision)
        {
            revision = 0;
            if (!TryResolveScopeHostInternal(in scope, in context, out Entity scopeHost) ||
                !_world.IsAlive(scopeHost) ||
                !_world.Has<ProgressionStateBuffer>(scopeHost))
            {
                return false;
            }

            ref readonly var state = ref _world.Get<ProgressionStateBuffer>(scopeHost);
            revision = state.Revision;
            return true;
        }

        public bool TryBindScope(Entity entity, string scopeKey, Entity scopeHost)
        {
            if (!_scopeKeys.TryGetId(scopeKey, out int scopeKeyId) || scopeKeyId <= 0)
            {
                return false;
            }

            return TryBindScope(entity, scopeKeyId, scopeHost);
        }

        public bool TryBindScope(Entity entity, int scopeKeyId, Entity scopeHost)
        {
            return _world.IsAlive(entity) &&
                   _world.Has<ScopeMemberTag>(entity) &&
                   _scopeResolver.TryBindScope(entity, scopeKeyId, scopeHost);
        }

        private bool EvaluateNode(ProgressionRequirementDefinition definition, int nodeIndex, in RoleResolverContext context)
        {
            ProgressionRequirementNode[] nodes = definition.Nodes;
            if ((uint)nodeIndex >= (uint)nodes.Length)
            {
                return false;
            }

            ref readonly var node = ref nodes[nodeIndex];
            switch (node.Kind)
            {
                case ProgressionRequirementNodeKind.None:
                    return true;
                case ProgressionRequirementNodeKind.All:
                    return EvaluateAll(definition, in node, in context);
                case ProgressionRequirementNodeKind.Any:
                    return EvaluateAny(definition, in node, in context);
                case ProgressionRequirementNodeKind.Not:
                    return node.ChildCount == 1 &&
                           TryGetChildNodeIndex(definition.ChildIndices, in node, 0, out int childIndex) &&
                           !EvaluateNode(definition, childIndex, in context);
                case ProgressionRequirementNodeKind.ProgressionCompleted:
                    return EvaluateProgressionLevelAtLeast(in node, in context, requiredLevel: 1);
                case ProgressionRequirementNodeKind.ProgressionLevelAtLeast:
                    return EvaluateProgressionLevelAtLeast(in node, in context, Math.Max(1, node.RequiredCount));
                case ProgressionRequirementNodeKind.EntityCount:
                    return CountMatchingEntities(in node, in context) >= Math.Max(1, node.RequiredCount);
                case ProgressionRequirementNodeKind.TagAll:
                    return EvaluateTagAll(in node, in context);
                case ProgressionRequirementNodeKind.GraphValidation:
                    return EvaluateGraphValidation(in node, in context);
                default:
                    throw new ArgumentOutOfRangeException(nameof(node.Kind), node.Kind, "Unsupported progression requirement node kind.");
            }
        }

        private bool EvaluateAll(ProgressionRequirementDefinition definition, in ProgressionRequirementNode node, in RoleResolverContext context)
        {
            for (int i = 0; i < node.ChildCount; i++)
            {
                if (!TryGetChildNodeIndex(definition.ChildIndices, in node, i, out int childIndex) ||
                    !EvaluateNode(definition, childIndex, in context))
                {
                    return false;
                }
            }

            return true;
        }

        private bool EvaluateAny(ProgressionRequirementDefinition definition, in ProgressionRequirementNode node, in RoleResolverContext context)
        {
            for (int i = 0; i < node.ChildCount; i++)
            {
                if (TryGetChildNodeIndex(definition.ChildIndices, in node, i, out int childIndex) &&
                    EvaluateNode(definition, childIndex, in context))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryGetChildNodeIndex(int[] childIndices, in ProgressionRequirementNode node, int childOffset, out int childIndex)
        {
            int index = node.FirstChild + childOffset;
            if ((uint)childOffset >= (uint)node.ChildCount || (uint)index >= (uint)childIndices.Length)
            {
                childIndex = -1;
                return false;
            }

            childIndex = childIndices[index];
            return childIndex >= 0;
        }

        private bool EvaluateProgressionLevelAtLeast(in ProgressionRequirementNode node, in RoleResolverContext context, int requiredLevel)
        {
            if (!TryResolveScopeHostInternal(in node.Scope, in context, out Entity scopeHost) ||
                !_world.IsAlive(scopeHost) ||
                !_world.Has<ProgressionStateBuffer>(scopeHost))
            {
                return false;
            }

            ref readonly var state = ref _world.Get<ProgressionStateBuffer>(scopeHost);
            return state.HasLevelAtLeast(node.ProgressionId, requiredLevel);
        }

        private static bool ApplyChange(ref ProgressionStateBuffer state, int progressionId, in ProgressionLevelChange change)
        {
            if (change.Delta > 0)
            {
                return state.TryAddLevel(progressionId, change.Delta);
            }

            if (change.Level > 0)
            {
                return state.TrySetLevelAtLeast(progressionId, change.Level);
            }

            return state.TryComplete(progressionId);
        }

        private bool EvaluateTagAll(in ProgressionRequirementNode node, in RoleResolverContext context)
        {
            switch (node.EntitySource)
            {
                case RoleSlot.Actor:
                    return HasRequiredTags(context.Actor, in node.RequiredTags);
                case RoleSlot.Subject:
                    return HasRequiredTags(context.Subject, in node.RequiredTags);
                case RoleSlot.ScopeHost:
                    return TryResolveScopeHostInternal(in node.Scope, in context, out Entity scopeHost) &&
                           HasRequiredTags(scopeHost, in node.RequiredTags);
                default:
                    return CountMatchingEntities(in node, in context) > 0;
            }
        }

        private bool EvaluateGraphValidation(in ProgressionRequirementNode node, in RoleResolverContext context)
        {
            if (node.GraphProgramId <= 0)
            {
                return true;
            }

            if (_graphPrograms == null || _graphApi == null)
            {
                throw new InvalidOperationException($"Progression requirement graph {node.GraphProgramId} cannot run because graph services are not configured.");
            }

            if (!_graphPrograms.TryGetProgram(node.GraphProgramId, out var program))
            {
                throw new InvalidOperationException($"Progression requirement references missing graph {node.GraphProgramId}.");
            }

            GraphKind kind = _graphPrograms.RequireKind(node.GraphProgramId, GraphKind.Validation);
            Entity graphTarget = ResolveEntitySource(node.EntitySource, in node.Scope, in context);
            return Ludots.Core.NodeLibraries.GASGraph.GraphExecutor.ExecuteValidation(
                _world,
                context.Actor,
                graphTarget,
                default(IntVector2),
                program,
                _graphApi,
                kind);
        }

        private int CountMatchingEntities(in ProgressionRequirementNode node, in RoleResolverContext context)
        {
            Entity direct = ResolveDirectEntitySource(node.EntitySource, in node.Scope, in context);
            if (_world.IsAlive(direct))
            {
                return HasRequiredTags(direct, in node.RequiredTags) ? 1 : 0;
            }

            if (!TryResolveScopeHostInternal(in node.Scope, in context, out Entity scopeHost))
            {
                return 0;
            }

            Span<Entity> members = stackalloc Entity[MaxResolvedScopeMembers];
            int memberCount = _scopeResolver.ResolveMembers(in node.Scope, in context, members);
            int count = 0;
            for (int i = 0; i < memberCount; i++)
            {
                if (HasRequiredTags(members[i], in node.RequiredTags))
                {
                    count++;
                }
            }

            return count;
        }

        private Entity ResolveEntitySource(RoleSlot source, in ScopeKey scope, in RoleResolverContext context)
        {
            Entity direct = ResolveDirectEntitySource(source, in scope, in context);
            if (_world.IsAlive(direct))
            {
                return direct;
            }

            return TryResolveScopeHostInternal(in scope, in context, out Entity scopeHost) ? scopeHost : Entity.Null;
        }

        private Entity ResolveDirectEntitySource(RoleSlot source, in ScopeKey scope, in RoleResolverContext context)
        {
            return source switch
            {
                RoleSlot.Actor => context.Actor,
                RoleSlot.Subject => context.Subject,
                RoleSlot.ScopeHost when TryResolveScopeHostInternal(in scope, in context, out Entity scopeHost) => scopeHost,
                _ => Entity.Null,
            };
        }

        private bool TryResolveScopeHostInternal(in ScopeKey scope, in RoleResolverContext context, out Entity scopeHost)
        {
            return _scopeResolver.TryResolveHost(in scope, in context, out scopeHost);
        }

        private bool HasRequiredTags(Entity entity, in GameplayTagContainer requiredTags)
        {
            if (!_world.IsAlive(entity))
            {
                return false;
            }

            if (requiredTags.IsEmpty)
            {
                return true;
            }

            if (!_world.Has<GameplayTagContainer>(entity))
            {
                return false;
            }

            ref var tags = ref _world.Get<GameplayTagContainer>(entity);
            return _tagOps.ContainsAll(ref tags, in requiredTags, TagSense.Effective);
        }

        private uint HashNodeRevision(uint revision, in ProgressionRequirementNode node, in RoleResolverContext context)
        {
            revision = HashCombine(revision, (uint)node.Kind);
            revision = HashCombine(revision, (uint)node.Scope.Kind);
            revision = HashCombine(revision, (uint)node.Scope.ScopeKeyId);
            revision = HashCombine(revision, (uint)node.EntitySource);
            revision = HashCombine(revision, (uint)node.ProgressionId);
            revision = HashCombine(revision, (uint)node.RequiredCount);
            revision = HashCombine(revision, (uint)node.GraphProgramId);

            Entity observed = ResolveEntitySource(node.EntitySource, in node.Scope, in context);
            revision = HashEntity(revision, observed);
            if (_world.IsAlive(observed) && _world.Has<GameplayTagContainer>(observed))
            {
                ref readonly var tags = ref _world.Get<GameplayTagContainer>(observed);
                revision = HashTagContainer(revision, in tags);
            }

            if (TryResolveScopeHostInternal(in node.Scope, in context, out Entity scopeHost))
            {
                revision = HashEntity(revision, scopeHost);
                if (_world.Has<ProgressionStateBuffer>(scopeHost))
                {
                    ref readonly var state = ref _world.Get<ProgressionStateBuffer>(scopeHost);
                    revision = HashCombine(revision, state.Revision);
                }
                if (_world.Has<ScopeMembershipRevision>(scopeHost))
                {
                    ref readonly var membership = ref _world.Get<ScopeMembershipRevision>(scopeHost);
                    revision = HashCombine(revision, membership.Revision);
                }

                if (node.EntitySource == RoleSlot.ScopeMembers &&
                    node.Scope.ScopeKeyId > 0)
                {
                    Span<Entity> members = stackalloc Entity[MaxResolvedScopeMembers];
                    int memberCount = _scopeResolver.ResolveMembers(in node.Scope, in context, members);
                    for (int i = 0; i < memberCount; i++)
                    {
                        Entity member = members[i];
                        revision = HashEntity(revision, member);
                        if (!_world.Has<GameplayTagContainer>(member))
                        {
                            continue;
                        }

                        ref readonly var tags = ref _world.Get<GameplayTagContainer>(member);
                        revision = HashTagContainer(revision, in tags);
                    }
                }
            }

            return revision;
        }

        private uint HashNodeScopeRevision(uint revision, in ProgressionRequirementNode node, in RoleResolverContext context)
        {
            revision = HashCombine(revision, (uint)node.Kind);
            revision = HashCombine(revision, (uint)node.Scope.Kind);
            revision = HashCombine(revision, (uint)node.Scope.ScopeKeyId);
            revision = HashCombine(revision, (uint)node.EntitySource);
            revision = HashCombine(revision, (uint)node.ProgressionId);
            revision = HashCombine(revision, (uint)node.RequiredCount);
            revision = HashCombine(revision, (uint)node.GraphProgramId);

            Entity observed = ResolveEntitySource(node.EntitySource, in node.Scope, in context);
            revision = HashEntity(revision, observed);

            if (TryResolveScopeHostInternal(in node.Scope, in context, out Entity scopeHost))
            {
                revision = HashEntity(revision, scopeHost);
                if (_world.Has<ProgressionStateBuffer>(scopeHost))
                {
                    ref readonly var state = ref _world.Get<ProgressionStateBuffer>(scopeHost);
                    revision = HashCombine(revision, state.Revision);
                }

                if (_world.Has<ScopeMembershipRevision>(scopeHost))
                {
                    ref readonly var membership = ref _world.Get<ScopeMembershipRevision>(scopeHost);
                    revision = HashCombine(revision, membership.Revision);
                }
            }

            return revision;
        }

        private static uint HashEntity(uint revision, Entity entity)
        {
            revision = HashCombine(revision, (uint)entity.Id);
            revision = HashCombine(revision, (uint)entity.WorldId);
            revision = HashCombine(revision, (uint)entity.Version);
            return revision;
        }

        private static uint HashTagContainer(uint revision, in GameplayTagContainer tags)
        {
            for (int tagId = 1; tagId <= GameplayTagContainer.MAX_TAG_ID; tagId++)
            {
                if (tags.HasTag(tagId))
                {
                    revision = HashCombine(revision, (uint)tagId);
                }
            }

            return revision;
        }

        private static uint HashCombine(uint current, uint value)
        {
            unchecked
            {
                return ((current ^ value) * 16777619u) + 1u;
            }
        }

    }
}

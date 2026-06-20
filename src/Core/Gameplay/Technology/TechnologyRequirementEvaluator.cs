using System;
using Arch.Core;
using Arch.System;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.Technology.Components;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Mathematics;
using Ludots.Core.NodeLibraries.GASGraph;

namespace Ludots.Core.Gameplay.Technology
{
    public sealed class TechnologyRequirementEvaluator
    {
        private static readonly QueryDescription ScopeMemberQuery = new QueryDescription()
            .WithAll<TechnologyScopeRefBuffer>();

        private readonly World _world;
        private readonly TechnologyRequirementRegistry _requirements;
        private readonly TechnologyScopeKeyRegistry _scopeKeys;
        private readonly GraphProgramRegistry? _graphPrograms;
        private readonly IGraphRuntimeApi? _graphApi;
        private readonly TagOps _tagOps;

        public TechnologyRequirementEvaluator(
            World world,
            TechnologyRequirementRegistry requirements,
            TechnologyScopeKeyRegistry scopeKeys,
            GraphProgramRegistry? graphPrograms = null,
            IGraphRuntimeApi? graphApi = null,
            TagOps? tagOps = null)
        {
            _world = world;
            _requirements = requirements ?? throw new ArgumentNullException(nameof(requirements));
            _scopeKeys = scopeKeys ?? throw new ArgumentNullException(nameof(scopeKeys));
            _graphPrograms = graphPrograms;
            _graphApi = graphApi;
            _tagOps = tagOps ?? new TagOps();
        }

        public TechnologyScopeKeyRegistry ScopeKeys => _scopeKeys;

        public bool Evaluate(int requirementId, in TechnologyRequirementEvaluationContext context)
        {
            if (requirementId <= 0)
            {
                return true;
            }

            if (!_requirements.TryGet(requirementId, out var definition))
            {
                throw new InvalidOperationException($"Technology requirement {requirementId} is not registered.");
            }

            if (definition.Nodes.Length == 0)
            {
                return true;
            }

            return EvaluateNode(definition, 0, in context);
        }

        public uint ComputeRevision(int requirementId, in TechnologyRequirementEvaluationContext context)
        {
            if (requirementId <= 0)
            {
                return 0;
            }

            if (!_requirements.TryGet(requirementId, out var definition))
            {
                throw new InvalidOperationException($"Technology requirement {requirementId} is not registered.");
            }

            uint revision = 2166136261u;
            revision = HashCombine(revision, (uint)requirementId);

            TechnologyRequirementNode[] nodes = definition.Nodes;
            for (int i = 0; i < nodes.Length; i++)
            {
                revision = HashNodeRevision(revision, in nodes[i], in context);
            }

            return revision;
        }

        public uint ComputeScopeRevision(int requirementId, in TechnologyRequirementEvaluationContext context)
        {
            if (requirementId <= 0)
            {
                return 0;
            }

            if (!_requirements.TryGet(requirementId, out var definition))
            {
                throw new InvalidOperationException($"Technology requirement {requirementId} is not registered.");
            }

            uint revision = 2166136261u;
            revision = HashCombine(revision, (uint)requirementId);

            TechnologyRequirementNode[] nodes = definition.Nodes;
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
                throw new InvalidOperationException($"Technology requirement {requirementId} is not registered.");
            }

            TechnologyRequirementNode[] nodes = definition.Nodes;
            for (int i = 0; i < nodes.Length; i++)
            {
                if (nodes[i].Scope.Kind == TechnologyScopeKind.Explicit)
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
                throw new InvalidOperationException($"Technology requirement {requirementId} is not registered.");
            }

            TechnologyRequirementNode[] nodes = definition.Nodes;
            for (int i = 0; i < nodes.Length; i++)
            {
                if (nodes[i].Kind == TechnologyRequirementNodeKind.GraphValidation)
                {
                    return true;
                }
            }

            return false;
        }

        public bool TryComplete(Entity scopeHost, int technologyId)
            => TryApply(scopeHost, technologyId, TechnologyLevelChange.Complete);

        public bool TryApply(Entity scopeHost, int technologyId, TechnologyLevelChange change)
        {
            if (!_world.IsAlive(scopeHost) || technologyId <= 0)
            {
                return false;
            }

            ref var state = ref _world.TryGetRef<TechnologyStateBuffer>(scopeHost, out bool hasState);
            if (!hasState)
            {
                return false;
            }

            return ApplyChange(ref state, technologyId, in change);
        }

        public bool TryComplete(Entity actor, TechnologyScopeSpec scope, int technologyId)
            => TryApply(actor, scope, technologyId, TechnologyLevelChange.Complete);

        public bool TryApply(Entity actor, TechnologyScopeSpec scope, int technologyId, TechnologyLevelChange change)
        {
            var context = new TechnologyRequirementEvaluationContext(actor, actor);
            return TryApply(in context, scope, technologyId, in change);
        }

        public bool TryComplete(in TechnologyRequirementEvaluationContext context, TechnologyScopeSpec scope, int technologyId)
            => TryApply(in context, scope, technologyId, TechnologyLevelChange.Complete);

        public bool TryApply(
            in TechnologyRequirementEvaluationContext context,
            TechnologyScopeSpec scope,
            int technologyId,
            in TechnologyLevelChange change)
        {
            if (!TryResolveScopeHostInternal(in scope, in context, out Entity scopeHost))
            {
                return false;
            }

            return TryApply(scopeHost, technologyId, change);
        }

        public bool TryResolveScopeHost(TechnologyScopeSpec scope, in TechnologyRequirementEvaluationContext context, out Entity scopeHost)
        {
            return TryResolveScopeHostInternal(in scope, in context, out scopeHost);
        }

        public bool TryGetScopeTechnologyRevision(in TechnologyScopeSpec scope, in TechnologyRequirementEvaluationContext context, out uint revision)
        {
            revision = 0;
            if (!TryResolveScopeHostInternal(in scope, in context, out Entity scopeHost) ||
                !_world.IsAlive(scopeHost) ||
                !_world.Has<TechnologyStateBuffer>(scopeHost))
            {
                return false;
            }

            ref readonly var state = ref _world.Get<TechnologyStateBuffer>(scopeHost);
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
            if (!_world.IsAlive(entity) || !_world.IsAlive(scopeHost) || scopeKeyId <= 0)
            {
                return false;
            }

            if (!_world.Has<TechnologyScopeRefBuffer>(entity) ||
                !_world.Has<TechnologyScopeMemberTag>(entity) ||
                !_world.Has<TechnologyScopeMembershipRevision>(scopeHost))
            {
                return false;
            }

            ref var refs = ref _world.Get<TechnologyScopeRefBuffer>(entity);
            if (!refs.TryAdd(scopeKeyId, scopeHost, out bool changed, out Entity previousScopeHost))
            {
                return false;
            }

            if (!changed)
            {
                return true;
            }

            ref var revision = ref _world.Get<TechnologyScopeMembershipRevision>(scopeHost);
            revision.Revision++;
            if (_world.IsAlive(previousScopeHost) &&
                (previousScopeHost.Id != scopeHost.Id ||
                 previousScopeHost.WorldId != scopeHost.WorldId ||
                 previousScopeHost.Version != scopeHost.Version) &&
                _world.Has<TechnologyScopeMembershipRevision>(previousScopeHost))
            {
                ref var previousRevision = ref _world.Get<TechnologyScopeMembershipRevision>(previousScopeHost);
                previousRevision.Revision++;
            }
            return true;
        }

        private bool EvaluateNode(TechnologyRequirementDefinition definition, int nodeIndex, in TechnologyRequirementEvaluationContext context)
        {
            TechnologyRequirementNode[] nodes = definition.Nodes;
            if ((uint)nodeIndex >= (uint)nodes.Length)
            {
                return false;
            }

            ref readonly var node = ref nodes[nodeIndex];
            switch (node.Kind)
            {
                case TechnologyRequirementNodeKind.None:
                    return true;
                case TechnologyRequirementNodeKind.All:
                    return EvaluateAll(definition, in node, in context);
                case TechnologyRequirementNodeKind.Any:
                    return EvaluateAny(definition, in node, in context);
                case TechnologyRequirementNodeKind.Not:
                    return node.ChildCount == 1 &&
                           TryGetChildNodeIndex(definition.ChildIndices, in node, 0, out int childIndex) &&
                           !EvaluateNode(definition, childIndex, in context);
                case TechnologyRequirementNodeKind.TechCompleted:
                    return EvaluateTechLevelAtLeast(in node, in context, requiredLevel: 1);
                case TechnologyRequirementNodeKind.TechLevelAtLeast:
                    return EvaluateTechLevelAtLeast(in node, in context, Math.Max(1, node.RequiredCount));
                case TechnologyRequirementNodeKind.EntityCount:
                    return CountMatchingEntities(in node, in context) >= Math.Max(1, node.RequiredCount);
                case TechnologyRequirementNodeKind.TagAll:
                    return EvaluateTagAll(in node, in context);
                case TechnologyRequirementNodeKind.GraphValidation:
                    return EvaluateGraphValidation(in node, in context);
                default:
                    throw new ArgumentOutOfRangeException(nameof(node.Kind), node.Kind, "Unsupported technology requirement node kind.");
            }
        }

        private bool EvaluateAll(TechnologyRequirementDefinition definition, in TechnologyRequirementNode node, in TechnologyRequirementEvaluationContext context)
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

        private bool EvaluateAny(TechnologyRequirementDefinition definition, in TechnologyRequirementNode node, in TechnologyRequirementEvaluationContext context)
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

        private static bool TryGetChildNodeIndex(int[] childIndices, in TechnologyRequirementNode node, int childOffset, out int childIndex)
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

        private bool EvaluateTechLevelAtLeast(in TechnologyRequirementNode node, in TechnologyRequirementEvaluationContext context, int requiredLevel)
        {
            if (!TryResolveScopeHostInternal(in node.Scope, in context, out Entity scopeHost) ||
                !_world.IsAlive(scopeHost) ||
                !_world.Has<TechnologyStateBuffer>(scopeHost))
            {
                return false;
            }

            ref readonly var state = ref _world.Get<TechnologyStateBuffer>(scopeHost);
            return state.HasLevelAtLeast(node.TechnologyId, requiredLevel);
        }

        private static bool ApplyChange(ref TechnologyStateBuffer state, int technologyId, in TechnologyLevelChange change)
        {
            if (change.Delta > 0)
            {
                return state.TryAddLevel(technologyId, change.Delta);
            }

            if (change.Level > 0)
            {
                return state.TrySetLevelAtLeast(technologyId, change.Level);
            }

            return state.TryComplete(technologyId);
        }

        private bool EvaluateTagAll(in TechnologyRequirementNode node, in TechnologyRequirementEvaluationContext context)
        {
            switch (node.EntitySource)
            {
                case TechnologyRequirementEntitySource.Actor:
                    return HasRequiredTags(context.Actor, in node.RequiredTags);
                case TechnologyRequirementEntitySource.Subject:
                    return HasRequiredTags(context.Subject, in node.RequiredTags);
                case TechnologyRequirementEntitySource.ScopeHost:
                    return TryResolveScopeHostInternal(in node.Scope, in context, out Entity scopeHost) &&
                           HasRequiredTags(scopeHost, in node.RequiredTags);
                default:
                    return CountMatchingEntities(in node, in context) > 0;
            }
        }

        private bool EvaluateGraphValidation(in TechnologyRequirementNode node, in TechnologyRequirementEvaluationContext context)
        {
            if (node.GraphProgramId <= 0)
            {
                return true;
            }

            if (_graphPrograms == null || _graphApi == null)
            {
                throw new InvalidOperationException($"Technology requirement graph {node.GraphProgramId} cannot run because graph services are not configured.");
            }

            if (!_graphPrograms.TryGetProgram(node.GraphProgramId, out var program))
            {
                throw new InvalidOperationException($"Technology requirement references missing graph {node.GraphProgramId}.");
            }

            Entity graphTarget = ResolveEntitySource(node.EntitySource, in node.Scope, in context);
            return Ludots.Core.NodeLibraries.GASGraph.GraphExecutor.ExecuteValidation(
                _world,
                context.Actor,
                graphTarget,
                default(IntVector2),
                program,
                _graphApi);
        }

        private int CountMatchingEntities(in TechnologyRequirementNode node, in TechnologyRequirementEvaluationContext context)
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

            var job = new CountScopeMembersJob
            {
                World = _world,
                ScopeKeyId = node.Scope.ScopeKeyId,
                ScopeHost = scopeHost,
                RequiredTags = node.RequiredTags,
                TagOps = _tagOps,
                Count = 0
            };
            _world.InlineEntityQuery<CountScopeMembersJob, TechnologyScopeRefBuffer>(in ScopeMemberQuery, ref job);
            return job.Count;
        }

        private Entity ResolveEntitySource(TechnologyRequirementEntitySource source, in TechnologyScopeSpec scope, in TechnologyRequirementEvaluationContext context)
        {
            Entity direct = ResolveDirectEntitySource(source, in scope, in context);
            if (_world.IsAlive(direct))
            {
                return direct;
            }

            return TryResolveScopeHostInternal(in scope, in context, out Entity scopeHost) ? scopeHost : Entity.Null;
        }

        private Entity ResolveDirectEntitySource(TechnologyRequirementEntitySource source, in TechnologyScopeSpec scope, in TechnologyRequirementEvaluationContext context)
        {
            return source switch
            {
                TechnologyRequirementEntitySource.Actor => context.Actor,
                TechnologyRequirementEntitySource.Subject => context.Subject,
                TechnologyRequirementEntitySource.ScopeHost when TryResolveScopeHostInternal(in scope, in context, out Entity scopeHost) => scopeHost,
                _ => Entity.Null,
            };
        }

        private bool TryResolveScopeHostInternal(in TechnologyScopeSpec scope, in TechnologyRequirementEvaluationContext context, out Entity scopeHost)
        {
            if (scope.Kind == TechnologyScopeKind.Explicit)
            {
                scopeHost = context.ExplicitScopeHost;
                return _world.IsAlive(scopeHost);
            }

            if (scope.Kind == TechnologyScopeKind.Self)
            {
                scopeHost = _world.IsAlive(context.Subject) ? context.Subject : context.Actor;
                return _world.IsAlive(scopeHost);
            }

            int scopeKeyId = scope.ScopeKeyId;
            if (scopeKeyId <= 0)
            {
                scopeHost = Entity.Null;
                return false;
            }

            if (TryResolveScopeHostFrom(context.Subject, scopeKeyId, out scopeHost))
            {
                return true;
            }

            return TryResolveScopeHostFrom(context.Actor, scopeKeyId, out scopeHost);
        }

        private bool TryResolveScopeHostFrom(Entity entity, int scopeKeyId, out Entity scopeHost)
        {
            if (!_world.IsAlive(entity) || !_world.Has<TechnologyScopeRefBuffer>(entity))
            {
                scopeHost = Entity.Null;
                return false;
            }

            ref readonly var refs = ref _world.Get<TechnologyScopeRefBuffer>(entity);
            return refs.TryGet(scopeKeyId, out scopeHost) && _world.IsAlive(scopeHost);
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

        private uint HashNodeRevision(uint revision, in TechnologyRequirementNode node, in TechnologyRequirementEvaluationContext context)
        {
            revision = HashCombine(revision, (uint)node.Kind);
            revision = HashCombine(revision, (uint)node.Scope.Kind);
            revision = HashCombine(revision, (uint)node.Scope.ScopeKeyId);
            revision = HashCombine(revision, (uint)node.EntitySource);
            revision = HashCombine(revision, (uint)node.TechnologyId);
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
                if (_world.Has<TechnologyStateBuffer>(scopeHost))
                {
                    ref readonly var state = ref _world.Get<TechnologyStateBuffer>(scopeHost);
                    revision = HashCombine(revision, state.Revision);
                }
                if (_world.Has<TechnologyScopeMembershipRevision>(scopeHost))
                {
                    ref readonly var membership = ref _world.Get<TechnologyScopeMembershipRevision>(scopeHost);
                    revision = HashCombine(revision, membership.Revision);
                }

                if (node.EntitySource == TechnologyRequirementEntitySource.ScopeMembers &&
                    node.Scope.ScopeKeyId > 0)
                {
                    var job = new HashScopeMembersJob
                    {
                        World = _world,
                        ScopeKeyId = node.Scope.ScopeKeyId,
                        ScopeHost = scopeHost,
                        Revision = revision
                    };
                    _world.InlineEntityQuery<HashScopeMembersJob, TechnologyScopeRefBuffer>(in ScopeMemberQuery, ref job);
                    revision = job.Revision;
                }
            }

            return revision;
        }

        private uint HashNodeScopeRevision(uint revision, in TechnologyRequirementNode node, in TechnologyRequirementEvaluationContext context)
        {
            revision = HashCombine(revision, (uint)node.Kind);
            revision = HashCombine(revision, (uint)node.Scope.Kind);
            revision = HashCombine(revision, (uint)node.Scope.ScopeKeyId);
            revision = HashCombine(revision, (uint)node.EntitySource);
            revision = HashCombine(revision, (uint)node.TechnologyId);
            revision = HashCombine(revision, (uint)node.RequiredCount);
            revision = HashCombine(revision, (uint)node.GraphProgramId);

            Entity observed = ResolveEntitySource(node.EntitySource, in node.Scope, in context);
            revision = HashEntity(revision, observed);

            if (TryResolveScopeHostInternal(in node.Scope, in context, out Entity scopeHost))
            {
                revision = HashEntity(revision, scopeHost);
                if (_world.Has<TechnologyStateBuffer>(scopeHost))
                {
                    ref readonly var state = ref _world.Get<TechnologyStateBuffer>(scopeHost);
                    revision = HashCombine(revision, state.Revision);
                }

                if (_world.Has<TechnologyScopeMembershipRevision>(scopeHost))
                {
                    ref readonly var membership = ref _world.Get<TechnologyScopeMembershipRevision>(scopeHost);
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

        private struct CountScopeMembersJob : IForEachWithEntity<TechnologyScopeRefBuffer>
        {
            public World World;
            public int ScopeKeyId;
            public Entity ScopeHost;
            public GameplayTagContainer RequiredTags;
            public TagOps TagOps;
            public int Count;

            public void Update(Entity entity, ref TechnologyScopeRefBuffer refs)
            {
                if (!refs.TryGet(ScopeKeyId, out Entity host) ||
                    host.Id != ScopeHost.Id ||
                    host.WorldId != ScopeHost.WorldId ||
                    host.Version != ScopeHost.Version)
                {
                    return;
                }

                if (RequiredTags.IsEmpty)
                {
                    Count++;
                    return;
                }

                if (!World.Has<GameplayTagContainer>(entity))
                {
                    return;
                }

                ref var tags = ref World.Get<GameplayTagContainer>(entity);
                if (TagOps.ContainsAll(ref tags, in RequiredTags, TagSense.Effective))
                {
                    Count++;
                }
            }
        }

        private struct HashScopeMembersJob : IForEachWithEntity<TechnologyScopeRefBuffer>
        {
            public World World;
            public int ScopeKeyId;
            public Entity ScopeHost;
            public uint Revision;

            public void Update(Entity entity, ref TechnologyScopeRefBuffer refs)
            {
                if (!refs.TryGet(ScopeKeyId, out Entity host) ||
                    host.Id != ScopeHost.Id ||
                    host.WorldId != ScopeHost.WorldId ||
                    host.Version != ScopeHost.Version)
                {
                    return;
                }

                Revision = HashEntity(Revision, entity);
                if (!World.Has<GameplayTagContainer>(entity))
                {
                    return;
                }

                ref readonly var tags = ref World.Get<GameplayTagContainer>(entity);
                Revision = HashTagContainer(Revision, in tags);
            }
        }
    }
}

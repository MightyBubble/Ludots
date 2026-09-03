using System;
using System.Collections.Generic;
using Arch.Core;
using Arch.System;
using Ludots.Core.EntityCollections;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Mathematics;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Gameplay.MapTriggers
{
    /// <summary>
    /// Case E §05: while an interaction context is active, every tick run the profile's
    /// <c>whileActive.graph</c> so the hit function can WriteCollection-write the
    /// preview set. When the subject leaves every whileActive context, clear those collection
    /// keys so membership events tear down preview highlights.
    /// </summary>
    public sealed class InteractionContextWhileActiveSystem : BaseSystem<World, float>
    {
        private static readonly QueryDescription ActiveContextQuery =
            new QueryDescription().WithAny<InteractionContextInstance, InteractionContextInstances>();

        private readonly InteractionContextProfileRegistry _contextProfiles;
        private readonly GraphReturnWriter _graphReturnWriter;
        private readonly GraphProgramRegistry _programs;
        private readonly EntityCollectionStore _collections;
        private readonly IGraphRuntimeApi _graphApi;

        private Entity[] _subjects = new Entity[16];
        private int _subjectCount;
        private readonly Dictionary<Entity, int> _previousGraphBySubject = new();
        private readonly Dictionary<Entity, int> _activeGraphBySubject = new();

        public InteractionContextWhileActiveSystem(
            World world,
            InteractionContextProfileRegistry contextProfiles,
            GraphReturnWriter graphReturnWriter,
            GraphProgramRegistry programs,
            EntityCollectionStore collections,
            IGraphRuntimeApi graphApi)
            : base(world)
        {
            _contextProfiles = contextProfiles ?? throw new ArgumentNullException(nameof(contextProfiles));
            _graphReturnWriter = graphReturnWriter ?? throw new ArgumentNullException(nameof(graphReturnWriter));
            _programs = programs ?? throw new ArgumentNullException(nameof(programs));
            _collections = collections ?? throw new ArgumentNullException(nameof(collections));
            _graphApi = graphApi ?? throw new ArgumentNullException(nameof(graphApi));
        }

        public override void Update(in float deltaTime)
        {
            _activeGraphBySubject.Clear();
            CollectSubjects();
            for (int i = 0; i < _subjectCount; i++)
            {
                Entity subject = _subjects[i];
                if (!TryResolveWhileActiveGraph(subject, out int graphId))
                {
                    continue;
                }

                _graphReturnWriter.Execute(
                    graphId,
                    caster: subject,
                    explicitTarget: Entity.Null,
                    targetContext: Entity.Null,
                    targetPosCm: default(IntVector2),
                    randomSeed: 0u,
                    api: _graphApi);
                _activeGraphBySubject[subject] = graphId;
            }

            foreach (KeyValuePair<Entity, int> previous in _previousGraphBySubject)
            {
                if (_activeGraphBySubject.ContainsKey(previous.Key))
                {
                    continue;
                }

                ClearDispatchedCollections(previous.Key, previous.Value);
            }

            _previousGraphBySubject.Clear();
            foreach (KeyValuePair<Entity, int> active in _activeGraphBySubject)
            {
                _previousGraphBySubject[active.Key] = active.Value;
            }
        }

        private void CollectSubjects()
        {
            _subjectCount = World.CountEntities(in ActiveContextQuery);
            if (_subjectCount == 0)
            {
                return;
            }

            if (_subjectCount > _subjects.Length)
            {
                int next = _subjects.Length;
                while (next < _subjectCount)
                {
                    next *= 2;
                }

                _subjects = new Entity[next];
            }

            World.GetEntities(in ActiveContextQuery, _subjects);
        }

        private bool TryResolveWhileActiveGraph(Entity subject, out int graphId)
        {
            graphId = 0;
            if (World.TryGet(subject, out InteractionContextInstances instances))
            {
                for (int i = 0; i < instances.Count; i++)
                {
                    if (_contextProfiles.TryGetWhileActiveGraphId(instances[i].ContextId, out int candidate) &&
                        candidate > 0)
                    {
                        if (graphId != 0 && graphId != candidate)
                        {
                            throw new InvalidOperationException(
                                $"Entity {subject} has multiple active contexts with distinct whileActive graphs ({graphId} vs {candidate}); declare at most one whileActive per subject.");
                        }

                        graphId = candidate;
                    }
                }
            }

            if (World.TryGet(subject, out InteractionContextInstance baseInstance) &&
                _contextProfiles.TryGetWhileActiveGraphId(baseInstance.ContextId, out int baseGraph) &&
                baseGraph > 0)
            {
                if (graphId != 0 && graphId != baseGraph)
                {
                    throw new InvalidOperationException(
                        $"Entity {subject} has multiple active contexts with distinct whileActive graphs ({graphId} vs {baseGraph}); declare at most one whileActive per subject.");
                }

                graphId = baseGraph;
            }

            return graphId > 0;
        }

        private void ClearDispatchedCollections(Entity owner, int graphId)
        {
            if (!World.IsAlive(owner))
            {
                return;
            }

            if (!_programs.TryGetProgram(graphId, out ReadOnlySpan<GraphInstruction> program))
            {
                throw new InvalidOperationException(
                    $"WhileActive clear references unknown graph program id {graphId}.");
            }

            for (int i = 0; i < program.Length; i++)
            {
                GraphInstruction instruction = program[i];
                if (instruction.Op != (ushort)GraphNodeOp.WriteCollection)
                {
                    continue;
                }

                int collectionKeyId = instruction.Imm;
                if (collectionKeyId <= 0)
                {
                    throw new InvalidOperationException(
                        $"WhileActive graph id {graphId} WriteCollection has no resolved collection key.");
                }

                // Imm is the EntityCollectionStore key id (see GraphProgramSymbolPatcher), not ConfigKeyRegistry.
                string collectionKey = _collections.KeyRegistry.GetName(collectionKeyId)
                    ?? throw new InvalidOperationException(
                        $"WhileActive graph id {graphId} collection key id {collectionKeyId} resolves to no entity-collection key.");
                var descriptor = EntityCollectionDescriptor.Create(
                    collectionKey,
                    EntityCollectionSourceKind.GasGraphResult,
                    EntityCollectionRoleKind.AcquisitionPreview,
                    title: collectionKey,
                    summary: "whileActive-cleared");
                _collections.Replace(owner, collectionKeyId, descriptor, ReadOnlySpan<Entity>.Empty);
            }
        }
    }
}

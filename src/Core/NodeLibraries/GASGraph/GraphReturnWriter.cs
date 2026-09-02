using System;
using Arch.Core;
using Ludots.Core.EntityCollections;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Mathematics;
using Ludots.Core.TypedCollections;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.NodeLibraries.GASGraph
{
    public sealed class GraphReturnWriter
    {
        private readonly World _world;
        private readonly GraphProgramRegistry _programs;
        private readonly GraphOutputSchemaRegistry _schemas;
        private readonly GasGraphOpHandlerTable _handlers;
        private readonly EntityCollectionStore _collections;
        private readonly IntIdCollectionStore _intIdCollections;
        private readonly GraphOutputValueStore _values;

        public GraphReturnWriter(
            World world,
            GraphProgramRegistry programs,
            GraphOutputSchemaRegistry schemas,
            GasGraphOpHandlerTable handlers,
            EntityCollectionStore collections,
            IntIdCollectionStore intIdCollections,
            GraphOutputValueStore values)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _programs = programs ?? throw new ArgumentNullException(nameof(programs));
            _schemas = schemas ?? throw new ArgumentNullException(nameof(schemas));
            _handlers = handlers ?? throw new ArgumentNullException(nameof(handlers));
            _collections = collections ?? throw new ArgumentNullException(nameof(collections));
            _intIdCollections = intIdCollections ?? throw new ArgumentNullException(nameof(intIdCollections));
            _values = values ?? throw new ArgumentNullException(nameof(values));
        }

        public void ExecuteAndWrite(
            int graphId,
            Entity owner,
            Entity caster,
            Entity explicitTarget,
            Entity targetContext,
            IntVector2 targetPosCm,
            uint randomSeed,
            IGraphRuntimeApi api,
            int subjectIntId = 0)
        {
            Entity resolvedOwner = owner == Entity.Null ? caster : owner;
            if (resolvedOwner == Entity.Null)
            {
                throw new InvalidOperationException("Graph return writer requires an owner or caster entity for materialized outputs.");
            }

            GraphOutputSchema schema = _schemas.Get(graphId);
            if (!schema.HasBindings)
            {
                throw new InvalidOperationException($"Graph program id {graphId} has no output schema.");
            }

            if (!_programs.TryGetProgram(graphId, out ReadOnlySpan<GraphInstruction> program))
            {
                throw new InvalidOperationException($"Graph return writer references unknown graph program id {graphId}.");
            }

            _programs.RequireKind(graphId, GraphKind.Query);
            GraphKindOperationPolicy.RequireAllowed(
                GraphKind.Query,
                program,
                _handlers,
                graphId,
                nameof(GraphReturnWriter));

            Span<float> floats = stackalloc float[GraphVmLimits.MaxFloatRegisters];
            Span<int> ints = stackalloc int[GraphVmLimits.MaxIntRegisters];
            Span<byte> bools = stackalloc byte[GraphVmLimits.MaxBoolRegisters];
            Span<Entity> entities = stackalloc Entity[GraphVmLimits.MaxEntityRegisters];
            Span<Entity> targets = stackalloc Entity[GraphVmLimits.MaxTargets];
            Span<int> intIds = stackalloc int[GraphVmLimits.MaxIntIds];
            Span<int> callStack = stackalloc int[GraphVmLimits.MaxCallStackDepth];
            GraphFrame frame = GraphFrame.Bind(
                GraphKind.Query,
                GraphEntityPreset.TargetContext(targetContext),
                _world,
                caster,
                explicitTarget,
                targetPosCm,
                api ?? throw new ArgumentNullException(nameof(api)),
                _programs,
                floats,
                ints,
                bools,
                entities,
                targets,
                intIds,
                callStack,
                randomSeed: randomSeed,
                subjectIntId: subjectIntId);
            GraphExecutor.Execute(ref frame, program, programAlreadyValidated: true);
            WriteOutputs(resolvedOwner, caster, explicitTarget, targetContext, schema, ref frame);
        }

        /// <summary>
        /// Run a Query program for its authored side effects (e.g. DispatchCollectionEvent)
        /// without materializing <c>outputs[]</c>. Continuous context ticks use this so the
        /// graph itself owns collection writes — GraphReturnWriter must not steal that job.
        /// </summary>
        public void Execute(
            int graphId,
            Entity caster,
            Entity explicitTarget,
            Entity targetContext,
            IntVector2 targetPosCm,
            uint randomSeed,
            IGraphRuntimeApi api,
            int subjectIntId = 0)
        {
            if (!_programs.TryGetProgram(graphId, out ReadOnlySpan<GraphInstruction> program))
            {
                throw new InvalidOperationException($"Graph return writer references unknown graph program id {graphId}.");
            }

            _programs.RequireKind(graphId, GraphKind.Query);
            GraphKindOperationPolicy.RequireAllowed(
                GraphKind.Query,
                program,
                _handlers,
                graphId,
                nameof(GraphReturnWriter));

            Span<float> floats = stackalloc float[GraphVmLimits.MaxFloatRegisters];
            Span<int> ints = stackalloc int[GraphVmLimits.MaxIntRegisters];
            Span<byte> bools = stackalloc byte[GraphVmLimits.MaxBoolRegisters];
            Span<Entity> entities = stackalloc Entity[GraphVmLimits.MaxEntityRegisters];
            Span<Entity> targets = stackalloc Entity[GraphVmLimits.MaxTargets];
            Span<int> intIds = stackalloc int[GraphVmLimits.MaxIntIds];
            Span<int> callStack = stackalloc int[GraphVmLimits.MaxCallStackDepth];
            GraphFrame frame = GraphFrame.Bind(
                GraphKind.Query,
                GraphEntityPreset.TargetContext(targetContext),
                _world,
                caster,
                explicitTarget,
                targetPosCm,
                api ?? throw new ArgumentNullException(nameof(api)),
                _programs,
                floats,
                ints,
                bools,
                entities,
                targets,
                intIds,
                callStack,
                randomSeed: randomSeed,
                subjectIntId: subjectIntId);
            GraphExecutor.Execute(ref frame, program, programAlreadyValidated: true);
        }

        private void WriteOutputs(
            Entity owner,
            Entity caster,
            Entity explicitTarget,
            Entity targetContext,
            GraphOutputSchema schema,
            ref GraphFrame state)
        {
            GraphOutputBinding[] bindings = schema.Bindings;
            for (int i = 0; i < bindings.Length; i++)
            {
                GraphOutputBinding binding = bindings[i];
                switch (binding.Destination)
                {
                    case GraphOutputDestinationKind.Summary:
                        WriteSummary(owner, binding, ref state);
                        break;
                    default:
                        if (GraphOutputDestinationKinds.IsEntityBagDestination(binding.Destination))
                        {
                            WriteEntityCollection(owner, caster, explicitTarget, targetContext, binding, ref state);
                            break;
                        }

                        if (GraphOutputDestinationKinds.IsIntIdBagDestination(binding.Destination))
                        {
                            WriteIntIdCollection(owner, binding, ref state);
                            break;
                        }

                        throw new InvalidOperationException($"Unsupported graph output destination '{binding.Destination}' for output '{binding.Id}'.");
                }
            }
        }

        private void WriteEntityCollection(
            Entity owner,
            Entity caster,
            Entity explicitTarget,
            Entity targetContext,
            in GraphOutputBinding binding,
            ref GraphFrame state)
        {
            if (binding.ValueKind != GraphOutputValueKind.TargetList)
            {
                throw new InvalidOperationException($"Graph output '{binding.Id}' writes an entity collection but its value kind is '{binding.ValueKind}'.");
            }

            if (string.IsNullOrWhiteSpace(binding.CollectionKey))
            {
                throw new InvalidOperationException($"Graph collection output '{binding.Id}' requires collectionKey.");
            }

            Entity context = targetContext != Entity.Null ? targetContext : explicitTarget;
            var descriptor = EntityCollectionDescriptor.Create(
                binding.CollectionKey,
                EntityCollectionSourceKind.GasGraphResult,
                binding.CollectionRole,
                contextEntity: context,
                primaryEntity: caster,
                title: binding.Title,
                summary: binding.Summary);
            if (binding.CollectionKeyId > 0)
            {
                _collections.Replace(owner, binding.CollectionKeyId, descriptor, state.TargetList.Span);
                return;
            }

            _collections.Replace(owner, descriptor, state.TargetList.Span);
        }

        private void WriteIntIdCollection(
            Entity owner,
            in GraphOutputBinding binding,
            ref GraphFrame state)
        {
            if (binding.ValueKind != GraphOutputValueKind.IntIdList)
            {
                throw new InvalidOperationException($"Graph output '{binding.Id}' writes an int-id collection but its value kind is '{binding.ValueKind}'.");
            }

            if (string.IsNullOrWhiteSpace(binding.CollectionKey))
            {
                throw new InvalidOperationException($"Graph collection output '{binding.Id}' requires collectionKey.");
            }

            var descriptor = IntIdCollectionDescriptor.Create(
                binding.CollectionKey,
                EntityCollectionSourceKind.GasGraphResult,
                binding.CollectionRole,
                title: binding.Title,
                summary: binding.Summary);
            if (binding.CollectionKeyId > 0)
            {
                _intIdCollections.Replace(owner, binding.CollectionKeyId, descriptor, state.IntIdList.Span);
                return;
            }

            _intIdCollections.Replace(owner, descriptor, state.IntIdList.Span);
        }

        private void WriteSummary(Entity owner, in GraphOutputBinding binding, ref GraphFrame state)
        {
            if (binding.KeyId <= 0)
            {
                throw new InvalidOperationException($"Graph summary output '{binding.Id}' requires a resolved key id.");
            }

            switch (binding.ValueKind)
            {
                case GraphOutputValueKind.Bool:
                    _values.SetBool(owner, binding.KeyId, state.B[binding.Register] != 0);
                    break;
                case GraphOutputValueKind.Int:
                    _values.SetInt(owner, binding.KeyId, state.I[binding.Register]);
                    break;
                case GraphOutputValueKind.Float:
                    _values.SetFloat(owner, binding.KeyId, state.F[binding.Register]);
                    break;
                case GraphOutputValueKind.Entity:
                    _values.SetEntity(owner, binding.KeyId, state.E[binding.Register]);
                    break;
                default:
                    throw new InvalidOperationException($"Graph summary output '{binding.Id}' cannot write value kind '{binding.ValueKind}'.");
            }
        }
    }
}

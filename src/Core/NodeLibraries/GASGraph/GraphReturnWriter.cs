using System;
using Arch.Core;
using Ludots.Core.EntityCollections;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Mathematics;

namespace Ludots.Core.NodeLibraries.GASGraph
{
    public sealed class GraphReturnWriter
    {
        private readonly World _world;
        private readonly GraphProgramRegistry _programs;
        private readonly GraphOutputSchemaRegistry _schemas;
        private readonly GasGraphOpHandlerTable _handlers;
        private readonly EntityCollectionStore _collections;
        private readonly GraphOutputValueStore _values;

        public GraphReturnWriter(
            World world,
            GraphProgramRegistry programs,
            GraphOutputSchemaRegistry schemas,
            GasGraphOpHandlerTable handlers,
            EntityCollectionStore collections,
            GraphOutputValueStore values)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _programs = programs ?? throw new ArgumentNullException(nameof(programs));
            _schemas = schemas ?? throw new ArgumentNullException(nameof(schemas));
            _handlers = handlers ?? throw new ArgumentNullException(nameof(handlers));
            _collections = collections ?? throw new ArgumentNullException(nameof(collections));
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
            IGraphRuntimeApi api)
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

            GraphOutputSchema schema = _schemas.Get(graphId);
            if (!schema.HasBindings)
            {
                throw new InvalidOperationException($"Graph program id {graphId} has no output schema.");
            }

            Entity resolvedOwner = owner == Entity.Null ? caster : owner;
            if (resolvedOwner == Entity.Null)
            {
                throw new InvalidOperationException("Graph return writer requires an owner or caster entity for materialized outputs.");
            }

            Span<float> floats = stackalloc float[GraphVmLimits.MaxFloatRegisters];
            Span<int> ints = stackalloc int[GraphVmLimits.MaxIntRegisters];
            Span<byte> bools = stackalloc byte[GraphVmLimits.MaxBoolRegisters];
            Span<Entity> entities = stackalloc Entity[GraphVmLimits.MaxEntityRegisters];
            Span<Entity> targets = stackalloc Entity[GraphVmLimits.MaxTargets];
            Span<int> callStack = stackalloc int[GraphVmLimits.MaxCallStackDepth];
            var targetList = new GraphTargetList(targets);

            var state = new GraphExecutionState
            {
                World = _world,
                Caster = caster,
                ExplicitTarget = explicitTarget,
                TargetContext = targetContext,
                TargetPosCm = targetPosCm,
                RandomSeed = randomSeed,
                Api = api ?? throw new ArgumentNullException(nameof(api)),
                F = floats,
                I = ints,
                B = bools,
                E = entities,
                Targets = targets,
                TargetList = targetList,
                CallStack = callStack,
                CallStackCount = 0,
            };

            GasGraphOpHandlerTable.Execute(ref state, program, _handlers);
            WriteOutputs(resolvedOwner, caster, explicitTarget, targetContext, schema, ref state);
        }

        private void WriteOutputs(
            Entity owner,
            Entity caster,
            Entity explicitTarget,
            Entity targetContext,
            GraphOutputSchema schema,
            ref GraphExecutionState state)
        {
            GraphOutputBinding[] bindings = schema.Bindings;
            for (int i = 0; i < bindings.Length; i++)
            {
                GraphOutputBinding binding = bindings[i];
                switch (binding.Destination)
                {
                    case GraphOutputDestinationKind.EntityCollection:
                        WriteCollection(owner, caster, explicitTarget, targetContext, binding, ref state);
                        break;
                    case GraphOutputDestinationKind.Summary:
                        WriteSummary(owner, binding, ref state);
                        break;
                    default:
                        throw new InvalidOperationException($"Unsupported graph output destination '{binding.Destination}' for output '{binding.Id}'.");
                }
            }
        }

        private void WriteCollection(
            Entity owner,
            Entity caster,
            Entity explicitTarget,
            Entity targetContext,
            in GraphOutputBinding binding,
            ref GraphExecutionState state)
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
            _collections.Replace(owner, descriptor, state.TargetList.Span);
        }

        private void WriteSummary(Entity owner, in GraphOutputBinding binding, ref GraphExecutionState state)
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

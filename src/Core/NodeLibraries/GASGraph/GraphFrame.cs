using System;
using Arch.Core;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Map;
using Ludots.Core.Mathematics;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.NodeLibraries.GASGraph
{
    public enum GraphEntityPresetKind : byte
    {
        None = 0,
        TargetContext = 1,
        Viewer = 2,
        PreviewTarget = 3
    }

    public readonly struct GraphEntityPreset
    {
        public GraphEntityPreset(GraphEntityPresetKind kind, Entity entity)
        {
            if (kind != GraphEntityPresetKind.None &&
                kind is not (GraphEntityPresetKind.TargetContext or GraphEntityPresetKind.Viewer or GraphEntityPresetKind.PreviewTarget))
            {
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "Graph entity preset kind is not supported.");
            }

            Kind = kind;
            Entity = entity;
        }

        public GraphEntityPresetKind Kind { get; }
        public Entity Entity { get; }

        public static GraphEntityPreset None => default;

        public static GraphEntityPreset TargetContext(Entity entity)
            => new(GraphEntityPresetKind.TargetContext, entity);

        public static GraphEntityPreset Viewer(Entity entity)
            => new(GraphEntityPresetKind.Viewer, entity);

        public static GraphEntityPreset PreviewTarget(Entity entity)
            => new(GraphEntityPresetKind.PreviewTarget, entity);
    }

    public ref struct GraphFrame
    {
        public GraphKind Kind;
        public GraphEntityPreset Slot2;
        public World? World;
        public Entity Caster;
        public Entity ExplicitTarget;
        public Entity TargetContext;
        public Entity Viewer;
        public IntVector2 TargetPosCm;
        public uint RandomSeed;
        public GraphEventPayload EventPayload;
        public IGraphRuntimeApi? Api;
        public GraphProgramRegistry? Programs;
        public Span<float> F;
        public Span<int> I;
        public Span<byte> B;
        public Span<Entity> E;
        public Span<Entity> Targets;
        public GraphTargetList TargetList;
        public Span<int> IntIds;
        public GraphIntIdList IntIdList;
        public int SubjectIntId;
        public Span<int> CallStack;
        public GraphTextHeap Text;
        public GraphExecutionCursor Cursor;
        public GraphDebugTrace? DebugTrace;
        public int GraphId;
        public MapId? MapScope;
        public GraphEntryPayloadTable? EntryPayload;
        public GraphEntryPayloadTable? InvokeArgs;

        public static GraphFrame Bind(
            GraphKind kind,
            GraphEntityPreset slot2,
            World? world,
            Entity caster,
            Entity explicitTarget,
            IntVector2 targetPosCm,
            IGraphRuntimeApi? api,
            GraphProgramRegistry? programs,
            Span<float> floats,
            Span<int> ints,
            Span<byte> bools,
            Span<Entity> entities,
            Span<Entity> targets,
            Span<int> intIds,
            Span<int> callStack,
            GraphExecutionCursor cursor = default,
            uint randomSeed = 0,
            GraphEventPayload eventPayload = default,
            GraphDebugTrace? debugTrace = null,
            MapId? mapScope = null,
            GraphEntryPayloadTable? entryPayload = null,
            GraphEntryPayloadTable? invokeArgs = null,
            int subjectIntId = 0)
        {
            if (kind is not (GraphKind.Effect or GraphKind.Query or GraphKind.Score or GraphKind.Validation or GraphKind.Derived or GraphKind.Script))
            {
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "Graph frame requires an explicit supported kind.");
            }

            if (floats.Length < GraphVmLimits.MaxFloatRegisters ||
                ints.Length < GraphVmLimits.MaxIntRegisters ||
                bools.Length < GraphVmLimits.MaxBoolRegisters ||
                entities.Length < GraphVmLimits.MaxEntityRegisters ||
                targets.Length < GraphVmLimits.MaxTargets ||
                intIds.Length < GraphVmLimits.MaxIntIds ||
                callStack.Length < GraphVmLimits.MaxCallStackDepth)
            {
                throw new ArgumentException("Graph frame register/call-stack spans are smaller than GraphVmLimits.");
            }

            entities[0] = caster;
            entities[1] = explicitTarget;
            entities[2] = slot2.Entity;

            Entity targetContext = default;
            Entity viewer = default;
            switch (slot2.Kind)
            {
                case GraphEntityPresetKind.None:
                    break;
                case GraphEntityPresetKind.TargetContext:
                case GraphEntityPresetKind.PreviewTarget:
                    targetContext = slot2.Entity;
                    break;
                case GraphEntityPresetKind.Viewer:
                    viewer = slot2.Entity;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(slot2), slot2.Kind, "Graph frame E[2] preset is not supported.");
            }

            return new GraphFrame
            {
                Kind = kind,
                Slot2 = slot2,
                World = world,
                Caster = caster,
                ExplicitTarget = explicitTarget,
                TargetContext = targetContext,
                Viewer = viewer,
                TargetPosCm = targetPosCm,
                RandomSeed = randomSeed,
                EventPayload = eventPayload,
                Api = api,
                Programs = programs,
                F = floats,
                I = ints,
                B = bools,
                E = entities,
                Targets = targets,
                TargetList = new GraphTargetList(targets),
                IntIds = intIds,
                IntIdList = new GraphIntIdList(intIds),
                SubjectIntId = subjectIntId,
                CallStack = callStack,
                Text = GraphTextHeap.ForCurrentThread(),
                Cursor = cursor,
                DebugTrace = debugTrace,
                GraphId = 0,
                MapScope = mapScope,
                EntryPayload = entryPayload,
                InvokeArgs = invokeArgs
            };
        }

        internal GraphExecutionState CreateState()
        {
            return new GraphExecutionState
            {
                World = World!,
                Caster = Caster,
                ExplicitTarget = ExplicitTarget,
                TargetContext = TargetContext,
                Viewer = Viewer,
                EventPayload = EventPayload,
                TargetPosCm = TargetPosCm,
                RandomSeed = RandomSeed,
                Api = Api!,
                Programs = Programs,
                F = F,
                I = I,
                B = B,
                E = E,
                Targets = Targets,
                TargetList = TargetList,
                IntIds = IntIds,
                IntIdList = IntIdList,
                SubjectIntId = SubjectIntId,
                CallStack = CallStack,
                Text = Text ?? throw new InvalidOperationException("Graph frame requires a GraphTextHeap."),
                CallStackCount = Cursor.CallStackCount,
                ReturnInt = Cursor.ReturnInt,
                InvokeDepth = Cursor.InvokeDepth,
                Status = GraphExecutionStatus.Running,
                CurrentGraphId = GraphId,
                DebugTrace = DebugTrace,
                MapScope = MapScope,
                EntryPayload = EntryPayload,
                InvokeArgs = InvokeArgs
            };
        }
    }
}

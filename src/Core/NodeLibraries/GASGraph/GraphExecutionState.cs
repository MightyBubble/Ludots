using System;
using Arch.Core;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Mathematics;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.NodeLibraries.GASGraph
{
    /// <summary>
    /// Event payload slots exposed to graph programs via LoadEventPayloadInt/Float
    /// (RFC-0065 PROV-4b). Filled by presentation-event driven executors
    /// (e.g. PresenterRuleSystem); defaults to zero elsewhere.
    /// </summary>
    public struct GraphEventPayload
    {
        public int PayloadA;
        public int PayloadB;
        public float FloatA;
        public float FloatB;
        public float FloatC;
        public float FloatD;
    }

    /// <summary>
    /// Per-execution state for the GAS Graph VM.
    /// Passed by ref to each opcode handler.
    /// </summary>
    public ref struct GraphExecutionState
    {
        public World World;
        public Entity Caster;
        public Entity ExplicitTarget;
        /// <summary>
        /// Additional context entity (e.g. AOE center, original target for chained effects).
        /// Set from EffectContext.TargetContext when executing phase graphs.
        /// </summary>
        public Entity TargetContext;
        /// <summary>
        /// Viewer anchor entity for viewer-relative condition graphs (RFC-0065 DEC-12 #1).
        /// Set from PresentationEvent.Viewer by PresenterRuleSystem; Entity.Null elsewhere.
        /// </summary>
        public Entity Viewer;
        /// <summary>Event payload slots read by LoadEventPayloadInt/Float ops.</summary>
        public GraphEventPayload EventPayload;
        /// <summary>Target position in world centimeters.</summary>
        public IntVector2 TargetPosCm;
        public uint RandomSeed;
        public IGraphRuntimeApi Api;
        public GraphProgramRegistry? Programs;
        public Span<float> F;
        public Span<int> I;
        public Span<byte> B;
        public Span<Entity> E;
        public Span<Entity> Targets;
        public GraphTargetList TargetList;
        public Span<int> CallStack;
        public int CallStackCount;
        public int ReturnInt;
        public GraphExecutionStatus Status;
        /// <summary>Set by the executor for the active program; used by Call absolute-target checks.</summary>
        public int ProgramLength;
        public int InvokeDepth;
        public int TreeSteps;
    }
}

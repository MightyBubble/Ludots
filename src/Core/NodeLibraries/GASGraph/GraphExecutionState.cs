using System;
using Arch.Core;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Mathematics;

namespace Ludots.Core.NodeLibraries.GASGraph
{
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
        public IntVector2 TargetPos;
        public IGraphRuntimeApi Api;
        public Span<float> F;
        public Span<int> I;
        public Span<byte> B;
        public Span<Entity> E;
        public Span<Entity> Targets;
        public GraphTargetList TargetList;
    }
}

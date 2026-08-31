using System;
using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Map;
using Ludots.Core.Mathematics;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Gameplay.Story
{
    /// <summary>
    /// Dialogue/Sequencer 对 Graph 的唯一调用门：条件走 Query，副作用走 TriggerGraph。
    /// TriggerGraph 以与挂载路径相同的 Script 切片宿主执行；对话/信号提交要求单切片 Halt。
    /// </summary>
    public sealed class StoryGraphInvoker
    {
        private readonly GameEngine _engine;
        private readonly int[] _ints = new int[GraphVmLimits.MaxIntRegisters];
        private readonly byte[] _bools = new byte[GraphVmLimits.MaxBoolRegisters];
        private readonly float[] _floats = new float[GraphVmLimits.MaxFloatRegisters];
        private readonly Entity[] _entities = new Entity[GraphVmLimits.MaxEntityRegisters];
        private readonly Entity[] _targets = new Entity[GraphVmLimits.MaxTargets];
        private readonly int[] _callStack = new int[GraphVmLimits.MaxCallStackDepth];

        public StoryGraphInvoker(GameEngine engine)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        }

        public bool EvaluateCondition(string conditionGraphId, Entity subject)
        {
            if (string.IsNullOrWhiteSpace(conditionGraphId))
            {
                return true;
            }

            GraphProgramRegistry programs = RequirePrograms();
            IGraphRuntimeApi api = RequireApi();
            int graphId = ResolveGraphId(conditionGraphId);
            GraphInstruction[] program = programs.RequireProgramArray(graphId, GraphKind.Query, "Story condition");
            GraphKindOperationPolicy.RequireAllowed(GraphKind.Query, program, GasGraphOpHandlerTable.Instance, graphId, nameof(StoryGraphInvoker));

            Array.Clear(_floats, 0, _floats.Length);
            Array.Clear(_ints, 0, _ints.Length);
            Array.Clear(_bools, 0, _bools.Length);
            Array.Clear(_entities, 0, _entities.Length);
            Array.Clear(_targets, 0, _targets.Length);
            Array.Clear(_callStack, 0, _callStack.Length);

            MapId? mapScope = _engine.CurrentMapSession?.MapId;
            Span<int> intIds = stackalloc int[GraphVmLimits.MaxIntIds];
            GraphFrame frame = GraphFrame.Bind(
                GraphKind.Query,
                GraphEntityPreset.None,
                _engine.World,
                subject,
                subject,
                IntVector2.Zero,
                api,
                programs,
                _floats,
                _ints,
                _bools,
                _entities,
                _targets,
                intIds,
                _callStack,
                mapScope: mapScope);
            GraphExecutor.Execute(ref frame, program, programAlreadyValidated: true);
            return frame.Cursor.ReturnInt != 0 || frame.B[0] != 0;
        }

        public void ExecuteAction(string actionGraphId, Entity subject)
        {
            if (string.IsNullOrWhiteSpace(actionGraphId))
            {
                return;
            }

            GraphProgramRegistry programs = RequirePrograms();
            IGraphRuntimeApi api = RequireApi();
            int graphId = ResolveGraphId(actionGraphId);
            GraphInstruction[] program = programs.RequireProgramArray(graphId, GraphKind.TriggerGraph, "Story action");
            GraphKindOperationPolicy.RequireAllowed(GraphKind.TriggerGraph, program, GasGraphOpHandlerTable.Instance, graphId, nameof(StoryGraphInvoker));

            Array.Clear(_floats, 0, _floats.Length);
            Array.Clear(_ints, 0, _ints.Length);
            Array.Clear(_bools, 0, _bools.Length);
            Array.Clear(_entities, 0, _entities.Length);
            Array.Clear(_targets, 0, _targets.Length);
            Array.Clear(_callStack, 0, _callStack.Length);

            var cursor = new GraphExecutionCursor();
            MapId? mapScope = _engine.CurrentMapSession?.MapId;
            GraphSliceResult result = GraphExecutor.ExecuteScriptSlice(
                _engine.World,
                subject,
                subject,
                IntVector2.Zero,
                program,
                api,
                programs,
                _floats,
                _ints,
                _bools,
                _entities,
                _targets,
                _callStack,
                ref cursor,
                GraphVmLimits.MaxInstructionsPerExecution,
                GraphKind.TriggerGraph,
                mapScope: mapScope);

            if (!result.Halted)
            {
                throw new InvalidOperationException(
                    $"Story action TriggerGraph '{actionGraphId}' must Halt in one slice during Dialogue/Sequencer commit; Yield/budget suspend is not allowed here.");
            }
        }

        private static int ResolveGraphId(string graphName)
        {
            int graphId = GraphIdRegistry.GetId(graphName);
            if (graphId <= 0)
            {
                throw new InvalidOperationException(
                    $"Graph '{graphName}' is not registered. Story Dialogue/Sequencer only references registered Query/TriggerGraph programs.");
            }

            return graphId;
        }

        private GraphProgramRegistry RequirePrograms()
        {
            return _engine.GetService(CoreServiceKeys.GraphProgramRegistry)
                ?? throw new InvalidOperationException("StoryGraphInvoker requires GraphProgramRegistry.");
        }

        private IGraphRuntimeApi RequireApi()
        {
            return _engine.GetService(CoreServiceKeys.GasGraphRuntimeApi)
                ?? throw new InvalidOperationException("StoryGraphInvoker requires GasGraphRuntimeApi.");
        }
    }
}

using System;
using System.Threading.Tasks;
using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Scripting;

namespace Ludots.Core.Gameplay.MapTriggers
{
    public sealed class MapTriggerGraphMountTrigger : Trigger
    {
        private readonly int _graphId;
        private readonly string _graphName;
        private readonly MapTriggerGraphEntry _entry;
        private readonly Entity _scope;
        private readonly int[] _vmIntRegisters = new int[GraphVmLimits.MaxIntRegisters];
        private readonly byte[] _vmBoolRegisters = new byte[GraphVmLimits.MaxBoolRegisters];
        private readonly int[] _vmCallStack = new int[GraphVmLimits.MaxCallStackDepth];
        private bool _ranToHaltOnce;

        public MapTriggerGraphMountTrigger(int graphId, string graphName, MapTriggerGraphEntry entry, Entity scope)
        {
            if (graphId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(graphId));
            }

            if (string.IsNullOrWhiteSpace(graphName))
            {
                throw new ArgumentException("Graph name is required.", nameof(graphName));
            }

            if (entry.EventName == null || string.IsNullOrWhiteSpace(entry.EventName))
            {
                throw new ArgumentException(
                    $"MapTriggerGraph '{graphName}' entry '{entry.Label}' requires a non-empty event name.",
                    nameof(entry));
            }

            _graphId = graphId;
            _graphName = graphName;
            _entry = entry;
            _scope = scope;
            EventKey = new EventKey(entry.EventName);
            Priority = 0;
        }

        public override string Name => $"MapTriggerGraph:{_graphName}:{_entry.Label}";

        public GraphSliceResult LastSliceResult { get; private set; }

        public override bool CheckConditions(ScriptContext context)
        {
            if (_entry.Once && _ranToHaltOnce)
            {
                return false;
            }

            return base.CheckConditions(context);
        }

        public override Task ExecuteAsync(ScriptContext context)
        {
            if (!CheckConditions(context))
            {
                return Task.CompletedTask;
            }

            MapTriggerGraphTriggerDependencies dependencies = ResolveDependencies(context);
            GraphInstruction[] program = dependencies.Programs.RequireProgramArray(
                _graphId,
                GraphKind.MapTrigger,
                "地图触发器挂载");

            ResetExecutionState();
            var cursor = new GraphExecutionCursor(_entry.StartPc);
            GraphSliceResult result = GraphExecutor.ExecuteResolvedRegisteredScriptSlice(
                dependencies.Programs,
                program,
                _vmIntRegisters,
                _vmBoolRegisters,
                _vmCallStack,
                ref cursor,
                budgetSteps: MapTriggerGraphLimits.SliceBudgetSteps,
                world: dependencies.Engine.World,
                caster: _scope,
                explicitTarget: _scope,
                api: dependencies.GraphApi);
            LastSliceResult = result;
            if (!result.Halted)
            {
                throw new InvalidOperationException(
                    $"MapTriggerGraph '{_graphName}' entry '{_entry.Label}' must halt in one slice (got {result.Status}).");
            }

            _ranToHaltOnce = true;
            return Task.CompletedTask;
        }

        private static MapTriggerGraphTriggerDependencies ResolveDependencies(ScriptContext context)
        {
            GameEngine engine = context.Get(CoreServiceKeys.Engine)
                ?? throw new InvalidOperationException($"{nameof(MapTriggerGraphMountTrigger)} requires GameEngine.");
            GraphProgramRegistry programs = engine.GetService(CoreServiceKeys.GraphProgramRegistry)
                ?? throw new InvalidOperationException($"{nameof(MapTriggerGraphMountTrigger)} requires GraphProgramRegistry.");
            GasGraphRuntimeApi graphApi = engine.GetService(CoreServiceKeys.GasGraphRuntimeApi)
                ?? throw new InvalidOperationException($"{nameof(MapTriggerGraphMountTrigger)} requires GasGraphRuntimeApi.");

            return new MapTriggerGraphTriggerDependencies(engine, programs, graphApi);
        }

        private void ResetExecutionState()
        {
            Array.Clear(_vmIntRegisters, 0, _vmIntRegisters.Length);
            Array.Clear(_vmBoolRegisters, 0, _vmBoolRegisters.Length);
            Array.Clear(_vmCallStack, 0, _vmCallStack.Length);
        }

        private readonly record struct MapTriggerGraphTriggerDependencies(
            GameEngine Engine,
            GraphProgramRegistry Programs,
            GasGraphRuntimeApi GraphApi);
    }
}

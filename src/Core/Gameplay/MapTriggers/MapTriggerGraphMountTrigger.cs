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
    public enum MapTriggerGraphRefirePolicy
    {
        Ignore = 0,
        Restart = 1,
    }

    /// <summary>
    /// One mounted MapTriggerGraph entry. Entry dispatch executes one slice
    /// (MapTriggerGraphLimits.SliceBudgetSteps); a Yield or slice-budget
    /// suspension parks the run inside this instance (cursor, registers, call
    /// stack) and the map's think wave ("ThinkWaveElapsed") resumes it one
    /// slice per wave until Halt. Resume wiring: a companion
    /// MapTriggerGraphResumeTrigger is registered per entry unless the entry's
    /// EventName IS the resume event, in which case the entry's own dispatch
    /// resumes the suspended run on that tick (a wave tick on a suspended
    /// entry always resumes, never refires). A run's cumulative steps are
    /// capped by GraphVmLimits.MaxInstructionsPerExecution; a run that keeps
    /// suspending past the cap fails closed. First slice of a run seeds
    /// registers from the dispatch ScriptContext (cleared on restart,
    /// preserved across resumes): E[0] payload "MapTrigger.SourceEntity"
    /// (also the run's caster; absent falls back to the mount scope), I[0]
    /// "MapTrigger.SourceTeamId", I[1] "MapTrigger.Count", F[0]
    /// "MapTrigger.VarValueFloat". Refire while suspended: Ignore (default)
    /// drops the event and counts it in DroppedCount; Restart resets the run
    /// and re-executes from StartPc. Once entries allow one halted run per
    /// map lifetime.
    /// </summary>
    public sealed class MapTriggerGraphMountTrigger : Trigger
    {
        private readonly int _graphId;
        private readonly string _graphName;
        private readonly MapTriggerGraphEntry _entry;
        private readonly Entity _scope;
        private readonly MapTriggerGraphRefirePolicy _refirePolicy;
        private readonly bool _entryIsResumeEvent;
        private readonly int[] _vmIntRegisters = new int[GraphVmLimits.MaxIntRegisters];
        private readonly byte[] _vmBoolRegisters = new byte[GraphVmLimits.MaxBoolRegisters];
        private readonly float[] _vmFloatRegisters = new float[GraphVmLimits.MaxFloatRegisters];
        private readonly Entity[] _vmEntityRegisters = new Entity[GraphVmLimits.MaxEntityRegisters];
        private readonly Entity[] _vmTargetRegisters = new Entity[GraphVmLimits.MaxTargets];
        private readonly int[] _vmCallStack = new int[GraphVmLimits.MaxCallStackDepth];
        private GraphExecutionCursor _cursor;
        private Entity _runCaster;
        private bool _runActive;
        private bool _ranToHaltOnce;

        public MapTriggerGraphMountTrigger(
            int graphId,
            string graphName,
            MapTriggerGraphEntry entry,
            Entity scope,
            MapTriggerGraphRefirePolicy refirePolicy = MapTriggerGraphRefirePolicy.Ignore)
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

            if (!Enum.IsDefined(typeof(MapTriggerGraphRefirePolicy), refirePolicy))
            {
                throw new ArgumentOutOfRangeException(nameof(refirePolicy));
            }

            _graphId = graphId;
            _graphName = graphName;
            _entry = entry;
            _scope = scope;
            _refirePolicy = refirePolicy;
            _entryIsResumeEvent = new EventKey(entry.EventName) == GameEvents.ThinkWaveElapsed;
            _runCaster = scope;
            EventKey = new EventKey(entry.EventName);
            Priority = 0;
        }

        public override string Name => $"MapTriggerGraph:{_graphName}:{_entry.Label}";

        public GraphSliceResult LastSliceResult { get; private set; }

        public bool IsSuspended => _runActive;

        public bool EntryIsResumeEvent => _entryIsResumeEvent;

        public int DroppedCount { get; private set; }

        public override bool CheckConditions(ScriptContext context)
        {
            if (_entry.Once && _ranToHaltOnce)
            {
                return false;
            }

            if (!MapTriggerEntryFiltersEvaluator.Matches(context, _entry.Filters))
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

            if (_runActive)
            {
                if (_entryIsResumeEvent)
                {
                    RunSlice(context);
                    return Task.CompletedTask;
                }

                if (_refirePolicy == MapTriggerGraphRefirePolicy.Ignore)
                {
                    DroppedCount++;
                    return Task.CompletedTask;
                }
            }

            StartRun(context);
            return Task.CompletedTask;
        }

        internal void ResumeFromSuspension(ScriptContext context)
        {
            if (!_runActive)
            {
                return;
            }

            RunSlice(context);
        }

        private void StartRun(ScriptContext context)
        {
            ResetExecutionState();
            _cursor = new GraphExecutionCursor(_entry.StartPc);
            _runCaster = ResolveRunCaster(context);
            SeedEntryRegisters(context);
            RunSlice(context);
        }

        private void RunSlice(ScriptContext context)
        {
            MapTriggerGraphTriggerDependencies dependencies = ResolveDependencies(context);
            GraphInstruction[] program = dependencies.Programs.RequireProgramArray(
                _graphId,
                GraphKind.MapTrigger,
                "地图触发器挂载");

            GraphSliceResult result = GraphExecutor.ExecuteScriptSlice(
                dependencies.Engine.World,
                _runCaster,
                _scope,
                default,
                program,
                dependencies.GraphApi,
                dependencies.Programs,
                _vmFloatRegisters,
                _vmIntRegisters,
                _vmBoolRegisters,
                _vmEntityRegisters,
                _vmTargetRegisters,
                _vmCallStack,
                ref _cursor,
                MapTriggerGraphLimits.SliceBudgetSteps);
            LastSliceResult = result;

            if (result.Halted)
            {
                _runActive = false;
                _ranToHaltOnce = true;
                return;
            }

            _runActive = true;
            if (_cursor.Steps >= GraphVmLimits.MaxInstructionsPerExecution)
            {
                throw new InvalidOperationException(
                    $"MapTriggerGraph '{_graphName}' entry '{_entry.Label}' exceeded the per-run instruction cap "
                    + $"{nameof(GraphVmLimits.MaxInstructionsPerExecution)} ({GraphVmLimits.MaxInstructionsPerExecution} steps across resumes) without halting.");
            }
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

        private Entity ResolveRunCaster(ScriptContext context)
        {
            // ScriptContext.Get<Entity> returns default(Entity)={0,0} for absent payloads;
            // Entity.Null is {-1,-1} in this Arch build, so both must count as "absent".
            Entity source = context.Get<Entity>(MapTriggerEventPayloadKeys.SourceEntity);
            return source == Entity.Null || source == default ? _scope : source;
        }

        private void SeedEntryRegisters(ScriptContext context)
        {
            _vmEntityRegisters[0] = context.Get<Entity>(MapTriggerEventPayloadKeys.SourceEntity);
            _vmIntRegisters[0] = context.Get<int>(MapTriggerEventPayloadKeys.SourceTeamId);
            _vmIntRegisters[1] = context.Get<int>(MapTriggerEventPayloadKeys.Count);
            _vmFloatRegisters[0] = context.Get<float>(MapTriggerEventPayloadKeys.VarValueFloat);
        }

        private void ResetExecutionState()
        {
            Array.Clear(_vmIntRegisters, 0, _vmIntRegisters.Length);
            Array.Clear(_vmBoolRegisters, 0, _vmBoolRegisters.Length);
            Array.Clear(_vmFloatRegisters, 0, _vmFloatRegisters.Length);
            Array.Clear(_vmEntityRegisters, 0, _vmEntityRegisters.Length);
            Array.Clear(_vmTargetRegisters, 0, _vmTargetRegisters.Length);
            Array.Clear(_vmCallStack, 0, _vmCallStack.Length);
            _runCaster = _scope;
        }

        private readonly record struct MapTriggerGraphTriggerDependencies(
            GameEngine Engine,
            GraphProgramRegistry Programs,
            GasGraphRuntimeApi GraphApi);
    }

    /// <summary>
    /// Think-wave resume companion for one mounted entry. Dispatches only while
    /// its owner has a suspended run; a wave with nothing suspended evaluates
    /// CheckConditions false and never re-enters the graph, so a suspended run
    /// is resumed exactly once per wave by exactly one trigger.
    /// </summary>
    public sealed class MapTriggerGraphResumeTrigger : Trigger
    {
        private readonly MapTriggerGraphMountTrigger _owner;

        public MapTriggerGraphResumeTrigger(MapTriggerGraphMountTrigger owner)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            EventKey = GameEvents.ThinkWaveElapsed;
            Priority = 0;
        }

        public override string Name => $"{_owner.Name}:Resume";

        public override bool CheckConditions(ScriptContext context) => _owner.IsSuspended;

        public override Task ExecuteAsync(ScriptContext context)
        {
            _owner.ResumeFromSuspension(context);
            return Task.CompletedTask;
        }
    }
}

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
    public enum TriggerGraphRefirePolicy
    {
        Ignore = 0,
        Restart = 1,
    }

    /// <summary>
    /// One mounted TriggerGraph entry. Entry dispatch executes one slice
    /// (TriggerGraphLimits.SliceBudgetSteps); a Yield or slice-budget
    /// suspension parks the run inside this instance (cursor, registers, call
    /// stack) and the map's think wave ("MapHeartbeat") resumes it one
    /// slice per wave until Halt. Resume wiring: a companion
    /// TriggerGraphResumeTrigger is registered per entry unless the entry's
    /// EventName IS the resume event, in which case the entry's own dispatch
    /// resumes the suspended run on that tick (a wave tick on a suspended
    /// entry always resumes, never refires). A run's cumulative steps are
    /// capped by GraphVmLimits.MaxInstructionsPerExecution; a run that keeps
    /// suspending past the cap fails closed. Refire while suspended: Ignore
    /// (default) drops the event and counts it in DroppedCount; Restart resets
    /// the run and re-executes from StartPc. Once entries allow one halted run
    /// per map lifetime.
    ///
    /// Register seeding (first slice of a run; cleared on restart, preserved
    /// across resumes) — payload key strings are owned by
    /// MapTriggerEventPayloadKeys ("MapTrigger.*" keys survive the dialect
    /// rename; they are the event payload contract, not the dialect name):
    ///   E[0] = "MapTrigger.SourceEntity"   (Entity;  also the run's caster,
    ///                                       absent falls back to the mount scope)
    ///   E[1] = "MapTrigger.TargetEntity"   (Entity;  seeded only when present)
    ///   I[0] = "MapTrigger.SourceTeamId"   (int;     default 0 when absent)
    ///   I[1] = "MapTrigger.Count"          (int;     default 0 when absent)
    ///   I[2] = "MapTrigger.TagId"          (int;     seeded only when present)
    ///   F[0] = "MapTrigger.VarValueFloat"  (float;   default 0 when absent)
    ///   F[1] = "MapTrigger.Magnitude"      (float;   seeded only when present)
    ///
    /// Mount domains: map mounts behave exactly as above for every event. For
    /// entity mounts (scope = the mounted entity itself; caster = explicit
    /// target = E[0] convention = self), the lifecycle events are dispatched by
    /// the entity mount pipeline instead of the TriggerManager bus: an
    /// "EntitySpawned" entry executes immediately at mount creation (same tick
    /// as the spawn; map-domain observers of EntitySpawned keep think-wave
    /// granularity), and an "EntityDied" entry executes on the destroy tick for
    /// that entity's own mounts. Entity mounts may declare entries on any other
    /// event key; those dispatch through the map's bus registration normally. A
    /// dead entity's mounts are inert (CheckConditions false) and lazily swept
    /// at think waves by the mount pipeline.
    /// </summary>
    public sealed class TriggerGraphMountTrigger : Trigger
    {
        private const string TargetEntityPayloadKey = "MapTrigger.TargetEntity";
        private const string TagIdPayloadKey = "MapTrigger.TagId";
        private const string MagnitudePayloadKey = "MapTrigger.Magnitude";

        private readonly int _graphId;
        private readonly string _graphName;
        private readonly TriggerGraphEntry _entry;
        private readonly Entity _scope;
        private readonly TriggerGraphMountDomain _domain;
        private readonly TriggerGraphRefirePolicy _refirePolicy;
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
        private bool _lifecycleDispatch;

        public TriggerGraphMountTrigger(
            int graphId,
            string graphName,
            TriggerGraphEntry entry,
            Entity scope,
            TriggerGraphRefirePolicy refirePolicy = TriggerGraphRefirePolicy.Ignore,
            TriggerGraphMountDomain domain = TriggerGraphMountDomain.Map)
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
                    $"TriggerGraph '{graphName}' entry '{entry.Label}' requires a non-empty event name.",
                    nameof(entry));
            }

            if (!Enum.IsDefined(typeof(TriggerGraphRefirePolicy), refirePolicy))
            {
                throw new ArgumentOutOfRangeException(nameof(refirePolicy));
            }

            if (!Enum.IsDefined(typeof(TriggerGraphMountDomain), domain))
            {
                throw new ArgumentOutOfRangeException(nameof(domain));
            }

            _graphId = graphId;
            _graphName = graphName;
            _entry = entry;
            _scope = scope;
            _domain = domain;
            _refirePolicy = refirePolicy;
            _entryIsResumeEvent = new EventKey(entry.EventName) == GameEvents.MapHeartbeat;
            _runCaster = scope;
            EventKey = new EventKey(entry.EventName);
            Priority = 0;
        }

        public override string Name => $"TriggerGraph:{_graphName}:{_entry.Label}";

        public TriggerGraphMountDomain Domain => _domain;

        public GraphSliceResult LastSliceResult { get; private set; }

        public bool IsSuspended => _runActive;

        public bool EntryIsResumeEvent => _entryIsResumeEvent;

        public int DroppedCount { get; private set; }

        private bool IsSelfLifecycleEvent => EventKey == GameEvents.EntitySpawned || EventKey == GameEvents.EntityDied;

        public override bool CheckConditions(ScriptContext context)
        {
            if (_entry.Once && _ranToHaltOnce)
            {
                return false;
            }

            if (_domain == TriggerGraphMountDomain.Entity && !_lifecycleDispatch && IsSelfLifecycleEvent)
            {
                // Entity-domain lifecycle entries never ride the think-wave bus
                // broadcasts; the mount pipeline dispatches them at spawn/destroy ticks.
                return false;
            }

            if (!IsScopeDispatchable(context))
            {
                return false;
            }

            if (!TriggerGraphEntryFiltersEvaluator.Matches(context, _entry.Filters))
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

                if (_refirePolicy == TriggerGraphRefirePolicy.Ignore)
                {
                    DroppedCount++;
                    return Task.CompletedTask;
                }
            }

            StartRun(context);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Mount-pipeline dispatch for entity-domain lifecycle events. The lifecycle
        /// marker relaxes exactly two guards: the think-wave exclusion of entity-domain
        /// EntitySpawned/EntityDied entries and the dead-scope inertness check (the
        /// destroy-tick dispatch happens while the scope entity is being destroyed).
        /// </summary>
        internal Task ExecuteLifecycleDispatch(ScriptContext context)
        {
            _lifecycleDispatch = true;
            try
            {
                return ExecuteAsync(context);
            }
            finally
            {
                _lifecycleDispatch = false;
            }
        }

        internal void ResumeFromSuspension(ScriptContext context)
        {
            if (!_runActive)
            {
                return;
            }

            RunSlice(context);
        }

        internal bool IsScopeDispatchable(ScriptContext context)
        {
            if (_lifecycleDispatch)
            {
                return true;
            }

            if (_domain != TriggerGraphMountDomain.Entity)
            {
                return true;
            }

            if (_scope == Entity.Null || _scope == default)
            {
                return false;
            }

            GameEngine engine = context.Get(CoreServiceKeys.Engine);
            return engine != null && engine.World.IsAlive(_scope);
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
            TriggerGraphTriggerDependencies dependencies = ResolveDependencies(context);
            GraphInstruction[] program = dependencies.Programs.RequireProgramArray(
                _graphId,
                GraphKind.TriggerGraph,
                "触发器图挂载");

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
                TriggerGraphLimits.SliceBudgetSteps);
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
                    $"TriggerGraph '{_graphName}' entry '{_entry.Label}' exceeded the per-run instruction cap "
                    + $"{nameof(GraphVmLimits.MaxInstructionsPerExecution)} ({GraphVmLimits.MaxInstructionsPerExecution} steps across resumes) without halting.");
            }
        }

        private static TriggerGraphTriggerDependencies ResolveDependencies(ScriptContext context)
        {
            GameEngine engine = context.Get(CoreServiceKeys.Engine)
                ?? throw new InvalidOperationException($"{nameof(TriggerGraphMountTrigger)} requires GameEngine.");
            GraphProgramRegistry programs = engine.GetService(CoreServiceKeys.GraphProgramRegistry)
                ?? throw new InvalidOperationException($"{nameof(TriggerGraphMountTrigger)} requires GraphProgramRegistry.");
            GasGraphRuntimeApi graphApi = engine.GetService(CoreServiceKeys.GasGraphRuntimeApi)
                ?? throw new InvalidOperationException($"{nameof(TriggerGraphMountTrigger)} requires GasGraphRuntimeApi.");

            return new TriggerGraphTriggerDependencies(engine, programs, graphApi);
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
            if (context.Contains(TargetEntityPayloadKey))
            {
                _vmEntityRegisters[1] = context.Get<Entity>(TargetEntityPayloadKey);
            }

            if (context.Contains(TagIdPayloadKey))
            {
                _vmIntRegisters[2] = context.Get<int>(TagIdPayloadKey);
            }

            if (context.Contains(MagnitudePayloadKey))
            {
                _vmFloatRegisters[1] = context.Get<float>(MagnitudePayloadKey);
            }
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

        private readonly record struct TriggerGraphTriggerDependencies(
            GameEngine Engine,
            GraphProgramRegistry Programs,
            GasGraphRuntimeApi GraphApi);
    }

    /// <summary>
    /// Think-wave resume companion for one mounted entry. Dispatches only while
    /// its owner has a suspended run; a wave with nothing suspended evaluates
    /// CheckConditions false and never re-enters the graph, so a suspended run
    /// is resumed exactly once per wave by exactly one trigger. Entity-domain
    /// owners whose scope entity died stay parked forever (the dead mount is
    /// swept by the entity mount pipeline instead).
    /// </summary>
    public sealed class TriggerGraphResumeTrigger : Trigger
    {
        private readonly TriggerGraphMountTrigger _owner;

        public TriggerGraphResumeTrigger(TriggerGraphMountTrigger owner)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            EventKey = GameEvents.MapHeartbeat;
            Priority = 0;
        }

        public override string Name => $"{_owner.Name}:Resume";

        public override bool CheckConditions(ScriptContext context)
            => _owner.IsSuspended && _owner.IsScopeDispatchable(context);

        public override Task ExecuteAsync(ScriptContext context)
        {
            _owner.ResumeFromSuspension(context);
            return Task.CompletedTask;
        }
    }
}

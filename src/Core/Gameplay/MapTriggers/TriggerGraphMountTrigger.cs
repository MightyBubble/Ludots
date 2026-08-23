using System;
using System.Threading.Tasks;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Engine;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Map;
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
    /// event key; those dispatch through the map's bus registration normally and
    /// are filtered to the mounted entity's source/target. A scope carrying
    /// EntityTriggerGraphAggregateRoot also accepts attached descendants. A dead
    /// entity's mounts are inert (CheckConditions false) and lazily swept at think
    /// waves by the mount pipeline.
    /// </summary>
    public sealed class TriggerGraphMountTrigger : Trigger, IMapTriggerRoute
    {
        private const string TargetEntityPayloadKey = "MapTrigger.TargetEntity";
        private const string TagIdPayloadKey = "MapTrigger.TagId";
        private const string MagnitudePayloadKey = "MapTrigger.Magnitude";
        private const string ModIdPayloadKey = MapTriggerEventPayloadKeys.ModId;

        private readonly int _graphId;
        private readonly string _graphName;
        private readonly TriggerGraphEntry _entry;
        private readonly Entity _scope;
        private readonly TriggerGraphMountDomain _domain;
        private readonly TriggerGraphMountRoute _route;
        private readonly int _abilityIdFilter;
        private readonly string? _modIdFilter;
        private readonly TriggerGraphRefirePolicy _refirePolicy;
        private readonly bool _entryIsResumeEvent;
        private readonly int[] _vmIntRegisters = new int[GraphVmLimits.MaxIntRegisters];
        private readonly int[] _previousIntRegisters = new int[GraphVmLimits.MaxIntRegisters];
        private readonly byte[] _vmBoolRegisters = new byte[GraphVmLimits.MaxBoolRegisters];
        private readonly byte[] _previousBoolRegisters = new byte[GraphVmLimits.MaxBoolRegisters];
        private readonly float[] _vmFloatRegisters = new float[GraphVmLimits.MaxFloatRegisters];
        private readonly float[] _previousFloatRegisters = new float[GraphVmLimits.MaxFloatRegisters];
        private readonly Entity[] _vmEntityRegisters = new Entity[GraphVmLimits.MaxEntityRegisters];
        private readonly Entity[] _previousEntityRegisters = new Entity[GraphVmLimits.MaxEntityRegisters];
        private readonly Entity[] _vmTargetRegisters = new Entity[GraphVmLimits.MaxTargets];
        private readonly int[] _vmCallStack = new int[GraphVmLimits.MaxCallStackDepth];
        private readonly GraphDebugTrace _debugTrace = new();
        private GraphExecutionCursor _cursor;
        private Entity _runCaster;
        private MapId? _mapScope;
        private bool _mapScopeResolved;
        private bool _runActive;
        private bool _ranToHaltOnce;
        private bool _lifecycleDispatch;

        public TriggerGraphMountTrigger(
            int graphId,
            string graphName,
            TriggerGraphEntry entry,
            Entity scope,
            TriggerGraphRefirePolicy refirePolicy = TriggerGraphRefirePolicy.Ignore,
            TriggerGraphMountDomain domain = TriggerGraphMountDomain.Map,
            TriggerGraphMountRoute route = TriggerGraphMountRoute.Local,
            int abilityIdFilter = 0,
            string? modIdFilter = null)
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

            if (!Enum.IsDefined(typeof(TriggerGraphMountRoute), route))
            {
                throw new ArgumentOutOfRangeException(nameof(route));
            }

            if (domain == TriggerGraphMountDomain.Ability && abilityIdFilter <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(abilityIdFilter), "Ability-domain mounts require a positive ability id filter.");
            }

            if (domain != TriggerGraphMountDomain.Ability && abilityIdFilter != 0)
            {
                throw new ArgumentException("Only ability-domain mounts may specify an ability id filter.", nameof(abilityIdFilter));
            }

            if (domain == TriggerGraphMountDomain.Mod && string.IsNullOrWhiteSpace(modIdFilter))
            {
                throw new ArgumentException("Mod-domain mounts require an owning mod id.", nameof(modIdFilter));
            }

            if (domain != TriggerGraphMountDomain.Mod && modIdFilter != null)
            {
                throw new ArgumentException("Only mod-domain mounts may specify an owning mod id.", nameof(modIdFilter));
            }

            _graphId = graphId;
            _graphName = graphName;
            _entry = entry;
            _scope = scope;
            _domain = domain;
            _route = route;
            _abilityIdFilter = abilityIdFilter;
            _modIdFilter = modIdFilter;
            _refirePolicy = refirePolicy;
            _entryIsResumeEvent = new EventKey(entry.EventName) == GameEvents.MapHeartbeat;
            _runCaster = scope;
            EventKey = new EventKey(entry.EventName);
            Priority = 0;
        }

        public override string Name => $"TriggerGraph:{_graphName}:{_entry.Label}";

        public TriggerGraphMountDomain Domain => _domain;

        public int GraphId => _graphId;

        public string GraphName => _graphName;

        public string EntryLabel => _entry.Label;

        public Entity Scope => _scope;

        public GraphDebugTrace DebugTrace => _debugTrace;

        public GraphExecutionCursor Cursor => _cursor;

        public TriggerGraphMountRoute Route => _route;

        public bool IsGlobalRoute => _route == TriggerGraphMountRoute.Global;

        public int AbilityIdFilter => _abilityIdFilter;

        public string? ModIdFilter => _modIdFilter;

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

            if (!MatchesEntityScope(context))
            {
                return false;
            }

            if (_domain == TriggerGraphMountDomain.Ability &&
                (!context.Contains(MapTriggerEventPayloadKeys.AbilityId) ||
                 context.Get<int>(MapTriggerEventPayloadKeys.AbilityId) != _abilityIdFilter))
            {
                return false;
            }

            if (_domain == TriggerGraphMountDomain.Mod &&
                (!context.Contains(ModIdPayloadKey) ||
                 !string.Equals(context.Get<string>(ModIdPayloadKey), _modIdFilter, StringComparison.Ordinal)))
            {
                return false;
            }

            if (!TriggerGraphEntryFiltersEvaluator.Matches(context, _entry.Filters))
            {
                return false;
            }

            return base.CheckConditions(context);
        }

        private bool MatchesEntityScope(ScriptContext context)
        {
            if (_domain != TriggerGraphMountDomain.Entity || _lifecycleDispatch)
            {
                return true;
            }

            bool hasEntityPayload = false;
            bool matches = false;
            GameEngine engine = context.Get(CoreServiceKeys.Engine)
                ?? throw new InvalidOperationException($"{nameof(TriggerGraphMountTrigger)} requires GameEngine for entity scope evaluation.");
            if (context.Contains(MapTriggerEventPayloadKeys.SourceEntity))
            {
                hasEntityPayload = true;
                Entity source = context.Get<Entity>(MapTriggerEventPayloadKeys.SourceEntity);
                matches |= IsInScope(source, engine.World);
            }

            if (context.Contains(MapTriggerEventPayloadKeys.TargetEntity))
            {
                hasEntityPayload = true;
                Entity target = context.Get<Entity>(MapTriggerEventPayloadKeys.TargetEntity);
                matches |= IsInScope(target, engine.World);
            }

            return !hasEntityPayload || matches;
        }

        private bool IsInScope(Entity entity, Arch.Core.World world)
        {
            if (entity == Entity.Null || entity == default || !world.IsAlive(entity))
            {
                return false;
            }

            if (entity == _scope)
            {
                return true;
            }

            if (!world.Has<EntityTriggerGraphAggregateRoot>(_scope))
            {
                return false;
            }

            Entity current = entity;
            for (int depth = 0; depth < 1024 && world.IsAlive(current); depth++)
            {
                if (!world.Has<ChildOf>(current))
                {
                    return false;
                }

                current = world.Get<ChildOf>(current).Parent;
                if (current == _scope)
                {
                    return true;
                }
            }

            return false;
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
                ResolveTargetPosCm(context),
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
                TriggerGraphLimits.SliceBudgetSteps,
                GraphKind.Script,
                _debugTrace,
                ResolveMapScopeOnce(dependencies));
            LastSliceResult = result;

            RecordDebugTrace(result);

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

        private void RecordDebugTrace(GraphSliceResult result)
        {
            if (_debugTrace.Mode == GraphDebugTraceMode.Disabled)
            {
                return;
            }

            int sourcePc = _cursor.LastInstructionPc;
            if (sourcePc < 0)
            {
                throw new InvalidOperationException(
                    $"TriggerGraph '{_graphName}' produced a debug slice without an executed instruction source.");
            }

            if (result.Halted)
            {
                _debugTrace.RecordNode(sourcePc, _cursor.Pc, _cursor.Steps, GraphDebugTraceEvent.Halted);
            }
            else if (result.Yielded || result.BudgetSuspended)
            {
                _debugTrace.RecordNode(sourcePc, _cursor.Pc, _cursor.Steps, GraphDebugTraceEvent.Suspended);
            }

            if (_debugTrace.Mode != GraphDebugTraceMode.NodeAndPins)
            {
                return;
            }

            for (int i = 0; i < _vmIntRegisters.Length; i++)
            {
                if (_vmIntRegisters[i] != _previousIntRegisters[i])
                {
                    _debugTrace.RecordIntPin(sourcePc, i, _vmIntRegisters[i], _cursor.Pc, _cursor.Steps);
                    _previousIntRegisters[i] = _vmIntRegisters[i];
                }
            }

            for (int i = 0; i < _vmBoolRegisters.Length; i++)
            {
                if (_vmBoolRegisters[i] != _previousBoolRegisters[i])
                {
                    _debugTrace.RecordBoolPin(sourcePc, i, _vmBoolRegisters[i] != 0, _cursor.Pc, _cursor.Steps);
                    _previousBoolRegisters[i] = _vmBoolRegisters[i];
                }
            }

            for (int i = 0; i < _vmFloatRegisters.Length; i++)
            {
                if (_vmFloatRegisters[i] != _previousFloatRegisters[i])
                {
                    _debugTrace.RecordFloatPin(sourcePc, i, _vmFloatRegisters[i], _cursor.Pc, _cursor.Steps);
                    _previousFloatRegisters[i] = _vmFloatRegisters[i];
                }
            }

            for (int i = 0; i < _vmEntityRegisters.Length; i++)
            {
                if (_vmEntityRegisters[i] != _previousEntityRegisters[i])
                {
                    _debugTrace.RecordEntityPin(sourcePc, i, _vmEntityRegisters[i], _cursor.Pc, _cursor.Steps);
                    _previousEntityRegisters[i] = _vmEntityRegisters[i];
                }
            }
        }

        private MapId? ResolveMapScopeOnce(TriggerGraphTriggerDependencies dependencies)
        {
            if (!_mapScopeResolved)
            {
                _mapScope = ResolveMapScope(dependencies);
                _mapScopeResolved = true;
            }

            return _mapScope;
        }

        private MapId? ResolveMapScope(TriggerGraphTriggerDependencies dependencies)
        {
            // Scope-less or dead mounts must resolve to no map anchor before touching
            // Arch. Map-variable ops then fail closed with their existing scope error.
            return _scope != Entity.Null &&
                _scope != default &&
                dependencies.Engine.World.IsAlive(_scope) &&
                dependencies.Engine.World.TryGet<MapEntity>(_scope, out MapEntity anchor)
                ? anchor.MapId
                : null;
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

        private static Ludots.Platform.Abstractions.IntVector2 ResolveTargetPosCm(ScriptContext context)
        {
            if (context.Contains(MapTriggerEventPayloadKeys.GroundXCm) &&
                context.Contains(MapTriggerEventPayloadKeys.GroundYCm))
            {
                return new Ludots.Platform.Abstractions.IntVector2(
                    (int)context.Get<float>(MapTriggerEventPayloadKeys.GroundXCm),
                    (int)context.Get<float>(MapTriggerEventPayloadKeys.GroundYCm));
            }

            return default;
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
            Array.Clear(_previousIntRegisters, 0, _previousIntRegisters.Length);
            Array.Clear(_vmBoolRegisters, 0, _vmBoolRegisters.Length);
            Array.Clear(_previousBoolRegisters, 0, _previousBoolRegisters.Length);
            Array.Clear(_vmFloatRegisters, 0, _vmFloatRegisters.Length);
            Array.Clear(_previousFloatRegisters, 0, _previousFloatRegisters.Length);
            Array.Clear(_vmEntityRegisters, 0, _vmEntityRegisters.Length);
            Array.Clear(_previousEntityRegisters, 0, _previousEntityRegisters.Length);
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

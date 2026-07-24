using System;
using System.Numerics;
using System.Threading.Tasks;
using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.Layers;
using Ludots.Core.Physics3D;
using Ludots.Core.Presentation;
using Ludots.Core.Scripting;

namespace CapabilityStandardPhysics3DShowcaseMod.Runtime;

internal sealed partial class Physics3DShowcaseRuntime : IBenchmarkSceneController, IDisposable
{
    private const int CommandCapacity = 64;
    private const int QueryKindCount = 7;

    private readonly Physics3DShowcaseCommand[] _commands = new Physics3DShowcaseCommand[CommandCapacity];
    private readonly int[] _queryHitCounts = new int[QueryKindCount];
    private readonly byte[] _queryHasFirstHit = new byte[QueryKindCount];
    private readonly Vector3[] _queryFirstHitPositionsCm = new Vector3[QueryKindCount];
    private readonly Vector3[] _queryOriginsCm = new Vector3[QueryKindCount];
    private readonly Vector3[] _queryDirections = new Vector3[QueryKindCount];
    private readonly Vector3[] _querySizesCm = new Vector3[QueryKindCount];
    private readonly float[] _queryDistancesCm = new float[QueryKindCount];

    private World? _ecsWorld;
    private IPhysics3DWorld? _physicsWorld;
    private Physics3DSimulationSystem? _simulation;
    private Physics3DShowcaseConfig? _config;
    private Physics3DBodyId[] _bodyIds = Array.Empty<Physics3DBodyId>();
    private Entity[] _bodyEntities = Array.Empty<Entity>();
    private Physics3DBodyKind[] _bodyKinds = Array.Empty<Physics3DBodyKind>();
    private Physics3DShapeKind[] _bodyShapeKinds = Array.Empty<Physics3DShapeKind>();
    private Vector3[] _bodyVisualSizesCm = Array.Empty<Vector3>();
    private float[] _bodyCapsuleCylinderLengthsCm = Array.Empty<float>();
    private Vector4[] _bodyColors = Array.Empty<Vector4>();
    private Physics3DConstraintId[] _constraintIds = Array.Empty<Physics3DConstraintId>();
    private Physics3DRaycastHit[] _rayHits = Array.Empty<Physics3DRaycastHit>();
    private Physics3DShapeCastHit[] _shapeCastHits = Array.Empty<Physics3DShapeCastHit>();
    private Physics3DOverlapHit[] _overlapHits = Array.Empty<Physics3DOverlapHit>();
    private Physics3DContactEvent[] _contactEvents = Array.Empty<Physics3DContactEvent>();
    private Physics3DBodyState[] _replayInitialStates = Array.Empty<Physics3DBodyState>();
    private Physics3DBodyState[] _replayRecordedStates = Array.Empty<Physics3DBodyState>();
    private ulong[] _replayHashes = Array.Empty<ulong>();

    private Physics3DShapeId _floorShape;
    private Physics3DShapeId _boxShape;
    private Physics3DShapeId _sphereShape;
    private Physics3DShapeId _capsuleShape;
    private Physics3DShapeId _plankShape;
    private Physics3DShapeId _thinWallShape;
    private Physics3DShapeId _projectileShape;
    private Physics3DShapeId _sphereRailXShape;
    private Physics3DShapeId _sphereRailZShape;
    private Physics3DShapeId _capsuleRailShape;

    private int _commandHead;
    private int _commandTail;
    private int _commandCount;
    private int _bodyCount;
    private int _dynamicBodyCount;
    private int _kinematicBodyCount;
    private int _staticBodyCount;
    private int _constraintCount;
    private int _visibleBodyCount;
    private int _benchmarkBodies;
    private int _kinematicBodyIndex = -1;
    private int _continuousFirstBodyIndex = -1;
    private int _contactBodyIndex = -1;
    private int _determinismFirstBodyIndex = -1;
    private int _determinismBodyCount;
    private int _replayCursor;
    private int _contactBeginCount;
    private int _contactStayCount;
    private int _contactEndCount;
    private long _sceneStep;
    private long _sceneRevision;
    private ulong _replayExpectedHash;
    private ulong _replayActualHash;
    private bool _manualStepRequestedThisTick;
    private bool _isActive;
    private Physics3DShowcaseScene _scene;
    private Physics3DShowcaseReplayStatus _replayStatus;
    private string _lastAction = "Physics3D Sample Lab is waiting for its map.";

    public bool IsActive => _isActive;
    public bool SupportsScatterControl => _isActive && _scene == Physics3DShowcaseScene.Benchmark;
    public bool IsCleanPerformanceScene => false;
    public bool SuppressHostDiagnosticUi => false;
    public bool SuppressHostDebugGuides => _isActive;
    public int ScatterMin => _config?.BenchmarkPresets[0] ?? 0;
    public int ScatterMax => _config?.BenchmarkPresets[^1] ?? 0;
    public int ScatterTarget => _benchmarkBodies;
    public int ScatterAppliedTotal => _scene == Physics3DShowcaseScene.Benchmark ? _dynamicBodyCount : 0;

    internal Physics3DShowcaseConfig ActiveConfig => _config
        ?? throw new InvalidOperationException("Physics3D showcase config is not loaded.");
    internal Physics3DShowcaseScene ActiveScene => _scene;
    internal Physics3DShowcaseReplayStatus ReplayStatus => _replayStatus;
    internal int BodyCount => _bodyCount;
    internal int DynamicBodyCount => _dynamicBodyCount;
    internal int KinematicBodyCount => _kinematicBodyCount;
    internal int StaticBodyCount => _staticBodyCount;
    internal int ConstraintCount => _constraintCount;
    internal int VisibleBodyCount => _visibleBodyCount;
    internal int BenchmarkBodyCount => _benchmarkBodies;
    internal int ContactBeginCount => _contactBeginCount;
    internal int ContactStayCount => _contactStayCount;
    internal int ContactEndCount => _contactEndCount;
    internal int ReplayCursor => _replayCursor;
    internal int ReplayBodyCount => _determinismBodyCount;
    internal ulong ReplayExpectedHash => _replayExpectedHash;
    internal ulong ReplayActualHash => _replayActualHash;
    internal long SceneStep => _sceneStep;
    internal long SceneRevision => _sceneRevision;

    internal int GetQueryHitCount(int index)
    {
        if ((uint)index >= QueryKindCount)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        return _queryHitCounts[index];
    }

    public Task HandleMapFocusedAsync(ScriptContext context)
    {
        GameEngine engine = context.GetEngine()
            ?? throw new InvalidOperationException("Physics3D showcase requires a live GameEngine.");
        Physics3DShowcaseConfig config = new Physics3DShowcaseConfigLoader(engine.ConfigPipeline).Load(
            engine.ConfigCatalog,
            engine.ConfigConflictReport);
        string? mapId = engine.CurrentMapSession?.MapId.Value;
        if (!string.Equals(mapId, config.MapId, StringComparison.Ordinal))
        {
            Deactivate();
            return Task.CompletedTask;
        }

        IPhysics3DWorld physicsWorld = engine.GetService(Physics3DServiceKeys.World)
            ?? throw new InvalidOperationException("Physics3D showcase requires Physics3DServiceKeys.World.");
        Physics3DSimulationSystem simulation = engine.GetService(Physics3DServiceKeys.SimulationSystem)
            ?? throw new InvalidOperationException("Physics3D showcase requires Physics3DServiceKeys.SimulationSystem.");
        Activate(engine.World, physicsWorld, simulation, config);
        return Task.CompletedTask;
    }

    public Task HandleMapUnloadedAsync(ScriptContext context)
    {
        _ = context ?? throw new ArgumentNullException(nameof(context));
        Deactivate();
        return Task.CompletedTask;
    }

    internal void ActivateForTests(
        World ecsWorld,
        IPhysics3DWorld physicsWorld,
        Physics3DSimulationSystem simulation,
        Physics3DShowcaseConfig config)
    {
        Activate(ecsWorld, physicsWorld, simulation, config);
    }

    internal void EnqueueCommand(in Physics3DShowcaseCommand command)
    {
        if (!_isActive)
        {
            throw new InvalidOperationException("Physics3D showcase cannot accept commands while inactive.");
        }

        if (_commandCount >= _commands.Length)
        {
            throw new InvalidOperationException($"Physics3D showcase command queue exceeded capacity {_commands.Length}.");
        }

        _commands[_commandTail] = command;
        _commandTail = (_commandTail + 1) % _commands.Length;
        _commandCount++;
    }

    internal void PrepareFixedStep()
    {
        if (!_isActive)
        {
            return;
        }

        _manualStepRequestedThisTick = false;
        while (_commandCount > 0)
        {
            Physics3DShowcaseCommand command = _commands[_commandHead];
            _commandHead = (_commandHead + 1) % _commands.Length;
            _commandCount--;
            ExecuteCommand(in command);
        }

        Physics3DSimulationSystem simulation = RequireSimulation();
        bool willStep = simulation.Enabled || _manualStepRequestedThisTick;
        if (!willStep)
        {
            return;
        }

        PrepareSceneForPhysicsStep();
    }

    internal void ObserveFixedStep()
    {
        if (!_isActive)
        {
            return;
        }

        int steps = RequireSimulation().PhysicsStepsLastUpdate;
        for (int i = 0; i < steps; i++)
        {
            _sceneStep++;
            ObserveSceneAfterPhysicsStep();
        }
    }

    internal Physics3DShowcasePanelState CapturePanelState()
    {
        if (!_isActive)
        {
            return Physics3DShowcasePanelState.Empty;
        }

        IPhysics3DWorld world = RequirePhysicsWorld();
        Physics3DSimulationSystem simulation = RequireSimulation();
        string querySummary = _scene == Physics3DShowcaseScene.Queries
            ? $"Ray {_queryHitCounts[0]} · Box {_queryHitCounts[1]} · Sphere {_queryHitCounts[2]} · Capsule {_queryHitCounts[3]} · Overlap {_queryHitCounts[4]}/{_queryHitCounts[5]}/{_queryHitCounts[6]}"
            : "Select Queries to inspect seven query paths.";
        string contactSummary = $"Begin {_contactBeginCount} · Stay {_contactStayCount} · End {_contactEndCount}";
        string replaySummary = _replayStatus switch
        {
            Physics3DShowcaseReplayStatus.Recording => $"1 RECORD · {_replayCursor}/{ActiveConfig.ReplaySteps}",
            Physics3DShowcaseReplayStatus.ReadyToReplay => $"2 REBUILT · {_replayHashes.Length} recorded frames ready",
            Physics3DShowcaseReplayStatus.Replaying => $"3 COMPARE · {_replayCursor}/{ActiveConfig.ReplaySteps}",
            Physics3DShowcaseReplayStatus.Passed => $"PASS · {_replayCursor}/{ActiveConfig.ReplaySteps} exact matches",
            Physics3DShowcaseReplayStatus.Failed => $"FAIL at {_replayCursor + 1} · expected {_replayExpectedHash:X16}, actual {_replayActualHash:X16}",
            _ => "Replay is not running."
        };

        return new Physics3DShowcasePanelState(
            Title: "Physics3D Sample Lab",
            Scene: _scene,
            SceneTitle: SceneTitle(_scene),
            SceneDescription: SceneDescription(_scene),
            LastAction: _lastAction,
            Paused: !simulation.Enabled,
            FixedHz: FixedHzFromDeltaTime(world.FixedDeltaSeconds),
            PhysicsStepsLastUpdate: simulation.PhysicsStepsLastUpdate,
            PhysicsUpdateMilliseconds: simulation.PhysicsUpdateMillisecondsLastUpdate,
            MaximumStepMilliseconds: simulation.MaximumStepMillisecondsLastUpdate,
            TotalPhysicsSteps: simulation.TotalPhysicsSteps,
            Bodies: _bodyCount,
            DynamicBodies: _dynamicBodyCount,
            KinematicBodies: _kinematicBodyCount,
            StaticBodies: _staticBodyCount,
            AwakeBodies: world.AwakeBodyCount,
            ContactPairs: world.ContactPairCount,
            ContactEvents: world.ContactEventCount,
            Constraints: _constraintCount,
            VisibleBodies: _visibleBodyCount,
            BenchmarkBodies: _benchmarkBodies,
            ReplayStatus: _replayStatus,
            ReplayCursor: _replayCursor,
            ReplaySteps: ActiveConfig.ReplaySteps,
            QuerySummary: querySummary,
            ContactSummary: contactSummary,
            ReplaySummary: replaySummary);
    }

    internal bool TryGetReplayComparisonVisual(
        int localBodyIndex,
        out Physics3DBodyState recordedState,
        out Physics3DBodyState actualState,
        out Vector3 visualSizeCm)
    {
        if (_scene != Physics3DShowcaseScene.Determinism ||
            (uint)localBodyIndex >= (uint)_determinismBodyCount)
        {
            recordedState = default;
            actualState = default;
            visualSizeCm = default;
            return false;
        }

        int bodyIndex = _determinismFirstBodyIndex + localBodyIndex;
        actualState = RequirePhysicsWorld().GetBodyState(_bodyIds[bodyIndex]);
        visualSizeCm = _bodyVisualSizesCm[bodyIndex];
        if (_replayStatus == Physics3DShowcaseReplayStatus.Recording)
        {
            recordedState = actualState;
            return true;
        }

        if (_replayStatus == Physics3DShowcaseReplayStatus.ReadyToReplay)
        {
            recordedState = _replayInitialStates[localBodyIndex];
            return true;
        }

        int recordedStep = Math.Clamp(_replayCursor - 1, 0, ActiveConfig.ReplaySteps - 1);
        recordedState = _replayRecordedStates[(recordedStep * _determinismBodyCount) + localBodyIndex];
        return true;
    }

    internal bool TryGetBodyVisual(
        int index,
        out Physics3DBodyState state,
        out Physics3DBodyKind bodyKind,
        out Physics3DShapeKind shapeKind,
        out Vector3 visualSizeCm,
        out float capsuleCylinderLengthCm,
        out Vector4 color)
    {
        if ((uint)index >= (uint)_bodyCount)
        {
            state = default;
            bodyKind = default;
            shapeKind = default;
            visualSizeCm = default;
            capsuleCylinderLengthCm = 0f;
            color = default;
            return false;
        }

        Physics3DBodyId body = _bodyIds[index];
        if (!RequirePhysicsWorld().ContainsBody(body))
        {
            throw new InvalidOperationException($"Physics3D showcase lost owned body '{body}' at visual index {index}.");
        }

        state = RequirePhysicsWorld().GetBodyState(body);
        bodyKind = _bodyKinds[index];
        shapeKind = _bodyShapeKinds[index];
        visualSizeCm = _bodyVisualSizesCm[index];
        capsuleCylinderLengthCm = _bodyCapsuleCylinderLengthsCm[index];
        color = _bodyColors[index];
        return true;
    }

    internal bool TryGetQueryVisual(int index, out Physics3DShowcaseQueryVisual visual)
    {
        if ((uint)index >= QueryKindCount || _scene != Physics3DShowcaseScene.Queries)
        {
            visual = default;
            return false;
        }

        visual = new Physics3DShowcaseQueryVisual(
            (Physics3DShowcaseQueryKind)(index + 1),
            _queryOriginsCm[index],
            _queryDirections[index],
            _queryDistancesCm[index],
            _querySizesCm[index],
            _queryHitCounts[index],
            _queryHasFirstHit[index] != 0,
            _queryFirstHitPositionsCm[index]);
        return true;
    }

    internal void SetVisibleBodyCount(int count)
    {
        if (count < 0 || count > ActiveConfig.VisibleBodyLimit)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        _visibleBodyCount = count;
    }

    public void SetScatterTargetFromRatio(float ratio)
    {
        Physics3DShowcaseConfig config = ActiveConfig;
        if (!float.IsFinite(ratio))
        {
            throw new ArgumentOutOfRangeException(nameof(ratio));
        }

        int min = config.BenchmarkPresets[0];
        int max = config.BenchmarkPresets[^1];
        _benchmarkBodies = (int)MathF.Round(min + ((max - min) * Math.Clamp(ratio, 0f, 1f)));
    }

    public void ApplyScatterTarget()
    {
        EnqueueCommand(new Physics3DShowcaseCommand(Physics3DShowcaseCommandKind.SetBenchmarkBodies, _benchmarkBodies));
    }

    public void ApplyScatterLayout(int total)
    {
        EnqueueCommand(new Physics3DShowcaseCommand(Physics3DShowcaseCommandKind.SetBenchmarkBodies, total));
    }

    public void Dispose()
    {
        Deactivate();
    }

    private void Activate(
        World ecsWorld,
        IPhysics3DWorld physicsWorld,
        Physics3DSimulationSystem simulation,
        Physics3DShowcaseConfig config)
    {
        ArgumentNullException.ThrowIfNull(ecsWorld);
        ArgumentNullException.ThrowIfNull(physicsWorld);
        ArgumentNullException.ThrowIfNull(simulation);
        ArgumentNullException.ThrowIfNull(config);

        if (_isActive)
        {
            if (!ReferenceEquals(_ecsWorld, ecsWorld) ||
                !ReferenceEquals(_physicsWorld, physicsWorld) ||
                !ReferenceEquals(_simulation, simulation))
            {
                throw new InvalidOperationException("Physics3D showcase cannot attach one runtime to multiple worlds.");
            }

            return;
        }

        _ecsWorld = ecsWorld;
        _physicsWorld = physicsWorld;
        _simulation = simulation;
        _config = config;
        EnsureStorage(config);
        RegisterCommonShapes(config);
        _benchmarkBodies = config.BenchmarkDefaultBodies;
        _scene = config.InitialScene;
        _isActive = true;
        simulation.Enabled = true;
        BuildSelectedScene();
    }

    private void Deactivate()
    {
        if (!_isActive)
        {
            return;
        }

        ClearOwnedScene();
        RequireSimulation().Enabled = true;
        _isActive = false;
        _commandHead = 0;
        _commandTail = 0;
        _commandCount = 0;
        _ecsWorld = null;
        _physicsWorld = null;
        _simulation = null;
        _config = null;
        _lastAction = "Physics3D Sample Lab is inactive.";
    }

    private void EnsureStorage(Physics3DShowcaseConfig config)
    {
        if (_bodyIds.Length != config.MaximumBodies)
        {
            _bodyIds = new Physics3DBodyId[config.MaximumBodies];
            _bodyEntities = new Entity[config.MaximumBodies];
            _bodyKinds = new Physics3DBodyKind[config.MaximumBodies];
            _bodyShapeKinds = new Physics3DShapeKind[config.MaximumBodies];
            _bodyVisualSizesCm = new Vector3[config.MaximumBodies];
            _bodyCapsuleCylinderLengthsCm = new float[config.MaximumBodies];
            _bodyColors = new Vector4[config.MaximumBodies];
            _replayInitialStates = new Physics3DBodyState[config.MaximumBodies];
        }

        int constraintCapacity = Math.Max(256, config.ChainLinkCount * 8);
        if (_constraintIds.Length != constraintCapacity)
        {
            _constraintIds = new Physics3DConstraintId[constraintCapacity];
        }

        if (_rayHits.Length != config.QueryHitCapacity)
        {
            _rayHits = new Physics3DRaycastHit[config.QueryHitCapacity];
            _shapeCastHits = new Physics3DShapeCastHit[config.QueryHitCapacity];
            _overlapHits = new Physics3DOverlapHit[config.QueryHitCapacity];
        }

        if (_contactEvents.Length != config.ContactEventCapacity)
        {
            _contactEvents = new Physics3DContactEvent[config.ContactEventCapacity];
        }

        if (_replayHashes.Length != config.ReplaySteps)
        {
            _replayHashes = new ulong[config.ReplaySteps];
        }

        int replayStateCapacity = checked(config.ReplaySteps * config.ReplayGridSize * config.ReplayGridSize);
        if (_replayRecordedStates.Length != replayStateCapacity)
        {
            _replayRecordedStates = new Physics3DBodyState[replayStateCapacity];
        }
    }

    private void RegisterCommonShapes(Physics3DShowcaseConfig config)
    {
        IPhysics3DWorld world = RequirePhysicsWorld();
        float size = config.BodySizeCm;
        float radius = size * 0.5f;
        float capsuleCylinder = size * 1.25f;
        _floorShape = world.RegisterBoxShape(new Vector3(config.FloorSizeCm, config.FloorThicknessCm, config.FloorSizeCm));
        _boxShape = world.RegisterBoxShape(new Vector3(size));
        _sphereShape = world.RegisterSphereShape(radius);
        _capsuleShape = world.RegisterCapsuleShape(radius, capsuleCylinder);
        _plankShape = world.RegisterBoxShape(new Vector3(size * 1.5f, size * 0.35f, size * 0.6f));
        _thinWallShape = world.RegisterBoxShape(new Vector3(size * 0.2f, size * 10f, size * 25f));
        _projectileShape = world.RegisterSphereShape(size * 0.2f);
        float railThickness = config.StackingRailThicknessCm;
        float sphereBaseSpan = ((config.SpherePyramidRows - 1) * config.SpherePyramidSpacingCm) + size;
        float sphereRailLength = sphereBaseSpan + (2f * (railThickness + config.StackingRailClearanceCm));
        _sphereRailXShape = world.RegisterBoxShape(new Vector3(
            sphereRailLength,
            config.StackingRailHeightCm,
            railThickness));
        _sphereRailZShape = world.RegisterBoxShape(new Vector3(
            railThickness,
            config.StackingRailHeightCm,
            sphereRailLength));
        _capsuleRailShape = world.RegisterBoxShape(new Vector3(
            railThickness,
            config.StackingRailHeightCm,
            size + capsuleCylinder + (2f * config.StackingRailClearanceCm)));
    }

    private void ExecuteCommand(in Physics3DShowcaseCommand command)
    {
        switch (command.Kind)
        {
            case Physics3DShowcaseCommandKind.SelectScene:
                if ((uint)command.Value > byte.MaxValue ||
                    !Enum.IsDefined(typeof(Physics3DShowcaseScene), (byte)command.Value))
                {
                    throw new InvalidOperationException($"Unknown Physics3D showcase scene value {command.Value}.");
                }

                _scene = (Physics3DShowcaseScene)command.Value;
                BuildSelectedScene();
                break;
            case Physics3DShowcaseCommandKind.Reset:
                BuildSelectedScene();
                _lastAction = $"Reset {SceneTitle(_scene)} to its authored starting state.";
                break;
            case Physics3DShowcaseCommandKind.TogglePause:
                RequireSimulation().Enabled = !RequireSimulation().Enabled;
                _lastAction = RequireSimulation().Enabled
                    ? "Simulation resumed at 30 fixed steps per second."
                    : "Simulation paused. Single Step advances exactly one fixed step.";
                break;
            case Physics3DShowcaseCommandKind.SingleStep:
                RequireSimulation().Enabled = false;
                RequireSimulation().RequestManualSteps(1);
                _manualStepRequestedThisTick = true;
                _lastAction = "Advanced one authoritative Physics3D step.";
                break;
            case Physics3DShowcaseCommandKind.Impact:
                ApplyImpact();
                break;
            case Physics3DShowcaseCommandKind.SetBenchmarkBodies:
                ValidateBenchmarkBodyCount(command.Value);
                _benchmarkBodies = command.Value;
                _scene = Physics3DShowcaseScene.Benchmark;
                BuildSelectedScene();
                break;
            case Physics3DShowcaseCommandKind.StartReplayComparison:
                StartReplayComparison();
                break;
            default:
                throw new InvalidOperationException($"Unknown Physics3D showcase command '{command.Kind}'.");
        }
    }

    private void ValidateBenchmarkBodyCount(int count)
    {
        if (count <= 0 || count >= ActiveConfig.MaximumBodies)
        {
            throw new InvalidOperationException(
                $"Benchmark body count {count} must be in [1, {ActiveConfig.MaximumBodies - 1}] so the floor remains owned.");
        }
    }

    private Physics3DBodyId AddOwnedBody(
        Physics3DBodyKind bodyKind,
        Physics3DShapeId shape,
        Physics3DShapeKind shapeKind,
        Vector3 visualSizeCm,
        float capsuleCylinderLengthCm,
        Vector3 positionCm,
        Quaternion orientation,
        Vector3 linearVelocityCmPerSecond,
        Vector3 angularVelocityRadiansPerSecond,
        Physics3DContinuousDetectionMode continuousDetection,
        Vector4 color,
        float mass = 1f)
    {
        if (_bodyCount >= _bodyIds.Length)
        {
            throw new InvalidOperationException($"Physics3D showcase exceeded owned body capacity {_bodyIds.Length}.");
        }

        World ecsWorld = RequireEcsWorld();
        Entity entity = ecsWorld.Create(
            new Physics3DBodyCm { Id = default, Kind = bodyKind },
            new Physics3DPoseCm
            {
                Position = positionCm,
                Orientation = orientation,
                LinearVelocity = linearVelocityCmPerSecond,
                AngularVelocity = angularVelocityRadiansPerSecond
            },
            new PreviousPhysics3DPoseCm { Position = positionCm, Orientation = orientation });

        Physics3DBodyId body;
        try
        {
            body = RequirePhysicsWorld().CreateBody(new Physics3DBodyDescription(
                entity,
                bodyKind,
                shape,
                positionCm,
                orientation,
                linearVelocityCmPerSecond,
                angularVelocityRadiansPerSecond,
                bodyKind == Physics3DBodyKind.Dynamic ? mass : 0f,
                LayerMask.All,
                CreateMaterial(),
                continuousDetection));
        }
        catch
        {
            ecsWorld.Destroy(entity);
            throw;
        }

        ecsWorld.Set(entity, new Physics3DBodyCm { Id = body, Kind = bodyKind });
        int index = _bodyCount++;
        _bodyIds[index] = body;
        _bodyEntities[index] = entity;
        _bodyKinds[index] = bodyKind;
        _bodyShapeKinds[index] = shapeKind;
        _bodyVisualSizesCm[index] = visualSizeCm;
        _bodyCapsuleCylinderLengthsCm[index] = capsuleCylinderLengthCm;
        _bodyColors[index] = color;
        switch (bodyKind)
        {
            case Physics3DBodyKind.Dynamic:
                _dynamicBodyCount++;
                break;
            case Physics3DBodyKind.Kinematic:
                _kinematicBodyCount++;
                break;
            case Physics3DBodyKind.Static:
                _staticBodyCount++;
                break;
            default:
                throw new InvalidOperationException($"Unsupported Physics3D body kind '{bodyKind}'.");
        }

        return body;
    }

    private void AddOwnedConstraint(Physics3DConstraintId constraint)
    {
        if (!constraint.IsValid)
        {
            throw new InvalidOperationException("Physics3D showcase received an invalid constraint id.");
        }

        if (_constraintCount >= _constraintIds.Length)
        {
            throw new InvalidOperationException($"Physics3D showcase exceeded constraint capacity {_constraintIds.Length}.");
        }

        _constraintIds[_constraintCount++] = constraint;
    }

    private void ClearOwnedScene()
    {
        if (_physicsWorld == null || _ecsWorld == null)
        {
            return;
        }

        for (int i = _constraintCount - 1; i >= 0; i--)
        {
            Physics3DConstraintId constraint = _constraintIds[i];
            if (_physicsWorld.ContainsConstraint(constraint))
            {
                _physicsWorld.DestroyConstraint(constraint);
            }
        }

        for (int i = _bodyCount - 1; i >= 0; i--)
        {
            Physics3DBodyId body = _bodyIds[i];
            if (_physicsWorld.ContainsBody(body))
            {
                _physicsWorld.DestroyBody(body);
            }

            Entity entity = _bodyEntities[i];
            if (_ecsWorld.IsAlive(entity))
            {
                _ecsWorld.Destroy(entity);
            }
        }

        Array.Clear(_constraintIds, 0, _constraintCount);
        Array.Clear(_bodyIds, 0, _bodyCount);
        Array.Clear(_bodyEntities, 0, _bodyCount);
        Array.Clear(_bodyKinds, 0, _bodyCount);
        Array.Clear(_bodyShapeKinds, 0, _bodyCount);
        Array.Clear(_bodyVisualSizesCm, 0, _bodyCount);
        Array.Clear(_bodyCapsuleCylinderLengthsCm, 0, _bodyCount);
        Array.Clear(_bodyColors, 0, _bodyCount);
        _bodyCount = 0;
        _dynamicBodyCount = 0;
        _kinematicBodyCount = 0;
        _staticBodyCount = 0;
        _constraintCount = 0;
        _visibleBodyCount = 0;
        _kinematicBodyIndex = -1;
        _continuousFirstBodyIndex = -1;
        _contactBodyIndex = -1;
        _determinismFirstBodyIndex = -1;
        _determinismBodyCount = 0;
    }

    private Physics3DMaterial CreateMaterial()
    {
        Physics3DShowcaseConfig config = ActiveConfig;
        return new Physics3DMaterial(
            config.FrictionCoefficient,
            config.MaximumRecoveryVelocityCmPerSecond,
            config.SpringAngularFrequency,
            config.SpringTwiceDampingRatio);
    }

    private Physics3DSpringSettings CreateSpring()
    {
        Physics3DShowcaseConfig config = ActiveConfig;
        return new Physics3DSpringSettings(config.SpringAngularFrequency, config.SpringTwiceDampingRatio);
    }

    private World RequireEcsWorld() => _ecsWorld
        ?? throw new InvalidOperationException("Physics3D showcase ECS world is unavailable.");

    private IPhysics3DWorld RequirePhysicsWorld() => _physicsWorld
        ?? throw new InvalidOperationException("Physics3D showcase physics world is unavailable.");

    private Physics3DSimulationSystem RequireSimulation() => _simulation
        ?? throw new InvalidOperationException("Physics3D showcase simulation system is unavailable.");

    private static int FixedHzFromDeltaTime(float deltaTime)
    {
        int hz = (int)MathF.Round(1f / deltaTime);
        if (hz <= 0 || MathF.Abs((1f / hz) - deltaTime) > 1e-5f)
        {
            throw new InvalidOperationException($"Physics3D fixed delta '{deltaTime}' is not an integer Hz rate.");
        }

        return hz;
    }

    private static string SceneTitle(Physics3DShowcaseScene scene) => scene switch
    {
        Physics3DShowcaseScene.Bodies => "Bodies",
        Physics3DShowcaseScene.Shapes => "Shapes",
        Physics3DShowcaseScene.Stacking => "Stacking",
        Physics3DShowcaseScene.Continuous => "Continuous",
        Physics3DShowcaseScene.Queries => "Queries",
        Physics3DShowcaseScene.ContactEvents => "Contact Events",
        Physics3DShowcaseScene.Joints => "Joints",
        Physics3DShowcaseScene.Determinism => "Determinism",
        Physics3DShowcaseScene.Benchmark => "Benchmark",
        _ => throw new InvalidOperationException($"Unsupported Physics3D showcase scene '{scene}'.")
    };

    private static string SceneDescription(Physics3DShowcaseScene scene) => scene switch
    {
        Physics3DShowcaseScene.Bodies => "Compare falling, authored-motion, and immovable bodies in one arena.",
        Physics3DShowcaseScene.Shapes => "Drop boxes, spheres, and capsules together and compare their contact behavior.",
        Physics3DShowcaseScene.Stacking => "A pyramid and two towers expose stability, sleeping, and impact recovery.",
        Physics3DShowcaseScene.Continuous => "Three high-speed projectiles compare discrete, passive, and continuous detection.",
        Physics3DShowcaseScene.Queries => "Seven visible lanes demonstrate ray, shape casts, and overlap queries.",
        Physics3DShowcaseScene.ContactEvents => "Watch Begin, Stay, and End as the contact actor separates and returns.",
        Physics3DShowcaseScene.Joints => "Ball sockets, hinges, and welds share one interactive joint gallery.",
        Physics3DShowcaseScene.Determinism => "Record the blue run, rebuild the world, then compare the gold replay side by side.",
        Physics3DShowcaseScene.Benchmark => "Counter-flowing layers keep 1K, 2K, 5K, or 10K authoritative bodies visibly active.",
        _ => throw new InvalidOperationException($"Unsupported Physics3D showcase scene '{scene}'.")
    };
}

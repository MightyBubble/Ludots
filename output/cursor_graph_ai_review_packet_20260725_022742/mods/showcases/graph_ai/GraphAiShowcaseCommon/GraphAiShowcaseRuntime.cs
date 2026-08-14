using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text.Json;
using System.Threading.Tasks;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Mathematics;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;

namespace GraphAiShowcaseCommon;

public sealed class GraphAiShowcaseRuntime
{
    private const string ConfigUri = "assets/GraphAiShowcase/showcase.json";
    private const int RequiredHotPathEntityCount = 50_000;
    private const int PanelX = 18;
    private const int PanelY = 18;
    private const int PanelWidth = 620;
    private const int ContentX = 42;
    private const int ContentWidth = 548;
    private static readonly Vector4 PanelFill = new(0.035f, 0.05f, 0.06f, 0.68f);
    private static readonly Vector4 PanelBorder = new(0.30f, 0.62f, 0.70f, 0.95f);
    private static readonly Vector4 TitleColor = new(0.94f, 0.97f, 0.94f, 1f);
    private static readonly Vector4 TextColor = new(0.75f, 0.86f, 0.82f, 1f);
    private static readonly Vector4 AccentColor = new(0.96f, 0.78f, 0.35f, 1f);
    private static readonly Vector4 MutedFill = new(0.07f, 0.10f, 0.11f, 0.76f);
    private static readonly Vector4 CompleteFill = new(0.12f, 0.34f, 0.28f, 0.82f);
    private static readonly Vector4 ActiveFill = new(0.31f, 0.23f, 0.09f, 0.84f);
    private static readonly Vector4 AlertFill = new(0.36f, 0.13f, 0.12f, 0.84f);
    private static readonly Vector4 LineColor = new(0.28f, 0.46f, 0.48f, 0.9f);
    private static readonly Vector4 WorldLabelFill = new(0.02f, 0.03f, 0.035f, 0.82f);
    private static readonly Vector4 WorldLabelBorder = new(0.98f, 0.82f, 0.22f, 0.96f);
    private static readonly Vector4 WorldTargetColor = new(0.98f, 0.82f, 0.22f, 0.94f);
    private static readonly Vector4 WorldActionLine = new(0.98f, 0.82f, 0.22f, 0.72f);
    private static readonly Vector4 WorldTrailLine = new(0.28f, 0.86f, 0.94f, 0.52f);

    private readonly string _modId;
    private readonly string _expectedMapId;
    private readonly string _runtimeKey;
    private readonly Dictionary<string, GraphInstruction[]> _programs = new(StringComparer.Ordinal);
    private readonly int[] _levelIntRegisters = new int[GraphAiVmLimits.IntRegisters];
    private readonly byte[] _levelBoolRegisters = new byte[GraphAiVmLimits.BoolRegisters];
    private GameEngine? _engine;
    private GraphAiShowcaseConfig? _config;
    private GraphAiLevelStepState[] _levelSteps = Array.Empty<GraphAiLevelStepState>();
    private GraphAiLevelCursorState _levelCursor;
    private GraphAiActorState[] _actors = Array.Empty<GraphAiActorState>();
    private GraphAiMotionTargetState[] _stanceTargetsByState = Array.Empty<GraphAiMotionTargetState>();
    private GraphAiMotionTargetState[] _behaviorTargetsByTask = Array.Empty<GraphAiMotionTargetState>();
    private int[][] _actorIntRegisters = Array.Empty<int[]>();
    private byte[][] _actorBoolRegisters = Array.Empty<byte[]>();
    private GraphInstruction[] _activeProgram = Array.Empty<GraphInstruction>();
    private GraphAiHotPathProbe? _hotPathProbe;
    private string _activeProgramId = string.Empty;
    private int _frame;
    private int _tick;
    private int _phase;
    private int _intent;
    private int _completedTasks;
    private float _elapsedSeconds;
    private float _beatAccumulator;

    public GraphAiShowcaseRuntime(string modId, string expectedMapId, string runtimeKey)
    {
        _modId = !string.IsNullOrWhiteSpace(modId) ? modId : throw new ArgumentException("Mod id is required.", nameof(modId));
        _expectedMapId = !string.IsNullOrWhiteSpace(expectedMapId) ? expectedMapId : throw new ArgumentException("Expected map id is required.", nameof(expectedMapId));
        _runtimeKey = !string.IsNullOrWhiteSpace(runtimeKey) ? runtimeKey : throw new ArgumentException("Runtime key is required.", nameof(runtimeKey));
        Snapshot = GraphAiShowcaseSnapshot.Inactive(runtimeKey);
    }

    public bool IsActive { get; private set; }
    public GraphAiShowcaseSnapshot Snapshot { get; private set; }

    public Task HandleMapFocusedAsync(ScriptContext context)
    {
        GameEngine? engine = context.GetEngine();
        if (engine == null)
        {
            return Task.CompletedTask;
        }

        string? mapId = engine.CurrentMapSession?.MapConfig?.Id;
        if (!string.Equals(mapId, _expectedMapId, StringComparison.Ordinal))
        {
            IsActive = false;
            return Task.CompletedTask;
        }

        _engine = engine;
        LoadConfig(engine);
        Reset();
        BindMapEntities(engine);
        IsActive = true;
        engine.GlobalContext[_runtimeKey] = this;
        WriteSnapshot();
        return Task.CompletedTask;
    }

    public Task HandleMapUnloadedAsync(ScriptContext context)
    {
        IsActive = false;
        _engine = null;
        _levelSteps = Array.Empty<GraphAiLevelStepState>();
        _actors = Array.Empty<GraphAiActorState>();
        Snapshot = GraphAiShowcaseSnapshot.Inactive(_runtimeKey);
        return Task.CompletedTask;
    }

    public void Update(float dt)
    {
        if (!IsActive || _config == null)
        {
            return;
        }

        if (!float.IsFinite(dt) || dt < 0f)
        {
            throw new InvalidOperationException($"Graph AI showcase '{_config.ShowcaseId}' received invalid dt '{dt.ToString(CultureInfo.InvariantCulture)}'.");
        }

        _frame++;
        _elapsedSeconds += dt;
        _beatAccumulator += dt;
        while (_beatAccumulator >= _config.BeatSeconds)
        {
            _beatAccumulator -= _config.BeatSeconds;
            _tick++;
            switch (_config.Mode)
            {
                case "LevelBlueprint":
                    TickLevelBlueprint();
                    break;
                case "StanceFsm":
                    TickStanceFsm();
                    break;
                case "ComplexBt":
                    TickComplexBt();
                    break;
                default:
                    throw new InvalidOperationException($"Graph AI showcase '{_config.ShowcaseId}' has unsupported mode '{_config.Mode}'.");
            }

            _hotPathProbe?.Update(_tick);
        }

        ApplyVisibleMotion(dt);
        WriteSnapshot();
    }

    public void RenderOverlay(GameEngine engine)
    {
        if (!IsActive || _config == null || engine.GetService(CoreServiceKeys.ScreenOverlayBuffer) is not ScreenOverlayBuffer overlay)
        {
            return;
        }

        int h = _config.Mode == "LevelBlueprint" ? 380 : 450;
        overlay.AddRect(PanelX, PanelY, PanelWidth, h, PanelFill, PanelBorder, stableId: 82000, dirtySerial: _frame);
        overlay.AddText(34, 32, _config.Title, 21, TitleColor, stableId: 82001, dirtySerial: _frame);
        overlay.AddText(34, 60, _config.Summary, 13, TextColor, stableId: 82002, dirtySerial: _frame);
        overlay.AddText(34, 84, $"Graph: {_activeProgramId} | beat {_tick}", 13, AccentColor, stableId: 82003, dirtySerial: _frame);
        overlay.AddText(34, 108, $"State: {Snapshot.StateLabel} | Intent: {Snapshot.IntentLabel}", 13, TextColor, stableId: 82004, dirtySerial: _frame);
        overlay.AddText(34, 132, _config.Boundary, 12, AccentColor, stableId: 82005, dirtySerial: _frame);

        switch (_config.Mode)
        {
            case "LevelBlueprint":
                RenderLevelBlueprintStage(overlay);
                RenderHotPath(overlay, 320);
                RenderLevelBlueprintWorldOverlay(engine, overlay);
                break;
            case "StanceFsm":
                RenderStanceStage(overlay);
                RenderHotPath(overlay, 390);
                RenderActorWorldOverlay(engine, overlay, 82500);
                break;
            case "ComplexBt":
                RenderBehaviorTreeStage(overlay);
                RenderHotPath(overlay, 390);
                RenderActorWorldOverlay(engine, overlay, 82600);
                break;
        }
    }

    private void LoadConfig(GameEngine engine)
    {
        using Stream stream = engine.VFS.GetStream($"{_modId}:{ConfigUri}");
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        GraphAiShowcaseConfig config = JsonSerializer.Deserialize<GraphAiShowcaseConfig>(stream, options)
            ?? throw new InvalidOperationException($"Graph AI showcase config '{_modId}:{ConfigUri}' is empty.");

        ValidateConfig(config);
        _config = config;
        _programs.Clear();
        for (int i = 0; i < config.Programs.Count; i++)
        {
            GraphAiProgramConfig program = config.Programs[i];
            if (!_programs.TryAdd(program.Id, GraphAiProgramCompiler.Compile(program)))
            {
                throw new InvalidOperationException($"Graph AI showcase '{config.ShowcaseId}' contains duplicate program '{program.Id}'.");
            }
        }

        if (!_programs.TryGetValue(config.GraphProgramId, out GraphInstruction[]? activeProgram) || activeProgram == null)
        {
            throw new InvalidOperationException($"Graph AI showcase '{config.ShowcaseId}' references missing graph program '{config.GraphProgramId}'.");
        }

        _activeProgram = activeProgram;
        _activeProgramId = config.GraphProgramId;
    }

    private void ValidateConfig(GraphAiShowcaseConfig config)
    {
        if (config.SchemaVersion != 1)
        {
            throw new InvalidOperationException($"Graph AI showcase '{_modId}' requires schemaVersion 1.");
        }

        if (!string.Equals(config.MapId, _expectedMapId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Graph AI showcase '{config.ShowcaseId}' mapId '{config.MapId}' does not match entry map '{_expectedMapId}'.");
        }

        if (string.IsNullOrWhiteSpace(config.ShowcaseId) ||
            string.IsNullOrWhiteSpace(config.Mode) ||
            string.IsNullOrWhiteSpace(config.GraphProgramId) ||
            config.Programs.Count == 0)
        {
            throw new InvalidOperationException($"Graph AI showcase '{_modId}' config is missing required id/mode/program data.");
        }

        if (!float.IsFinite(config.BeatSeconds) || config.BeatSeconds <= 0f)
        {
            throw new InvalidOperationException($"Graph AI showcase '{config.ShowcaseId}' requires positive beatSeconds.");
        }

        if (config.HotPath.EntityCount != RequiredHotPathEntityCount)
        {
            throw new InvalidOperationException($"Graph AI showcase '{config.ShowcaseId}' must declare hotPath.entityCount {RequiredHotPathEntityCount.ToString(CultureInfo.InvariantCulture)}.");
        }

        if (config.Mode == "LevelBlueprint")
        {
            if (string.IsNullOrWhiteSpace(config.LevelFlow.CursorInstanceId) || config.LevelFlow.Steps.Count != 4)
            {
                throw new InvalidOperationException($"Graph AI showcase '{config.ShowcaseId}' level blueprint requires one cursor and four authored steps.");
            }

            for (int i = 0; i < config.LevelFlow.Steps.Count; i++)
            {
                GraphAiLevelStepConfig step = config.LevelFlow.Steps[i];
                if (string.IsNullOrWhiteSpace(step.InstanceId) ||
                    string.IsNullOrWhiteSpace(step.TargetInstanceId) ||
                    string.IsNullOrWhiteSpace(step.Label) ||
                    string.IsNullOrWhiteSpace(step.ActionLabel))
                {
                    throw new InvalidOperationException($"Graph AI showcase '{config.ShowcaseId}' levelFlow.steps[{i}] requires instanceId, targetInstanceId, label, and actionLabel.");
                }

                if (!float.IsFinite(step.TargetWobbleXFrequency) || !float.IsFinite(step.TargetWobbleYFrequency))
                {
                    throw new InvalidOperationException($"Graph AI showcase '{config.ShowcaseId}' levelFlow.steps[{i}] requires finite target wobble frequencies.");
                }
            }
        }
        else if (config.Mode == "StanceFsm" || config.Mode == "ComplexBt")
        {
            if (config.Actors.Count == 0)
            {
                throw new InvalidOperationException($"Graph AI showcase '{config.ShowcaseId}' mode '{config.Mode}' requires authored actors.");
            }

            for (int i = 0; i < config.Actors.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(config.Actors[i].InstanceId))
                {
                    throw new InvalidOperationException($"Graph AI showcase '{config.ShowcaseId}' actor[{i}] requires instanceId.");
                }
            }

            IReadOnlyList<GraphAiMotionTargetConfig> targets = config.Mode == "StanceFsm"
                ? config.WorldTargets.StanceByState
                : config.WorldTargets.BehaviorByTask;
            if (targets.Count == 0)
            {
                throw new InvalidOperationException($"Graph AI showcase '{config.ShowcaseId}' mode '{config.Mode}' requires data-driven world targets.");
            }

            var keys = new HashSet<int>();
            for (int i = 0; i < targets.Count; i++)
            {
                GraphAiMotionTargetConfig target = targets[i];
                if (!keys.Add(target.Key))
                {
                    throw new InvalidOperationException($"Graph AI showcase '{config.ShowcaseId}' world target key '{target.Key}' is duplicated.");
                }

                if (target.Key < 0 ||
                    string.IsNullOrWhiteSpace(target.InstanceId) ||
                    string.IsNullOrWhiteSpace(target.ActionLabel) ||
                    target.SpeedCmPerSecond <= 0 ||
                    !float.IsFinite(target.WobbleXFrequency) ||
                    !float.IsFinite(target.WobbleYFrequency) ||
                    !float.IsFinite(target.FacingRad))
                {
                    throw new InvalidOperationException($"Graph AI showcase '{config.ShowcaseId}' world target[{i}] requires key, instanceId, actionLabel, finite motion settings, and positive speed.");
                }
            }
        }
    }

    private void Reset()
    {
        GraphAiShowcaseConfig config = RequireConfig();
        _frame = 0;
        _tick = 0;
        _phase = 0;
        _intent = 0;
        _completedTasks = 0;
        _elapsedSeconds = 0f;
        _beatAccumulator = config.BeatSeconds;
        _levelSteps = Array.Empty<GraphAiLevelStepState>();
        _levelCursor = default;
        _actors = new GraphAiActorState[config.Actors.Count];
        _stanceTargetsByState = Array.Empty<GraphAiMotionTargetState>();
        _behaviorTargetsByTask = Array.Empty<GraphAiMotionTargetState>();
        _actorIntRegisters = new int[config.Actors.Count][];
        _actorBoolRegisters = new byte[config.Actors.Count][];
        _hotPathProbe = new GraphAiHotPathProbe(_activeProgram, config.HotPath.EntityCount);
        for (int i = 0; i < config.Actors.Count; i++)
        {
            GraphAiActorConfig actor = config.Actors[i];
            if (string.IsNullOrWhiteSpace(actor.Name))
            {
                throw new InvalidOperationException($"Graph AI showcase '{config.ShowcaseId}' actor[{i}] requires a name.");
            }

            _actors[i] = new GraphAiActorState
            {
                Name = actor.Name,
                InstanceId = actor.InstanceId,
                State = actor.State,
                BtNode = actor.BtNode,
                EnemyDistanceCm = actor.EnemyDistanceCm,
                Health = actor.Health,
                Morale = actor.Morale,
                ActionLabel = "waiting for graph"
            };
            _actorIntRegisters[i] = new int[GraphAiVmLimits.IntRegisters];
            _actorBoolRegisters[i] = new byte[GraphAiVmLimits.BoolRegisters];
        }
    }

    private void BindMapEntities(GameEngine engine)
    {
        GraphAiShowcaseConfig config = RequireConfig();
        if (config.Mode == "LevelBlueprint")
        {
            _levelSteps = new GraphAiLevelStepState[config.LevelFlow.Steps.Count];
            for (int i = 0; i < config.LevelFlow.Steps.Count; i++)
            {
                GraphAiLevelStepConfig step = config.LevelFlow.Steps[i];
                string instanceId = step.InstanceId;
                Entity entity = ResolveRequiredEntity(engine, instanceId, $"levelFlow.steps[{i}]", mustBeGraphDriven: true);
                Vector2 home = ReadEntityPosition(engine, entity, instanceId);
                Entity targetEntity = ResolveRequiredEntity(engine, step.TargetInstanceId, $"levelFlow.steps[{i}].targetInstanceId", mustBeGraphDriven: true);
                Vector2 targetHome = ReadEntityPosition(engine, targetEntity, step.TargetInstanceId);
                _levelSteps[i] = new GraphAiLevelStepState(
                    instanceId,
                    entity,
                    home,
                    step.TargetInstanceId,
                    targetEntity,
                    targetHome,
                    step.Label,
                    step.ActionLabel,
                    step.TargetActiveOffsetXCm,
                    step.TargetActiveOffsetYCm,
                    step.TargetCompleteOffsetXCm,
                    step.TargetCompleteOffsetYCm,
                    step.TargetWobbleXCm,
                    step.TargetWobbleYCm,
                    step.TargetWobbleXFrequency,
                    step.TargetWobbleYFrequency);
            }

            Entity cursorEntity = ResolveRequiredEntity(engine, config.LevelFlow.CursorInstanceId, "levelFlow.cursorInstanceId", mustBeGraphDriven: true);
            Vector2 cursorHome = ReadEntityPosition(engine, cursorEntity, config.LevelFlow.CursorInstanceId);
            _levelCursor = new GraphAiLevelCursorState(config.LevelFlow.CursorInstanceId, cursorEntity, cursorHome, cursorHome);
            return;
        }

        _stanceTargetsByState = BindMotionTargets(engine, config.WorldTargets.StanceByState, "worldTargets.stanceByState");
        _behaviorTargetsByTask = BindMotionTargets(engine, config.WorldTargets.BehaviorByTask, "worldTargets.behaviorByTask");
        for (int i = 0; i < _actors.Length; i++)
        {
            Entity entity = ResolveRequiredEntity(engine, _actors[i].InstanceId, $"actors[{i}]", mustBeGraphDriven: true);
            Vector2 home = ReadEntityPosition(engine, entity, _actors[i].InstanceId);
            _actors[i].Entity = entity;
            _actors[i].Home = home;
            _actors[i].Current = home;
            _actors[i].Target = home;
        }
    }

    private void TickLevelBlueprint()
    {
        GraphAiVmState vm = GraphAiVmState.Create(_levelIntRegisters, _levelBoolRegisters);
        ClearVm(ref vm);
        SeedCommonInputs(ref vm, state: _phase, actor: default);
        Execute(ref vm);
        GraphAiOutputConfig outputs = RequireConfig().Outputs;
        _phase = vm.I[outputs.StateRegister];
        _intent = vm.I[outputs.IntentRegister];
    }

    private void TickStanceFsm()
    {
        GraphAiOutputConfig outputs = RequireConfig().Outputs;
        for (int i = 0; i < _actors.Length; i++)
        {
            GraphAiVmState vm = GraphAiVmState.Create(_actorIntRegisters[i], _actorBoolRegisters[i]);
            ClearVm(ref vm);
            SeedCommonInputs(ref vm, _actors[i].State, _actors[i]);
            Execute(ref vm);
            _actors[i].State = vm.I[outputs.StateRegister];
            _actors[i].Intent = vm.I[outputs.IntentRegister];
        }
    }

    private void TickComplexBt()
    {
        GraphAiOutputConfig outputs = RequireConfig().Outputs;
        for (int i = 0; i < _actors.Length; i++)
        {
            if (_actors[i].TaskRemainingTicks > 0)
            {
                _actors[i].TaskRemainingTicks--;
                if (_actors[i].TaskRemainingTicks == 0)
                {
                    _completedTasks++;
                }

                continue;
            }

            GraphAiVmState vm = GraphAiVmState.Create(_actorIntRegisters[i], _actorBoolRegisters[i]);
            ClearVm(ref vm);
            SeedCommonInputs(ref vm, _actors[i].State, _actors[i]);
            Execute(ref vm);
            int taskId = vm.I[outputs.TaskIdRegister];
            int duration = vm.I[outputs.TaskDurationRegister];
            if (taskId <= 0 || duration <= 0)
            {
                throw new InvalidOperationException($"Complex BT graph '{_activeProgramId}' must output positive task id and duration.");
            }

            _actors[i].State = vm.I[outputs.StateRegister];
            _actors[i].BtNode = vm.I[outputs.BtNodeRegister];
            _actors[i].Intent = vm.I[outputs.IntentRegister];
            _actors[i].TaskId = taskId;
            _actors[i].TaskRemainingTicks = duration;
            _actors[i].TaskDurationTicks = duration;
        }
    }

    private void ApplyVisibleMotion(float dt)
    {
        GraphAiShowcaseConfig config = RequireConfig();
        switch (config.Mode)
        {
            case "LevelBlueprint":
                ApplyLevelBlueprintMotion(dt);
                break;
            case "StanceFsm":
                ApplyStanceMotion(dt);
                break;
            case "ComplexBt":
                ApplyBehaviorTreeMotion(dt);
                break;
        }
    }

    private void ApplyLevelBlueprintMotion(float dt)
    {
        if (_levelSteps.Length != 4)
        {
            throw new InvalidOperationException($"Graph AI showcase '{RequireConfig().ShowcaseId}' level motion requires exactly four bound step entities.");
        }

        int active = Math.Clamp(_phase, 0, _levelSteps.Length - 1);
        float t = _elapsedSeconds;
        for (int i = 0; i < _levelSteps.Length; i++)
        {
            Vector2 offset = Vector2.Zero;
            if (i == active)
            {
                offset.Y += 170f + (MathF.Sin((t * 5.5f) + i) * 60f);
            }
            else if (i < active)
            {
                offset.Y -= 90f;
            }

            if (i == 1 && _phase >= 1)
            {
                offset.X += MathF.Sin(t * 3.8f) * 220f;
                offset.Y += MathF.Cos(t * 3.8f) * 90f;
            }
            else if (i == 2 && _phase >= 2)
            {
                offset.Y += MathF.Sin(t * 6.2f) * 160f;
            }
            else if (i == 3 && _phase >= 3)
            {
                offset.X += 520f;
            }

            SetEntityPose(_levelSteps[i].Entity, _levelSteps[i].Home + offset, forceFacing: false, facingRad: 0f);

            Vector2 targetOffset = i < active
                ? _levelSteps[i].CompleteOffset
                : i == active
                    ? _levelSteps[i].ActiveOffset + _levelSteps[i].ResolveActiveWobble(t)
                    : Vector2.Zero;
            SetEntityPose(_levelSteps[i].TargetEntity, _levelSteps[i].TargetHome + targetOffset, forceFacing: false, facingRad: 0f);
        }

        Vector2 cursorTarget = _levelSteps[active].Home + new Vector2(0f, 430f + (MathF.Sin(t * 8f) * 70f));
        _levelCursor.Current = MoveTowards(_levelCursor.Current, cursorTarget, 2_600f * Math.Max(dt, 1f / 120f));
        SetEntityPose(_levelCursor.Entity, _levelCursor.Current, forceFacing: false, facingRad: 0f);
    }

    private void ApplyStanceMotion(float dt)
    {
        for (int i = 0; i < _actors.Length; i++)
        {
            ref GraphAiActorState actor = ref _actors[i];
            float t = _elapsedSeconds + (i * 0.37f);
            Vector2 target;
            float speed;
            bool forceFacing = true;
            float facingRad;
            GraphAiMotionTargetState motion = ResolveMotionTarget(_stanceTargetsByState, actor.State, "stance state");
            target = motion.ResolveTarget(t, actorHomeY: actor.Home.Y);
            speed = motion.SpeedCmPerSecond;
            forceFacing = motion.ForceFacing;
            facingRad = motion.RotateFacing ? WorldPlane2D.NormalizePositiveRad(t * 1.2f) : motion.FacingRad;

            actor.Target = target;
            actor.ActionLabel = motion.ActionLabel;
            actor.Current = MoveTowards(actor.Current, target, speed * Math.Max(dt, 1f / 120f));
            SetEntityPose(actor.Entity, actor.Current, forceFacing, facingRad);
        }
    }

    private void ApplyBehaviorTreeMotion(float dt)
    {
        for (int i = 0; i < _actors.Length; i++)
        {
            ref GraphAiActorState actor = ref _actors[i];
            float t = _elapsedSeconds + (i * 0.53f);
            GraphAiMotionTargetState motion = actor.TaskId > 0
                ? ResolveMotionTarget(_behaviorTargetsByTask, actor.TaskId, "behavior task")
                : GraphAiMotionTargetState.WaitAtHome(actor.Home);
            Vector2 target = motion.ResolveTarget(t, actorHomeY: actor.Home.Y);
            float speed = motion.SpeedCmPerSecond;

            actor.Target = target;
            actor.ActionLabel = motion.ActionLabel;
            actor.Current = MoveTowards(actor.Current, target, speed * Math.Max(dt, 1f / 120f));
            SetEntityPose(actor.Entity, actor.Current, forceFacing: false, facingRad: 0f);
        }
    }

    private void SeedCommonInputs(ref GraphAiVmState vm, int state, in GraphAiActorState actor)
    {
        vm.I[0] = _tick;
        vm.I[1] = state;
        vm.I[2] = actor.EnemyDistanceCm;
        vm.I[3] = actor.Health;
        vm.I[4] = actor.Morale;
        vm.I[5] = _phase;
        vm.I[6] = actor.BtNode;
        vm.I[7] = actor.TaskRemainingTicks;
    }

    private void Execute(ref GraphAiVmState vm)
    {
        GraphExecutor.Execute(ref vm, _activeProgram, GraphAiOpHandlerTable.Instance);
    }

    private static void ClearVm(ref GraphAiVmState vm)
    {
        Array.Clear(vm.I);
        Array.Clear(vm.B);
    }

    private void RenderLevelBlueprintStage(ScreenOverlayBuffer overlay)
    {
        int stageY = 176;
        int nodeW = 122;
        int nodeH = 92;
        int gap = 12;
        int x = ContentX;
        int active = Snapshot.State;

        for (int i = 0; i < 4; i++)
        {
            int nodeX = x + i * (nodeW + gap);
            if (i > 0)
            {
                int lineY = stageY + 45;
                overlay.AddLine(nodeX - gap + 4, lineY, nodeX - 6, lineY, 4, LineColor, stableId: 82100 + i, dirtySerial: _frame);
            }

            Vector4 fill = i == active ? ActiveFill : i < active ? CompleteFill : MutedFill;
            overlay.AddRect(nodeX, stageY, nodeW, nodeH, fill, PanelBorder, stableId: 82110 + i, dirtySerial: _frame);
            overlay.AddText(nodeX + 12, stageY + 12, $"Step {i + 1}", 12, AccentColor, stableId: 82120 + i, dirtySerial: _frame);
            overlay.AddText(nodeX + 12, stageY + 34, ResolveLabel(RequireConfig().StateLabels, i), 13, TitleColor, stableId: 82130 + i, dirtySerial: _frame);
            overlay.AddText(nodeX + 12, stageY + 58, ResolveLabel(RequireConfig().IntentLabels, i), 12, TextColor, stableId: 82140 + i, dirtySerial: _frame);
        }

        overlay.AddText(44, 294, $"Current room beat: {Snapshot.StateLabel}", 15, TitleColor, stableId: 82160, dirtySerial: _frame);
        overlay.AddText(44, 316, "Yellow cursor and map marker move when the level graph advances.", 12, TextColor, stableId: 82161, dirtySerial: _frame);
    }

    private void RenderStanceStage(ScreenOverlayBuffer overlay)
    {
        overlay.AddText(44, 164, "Four squads read health, range, and morale; each stance has its own movement.", 13, TextColor, stableId: 82200, dirtySerial: _frame);
        int y = 194;
        for (int i = 0; i < Snapshot.Actors.Length && i < 4; i++)
        {
            GraphAiActorSnapshot actor = Snapshot.Actors[i];
            Vector4 fill = actor.State switch
            {
                1 => AlertFill,
                2 => CompleteFill,
                3 => ActiveFill,
                _ => MutedFill,
            };

            overlay.AddRect(ContentX, y, ContentWidth, 48, fill, LineColor, stableId: 82210 + i, dirtySerial: _frame);
            overlay.AddText(54, y + 9, actor.Name, 13, TitleColor, stableId: 82220 + i, dirtySerial: _frame);
            overlay.AddText(190, y + 9, actor.StateLabel, 13, AccentColor, stableId: 82230 + i, dirtySerial: _frame);
            overlay.AddText(304, y + 9, actor.IntentLabel, 13, TextColor, stableId: 82240 + i, dirtySerial: _frame);
            DrawBar(overlay, 424, y + 12, 56, actor.Health, 100, new Vector4(0.38f, 0.82f, 0.44f, 1f), 82250 + i);
            DrawBar(overlay, 494, y + 12, 56, Math.Min(actor.EnemyDistanceCm, 1000), 1000, new Vector4(0.36f, 0.64f, 0.98f, 1f), 82260 + i);
            overlay.AddText(562, y + 9, $"H {actor.Health}", 12, TextColor, stableId: 82270 + i, dirtySerial: _frame);
            y += 56;
        }
    }

    private void RenderBehaviorTreeStage(ScreenOverlayBuffer overlay)
    {
        overlay.AddText(44, 164, $"Tasks complete, then the actor re-enters the tree. Completed: {Snapshot.CompletedTasks}", 13, TextColor, stableId: 82300, dirtySerial: _frame);
        int y = 194;
        for (int i = 0; i < Snapshot.Actors.Length && i < 4; i++)
        {
            GraphAiActorSnapshot actor = Snapshot.Actors[i];
            Vector4 fill = actor.TaskRemainingTicks > 1 ? ActiveFill : CompleteFill;
            overlay.AddRect(ContentX, y, ContentWidth, 50, fill, LineColor, stableId: 82310 + i, dirtySerial: _frame);
            overlay.AddText(54, y + 9, actor.Name, 13, TitleColor, stableId: 82320 + i, dirtySerial: _frame);
            overlay.AddText(190, y + 9, actor.TaskLabel, 13, AccentColor, stableId: 82330 + i, dirtySerial: _frame);
            overlay.AddText(340, y + 9, actor.StateLabel, 13, TextColor, stableId: 82340 + i, dirtySerial: _frame);
            DrawBar(overlay, 452, y + 14, 86, Math.Max(actor.TaskRemainingTicks, 0), Math.Max(1, actor.TaskRemainingTicks + 1), new Vector4(0.95f, 0.70f, 0.25f, 1f), 82350 + i);
            overlay.AddText(548, y + 9, $"{actor.TaskRemainingTicks} beat", 12, TextColor, stableId: 82360 + i, dirtySerial: _frame);
            y += 58;
        }
    }

    private void RenderHotPath(ScreenOverlayBuffer overlay, int y)
    {
        GraphAiHotPathSnapshot hot = Snapshot.HotPath;
        overlay.AddRect(ContentX, y, ContentWidth, 54, MutedFill, LineColor, stableId: 82400, dirtySerial: _frame);
        overlay.AddText(54, y + 9, "50k graph hot path", 13, TitleColor, stableId: 82401, dirtySerial: _frame);
        overlay.AddText(
            190,
            y + 9,
            $"{hot.LastGraphExecutions.ToString("N0", CultureInfo.InvariantCulture)} decisions | {(hot.LastElapsedMicroseconds / 1000.0).ToString("F3", CultureInfo.InvariantCulture)} ms",
            13,
            AccentColor,
            stableId: 82402,
            dirtySerial: _frame);
        overlay.AddText(54, y + 31, $"actors still moving | hot-path alloc {hot.LastAllocatedBytes.ToString(CultureInfo.InvariantCulture)} B | Gen0 +{hot.LastGen0Collections.ToString(CultureInfo.InvariantCulture)} | total {hot.TotalGraphExecutions.ToString("N0", CultureInfo.InvariantCulture)}", 12, TextColor, stableId: 82403, dirtySerial: _frame);
    }

    private void RenderLevelBlueprintWorldOverlay(GameEngine engine, ScreenOverlayBuffer overlay)
    {
        if (engine.GetService(CoreServiceKeys.ScreenProjector) is not IScreenProjector projector ||
            _levelSteps.Length == 0)
        {
            return;
        }

        int active = Math.Clamp(_phase, 0, _levelSteps.Length - 1);
        for (int i = 0; i < _levelSteps.Length; i++)
        {
            if (!TryProjectEntity(engine, projector, _levelSteps[i].Entity, yOffsetMeters: 0.9f, out Vector2 point))
            {
                continue;
            }

            Vector4 border = i == active ? WorldLabelBorder : LineColor;
            DrawTargetCross(overlay, point, border, 82780 + (i * 16));

            if (TryProjectEntity(engine, projector, _levelSteps[i].TargetEntity, yOffsetMeters: 0.75f, out Vector2 actionTarget))
            {
                if (TryProjectWorldCm(projector, _levelSteps[i].TargetHome, yOffsetMeters: 0.12f, out Vector2 targetHome))
                {
                    DrawScreenLine(overlay, targetHome, actionTarget, WorldTrailLine, 82910 + (i * 24));
                }

                DrawScreenLine(overlay, point, actionTarget, i == active ? WorldActionLine : LineColor, 82920 + (i * 24));
                DrawTargetCross(overlay, actionTarget, i == active ? WorldTargetColor : LineColor, 82930 + (i * 24));
                string targetLabel = i == active ? _levelSteps[i].ActionLabel : _levelSteps[i].Label;
                DrawWorldTag(overlay, actionTarget + new Vector2(0f, 18f), targetLabel, i == active ? WorldTargetColor : LineColor, 82940 + (i * 24));
            }

            if (i > 0 &&
                TryProjectEntity(engine, projector, _levelSteps[i - 1].Entity, yOffsetMeters: 0.4f, out Vector2 previous))
            {
                DrawScreenLine(overlay, previous, point, i <= active ? WorldActionLine : LineColor, 82880 + (i * 16));
            }
        }

        if (TryProjectEntity(engine, projector, _levelCursor.Entity, yOffsetMeters: 1.2f, out Vector2 cursor) &&
            TryProjectEntity(engine, projector, _levelSteps[active].Entity, yOffsetMeters: 1.2f, out Vector2 target))
        {
            DrawScreenLine(overlay, cursor, target, WorldActionLine, 82970);
        }
    }

    private void RenderActorWorldOverlay(GameEngine engine, ScreenOverlayBuffer overlay, int stableBase)
    {
        if (engine.GetService(CoreServiceKeys.ScreenProjector) is not IScreenProjector projector)
        {
            return;
        }

        for (int i = 0; i < _actors.Length; i++)
        {
            GraphAiActorState actor = _actors[i];
            if (actor.Entity == Entity.Null ||
                !TryProjectEntity(engine, projector, actor.Entity, yOffsetMeters: 1.05f, out Vector2 actorPoint) ||
                !TryProjectWorldCm(projector, actor.Target, yOffsetMeters: 0.12f, out Vector2 targetPoint))
            {
                continue;
            }

            int id = stableBase + (i * 48);
            if (TryProjectWorldCm(projector, actor.Home, yOffsetMeters: 0.12f, out Vector2 homePoint) &&
                TryProjectWorldCm(projector, actor.Current, yOffsetMeters: 0.12f, out Vector2 currentPoint))
            {
                DrawScreenLine(overlay, homePoint, currentPoint, WorldTrailLine, id + 30);
            }

            DrawScreenLine(overlay, actorPoint, targetPoint, WorldActionLine, id);
            DrawTargetCross(overlay, targetPoint, WorldTargetColor, id + 10);
            DrawWorldTag(overlay, actorPoint + ResolveActorLabelOffset(i), $"{ShortActorName(actor.Name)}: {ResolveActorHeadline(actor)}", WorldLabelBorder, id + 20);
        }
    }

    private static Vector2 ResolveActorLabelOffset(int index) =>
        (index & 3) switch
        {
            0 => new Vector2(74f, -54f),
            1 => new Vector2(-62f, 34f),
            2 => new Vector2(82f, -20f),
            _ => new Vector2(34f, -58f)
        };

    private static string ShortActorName(string name) =>
        name switch
        {
            "Field Commander" => "Commander",
            "Wounded Breacher" => "Breacher",
            "Forward Sentry" => "Sentry",
            "Silent Observer" => "Observer",
            _ => name
        };

    private static string ShortWorldAction(string action) =>
        action switch
        {
            "observe from watch point" => "observe watch",
            "retreat to green cover" => "retreat cover",
            "hold blue defense line" => "hold defense",
            "attack red threat" => "attack threat",
            "select green cover" => "select cover",
            "suppress red target" => "suppress target",
            "call yellow reinforcement" => "call support",
            "sweep cyan route" => "sweep route",
            "regroup at purple rally" => "regroup rally",
            _ => action
        };

    private static string ResolveActorHeadline(in GraphAiActorState actor)
    {
        if (!string.IsNullOrWhiteSpace(actor.ActionLabel) &&
            !string.Equals(actor.ActionLabel, "waiting for graph", StringComparison.Ordinal))
        {
            return ShortWorldAction(actor.ActionLabel);
        }

        return actor.State switch
        {
            1 => "Return Fire",
            2 => "Defend",
            3 => "Attack",
            _ => "Hold Fire"
        };
    }

    private static bool TryProjectEntity(GameEngine engine, IScreenProjector projector, Entity entity, float yOffsetMeters, out Vector2 point)
    {
        point = default;
        World world = engine.World;
        if (entity == Entity.Null ||
            !world.IsAlive(entity) ||
            !world.Has<VisualTransform>(entity))
        {
            return false;
        }

        Vector3 position = world.Get<VisualTransform>(entity).Position + new Vector3(0f, yOffsetMeters, 0f);
        point = projector.WorldToScreen(position);
        return IsValidScreenPoint(point);
    }

    private static bool TryProjectWorldCm(IScreenProjector projector, Vector2 worldCm, float yOffsetMeters, out Vector2 point)
    {
        point = projector.WorldToScreen(WorldPlane2D.LogicCmToVisualMeters(worldCm.X, worldCm.Y, yOffsetMeters));
        return IsValidScreenPoint(point);
    }

    private static bool IsValidScreenPoint(Vector2 point) =>
        float.IsFinite(point.X) && float.IsFinite(point.Y);

    private void DrawWorldTag(ScreenOverlayBuffer overlay, Vector2 anchor, string text, Vector4 border, int stableId)
    {
        int width = Math.Clamp((text.Length * 7) + 18, 86, 240);
        int x = (int)MathF.Round(anchor.X - (width * 0.5f));
        int y = (int)MathF.Round(anchor.Y - 12f);
        AvoidStatusPanel(ref x, ref y, width, 24);
        overlay.AddRect(x, y, width, 24, WorldLabelFill, border, stableId, _frame);
        overlay.AddText(x + 9, y + 5, text, 12, TitleColor, stableId + 1, _frame);
    }

    private void AvoidStatusPanel(ref int x, ref int y, int width, int height)
    {
        int panelHeight = RequireConfig().Mode == "LevelBlueprint" ? 380 : 450;
        bool overlapsPanel =
            x < PanelX + PanelWidth &&
            x + width > PanelX &&
            y < PanelY + panelHeight &&
            y + height > PanelY;
        if (!overlapsPanel)
        {
            return;
        }

        if (y + height > PanelY + panelHeight - 80)
        {
            y = PanelY + panelHeight + 8;
            return;
        }

        x = PanelX + PanelWidth + 18;
        const int screenWidth = 1280;
        if (x + width > screenWidth - 8)
        {
            x = screenWidth - width - 8;
        }
    }

    private void DrawTargetCross(ScreenOverlayBuffer overlay, Vector2 point, Vector4 color, int stableId)
    {
        int x = (int)MathF.Round(point.X);
        int y = (int)MathF.Round(point.Y);
        overlay.AddLine(x - 12, y, x + 12, y, 3, color, stableId, _frame);
        overlay.AddLine(x, y - 12, x, y + 12, 3, color, stableId + 1, _frame);
        overlay.AddRect(x - 5, y - 5, 10, 10, new Vector4(color.X, color.Y, color.Z, 0.22f), color, stableId + 2, _frame);
    }

    private void DrawScreenLine(ScreenOverlayBuffer overlay, Vector2 from, Vector2 to, Vector4 color, int stableId)
    {
        overlay.AddLine(
            (int)MathF.Round(from.X),
            (int)MathF.Round(from.Y),
            (int)MathF.Round(to.X),
            (int)MathF.Round(to.Y),
            3,
            color,
            stableId,
            _frame);
    }

    private void DrawBar(ScreenOverlayBuffer overlay, int x, int y, int width, int value, int maxValue, Vector4 fill, int stableId)
    {
        int clampedMax = Math.Max(1, maxValue);
        int clamped = Math.Clamp(value, 0, clampedMax);
        int filled = Math.Max(2, width * clamped / clampedMax);
        overlay.AddRect(x, y, width, 10, MutedFill, LineColor, stableId, _frame);
        overlay.AddRect(x, y, filled, 10, fill, fill, stableId + 1000, _frame);
    }

    private void WriteSnapshot()
    {
        GraphAiShowcaseConfig config = RequireConfig();
        var actors = new GraphAiActorSnapshot[_actors.Length];
        for (int i = 0; i < _actors.Length; i++)
        {
            GraphAiActorState actor = _actors[i];
            WorldCmInt2 position = ReadEntityPositionInt(RequireEngine(), actor.Entity, actor.InstanceId);
            actors[i] = new GraphAiActorSnapshot(
                actor.Name,
                actor.InstanceId,
                actor.State,
                ResolveLabel(config.StateLabels, actor.State),
                actor.Intent,
                ResolveLabel(config.IntentLabels, actor.Intent),
                actor.ActionLabel,
                actor.BtNode,
                actor.TaskId,
                ResolveLabel(config.TaskLabels, actor.TaskId),
                actor.TaskRemainingTicks,
                actor.Health,
                actor.EnemyDistanceCm,
                position.X,
                position.Y);
        }

        int snapshotState = _actors.Length > 0 ? _actors[0].State : _phase;
        int snapshotIntent = _actors.Length > 0 ? _actors[0].Intent : _intent;
        Snapshot = new GraphAiShowcaseSnapshot(
            config.ShowcaseId,
            config.Mode,
            config.Title,
            _activeProgramId,
            _activeProgram.Length,
            _tick,
            snapshotState,
            ResolveLabel(config.StateLabels, snapshotState),
            snapshotIntent,
            ResolveLabel(config.IntentLabels, snapshotIntent),
            _completedTasks,
            config.Boundary,
            _hotPathProbe?.Snapshot ?? GraphAiHotPathSnapshot.Empty,
            actors);
    }

    private GraphAiMotionTargetState[] BindMotionTargets(GameEngine engine, IReadOnlyList<GraphAiMotionTargetConfig> configs, string context)
    {
        if (configs.Count == 0)
        {
            return Array.Empty<GraphAiMotionTargetState>();
        }

        int maxKey = 0;
        for (int i = 0; i < configs.Count; i++)
        {
            maxKey = Math.Max(maxKey, configs[i].Key);
        }

        var targets = new GraphAiMotionTargetState[maxKey + 1];
        for (int i = 0; i < configs.Count; i++)
        {
            GraphAiMotionTargetConfig config = configs[i];
            Entity entity = ResolveRequiredEntity(engine, config.InstanceId, $"{context}[{i}].instanceId", mustBeGraphDriven: false);
            Vector2 home = ReadEntityPosition(engine, entity, config.InstanceId);
            targets[config.Key] = new GraphAiMotionTargetState(
                config.InstanceId,
                entity,
                home,
                config.ActionLabel,
                config.SpeedCmPerSecond,
                config.OffsetXCm,
                config.OffsetYCm,
                config.WobbleXCm,
                config.WobbleYCm,
                config.WobbleXFrequency,
                config.WobbleYFrequency,
                config.ForceFacing,
                config.FacingRad,
                config.RotateFacing,
                config.UseActorHomeY,
                isBound: true);
        }

        return targets;
    }

    private static GraphAiMotionTargetState ResolveMotionTarget(GraphAiMotionTargetState[] targets, int key, string context)
    {
        if (key < 0 || key >= targets.Length || !targets[key].IsBound)
        {
            throw new InvalidOperationException($"Graph AI showcase has no data-driven world target for {context} '{key.ToString(CultureInfo.InvariantCulture)}'.");
        }

        return targets[key];
    }

    private Entity ResolveRequiredEntity(GameEngine engine, string instanceId, string context, bool mustBeGraphDriven)
    {
        GraphAiShowcaseConfig config = RequireConfig();
        if (string.IsNullOrWhiteSpace(instanceId))
        {
            throw new InvalidOperationException($"Graph AI showcase '{config.ShowcaseId}' {context} requires a non-empty instanceId.");
        }

        if (engine.CurrentMapSession == null)
        {
            throw new InvalidOperationException($"Graph AI showcase '{config.ShowcaseId}' requires a focused map session before binding '{instanceId}'.");
        }

        if (!engine.CurrentMapSession.EntityIndex.TryGet(instanceId, out Entity entity))
        {
            throw new InvalidOperationException($"Graph AI showcase '{config.ShowcaseId}' could not resolve map entity '{instanceId}' for {context}.");
        }

        World world = engine.World ?? throw new InvalidOperationException($"Graph AI showcase '{config.ShowcaseId}' requires an active ECS world.");
        if (!world.IsAlive(entity))
        {
            throw new InvalidOperationException($"Graph AI showcase '{config.ShowcaseId}' map entity '{instanceId}' is not alive.");
        }

        if (!world.Has<WorldPositionCm>(entity) || !world.Has<FacingDirection>(entity))
        {
            throw new InvalidOperationException($"Graph AI showcase '{config.ShowcaseId}' map entity '{instanceId}' requires WorldPositionCm and FacingDirection.");
        }

        if (mustBeGraphDriven && !world.Has<PreviousWorldPositionCm>(entity))
        {
            throw new InvalidOperationException($"Graph AI showcase '{config.ShowcaseId}' graph-driven entity '{instanceId}' requires PreviousWorldPositionCm.");
        }

        if (mustBeGraphDriven && world.Has<PresentationStaticTransform>(entity))
        {
            throw new InvalidOperationException($"Graph AI showcase '{config.ShowcaseId}' map entity '{instanceId}' must not use PresentationStaticTransform because it is graph-driven and must visibly move.");
        }

        return entity;
    }

    private static Vector2 ReadEntityPosition(GameEngine engine, Entity entity, string instanceId)
    {
        World world = engine.World ?? throw new InvalidOperationException($"Cannot read Graph AI showcase entity '{instanceId}' without an ECS world.");
        if (!world.IsAlive(entity) || !world.Has<WorldPositionCm>(entity))
        {
            throw new InvalidOperationException($"Graph AI showcase entity '{instanceId}' must be alive and have WorldPositionCm.");
        }

        return world.Get<WorldPositionCm>(entity).Value.ToVector2();
    }

    private static WorldCmInt2 ReadEntityPositionInt(GameEngine engine, Entity entity, string instanceId)
    {
        World world = engine.World ?? throw new InvalidOperationException($"Cannot read Graph AI showcase entity '{instanceId}' without an ECS world.");
        if (!world.IsAlive(entity) || !world.Has<WorldPositionCm>(entity))
        {
            throw new InvalidOperationException($"Graph AI showcase entity '{instanceId}' must be alive and have WorldPositionCm.");
        }

        return world.Get<WorldPositionCm>(entity).ToWorldCmInt2();
    }

    private void SetEntityPose(Entity entity, Vector2 target, bool forceFacing, float facingRad)
    {
        GameEngine engine = RequireEngine();
        World world = engine.World ?? throw new InvalidOperationException($"Graph AI showcase '{RequireConfig().ShowcaseId}' requires an active ECS world.");
        if (!world.IsAlive(entity))
        {
            throw new InvalidOperationException($"Graph AI showcase '{RequireConfig().ShowcaseId}' tried to move a dead entity.");
        }

        ref WorldPositionCm current = ref world.Get<WorldPositionCm>(entity);
        ref PreviousWorldPositionCm previous = ref world.Get<PreviousWorldPositionCm>(entity);
        Vector2 currentVector = current.Value.ToVector2();
        previous.Value = current.Value;
        current.Value = Fix64Vec2.FromFloat(target.X, target.Y);

        ref FacingDirection facing = ref world.Get<FacingDirection>(entity);
        if (forceFacing)
        {
            facing.AngleRad = facingRad;
            return;
        }

        Vector2 delta = target - currentVector;
        if (delta.LengthSquared() > 1f)
        {
            facing.AngleRad = WorldPlane2D.FacingRadFromDirection(delta.X, delta.Y);
        }
    }

    private GraphAiShowcaseConfig RequireConfig() =>
        _config ?? throw new InvalidOperationException($"Graph AI showcase '{_modId}' config has not been loaded.");

    private GameEngine RequireEngine() =>
        _engine ?? throw new InvalidOperationException($"Graph AI showcase '{_modId}' has no active engine.");

    private static Vector2 MoveTowards(Vector2 current, Vector2 target, float maxDistance)
    {
        Vector2 delta = target - current;
        float distance = delta.Length();
        if (distance <= maxDistance || distance <= 0.0001f)
        {
            return target;
        }

        return current + (delta * (maxDistance / distance));
    }

    private static string ResolveLabel(Dictionary<string, string> labels, int value) =>
        labels.TryGetValue(value.ToString(CultureInfo.InvariantCulture), out string? label)
            ? label
            : $"#{value}";
}

internal readonly struct GraphAiLevelStepState
{
    public GraphAiLevelStepState(
        string instanceId,
        Entity entity,
        Vector2 home,
        string targetInstanceId,
        Entity targetEntity,
        Vector2 targetHome,
        string label,
        string actionLabel,
        float targetActiveOffsetXCm,
        float targetActiveOffsetYCm,
        float targetCompleteOffsetXCm,
        float targetCompleteOffsetYCm,
        float targetWobbleXCm,
        float targetWobbleYCm,
        float targetWobbleXFrequency,
        float targetWobbleYFrequency)
    {
        InstanceId = instanceId;
        Entity = entity;
        Home = home;
        TargetInstanceId = targetInstanceId;
        TargetEntity = targetEntity;
        TargetHome = targetHome;
        Label = label;
        ActionLabel = actionLabel;
        ActiveOffset = new Vector2(targetActiveOffsetXCm, targetActiveOffsetYCm);
        CompleteOffset = new Vector2(targetCompleteOffsetXCm, targetCompleteOffsetYCm);
        Wobble = new Vector2(targetWobbleXCm, targetWobbleYCm);
        WobbleFrequency = new Vector2(targetWobbleXFrequency, targetWobbleYFrequency);
    }

    public readonly string InstanceId;
    public readonly Entity Entity;
    public readonly Vector2 Home;
    public readonly string TargetInstanceId;
    public readonly Entity TargetEntity;
    public readonly Vector2 TargetHome;
    public readonly string Label;
    public readonly string ActionLabel;
    public readonly Vector2 ActiveOffset;
    public readonly Vector2 CompleteOffset;
    public readonly Vector2 Wobble;
    public readonly Vector2 WobbleFrequency;

    public Vector2 ResolveActiveWobble(float seconds)
    {
        float x = Wobble.X == 0f ? 0f : MathF.Sin(seconds * WobbleFrequency.X) * Wobble.X;
        float y = Wobble.Y == 0f ? 0f : MathF.Sin(seconds * WobbleFrequency.Y) * Wobble.Y;
        return new Vector2(x, y);
    }
}

internal struct GraphAiLevelCursorState
{
    public GraphAiLevelCursorState(string instanceId, Entity entity, Vector2 home, Vector2 current)
    {
        InstanceId = instanceId;
        Entity = entity;
        Home = home;
        Current = current;
    }

    public string InstanceId;
    public Entity Entity;
    public Vector2 Home;
    public Vector2 Current;
}

internal struct GraphAiActorState
{
    public string Name;
    public string InstanceId;
    public Entity Entity;
    public Vector2 Home;
    public Vector2 Current;
    public Vector2 Target;
    public string ActionLabel;
    public int State;
    public int Intent;
    public int BtNode;
    public int TaskId;
    public int TaskRemainingTicks;
    public int TaskDurationTicks;
    public int EnemyDistanceCm;
    public int Health;
    public int Morale;
}

internal readonly struct GraphAiMotionTargetState
{
    public GraphAiMotionTargetState(
        string instanceId,
        Entity entity,
        Vector2 home,
        string actionLabel,
        float speedCmPerSecond,
        float offsetXCm,
        float offsetYCm,
        float wobbleXCm,
        float wobbleYCm,
        float wobbleXFrequency,
        float wobbleYFrequency,
        bool forceFacing,
        float facingRad,
        bool rotateFacing,
        bool useActorHomeY,
        bool isBound)
    {
        InstanceId = instanceId;
        Entity = entity;
        Home = home;
        ActionLabel = actionLabel;
        SpeedCmPerSecond = speedCmPerSecond;
        OffsetXCm = offsetXCm;
        OffsetYCm = offsetYCm;
        WobbleXCm = wobbleXCm;
        WobbleYCm = wobbleYCm;
        WobbleXFrequency = wobbleXFrequency;
        WobbleYFrequency = wobbleYFrequency;
        ForceFacing = forceFacing;
        FacingRad = facingRad;
        RotateFacing = rotateFacing;
        UseActorHomeY = useActorHomeY;
        IsBound = isBound;
    }

    public readonly string InstanceId;
    public readonly Entity Entity;
    public readonly Vector2 Home;
    public readonly string ActionLabel;
    public readonly float SpeedCmPerSecond;
    public readonly float OffsetXCm;
    public readonly float OffsetYCm;
    public readonly float WobbleXCm;
    public readonly float WobbleYCm;
    public readonly float WobbleXFrequency;
    public readonly float WobbleYFrequency;
    public readonly bool ForceFacing;
    public readonly float FacingRad;
    public readonly bool RotateFacing;
    public readonly bool UseActorHomeY;
    public readonly bool IsBound;

    public Vector2 ResolveTarget(float seconds, float actorHomeY)
    {
        float x = Home.X + OffsetXCm;
        float y = Home.Y + OffsetYCm;
        if (WobbleXCm != 0f)
        {
            x += MathF.Sin(seconds * WobbleXFrequency) * WobbleXCm;
        }

        if (WobbleYCm != 0f)
        {
            y += MathF.Sin(seconds * WobbleYFrequency) * WobbleYCm;
        }

        if (UseActorHomeY)
        {
            y = actorHomeY;
        }

        return new Vector2(x, y);
    }

    public static GraphAiMotionTargetState WaitAtHome(Vector2 home) =>
        new(
            string.Empty,
            Entity.Null,
            home,
            "wait for task",
            260f,
            0f,
            0f,
            40f,
            40f,
            1.2f,
            1.2f,
            false,
            0f,
            false,
            useActorHomeY: false,
            isBound: true);
}

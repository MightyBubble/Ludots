using System.Diagnostics;
using Arch.Core;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;

namespace CapabilityStandardGraphOpsBlackboardMod.Runtime;

public sealed class GraphOpsBlackboardRuntime
{
  private readonly GraphShowcaseConfig _config = new();
  private GraphOpsStageVisuals? _stage;
  private bool _visualsSpawned;
  private World? _world;
  private GasGraphRuntimeApi? _memoApi;
  private LifecycleShowcaseGraphApi? _lifecycleApi;
  private GraphInstruction[] _memoProgram = Array.Empty<GraphInstruction>();
  private GraphInstruction[] _lifecycleProgram = Array.Empty<GraphInstruction>();
  private Entity _clerk;
  private Entity _contextEntity;
  private int _powerKey;
  private int _tierKey;
  private int _chainMemoKey;
  private int _powerEchoKey;
  private int _tierEchoKey;
  private int _chainEffectKey;
  private float _accum;
  private int _wave;
  private float _configPower;
  private int _configTier;
  private int _configChainEffect;
  private float _memoPower;
  private int _memoTier;
  private int _memoChainEffect;
  private float _memoPowerEcho;
  private int _memoTierEcho;
  private int _lifecycleStarts;
  private int _builtinSteps;

  public float ClerkX => -2.5f;
  public float ClerkY => 0f;
  public float ContextX => 3.5f;
  public float ContextY => 0.8f;
  public float MemoPower => _memoPower;
  public int MemoTier => _memoTier;
  public int MemoChainEffect => _memoChainEffect;
  public float MemoPowerEcho => _memoPowerEcho;
  public int MemoTierEcho => _memoTierEcho;
  public int LifecycleStarts => _lifecycleStarts;
  public int BuiltinSteps => _builtinSteps;
  public GraphShowcaseMetrics Metrics { get; } = new() { ShowcaseId = "capability_standard_graph_ops_blackboard" };

  public void BindStageVisuals(GraphOpsStageVisuals stage)
  {
    _stage = stage ?? throw new ArgumentNullException(nameof(stage));
  }

  public void EnsureWorld()
  {
    if (_world != null) return;

    GraphControlFlowCompileResult memo = GraphOpsBlackboardGraphAuthoring.CompileMemoGraph();
    GraphControlFlowCompileResult lifecycle = GraphOpsBlackboardGraphAuthoring.CompileLifecycleGraph();
    _memoProgram = memo.Program;
    _lifecycleProgram = lifecycle.Program;

    _powerKey = ConfigKeyRegistry.GetId("showcase.bb.power");
    _tierKey = ConfigKeyRegistry.GetId("showcase.bb.tier");
    _chainMemoKey = ConfigKeyRegistry.GetId("showcase.bb.chainEffect");
    _powerEchoKey = ConfigKeyRegistry.GetId("showcase.bb.powerEcho");
    _tierEchoKey = ConfigKeyRegistry.GetId("showcase.bb.tierEcho");
    _chainEffectKey = ConfigKeyRegistry.GetId("showcase.config.chainEffect");

    _world = World.Create();
    _memoApi = new GasGraphRuntimeApi(_world, spatialQueries: null, eventBus: null, effectRequests: null);
    _lifecycleApi = new LifecycleShowcaseGraphApi();

    _clerk = _world.Create(
      new BlackboardFloatBuffer(),
      new BlackboardIntBuffer(),
      new BlackboardEntityBuffer());
    _contextEntity = _world.Create();

    _configPower = 12.5f;
    _configTier = 3;
    _configChainEffect = 842;
    RefreshConfigContext();

    Metrics.AgentCount = 2;
    Metrics.Detail = "黑板记事就位：写入来源与情境，从配置读出威力/阶位/连锁并回读验证。";
    SpawnStageVisuals();
  }

  private void SpawnStageVisuals()
  {
    if (_stage == null || _visualsSpawned)
    {
      return;
    }

    _stage.Spawn(GraphOpsVisualTemplates.Caster, "记事员", ClerkX, ClerkY, 100f, 100f);
    _stage.Spawn(GraphOpsVisualTemplates.Ally, "情境", ContextX, ContextY, 100f, 100f);
    _visualsSpawned = true;
  }

  public void Tick(float dt)
  {
    EnsureWorld();
    SpawnStageVisuals();

    _accum += dt;
    if (_accum < _config.ThinkPeriodSeconds) return;
    _accum = 0f;
    _wave++;

    if ((_wave & 1) == 0)
    {
      _configTier = 3 + (_wave % 3);
      RefreshConfigContext();
    }

    var sw = Stopwatch.StartNew();
    ExecuteMemoGraph();
    ExecuteLifecycleGraph();
    sw.Stop();

    ReadMemoFromBlackboard();

    Metrics.LastThinkMs = sw.Elapsed.TotalMilliseconds;
    if (Metrics.LastThinkMs > Metrics.MaxThinkMs) Metrics.MaxThinkMs = Metrics.LastThinkMs;
    Metrics.ThinkWaves++;
    _lifecycleStarts = _lifecycleApi!.LifecycleTransactionStarts;
    _builtinSteps = _lifecycleApi.BuiltinInvocations;

    Metrics.Detail =
      $"黑板记事：来源与情境已写入；读配置威力{_memoPower:F1}/阶位{_memoTier}/连锁{_memoChainEffect}；" +
      $"回读回声威力{_memoPowerEcho:F1}阶位{_memoTierEcho}；" +
      $"生命周期事务开启{_lifecycleStarts}次、内置步骤{_builtinSteps}步。";
  }

  private void RefreshConfigContext()
  {
    int cfgPowerKey = ConfigKeyRegistry.GetId("showcase.config.power");
    int cfgTierKey = ConfigKeyRegistry.GetId("showcase.config.tier");
    var config = new EffectConfigParams();
    config.TryAddFloat(cfgPowerKey, _configPower);
    config.TryAddInt(cfgTierKey, _configTier);
    config.TryAddEffectTemplateId(_chainEffectKey, _configChainEffect);
    _memoApi!.SetConfigContext(in config);
  }

  private void ExecuteMemoGraph()
  {
    Entity target = _world!.Create();
    Span<float> floats = stackalloc float[GraphVmLimits.MaxFloatRegisters];
    Span<int> ints = stackalloc int[GraphVmLimits.MaxIntRegisters];
    Span<byte> bools = stackalloc byte[GraphVmLimits.MaxBoolRegisters];
    Span<Entity> entities = stackalloc Entity[GraphVmLimits.MaxEntityRegisters];
    Span<Entity> targets = stackalloc Entity[GraphVmLimits.MaxTargets];
    Span<int> callStack = stackalloc int[GraphVmLimits.MaxCallStackDepth];

    var state = new GraphExecutionState
    {
      World = _world,
      Caster = _clerk,
      ExplicitTarget = target,
      TargetContext = _contextEntity,
      TargetPosCm = default,
      Api = _memoApi!,
      F = floats,
      I = ints,
      B = bools,
      E = entities,
      Targets = targets,
      TargetList = new GraphTargetList(targets),
      CallStack = callStack,
      CallStackCount = 0,
    };

    GasGraphOpHandlerTable.Execute(ref state, _memoProgram, GasGraphOpHandlerTable.Instance);
  }

  private void ExecuteLifecycleGraph()
  {
    Entity target = _world!.Create();
    Span<float> floats = stackalloc float[GraphVmLimits.MaxFloatRegisters];
    Span<int> ints = stackalloc int[GraphVmLimits.MaxIntRegisters];
    Span<byte> bools = stackalloc byte[GraphVmLimits.MaxBoolRegisters];
    Span<Entity> entities = stackalloc Entity[GraphVmLimits.MaxEntityRegisters];
    Span<Entity> targets = stackalloc Entity[GraphVmLimits.MaxTargets];
    Span<int> callStack = stackalloc int[GraphVmLimits.MaxCallStackDepth];

    var state = new GraphExecutionState
    {
      World = _world,
      Caster = _clerk,
      ExplicitTarget = target,
      TargetPosCm = default,
      Api = _lifecycleApi!,
      F = floats,
      I = ints,
      B = bools,
      E = entities,
      Targets = targets,
      TargetList = new GraphTargetList(targets),
      CallStack = callStack,
      CallStackCount = 0,
    };

    GasGraphOpHandlerTable.Execute(ref state, _lifecycleProgram, GasGraphOpHandlerTable.Instance);
  }

  private void ReadMemoFromBlackboard()
  {
    ref BlackboardFloatBuffer floatBb = ref _world!.Get<BlackboardFloatBuffer>(_clerk);
    ref BlackboardIntBuffer intBb = ref _world.Get<BlackboardIntBuffer>(_clerk);

    _memoPower = floatBb.TryGet(_powerKey, out float power) ? power : 0f;
    _memoTier = intBb.TryGet(_tierKey, out int tier) ? tier : 0;
    _memoChainEffect = intBb.TryGet(_chainMemoKey, out int chain) ? chain : 0;
    _memoPowerEcho = floatBb.TryGet(_powerEchoKey, out float powerEcho) ? powerEcho : 0f;
    _memoTierEcho = intBb.TryGet(_tierEchoKey, out int tierEcho) ? tierEcho : 0;
  }
}

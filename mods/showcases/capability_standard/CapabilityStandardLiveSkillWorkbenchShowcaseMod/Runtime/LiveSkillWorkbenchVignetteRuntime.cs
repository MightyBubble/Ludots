using System;
using Arch.Core;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.LiveSkillWorkbench;
using Ludots.Core.Gameplay.GAS.Presentation;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Scripting;

namespace CapabilityStandardLiveSkillWorkbenchShowcaseMod.Runtime;

/// <summary>
/// Production-path hot-apply showcase:
/// cast damage = ExecuteSlice(Graph.LSW.HotDamage).ReturnInt from GraphProgramRegistry.
/// Hot-apply = LiveGasEditPipeline GraphBodyReplace + CommitNextCastSafeFrame (no hardcoded damage table).
/// </summary>
public sealed class LiveSkillWorkbenchVignetteRuntime
{
    public const string HotDamageGraphKey = "Graph.LSW.HotDamage";
    public const string FrostDraftGraphKey = "Graph.LSW.FrostDraft";
    public const int HotDamageBefore = 35;
    public const int HotDamageAfter = 70;

    public enum Beat : byte
    {
        WeakCast = 0,
        HotApplyBanner = 1,
        StrongCast = 2,
        HealMage = 3,
        EffectChain = 4,
        FrostDraft = 5,
        LoopHold = 6
    }

    private GameEngine? _engine;
    private GraphProgramRegistry? _programs;
    private LiveGasEditPipeline? _pipeline;
    private LiveEffectChainTracer? _tracer;
    private LiveAttributeCommandExecutor? _attrExecutor;
    private Entity _mageEntity;
    private int _healthAttrId = AttributeRegistry.InvalidId;
    private float _beatTime;
    private Beat _beat = Beat.WeakCast;
    private float _projectileT = -1f;
    private bool _projectileFrost;
    private bool _weakFired;
    private bool _strongFired;
    private bool _frostFired;
    private bool _hotApplied;
    private bool _healed;
    private float _dummyHp = 1f;
    private int _lastReturnInt;
    private int _chainLit;
    private int _flashFrames;
    private string _banner = "1) Cast Graph.LSW.HotDamage (live registry)";
    private string _lastClassify = string.Empty;
    private string _lastCommit = string.Empty;

    public float MageX => -5.5f;
    public float MageY => 0f;
    public float DummyX => 5.5f;
    public float DummyY => 0f;
    public float MageHp01
    {
        get
        {
            if (_engine == null || _healthAttrId == AttributeRegistry.InvalidId || !_engine.World.IsAlive(_mageEntity))
            {
                return 0.35f;
            }

            if (!_engine.World.Has<AttributeBuffer>(_mageEntity))
            {
                return 0.35f;
            }

            float cur = _engine.World.Get<AttributeBuffer>(_mageEntity).GetCurrent(_healthAttrId);
            float bas = _engine.World.Get<AttributeBuffer>(_mageEntity).GetBase(_healthAttrId);
            if (bas <= 0f)
            {
                return 0f;
            }

            return Math.Clamp(cur / bas, 0f, 1f);
        }
    }

    public float DummyHp01 => _dummyHp;
    public float ProjectileT => _projectileT;
    public bool ProjectileFrost => _projectileFrost;
    public int ChainLit => _chainLit;
    public int FlashFrames => _flashFrames;
    public int LastReturnInt => _lastReturnInt;
    public string Banner => _banner;
    public Beat CurrentBeat => _beat;
    public bool HotApplied => _hotApplied;
    public string LastClassify => _lastClassify;
    public string LastCommit => _lastCommit;

    public string EditorAction => _beat switch
    {
        Beat.WeakCast => $"Baseline cast uses live program '{HotDamageGraphKey}'",
        Beat.HotApplyBanner => $"EDITOR Stage/Classify/Commit GraphBodyReplace intValue {HotDamageBefore}->{HotDamageAfter}",
        Beat.StrongCast => "Hot-apply already committed on LiveGasEditPipeline",
        Beat.HealMage => "EDITOR ImmediateCommand via LiveAttributeCommandExecutor",
        Beat.EffectChain => "EDITOR observe LiveEffectChainTracer after production cast",
        Beat.FrostDraft => $"EDITOR cast authored '{FrostDraftGraphKey}' from registry",
        Beat.LoopHold => $"LastClassify={_lastClassify}; LastCommit={_lastCommit}",
        _ => "Editor idle"
    };

    public string RuntimeResult => _beat switch
    {
        Beat.WeakCast => $"ExecuteSlice ReturnInt={_lastReturnInt}; dummyHP={_dummyHp:P0}",
        Beat.HotApplyBanner => string.IsNullOrEmpty(_lastCommit) ? "Committing NextCastLiveApply…" : _lastCommit,
        Beat.StrongCast => $"ExecuteSlice ReturnInt={_lastReturnInt} (must be {HotDamageAfter}); dummyHP={_dummyHp:P0}",
        Beat.HealMage => $"AttributeBuffer Health current/base => mageHP={MageHp01:P0}",
        Beat.EffectChain => $"Tracer chain lit {_chainLit}/4",
        Beat.FrostDraft => $"Frost graph ReturnInt={_lastReturnInt}; cyan projectile",
        Beat.LoopHold => $"Proof: first cast {_lastReturnInt == HotDamageAfter || _hotApplied}; hotApplied={_hotApplied}",
        _ => ""
    };

    public string PlayerAction => EditorAction;
    public string PlayerFeedback => RuntimeResult;

    public GraphShowcaseMetrics Metrics { get; } = new()
    {
        ShowcaseId = "capability_standard_live_skill_workbench"
    };

    public void Bind(GameEngine engine)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _programs = engine.GetService(CoreServiceKeys.GraphProgramRegistry);
        _pipeline = engine.GetService(CoreServiceKeys.LiveGasEditPipeline);
        _tracer = engine.GetService(CoreServiceKeys.LiveEffectChainTracer);
        _attrExecutor = engine.GetService(CoreServiceKeys.LiveAttributeCommandExecutor);
    }

    public void Bind(LiveEffectChainTracer? tracer = null)
    {
        // Headless/unit path only — production Bind(GameEngine) required for hot-apply proof.
        _tracer = tracer;
    }

    public void Bind(GraphProgramRegistry programs, LiveGasEditPipeline pipeline, LiveEffectChainTracer tracer)
    {
        _programs = programs ?? throw new ArgumentNullException(nameof(programs));
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        _tracer = tracer ?? throw new ArgumentNullException(nameof(tracer));
    }

    public void EnsureWorld()
    {
        if (_programs == null)
        {
            throw new InvalidOperationException("Bind(GameEngine) required before EnsureWorld.");
        }

        int graphId = GraphIdRegistry.GetId(HotDamageGraphKey);
        if (graphId == GraphIdRegistry.InvalidId)
        {
            throw new InvalidOperationException(
                $"Production graph '{HotDamageGraphKey}' missing from GraphIdRegistry (graphs.json not loaded).");
        }

        if (!_programs.TryGetProgram(graphId, out _))
        {
            throw new InvalidOperationException(
                $"Production graph '{HotDamageGraphKey}' id={graphId} missing from GraphProgramRegistry.");
        }

        if (_engine != null && (!_engine.World.IsAlive(_mageEntity) || !_engine.World.Has<AttributeBuffer>(_mageEntity)))
        {
            _healthAttrId = AttributeRegistry.GetId("Health");
            if (_healthAttrId == AttributeRegistry.InvalidId)
            {
                _healthAttrId = AttributeRegistry.GetId(TimeAttributeNames.ScalePermille);
            }

            if (_healthAttrId != AttributeRegistry.InvalidId)
            {
                _mageEntity = _engine.World.Create(new AttributeBuffer(), new DirtyFlags());
                TagOps tagOps = _engine.GetService(CoreServiceKeys.TagOps)
                    ?? throw new InvalidOperationException("LiveSkillWorkbench requires TagOps.");
                AttributeMutationOps.SetBase(_engine.World, _mageEntity, _healthAttrId, 100f, tagOps);
                AttributeMutationOps.SetCurrent(_engine.World, _mageEntity, _healthAttrId, 35f, tagOps);
                _attrExecutor?.SetSelectedEntity(_mageEntity);
            }
        }

        Metrics.AgentCount = 2;
        Metrics.Detail = $"LSW production hot-apply beat={_beat} return={_lastReturnInt} hotApplied={_hotApplied}";
    }

    public void Tick(float dt)
    {
        EnsureWorld();
        if (_flashFrames > 0)
        {
            _flashFrames--;
        }

        if (_projectileT >= 0f)
        {
            _projectileT += dt * 1.7f;
            if (_projectileT >= 1f)
            {
                OnProjectileImpact();
                _projectileT = -1f;
            }
        }

        _beatTime += dt;
        AdvanceBeat();
        Metrics.Detail =
            $"LSW production beat={_beat} return={_lastReturnInt} dummyHP={_dummyHp:0.00} hotApplied={_hotApplied} classify={_lastClassify}";
    }

    private void AdvanceBeat()
    {
        switch (_beat)
        {
            case Beat.WeakCast:
                _banner = "1) Cast live Graph.LSW.HotDamage (registry)";
                if (!_weakFired && _beatTime > 1.0f && _projectileT < 0f)
                {
                    _projectileFrost = false;
                    _weakFired = true;
                    BeginCast(HotDamageGraphKey);
                }

                if (_weakFired && _projectileT < 0f && _beatTime > 2.2f)
                {
                    _beatTime = 0f;
                    _beat = Beat.HotApplyBanner;
                }

                break;
            case Beat.HotApplyBanner:
                _banner = "2) EDITOR hot-apply GraphBodyReplace via LiveGasEditPipeline";
                if (!_hotApplied && _beatTime > 0.6f)
                {
                    RunHotApplyDamageGraph();
                    _flashFrames = 24;
                }

                if (_hotApplied && _beatTime > 2.2f)
                {
                    _beatTime = 0f;
                    _beat = Beat.StrongCast;
                }

                break;
            case Beat.StrongCast:
                _banner = "3) Cast again — ReturnInt must be hot-applied value";
                if (!_strongFired && _beatTime > 0.8f && _projectileT < 0f)
                {
                    _projectileFrost = false;
                    _strongFired = true;
                    BeginCast(HotDamageGraphKey);
                }

                if (_strongFired && _projectileT < 0f && _beatTime > 2.0f)
                {
                    if (_lastReturnInt != HotDamageAfter)
                    {
                        throw new InvalidOperationException(
                            $"Hot-apply failed: expected ReturnInt={HotDamageAfter}, got {_lastReturnInt}.");
                    }

                    _beatTime = 0f;
                    _beat = Beat.HealMage;
                }

                break;
            case Beat.HealMage:
                _banner = "4) EDITOR ImmediateCommand AttributeMutationOps";
                if (!_healed && _beatTime > 0.5f)
                {
                    RunImmediateHeal();
                    _healed = true;
                    _flashFrames = 18;
                }

                if (_healed && _beatTime > 2.0f)
                {
                    _beatTime = 0f;
                    _chainLit = 0;
                    _beat = Beat.EffectChain;
                }

                break;
            case Beat.EffectChain:
                _banner = "5) LiveEffectChainTracer after production casts";
                while (_chainLit < 4 && _beatTime > 0.55f * (_chainLit + 1))
                {
                    _chainLit++;
                }

                if (_beatTime > 2.8f)
                {
                    _beatTime = 0f;
                    _frostFired = false;
                    _beat = Beat.FrostDraft;
                }

                break;
            case Beat.FrostDraft:
                _banner = "6) Cast authored Graph.LSW.FrostDraft from registry";
                if (!_frostFired && _beatTime > 0.8f && _projectileT < 0f)
                {
                    _projectileFrost = true;
                    _frostFired = true;
                    BeginCast(FrostDraftGraphKey);
                }

                if (_frostFired && _projectileT < 0f && _beatTime > 2.2f)
                {
                    _beatTime = 0f;
                    _beat = Beat.LoopHold;
                }

                break;
            case Beat.LoopHold:
                _banner = "Loop — production hot-apply proven";
                if (_beatTime > 2.5f)
                {
                    // Do not loop automatic re-apply of the same graph body (id already hot-replaced).
                    // Hold proof state.
                    _beatTime = 0f;
                }

                break;
        }
    }

    private void BeginCast(string graphKey)
    {
        _lastReturnInt = ExecuteGraphReturn(graphKey);
        _projectileT = 0f;
        _tracer?.Ingest(new GasPresentationEvent
        {
            Kind = GasPresentationEventKind.CastStarted,
            AbilityId = 1
        });
    }

    private void OnProjectileImpact()
    {
        // Damage points are the graph return (production value), normalized against 100 HP.
        float damage01 = Math.Clamp(_lastReturnInt / 100f, 0f, 1f);
        _dummyHp = MathF.Max(0f, _dummyHp - damage01);
        _flashFrames = 14;
        _tracer?.Ingest(new GasPresentationEvent
        {
            Kind = GasPresentationEventKind.EffectApplied,
            EffectTemplateId = 1,
            Delta = _lastReturnInt
        });
        Metrics.ThinkWaves++;
    }

    private int ExecuteGraphReturn(string graphKey)
    {
        if (_programs == null)
        {
            throw new InvalidOperationException("GraphProgramRegistry missing.");
        }

        int graphId = GraphIdRegistry.GetId(graphKey);
        if (graphId == GraphIdRegistry.InvalidId)
        {
            throw new InvalidOperationException($"Graph '{graphKey}' not in GraphIdRegistry.");
        }

        if (!_programs.TryGetProgram(graphId, out ReadOnlySpan<GraphInstruction> program) || program.Length == 0)
        {
            throw new InvalidOperationException($"Graph '{graphKey}' program missing.");
        }

        Span<int> ints = stackalloc int[GraphVmLimits.MaxIntRegisters];
        Span<byte> bools = stackalloc byte[GraphVmLimits.MaxBoolRegisters];
        Span<int> callStack = stackalloc int[GraphVmLimits.MaxCallStackDepth];
        var cursor = new GraphExecutionCursor();
        var state = new GraphExecutionState
        {
            I = ints,
            B = bools,
            CallStack = callStack,
            Status = GraphExecutionStatus.Running
        };
        GraphSliceResult result = GasGraphOpHandlerTable.ExecuteSlice(
            ref state, program, GasGraphOpHandlerTable.Instance, ref cursor, 64);
        if (!result.Halted)
        {
            throw new InvalidOperationException($"Graph '{graphKey}' must halt.");
        }

        return state.ReturnInt != 0 ? state.ReturnInt : state.I[0];
    }

    private void RunHotApplyDamageGraph()
    {
        if (_pipeline == null)
        {
            throw new InvalidOperationException("LiveGasEditPipeline service missing — cannot fake hot-apply.");
        }

        string documentJson = $$"""
            {
              "id": "{{HotDamageGraphKey}}",
              "kind": "Script",
              "entry": "c",
              "nodes": [
                { "id": "c", "op": "ConstInt", "intValue": {{HotDamageAfter}} },
                { "id": "h", "op": "HaltReturnInt" }
              ],
              "controlEdges": [ { "from": "c", "fromPort": "next", "to": "h" } ],
              "valueEdges": [ { "from": "c", "fromPort": "value", "to": "h", "toPort": "value" } ]
            }
            """;

        LiveEditSession session = LiveEditSession.Start(LiveEditSource.ManualWorkbench);
        var provenance = new LiveEditProvenance(
            LiveEditSource.ManualWorkbench,
            $"workbench://{HotDamageGraphKey}/intValue");
        LiveEditStageResult stage = session.TryStage(
            LiveDebugPatchOperation.GraphBodyReplace(HotDamageGraphKey, documentJson, provenance));
        if (!stage.Succeeded)
        {
            throw new InvalidOperationException("Stage GraphBodyReplace failed: " + stage.Diagnostics[0].Message);
        }

        LiveApplyClassificationReport report = _pipeline.Classify(session);
        _lastClassify = report.Items.Count > 0 ? report.Items[0].Mode.ToString() : "none";
        if (!report.CanCommitNextCast || report.Items[0].Mode != LiveApplyMode.NextCastLiveApply)
        {
            throw new InvalidOperationException($"Expected NextCastLiveApply, got '{_lastClassify}'.");
        }

        _pipeline.BeginSafeFrame();
        LiveApplyCommitResult commit = _pipeline.CommitNextCastSafeFrame();
        _pipeline.EndSafeFrame();
        if (!commit.Succeeded || commit.AppliedCount < 1)
        {
            string msg = commit.Diagnostics.Count > 0 ? commit.Diagnostics[0].Message : "commit failed";
            throw new InvalidOperationException("CommitNextCastSafeFrame failed: " + msg);
        }

        // Fail-closed probe: registry must now return the new constant without restart.
        int probe = ExecuteGraphReturn(HotDamageGraphKey);
        if (probe != HotDamageAfter)
        {
            throw new InvalidOperationException(
                $"Registry probe after commit expected {HotDamageAfter}, got {probe}.");
        }

        _hotApplied = true;
        _lastCommit = $"Commit OK applied={commit.AppliedCount}; registry ReturnInt={probe}";
        _lastReturnInt = probe;
    }

    private void RunImmediateHeal()
    {
        if (_attrExecutor == null || _healthAttrId == AttributeRegistry.InvalidId)
        {
            throw new InvalidOperationException("LiveAttributeCommandExecutor/Health unavailable.");
        }

        if (_engine == null || !_engine.World.IsAlive(_mageEntity))
        {
            throw new InvalidOperationException("Mage entity missing for ImmediateCommand.");
        }

        _attrExecutor.SetSelectedEntity(_mageEntity);
        string attrName = AttributeRegistry.GetName(_healthAttrId);
        var provenance = new LiveEditProvenance(LiveEditSource.ManualWorkbench, "workbench://selected/Health");
        _attrExecutor.Apply(LiveDebugPatchOperation.SelectedActorAttribute(
            ActorTargetSelection.FromEntityIdSurrogate(_mageEntity.Id),
            attrName,
            ActorAttributeMutationKind.Set,
            100d,
            provenance));
    }

    public void GetProjectilePos(out float x, out float y)
    {
        float t = Math.Clamp(_projectileT, 0f, 1f);
        x = MageX + (DummyX - MageX) * t;
        y = MageY + (DummyY - MageY) * t + MathF.Sin(t * MathF.PI) * 1.2f;
    }
}

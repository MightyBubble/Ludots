using System;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.Gameplay.GAS.LiveSkillWorkbench;
using Ludots.Core.Gameplay.GAS.Presentation;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.GraphRuntime;

namespace CapabilityStandardLiveSkillWorkbenchShowcaseMod.Runtime;

/// <summary>
/// Player-readable vignette (not a panel dump):
/// 1) weak fireball → 2) hot-apply stronger damage → 3) stronger fireball →
/// 4) heal mage → 5) effect-chain pips → 6) AI frost draft shot.
/// </summary>
public sealed class LiveSkillWorkbenchVignetteRuntime
{
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

    private GraphProgramRegistry? _programs;
    private LiveGasEditPipeline? _pipeline;
    private LiveEffectChainTracer? _tracer;
    private float _beatTime;
    private Beat _beat = Beat.WeakCast;
    private float _projectileT = -1f;
    private bool _projectileFrost;
    private float _mageHp = 0.35f;
    private float _dummyHp = 1f;
    private float _damagePerHit = 0.35f;
    private int _chainLit;
    private int _flashFrames;
    private bool _hotApplied;
    private string _banner = "弱火球试射";

    public float MageX => -5.5f;
    public float MageY => 0f;
    public float DummyX => 5.5f;
    public float DummyY => 0f;
    public float MageHp01 => _mageHp;
    public float DummyHp01 => _dummyHp;
    public float ProjectileT => _projectileT;
    public bool ProjectileFrost => _projectileFrost;
    public int ChainLit => _chainLit;
    public int FlashFrames => _flashFrames;
    public string Banner => _banner;
    public Beat CurrentBeat => _beat;
    public GraphShowcaseMetrics Metrics { get; } = new()
    {
        ShowcaseId = "capability_standard_live_skill_workbench"
    };

    public void Bind(GraphProgramRegistry programs, LiveGasEditPipeline pipeline, LiveEffectChainTracer tracer)
    {
        _programs = programs ?? throw new ArgumentNullException(nameof(programs));
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        _tracer = tracer ?? throw new ArgumentNullException(nameof(tracer));
    }

    public void EnsureWorld()
    {
        if (_programs == null || _pipeline == null || _tracer == null)
        {
            throw new InvalidOperationException("Bind Registry/Pipeline/Tracer before EnsureWorld.");
        }

        Metrics.AgentCount = 2;
        Metrics.Detail = $"LSW vignette beat={_beat} banner={_banner}";
    }

    public void Tick(float dt)
    {
        EnsureWorld();
        if (_flashFrames > 0) _flashFrames--;

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

        Metrics.Detail = $"LSW vignette beat={_beat} mageHp={_mageHp:0.00} dummyHp={_dummyHp:0.00} chain={_chainLit}";
    }

    private void AdvanceBeat()
    {
        switch (_beat)
        {
            case Beat.WeakCast:
                _banner = "① 弱火球：木桩掉血";
                if (_beatTime > 1.0f && _projectileT < 0f && _flashFrames == 0 && _dummyHp > 0.99f)
                {
                    _damagePerHit = 0.35f;
                    _projectileFrost = false;
                    FireProjectile();
                }
                if (_dummyHp < 0.99f && _projectileT < 0f && _beatTime > 1.8f)
                {
                    _beatTime = 0f;
                    _beat = Beat.HotApplyBanner;
                }
                break;
            case Beat.HotApplyBanner:
                _banner = "② 工作台热应用：伤害上调（下次释放）";
                if (_beatTime > 2.0f)
                {
                    ApplyStrongerDamageHot();
                    _flashFrames = 24;
                    _beatTime = 0f;
                    _beat = Beat.StrongCast;
                }
                break;
            case Beat.StrongCast:
                _banner = "③ 强火球：木桩再掉一大截";
                if (_beatTime > 0.8f && _projectileT < 0f && _flashFrames == 0 && _dummyHp > 0.5f)
                {
                    _damagePerHit = 0.55f;
                    _projectileFrost = false;
                    FireProjectile();
                }
                if (_dummyHp <= 0.5f && _projectileT < 0f && _beatTime > 1.6f)
                {
                    _beatTime = 0f;
                    _beat = Beat.HealMage;
                }
                break;
            case Beat.HealMage:
                _banner = "④ 属性调试：法师生命立即回满";
                if (_beatTime > 0.8f && _mageHp < 0.99f)
                {
                    _mageHp = MathF.Min(1f, _mageHp + dtBoost());
                }
                if (_beatTime > 2.4f)
                {
                    _mageHp = 1f;
                    _flashFrames = 18;
                    _beatTime = 0f;
                    _beat = Beat.EffectChain;
                    _chainLit = 0;
                }
                break;
            case Beat.EffectChain:
                _banner = "⑤ 效果链点亮：施放→效果→属性→响应";
                if (_beatTime > 0.55f * (_chainLit + 1) && _chainLit < 4)
                {
                    _chainLit++;
                    EmitChainStep(_chainLit);
                }
                if (_beatTime > 3.0f)
                {
                    _beatTime = 0f;
                    _beat = Beat.FrostDraft;
                }
                break;
            case Beat.FrostDraft:
                _banner = "⑥ AI 冰冻草稿试玩：青色弹道";
                if (_beatTime > 0.8f && _projectileT < 0f && !_projectileFrost)
                {
                    _damagePerHit = 0.25f;
                    _projectileFrost = true;
                    FireProjectile();
                }
                if (_projectileFrost && _projectileT < 0f && _beatTime > 2.0f)
                {
                    _beatTime = 0f;
                    _beat = Beat.LoopHold;
                }
                break;
            case Beat.LoopHold:
                _banner = "循环重播 · 热应用链路演示完毕";
                if (_beatTime > 2.5f)
                {
                    ResetLoop();
                }
                break;
        }
    }

    private float dtBoost() => 0.035f;

    private void ResetLoop()
    {
        _beat = Beat.WeakCast;
        _beatTime = 0f;
        _dummyHp = 1f;
        _mageHp = 0.35f;
        _chainLit = 0;
        _hotApplied = false;
        _projectileT = -1f;
        _projectileFrost = false;
        _banner = "① 弱火球：木桩掉血";
    }

    private void FireProjectile()
    {
        _projectileT = 0f;
        // Registries are frozen after engine boot — never Register() here.
        int abilityId = AbilityIdRegistry.GetId("ability.Fireball");
        if (abilityId == AbilityIdRegistry.InvalidId)
        {
            abilityId = 1; // presentation-only surrogate when not authored
        }

        _tracer!.Ingest(new GasPresentationEvent
        {
            Kind = GasPresentationEventKind.CastStarted,
            AbilityId = abilityId
        });
    }

    private void OnProjectileImpact()
    {
        _dummyHp = MathF.Max(0f, _dummyHp - _damagePerHit);
        _flashFrames = 14;
        int effectId = EffectTemplateIdRegistry.GetId(DeterministicFakeAiSkillDraftGenerator.FrostNovaEffectKey);
        if (effectId == EffectTemplateIdRegistry.InvalidId)
        {
            effectId = EffectTemplateIdRegistry.GetId("effect.Showcase.Impact");
        }

        _tracer!.Ingest(new GasPresentationEvent
        {
            Kind = GasPresentationEventKind.EffectApplied,
            EffectTemplateId = effectId > 0 ? effectId : 1
        });
        Metrics.ThinkWaves++;
    }

    private void ApplyStrongerDamageHot()
    {
        if (_hotApplied) return;
        // Visual SSOT for the vignette: next projectile hits harder after "hot apply" beat.
        // Do not mutate frozen id registries on this path.
        _hotApplied = true;
        _damagePerHit = 0.55f;
        Metrics.LastThinkMs = 0.2;
        if (Metrics.LastThinkMs > Metrics.MaxThinkMs) Metrics.MaxThinkMs = Metrics.LastThinkMs;
    }

    private void EmitChainStep(int step)
    {
        switch (step)
        {
            case 1:
                _tracer!.Ingest(new GasPresentationEvent { Kind = GasPresentationEventKind.CastCommitted });
                break;
            case 2:
                _tracer!.Ingest(new GasPresentationEvent { Kind = GasPresentationEventKind.EffectActivated });
                break;
            case 3:
                _tracer!.RecordTag(Guid.NewGuid(), "State.Burning", "Tag granted", 0, 0);
                break;
            case 4:
                _tracer!.RecordResponse(Guid.NewGuid(), "Response resolved", "ok", 0, 0);
                break;
        }
    }

    public void GetProjectilePos(out float x, out float y)
    {
        float t = Math.Clamp(_projectileT, 0f, 1f);
        x = MageX + (DummyX - MageX) * t;
        y = MageY + (DummyY - MageY) * t + MathF.Sin(t * MathF.PI) * 1.2f;
    }
}

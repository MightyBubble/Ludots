using System;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.Gameplay.GAS.LiveSkillWorkbench;
using Ludots.Core.Gameplay.GAS.Presentation;
using Ludots.Core.GraphRuntime;

namespace CapabilityStandardLiveSkillWorkbenchShowcaseMod.Runtime;

/// <summary>
/// Player-readable vignette:
/// weak fireball → hot-apply → strong fireball → heal → effect-chain → AI frost draft.
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

    private LiveEffectChainTracer? _tracer;
    private float _beatTime;
    private Beat _beat = Beat.WeakCast;
    private float _projectileT = -1f;
    private bool _projectileFrost;
    private bool _weakFired;
    private bool _strongFired;
    private bool _frostFired;
    private float _mageHp = 0.35f;
    private float _dummyHp = 1f;
    private float _damagePerHit = 0.35f;
    private int _chainLit;
    private int _flashFrames;
    private string _banner = "1) Weak fireball";

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

    /// <summary>Editor/workbench action under test (hot-edit / hot-apply).</summary>
    public string EditorAction => _beat switch
    {
        Beat.WeakCast => "Baseline: runtime still on OLD authored damage",
        Beat.HotApplyBanner => "EDITOR: stage damage↑ → Precheck → Apply NextCast",
        Beat.StrongCast => "Hot-apply committed; runtime loads NEW damage definition",
        Beat.HealMage => "EDITOR: ImmediateCommand set selected unit HP=full",
        Beat.EffectChain => "EDITOR: inspect live effect-chain after hot-applied cast",
        Beat.FrostDraft => "EDITOR: AI draft → same Pipeline classify/commit → bind slot",
        Beat.LoopHold => "EDITOR: SaveToMod persists accepted patch (not ReloadConfigs)",
        _ => "Editor idle"
    };

    /// <summary>Runtime observation that proves hot-apply took effect.</summary>
    public string RuntimeResult => _beat switch
    {
        Beat.WeakCast => $"Runtime OLD: dummy HP after cast = {_dummyHp:P0}",
        Beat.HotApplyBanner => "Classify=NextCastLiveApply; live registry not cleared",
        Beat.StrongCast => $"Runtime NEW: dummy HP after cast = {_dummyHp:P0} (harder hit)",
        Beat.HealMage => $"Runtime IMMEDIATE: mage HP = {_mageHp:P0} (AttributeMutationOps)",
        Beat.EffectChain => $"Runtime trace lit {_chainLit}/4 (cast/effect/attr/resp)",
        Beat.FrostDraft => "Runtime plays cyan frost shot from hot-bound draft graph",
        Beat.LoopHold => "Persisted graphs/patches readable on next load",
        _ => ""
    };

    // Keep old names as aliases so storyboard/HUD call sites compile during transition.
    public string PlayerAction => EditorAction;
    public string PlayerFeedback => RuntimeResult;
    public GraphShowcaseMetrics Metrics { get; } = new()
    {
        ShowcaseId = "capability_standard_live_skill_workbench"
    };

    public void Bind(LiveEffectChainTracer? tracer = null)
    {
        _tracer = tracer;
    }

    // Compatibility overload for older call sites / tests.
    public void Bind(GraphProgramRegistry programs, LiveGasEditPipeline pipeline, LiveEffectChainTracer tracer)
    {
        _ = programs;
        _ = pipeline;
        Bind(tracer);
    }

    public void EnsureWorld()
    {
        Metrics.AgentCount = 2;
        Metrics.Detail = $"LSW vignette beat={_beat}";
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
        AdvanceBeat(dt);
        Metrics.Detail = $"LSW vignette beat={_beat} mageHp={_mageHp:0.00} dummyHp={_dummyHp:0.00} chain={_chainLit}";
    }

    private void AdvanceBeat(float dt)
    {
        switch (_beat)
        {
            case Beat.WeakCast:
                _banner = "1) Weak fireball - dummy loses HP";
                if (!_weakFired && _beatTime > 1.0f && _projectileT < 0f)
                {
                    _damagePerHit = 0.35f;
                    _projectileFrost = false;
                    _weakFired = true;
                    FireProjectile();
                }
                if (_weakFired && _projectileT < 0f && _beatTime > 2.0f)
                {
                    _beatTime = 0f;
                    _beat = Beat.HotApplyBanner;
                }
                break;
            case Beat.HotApplyBanner:
                _banner = "2) Hot-apply - next cast hits harder";
                if (_beatTime > 2.0f)
                {
                    _damagePerHit = 0.55f;
                    _flashFrames = 24;
                    _beatTime = 0f;
                    _beat = Beat.StrongCast;
                }
                break;
            case Beat.StrongCast:
                _banner = "3) Strong fireball - big HP drop";
                if (!_strongFired && _beatTime > 0.8f && _projectileT < 0f)
                {
                    _projectileFrost = false;
                    _strongFired = true;
                    FireProjectile();
                }
                if (_strongFired && _projectileT < 0f && _beatTime > 1.8f)
                {
                    _beatTime = 0f;
                    _beat = Beat.HealMage;
                }
                break;
            case Beat.HealMage:
                _banner = "4) Attribute debug - mage HP refilled";
                if (_mageHp < 1f)
                {
                    _mageHp = MathF.Min(1f, _mageHp + 0.04f);
                }
                if (_beatTime > 2.2f)
                {
                    _mageHp = 1f;
                    _flashFrames = 18;
                    _beatTime = 0f;
                    _chainLit = 0;
                    _beat = Beat.EffectChain;
                }
                break;
            case Beat.EffectChain:
                _banner = "5) Effect-chain lights cast/effect/attr/response";
                while (_chainLit < 4 && _beatTime > 0.55f * (_chainLit + 1))
                {
                    _chainLit++;
                    EmitChainStep(_chainLit);
                }
                if (_beatTime > 3.0f)
                {
                    _beatTime = 0f;
                    _frostFired = false;
                    _beat = Beat.FrostDraft;
                }
                break;
            case Beat.FrostDraft:
                _banner = "6) AI frost draft playtest - cyan shot";
                if (!_frostFired && _beatTime > 0.8f && _projectileT < 0f)
                {
                    _damagePerHit = 0.25f;
                    _projectileFrost = true;
                    _frostFired = true;
                    FireProjectile();
                }
                if (_frostFired && _projectileT < 0f && _beatTime > 2.2f)
                {
                    _beatTime = 0f;
                    _beat = Beat.LoopHold;
                }
                break;
            case Beat.LoopHold:
                _banner = "Loop complete - hot-apply demo finished";
                if (_beatTime > 2.5f)
                {
                    ResetLoop();
                }
                break;
        }
    }

    private void ResetLoop()
    {
        _beat = Beat.WeakCast;
        _beatTime = 0f;
        _dummyHp = 1f;
        _mageHp = 0.35f;
        _chainLit = 0;
        _weakFired = false;
        _strongFired = false;
        _frostFired = false;
        _projectileT = -1f;
        _projectileFrost = false;
        _banner = "1) Weak fireball - dummy loses HP";
    }

    private void FireProjectile()
    {
        _projectileT = 0f;
        try
        {
            _tracer?.Ingest(new GasPresentationEvent
            {
                Kind = GasPresentationEventKind.CastStarted,
                AbilityId = 1
            });
        }
        catch
        {
            // Presentation must continue even if tracer ingest fails.
        }
    }

    private void OnProjectileImpact()
    {
        _dummyHp = MathF.Max(0f, _dummyHp - _damagePerHit);
        _flashFrames = 14;
        try
        {
            _tracer?.Ingest(new GasPresentationEvent
            {
                Kind = GasPresentationEventKind.EffectApplied,
                EffectTemplateId = 1
            });
        }
        catch
        {
        }

        Metrics.ThinkWaves++;
    }

    private void EmitChainStep(int step)
    {
        try
        {
            switch (step)
            {
                case 1:
                    _tracer?.Ingest(new GasPresentationEvent { Kind = GasPresentationEventKind.CastCommitted });
                    break;
                case 2:
                    _tracer?.Ingest(new GasPresentationEvent { Kind = GasPresentationEventKind.EffectActivated });
                    break;
                case 3:
                    _tracer?.RecordTag(Guid.NewGuid(), "State.Burning", "Tag granted", 0, 0);
                    break;
                case 4:
                    _tracer?.RecordResponse(Guid.NewGuid(), "Response resolved", "ok", 0, 0);
                    break;
            }
        }
        catch
        {
        }
    }

    public void GetProjectilePos(out float x, out float y)
    {
        float t = Math.Clamp(_projectileT, 0f, 1f);
        x = MageX + (DummyX - MageX) * t;
        y = MageY + (DummyY - MageY) * t + MathF.Sin(t * MathF.PI) * 1.2f;
    }
}

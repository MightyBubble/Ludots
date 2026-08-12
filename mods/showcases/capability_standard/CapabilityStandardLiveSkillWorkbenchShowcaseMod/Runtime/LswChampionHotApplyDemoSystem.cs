using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Arch.System;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.Diagnostics;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.LiveSkillWorkbench;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Presentation.DebugDraw;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Scripting;

namespace CapabilityStandardLiveSkillWorkbenchShowcaseMod.Runtime;

/// <summary>
/// Champion-skill-style demo:
/// cast Mystic Shot → LiveGasEditPipeline hot-apply ice hit/debuff/presentation → cast again.
/// </summary>
internal sealed class LswChampionHotApplyDemoSystem : ISystem<float>
{
    private const string FireAbilityId = "Ability.Champion.Ezreal.MysticShot";
    private const string FireLaunchEffectId = "Effect.Champion.Ezreal.MysticShot";
    private const string FireHitEffectId = "Effect.Champion.Ezreal.MysticShotHit";
    private const string IceHitEffectId = "Effect.LSW.IceballHit";
    private const string IcePresentationEffectId = "Effect.Champion.Ezreal.EssenceFlux";
    private const string ChillTag = "State.LSW.Chilled";
    private const string ChillBarDefinitionId = "lsw.unit_chill_bar";
    private const string ChillCountdownDefinitionId = "lsw.unit_chill_countdown";
    private const string ChillRatioParamKey = "lsw.chill.ratio";
    private const string ChillSecondsParamKey = "lsw.chill.remainingSeconds";
    private const int ChillCountdownBodySlot = 0;

    private static readonly QueryDescription UnitQuery = new QueryDescription()
        .WithAll<Name, Team, MapEntity, AbilityStateBuffer, WorldPositionCm, AttributeBuffer, OrderBuffer>();

    private static readonly QueryDescription ProjectileQuery = new QueryDescription()
        .WithAll<ProjectileState, WorldPositionCm>();

    private readonly GameEngine _engine;
    private readonly LiveGasEditPipeline _pipeline;
    private readonly DebugDrawCommandBuffer _debugDraw;
    private readonly ScreenOverlayBuffer? _overlay;
    private readonly PerformerEntityRuntime? _performerRuntime;
    private readonly PerformerDefinitionRegistry? _performerDefinitions;
    private readonly int _castAbilityOrderTypeId;
    private readonly int _healthAttrId;
    private readonly int _chillTagId;
    private readonly int _iceHitEffectId;
    private readonly int _icePresentationEffectId;
    private int _chillBarDefId;
    private int _chillCountdownDefId;
    private int _chillRatioParamId;
    private int _chillSecondsParamId;
    private float _elapsed;
    private int _phase;
    private bool _hotApplied;
    private string _status = "Boot";
    private float _lastTargetHp = -1f;
    private float _lastTargetHpBase = 200f;
    private int _castCount;
    private bool _targetChilled;
    private float _chillRemainingSeconds;
    private float _chillRemainingRatio;
    private int _liveProjectileCount;
    private string _lastLoggedStatus = string.Empty;

    public LswChampionHotApplyDemoSystem(GameEngine engine)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _pipeline = engine.GetService(CoreServiceKeys.LiveGasEditPipeline)
            ?? throw new InvalidOperationException("LiveGasEditPipeline required.");
        _debugDraw = engine.GetService(CoreServiceKeys.DebugDrawCommandBuffer)
            ?? throw new InvalidOperationException("DebugDrawCommandBuffer required.");
        _overlay = engine.GetService(CoreServiceKeys.ScreenOverlayBuffer);
        _performerRuntime = engine.GetService(CoreServiceKeys.PerformerEntityRuntime);
        _performerDefinitions = engine.GetService(CoreServiceKeys.PerformerDefinitionRegistry);

        GameConfig config = engine.GetService(CoreServiceKeys.GameConfig)
            ?? throw new InvalidOperationException("GameConfig required.");
        _castAbilityOrderTypeId = config.Constants.OrderTypeIds["castAbility"];
        _healthAttrId = AttributeRegistry.GetId("Health");
        _chillTagId = TagRegistry.GetId(ChillTag);
        _iceHitEffectId = EffectTemplateIdRegistry.GetId(IceHitEffectId);
        _icePresentationEffectId = EffectTemplateIdRegistry.GetId(IcePresentationEffectId);
        _chillBarDefId = 0;
        _chillCountdownDefId = 0;
        _chillRatioParamId = 0;
        _chillSecondsParamId = 0;
    }

    private bool TryResolveHudBindings()
    {
        if (_performerRuntime == null || _performerDefinitions == null)
        {
            return false;
        }

        if (_chillBarDefId <= 0)
        {
            _chillBarDefId = _performerDefinitions.GetId(ChillBarDefinitionId);
        }

        if (_chillCountdownDefId <= 0)
        {
            _chillCountdownDefId = _performerDefinitions.GetId(ChillCountdownDefinitionId);
        }

        if (_chillRatioParamId == 0)
        {
            PerformerParamKeyRegistry.TryGetId(ChillRatioParamKey, out _chillRatioParamId);
        }

        if (_chillSecondsParamId == 0)
        {
            PerformerParamKeyRegistry.TryGetId(ChillSecondsParamKey, out _chillSecondsParamId);
        }

        return _chillBarDefId > 0 &&
               _chillCountdownDefId > 0 &&
               _chillRatioParamId != 0 &&
               _chillSecondsParamId != 0;
    }

    public void Initialize() { }
    public void BeforeUpdate(in float t) { }
    public void AfterUpdate(in float t) { }
    public void Dispose() { }

    public void Update(in float dt)
    {
        string? mapId = _engine.CurrentMapSession?.MapId.Value;
        if (!string.Equals(mapId, "lsw_hot_apply_arena", StringComparison.Ordinal)
            && !string.Equals(mapId, "champion_skill_sandbox", StringComparison.Ordinal))
        {
            return;
        }

        _elapsed += dt;
        try
        {
            TickDemo();
            ProbeTargetState();
            DriveChillHudPerformers();
        }
        catch (Exception ex)
        {
            _status = "FAIL: " + ex.Message;
        }

        LogStatusIfChanged();
        DrawEditorStateMarkers();
        DrawHud();
    }

    private void TickDemo()
    {
        // Phase timeline (seconds):
        // 0-2 settle, 2 cast fire #1, 5 hot-apply, 7.5 cast ice #2, 11+ hold
        if (_phase == 0 && _elapsed >= 2.0f)
        {
            CastMysticShot();
            _phase = 1;
            _status = "EDITOR: baseline cast MysticShot (firebolt)";
        }
        else if (_phase == 1 && _elapsed >= 5.0f && !_hotApplied)
        {
            RunHotApplyFireToIce();
            _hotApplied = true;
            _phase = 2;
            _status = "EDITOR: hot-apply impact→IceballHit, presentation→EssenceFlux, damage+chill";
        }
        else if (_phase == 2 && _elapsed >= 7.5f)
        {
            CastMysticShot();
            _phase = 3;
            _status = "RUNTIME: second cast uses ice hit/debuff/presentation (NextCast)";
        }
        else if (_phase == 3 && _elapsed >= 11.0f)
        {
            _phase = 4;
            _status = $"DONE casts={_castCount} hotApplied={_hotApplied} lastTargetHP={_lastTargetHp:F0} chilled={_targetChilled}";
        }
    }

    private void CastMysticShot()
    {
        if (!TryFindCasterAndTarget(out Entity caster, out Entity target, out float targetHp))
        {
            throw new InvalidOperationException("Need Ezreal (team1) and hostile target on arena map.");
        }

        _lastTargetHp = targetHp;
        int abilityId = AbilityIdRegistry.GetId(FireAbilityId);
        if (abilityId == AbilityIdRegistry.InvalidId)
        {
            throw new InvalidOperationException($"Ability '{FireAbilityId}' not registered.");
        }

        _ = abilityId;
        if (!_engine.World.IsAlive(caster) || !_engine.World.Has<OrderBuffer>(caster))
        {
            throw new InvalidOperationException("Caster missing OrderBuffer.");
        }

        if (!_engine.GlobalContext.TryGetValue(CoreServiceKeys.OrderQueue.Name, out object? qObj) ||
            qObj is not OrderQueue queue)
        {
            throw new InvalidOperationException("OrderQueue missing — champion cast pipeline unavailable.");
        }

        var order = new Order
        {
            OrderTypeId = _castAbilityOrderTypeId,
            PlayerId = 1,
            Actor = caster,
            Target = target,
            SubmitMode = OrderSubmitMode.Immediate,
            Args = new OrderArgs { I0 = 0 }
        };

        if (!queue.TryEnqueue(in order))
        {
            throw new InvalidOperationException("Failed to enqueue castAbility order for MysticShot.");
        }

        _castCount++;
    }

    private void RunHotApplyFireToIce()
    {
        LiveEditSession session = LiveEditSession.Start(LiveEditSource.ManualWorkbench);
        var prov = new LiveEditProvenance(LiveEditSource.ManualWorkbench, "workbench://lsw/fire-to-ice");

        StageOk(session, LiveDebugPatchOperation.EffectTemplateRef(
            FireLaunchEffectId, "projectile.impactEffect", IceHitEffectId, prov));
        StageOk(session, LiveDebugPatchOperation.EffectTemplateRef(
            FireLaunchEffectId, "projectile.hitEffect", IceHitEffectId, prov));
        StageOk(session, LiveDebugPatchOperation.EffectTemplateRef(
            FireLaunchEffectId, "projectile.presentationEffect", IcePresentationEffectId, prov));
        StageOk(session, LiveDebugPatchOperation.SkillEffectNumeric(
            FireHitEffectId, "modifiers.0.value", -45d, prov));

        LiveApplyClassificationReport report = _pipeline.Classify(session);
        if (!report.CanCommitNextCast)
        {
            throw new InvalidOperationException(
                $"Hot-apply classify failed. mapReload={report.RequiresMapReload} restart={report.RequiresEngineRestart}");
        }

        for (int i = 0; i < report.Items.Count; i++)
        {
            if (report.Items[i].Mode != LiveApplyMode.NextCastLiveApply)
            {
                throw new InvalidOperationException(
                    $"Expected NextCastLiveApply, got {report.Items[i].Mode} for {report.Items[i].TargetId}");
            }
        }

        _pipeline.BeginSafeFrame();
        LiveApplyCommitResult commit = _pipeline.CommitNextCastSafeFrame();
        _pipeline.EndSafeFrame();
        if (!commit.Succeeded || commit.AppliedCount < 1)
        {
            string msg = commit.Diagnostics.Count > 0 ? commit.Diagnostics[0].Message : "commit failed";
            throw new InvalidOperationException(msg);
        }

        EffectTemplateRegistry effects = _engine.GetService(CoreServiceKeys.EffectTemplateRegistry)
            ?? throw new InvalidOperationException("EffectTemplateRegistry missing.");
        int launchId = EffectTemplateIdRegistry.GetId(FireLaunchEffectId);
        int iceHitId = EffectTemplateIdRegistry.GetId(IceHitEffectId);
        if (!effects.TryGet(launchId, out EffectTemplateData launch) ||
            launch.Projectile.ImpactEffectTemplateId != iceHitId)
        {
            throw new InvalidOperationException("Probe failed: projectile.impactEffect not ice after commit.");
        }
    }

    private static void StageOk(LiveEditSession session, LiveDebugPatchOperation op)
    {
        LiveEditStageResult r = session.TryStage(op);
        if (!r.Succeeded)
        {
            throw new InvalidOperationException(r.Diagnostics[0].Message);
        }
    }

    private bool TryFindCasterAndTarget(out Entity caster, out Entity target, out float targetHp)
    {
        caster = Entity.Null;
        target = Entity.Null;
        targetHp = -1f;
        Entity foundCaster = Entity.Null;
        Entity foundTarget = Entity.Null;
        float foundHp = -1f;
        string mapId = _engine.CurrentMapSession!.MapId.Value;

        _engine.World.Query(
            in UnitQuery,
            (Entity entity, ref Name name, ref Team team, ref MapEntity map, ref AbilityStateBuffer _, ref WorldPositionCm _, ref AttributeBuffer attrs, ref OrderBuffer _) =>
            {
                if (!string.Equals(map.MapId.Value, mapId, StringComparison.Ordinal))
                {
                    return;
                }

                if (team.Id == 1 && foundCaster == Entity.Null &&
                    name.Value != null && name.Value.IndexOf("Ezreal", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    foundCaster = entity;
                }

                if (team.Id == 2 && foundTarget == Entity.Null)
                {
                    foundTarget = entity;
                    if (_healthAttrId != AttributeRegistry.InvalidId)
                    {
                        foundHp = attrs.GetCurrent(_healthAttrId);
                        _lastTargetHpBase = attrs.GetBase(_healthAttrId);
                    }
                }
            });

        caster = foundCaster;
        target = foundTarget;
        targetHp = foundHp;
        return caster != Entity.Null && target != Entity.Null;
    }

    private void ProbeTargetState()
    {
        if (!TryFindCasterAndTarget(out _, out Entity target, out float targetHp))
        {
            return;
        }

        _lastTargetHp = targetHp;
        _targetChilled = false;
        _chillRemainingSeconds = 0f;
        _chillRemainingRatio = 0f;
        if (_chillTagId == TagRegistry.InvalidId ||
            !_engine.World.IsAlive(target) ||
            !_engine.World.TryGet(target, out GameplayTagContainer tags))
        {
            return;
        }

        _targetChilled = tags.HasTag(_chillTagId);
        if (!_targetChilled ||
            !_engine.World.TryGet(target, out ActiveEffectContainer effects))
        {
            return;
        }

        float fixedDt = Time.FixedDeltaTime > 0f ? Time.FixedDeltaTime : 0.05f;
        for (int i = 0; i < effects.Count; i++)
        {
            Entity effectEntity = effects.GetEntity(i);
            if (!_engine.World.IsAlive(effectEntity) ||
                !_engine.World.TryGet(effectEntity, out GameplayEffect effect) ||
                !_engine.World.TryGet(effectEntity, out EffectTemplateRef templateRef))
            {
                continue;
            }

            if (_iceHitEffectId != EffectTemplateIdRegistry.InvalidId &&
                templateRef.TemplateId != _iceHitEffectId)
            {
                continue;
            }

            if (effect.RemainingTicks <= 0 || effect.TotalTicks <= 0)
            {
                continue;
            }

            _chillRemainingSeconds = effect.RemainingTicks * fixedDt;
            _chillRemainingRatio = Math.Clamp(effect.RemainingTicks / (float)effect.TotalTicks, 0f, 1f);
            return;
        }
    }

    private void DriveChillHudPerformers()
    {
        EnsureWorldHudVisible();
        if (!TryResolveHudBindings())
        {
            return;
        }

        if (!TryFindCasterAndTarget(out _, out Entity target, out _))
        {
            return;
        }

        float displaySeconds = _targetChilled
            ? MathF.Ceiling(_chillRemainingSeconds)
            : 0f;
        SyncChillBar(target, _chillRemainingRatio);
        SyncChillCountdown(target, displaySeconds, _targetChilled && displaySeconds > 0f);
    }

    private void EnsureWorldHudVisible()
    {
        RenderDebugState? renderDebug = _engine.GetService(CoreServiceKeys.RenderDebugState);
        if (renderDebug == null)
        {
            return;
        }

        renderDebug.DrawWorldHudBars = true;
        renderDebug.DrawWorldHudText = true;
    }

    private void SyncChillBar(Entity owner, float ratio)
    {
        IReadOnlyList<Entity> bars = _performerRuntime!.GetActiveByOwnerDefinition(_chillBarDefId, owner);
        for (int i = 0; i < bars.Count; i++)
        {
            _performerRuntime.SetParam(bars[i], _chillRatioParamId, ParamLane.Float, ratio, 0, Vector4.Zero);
        }
    }

    private void SyncChillCountdown(Entity owner, float displaySeconds, bool active)
    {
        if (!_performerDefinitions!.TryGet(_chillCountdownDefId, out PerformerDefinition definition))
        {
            return;
        }

        IReadOnlyList<Entity> texts = _performerRuntime!.GetActiveByOwnerDefinition(_chillCountdownDefId, owner);
        for (int i = 0; i < texts.Count; i++)
        {
            Entity performer = texts[i];
            _performerRuntime.SetParam(
                performer,
                _chillSecondsParamId,
                ParamLane.Float,
                displaySeconds,
                0,
                Vector4.Zero);
            _performerRuntime.SetBehaviorActive(performer, definition, ChillCountdownBodySlot, active);
        }
    }

    private void LogStatusIfChanged()
    {
        string line =
            $"t={_elapsed:F1}s phase={_phase} casts={_castCount} hotApplied={_hotApplied} targetHP={_lastTargetHp:F0}/{_lastTargetHpBase:F0} chilled={_targetChilled} chillLeft={_chillRemainingSeconds:F1}s projectiles={_liveProjectileCount} | {_status}";
        if (string.Equals(line, _lastLoggedStatus, StringComparison.Ordinal))
        {
            return;
        }

        _lastLoggedStatus = line;
        Log.Info(in LogChannels.GAS, "[LSW HotApply Demo] " + line);
    }

    private void DrawEditorStateMarkers()
    {
        if (_elapsed < 0.5f)
        {
            return;
        }

        _debugDraw.Clear();
        if (!TryFindCasterAndTarget(out Entity caster, out Entity target, out _))
        {
            return;
        }

        if (_engine.World.IsAlive(caster) && _engine.World.Has<WorldPositionCm>(caster))
        {
            var c = _engine.World.Get<WorldPositionCm>(caster).Value;
            float cx = c.X.ToFloat() * 0.01f;
            float cy = c.Y.ToFloat() * 0.01f;
            GraphShowcaseStagePresenter.DrawActor(
                _debugDraw,
                cx,
                cy,
                0.85f,
                _hotApplied ? DebugDrawColor.Cyan : DebugDrawColor.Yellow,
                0.12f);
        }

        if (_engine.World.IsAlive(target) && _engine.World.Has<WorldPositionCm>(target))
        {
            var t = _engine.World.Get<WorldPositionCm>(target).Value;
            float tx = t.X.ToFloat() * 0.01f;
            float ty = t.Y.ToFloat() * 0.01f;
            GraphShowcaseStagePresenter.DrawActor(
                _debugDraw,
                tx,
                ty,
                0.90f,
                _targetChilled ? DebugDrawColor.Cyan : DebugDrawColor.Red,
                0.12f);
            float fill = _lastTargetHp < 0f ? 1f : Math.Clamp(_lastTargetHp / 200f, 0f, 1f);
            GraphShowcaseStagePresenter.DrawActor(
                _debugDraw,
                tx,
                ty + 1.3f,
                0.18f + 0.27f * fill,
                DebugDrawColor.Yellow,
                0.14f);
        }

        // Real projectile entities from GAS cast pipeline (not a fake damage table).
        int projectileCount = 0;
        _engine.World.Query(
            in ProjectileQuery,
            (Entity _, ref ProjectileState projectile, ref WorldPositionCm position) =>
            {
                projectileCount++;
                float px = position.Value.X.ToFloat() * 0.01f;
                float py = position.Value.Y.ToFloat() * 0.01f;
                bool icePresentation = _hotApplied ||
                    (_icePresentationEffectId != EffectTemplateIdRegistry.InvalidId &&
                     projectile.PresentationEffectTemplateId == _icePresentationEffectId);
                GraphShowcaseStagePresenter.DrawActor(
                    _debugDraw,
                    px,
                    py,
                    icePresentation ? 0.55f : 0.48f,
                    icePresentation ? DebugDrawColor.Cyan : DebugDrawColor.Yellow,
                    0.18f);
            });
        _liveProjectileCount = projectileCount;
    }

    private void DrawHud()
    {
        if (_overlay == null)
        {
            return;
        }

        var white = new Vector4(0.96f, 0.98f, 1f, 1f);
        var yellow = new Vector4(1f, 0.86f, 0.42f, 1f);
        var green = new Vector4(0.55f, 0.95f, 0.70f, 1f);
        var cyan = new Vector4(0.45f, 0.90f, 1f, 1f);
        _overlay.AddText(20, 18, "LSW Hot-Apply — LiveGasEditPipeline on Champion Skill", 20, white, 71001, 1);
        _overlay.AddText(20, 46, "1) firebolt  2) editor NextCast hot-apply  3) icebolt + more damage + chill", 15, yellow, 71002, 1);
        _overlay.AddText(20, 72, _status, 16, _hotApplied ? cyan : green, 71003, Hash(_status));
        _overlay.AddText(
            20,
            98,
            $"Dummy HP {_lastTargetHp:F0}/{_lastTargetHpBase:F0}  |  chill {(_targetChilled ? $"{_chillRemainingSeconds:F1}s" : "off")}  |  casts={_castCount} hotApplied={_hotApplied} proj={_liveProjectileCount}",
            16,
            white,
            71004,
            Hash(_status + _phase + _castCount + _targetChilled + _liveProjectileCount + (int)_lastTargetHp + (int)(_chillRemainingSeconds * 10f)));
        if (_targetChilled)
        {
            _overlay.AddText(
                20,
                126,
                $"BUFF  CHILL  {_chillRemainingSeconds:F1}s  ({_chillRemainingRatio * 100f:F0}%)",
                22,
                cyan,
                71005,
                Hash("chill" + (int)(_chillRemainingSeconds * 10f)));
        }
    }

    private static int Hash(string s)
    {
        unchecked
        {
            int h = 17;
            for (int i = 0; i < s.Length; i++)
            {
                h = h * 31 + s[i];
            }

            return h == 0 ? 1 : h;
        }
    }
}

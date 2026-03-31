using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Presentation;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Input.Orders;
using Ludots.Core.Input.Selection;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.DebugDraw;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Scripting;
using Ludots.Core.Spatial;

namespace ChampionSkillSandboxMod.Runtime
{
    internal sealed class ChampionSkillSandboxVisualFeedback
    {
        private static readonly QueryDescription ProjectileVisualQuery = new QueryDescription()
            .WithAll<ProjectileState, WorldPositionCm, VisualTransform>();

        private static readonly QueryDescription EzrealMarkQuery = new QueryDescription()
            .WithAll<GameplayTagContainer, VisualTransform>();

        private struct CombatTextEntry
        {
            public int StableId;
            public Entity Anchor;
            public int RoundedDelta;
            public float Lifetime;
            public float TimeLeft;
            public Vector4 Color;
        }

        private struct TransientPrimitiveEntry
        {
            public int StableId;
            public Entity Anchor;
            public Vector3 WorldPosition;
            public Vector3 PositionOffset;
            public Vector4 Color;
            public float Lifetime;
            public float TimeLeft;
            public float StartRadius;
            public float EndRadius;
            public float VerticalDrift;
            public byte FollowAnchor;
        }

        private struct ProjectilePrimitiveSpec
        {
            public Vector4 HeadColor;
            public Vector4 TailColor;
            public float HeadRadius;
            public int SegmentCount;
            public float SegmentSpacing;
            public float RadiusFalloff;
            public float HeightOffset;
        }

        private static readonly Vector4 DamageTextColor = new(1.0f, 0.82f, 0.46f, 1.0f);
        private static readonly Vector4 HealTextColor = new(0.62f, 1.0f, 0.72f, 1.0f);
        private static readonly Vector4 EzrealQColor = new(0.36f, 0.9f, 1.0f, 0.94f);
        private static readonly Vector4 EzrealQTailColor = new(0.2f, 0.66f, 1.0f, 0.4f);
        private static readonly Vector4 EzrealWColor = new(1.0f, 0.8f, 0.34f, 0.96f);
        private static readonly Vector4 EzrealWTailColor = new(1.0f, 0.62f, 0.2f, 0.44f);
        private static readonly Vector4 EzrealEColor = new(0.56f, 0.94f, 1.0f, 0.96f);
        private static readonly Vector4 EzrealETailColor = new(0.3f, 0.76f, 1.0f, 0.42f);
        private static readonly Vector4 EzrealRColor = new(0.76f, 0.95f, 1.0f, 0.98f);
        private static readonly Vector4 EzrealRTailColor = new(0.3f, 0.78f, 1.0f, 0.38f);

        private readonly CombatTextEntry[] _combatTextEntries = new CombatTextEntry[32];
        private readonly TransientPrimitiveEntry[] _transientPrimitiveEntries = new TransientPrimitiveEntry[64];
        private readonly Dictionary<int, int> _castCueByAbility = new();
        private readonly Dictionary<int, int> _hitCueByEffect = new();
        private readonly Dictionary<int, ProjectilePrimitiveSpec> _projectilePrimitiveSpecs = new();
        private int _combatTextCount;
        private int _transientPrimitiveCount;
        private int _nextCombatTextStableId = 1;
        private int _nextTransientPrimitiveStableId = 1000;
        private int _combatDeltaTokenId;
        private int _sphereMeshAssetId;
        private int _ezrealMysticShotAbilityId;
        private int _ezrealEssenceFluxAbilityId;
        private int _ezrealArcaneShiftAbilityId;
        private int _ezrealTrueshotBarrageAbilityId;
        private int _ezrealMysticShotProjectileEffectId;
        private int _ezrealEssenceFluxProjectileEffectId;
        private int _ezrealArcaneShiftProjectileEffectId;
        private int _ezrealTrueshotProjectileEffectId;
        private int _ezrealMysticShotHitEffectId;
        private int _ezrealEssenceFluxHitEffectId;
        private int _ezrealEssenceFluxPopEffectId;
        private int _ezrealArcaneShiftHitEffectId;
        private int _ezrealTrueshotHitEffectId;
        private int _ezrealWMarkTagId;
        private int _duelistActionContextAbilityId;
        private int _duelistComboStage1AbilityId;
        private int _duelistComboStage2AbilityId;
        private int _duelistStepInAbilityId;
        private int _duelistCrowdSweepAbilityId;
        private int _duelistOpeningBreakerAbilityId;
        private bool _cueIdsInitialized;
        private bool _directIdsInitialized;
        private float _feedbackClock;

        public void Update(GameEngine engine, float dt)
        {
            if (!ChampionSkillSandboxIds.IsSandboxMap(engine.CurrentMapSession?.MapId.Value))
            {
                _combatTextCount = 0;
                _transientPrimitiveCount = 0;
                return;
            }

            float frameDt = dt <= 0f ? (1f / 60f) : dt;
            _feedbackClock += frameDt;

            GasPresentationEventBuffer? gasEvents = engine.GetService(CoreServiceKeys.GasPresentationEventBuffer);
            WorldHudBatchBuffer? worldHud = engine.GetService(CoreServiceKeys.PresentationWorldHudBuffer);
            PrimitiveDrawBuffer? primitives = engine.GetService(CoreServiceKeys.PresentationPrimitiveDrawBuffer);
            PresentationCommandBuffer? commands = engine.GetService(CoreServiceKeys.PresentationCommandBuffer);
            RenderDebugState? renderDebug = engine.GetService(CoreServiceKeys.RenderDebugState);
            DebugDrawCommandBuffer? debugDraw = engine.GetService(CoreServiceKeys.DebugDrawCommandBuffer);
            if (ChampionSkillSandboxIds.IsStressMap(engine.CurrentMapSession?.MapId.Value) &&
                renderDebug is { DrawCombatText: false })
            {
                _combatTextCount = 0;
            }

            EnsureIds(engine);
            TickCombatText(frameDt);
            TickTransientPrimitives(frameDt);
            EmitActiveEzrealProjectiles(engine.World, primitives);
            EmitEzrealMarks(engine.World, primitives);
            EmitTransientPrimitives(engine.World, primitives);
            EmitCombatTextEntries(engine.World, worldHud);
            EmitDuelistContextDebug(engine, debugDraw, renderDebug?.DrawDebugDraw != false);

            if (gasEvents == null || gasEvents.Count == 0)
            {
                return;
            }

            ReadOnlySpan<GasPresentationEvent> events = gasEvents.Events;
            for (int i = 0; i < events.Length; i++)
            {
                ref readonly var evt = ref events[i];
                switch (evt.Kind)
                {
                    case GasPresentationEventKind.CastCommitted:
                        if (!TryQueueEzrealCastCue(evt) && commands != null)
                        {
                            TryQueueCue(engine.World, commands, evt.Actor, ResolveCastCue(evt.AbilityId));
                        }
                        break;
                    case GasPresentationEventKind.EffectApplied:
                        EmitEffectAppliedFeedback(engine.World, worldHud, commands, renderDebug?.DrawCombatText != false, in evt);
                        break;
                    case GasPresentationEventKind.EffectActivated:
                        EmitEffectAppliedFeedback(engine.World, worldHud, commands, renderDebug?.DrawCombatText != false, in evt);
                        break;
                }
            }
        }

        private void EmitEffectAppliedFeedback(
            World world,
            WorldHudBatchBuffer? worldHud,
            PresentationCommandBuffer? commands,
            bool allowCombatText,
            in GasPresentationEvent evt)
        {
            Entity anchor = ResolveFeedbackAnchor(world, evt);
            if (anchor == Entity.Null)
            {
                return;
            }

            if (!TryQueueEzrealHitCue(anchor, evt.EffectTemplateId) && commands != null)
            {
                TryQueueCue(world, commands, anchor, ResolveHitCue(evt.EffectTemplateId));
            }

            if (evt.Delta == 0f)
            {
                return;
            }

            bool isDamage = evt.Delta < 0f;
            if (allowCombatText)
            {
                QueueCombatText(worldHud, anchor, evt.Delta, isDamage ? DamageTextColor : HealTextColor);
            }
        }

        private void QueueCombatText(
            WorldHudBatchBuffer? worldHud,
            Entity anchor,
            float delta,
            in Vector4 color)
        {
            if (worldHud == null || _combatDeltaTokenId <= 0)
            {
                return;
            }

            int roundedDelta = (int)MathF.Round(delta);
            if (roundedDelta == 0 || _combatTextCount >= _combatTextEntries.Length)
            {
                return;
            }

            _combatTextEntries[_combatTextCount++] = new CombatTextEntry
            {
                StableId = _nextCombatTextStableId++,
                Anchor = anchor,
                RoundedDelta = roundedDelta,
                Lifetime = 0.72f,
                TimeLeft = 0.72f,
                Color = color,
            };
        }

        private void TickCombatText(float dt)
        {
            float delta = dt <= 0f ? (1f / 60f) : dt;
            for (int i = 0; i < _combatTextCount;)
            {
                _combatTextEntries[i].TimeLeft -= delta;
                if (_combatTextEntries[i].TimeLeft <= 0f)
                {
                    _combatTextCount--;
                    if (i < _combatTextCount)
                    {
                        _combatTextEntries[i] = _combatTextEntries[_combatTextCount];
                    }

                    continue;
                }

                i++;
            }
        }

        private void TickTransientPrimitives(float dt)
        {
            for (int i = 0; i < _transientPrimitiveCount;)
            {
                _transientPrimitiveEntries[i].TimeLeft -= dt;
                if (_transientPrimitiveEntries[i].TimeLeft <= 0f)
                {
                    _transientPrimitiveCount--;
                    if (i < _transientPrimitiveCount)
                    {
                        _transientPrimitiveEntries[i] = _transientPrimitiveEntries[_transientPrimitiveCount];
                    }

                    continue;
                }

                i++;
            }
        }

        private void EmitCombatTextEntries(World world, WorldHudBatchBuffer? worldHud)
        {
            if (worldHud == null || _combatDeltaTokenId <= 0)
            {
                return;
            }

            for (int i = 0; i < _combatTextCount; i++)
            {
                ref CombatTextEntry entry = ref _combatTextEntries[i];
                if (!world.IsAlive(entry.Anchor) || !world.Has<Ludots.Core.Presentation.Components.VisualTransform>(entry.Anchor))
                {
                    continue;
                }

                float progress = 1f - (entry.TimeLeft / entry.Lifetime);
                Vector4 color = entry.Color;
                color.W *= 1f - progress;

                Vector3 worldPosition = world.Get<Ludots.Core.Presentation.Components.VisualTransform>(entry.Anchor).Position
                    + new Vector3(0f, 1.42f + progress * 0.42f, 0f);
                var packet = PresentationTextPacket.FromToken(_combatDeltaTokenId);
                packet.SetArg(0, PresentationTextArg.FromInt32(entry.RoundedDelta));

                worldHud.TryAdd(new WorldHudItem
                {
                    StableId = entry.StableId,
                    DirtySerial = HudItemIdentity.ComposeTextDirtySerial(
                        fontSize: 22,
                        legacyStringId: 0,
                        legacyModeId: 0,
                        value0: 0f,
                        value1: 0f,
                        color,
                        packet),
                    Kind = WorldHudItemKind.Text,
                    WorldPosition = worldPosition,
                    Width = 72f,
                    FontSize = 22,
                    Color0 = color,
                    Text = packet,
                });
            }
        }

        private void EmitTransientPrimitives(World world, PrimitiveDrawBuffer? primitives)
        {
            if (primitives == null || _sphereMeshAssetId <= 0)
            {
                return;
            }

            for (int i = 0; i < _transientPrimitiveCount; i++)
            {
                ref readonly TransientPrimitiveEntry entry = ref _transientPrimitiveEntries[i];
                Vector3 basePosition;
                if (entry.FollowAnchor != 0)
                {
                    if (!world.IsAlive(entry.Anchor) || !world.Has<VisualTransform>(entry.Anchor))
                    {
                        continue;
                    }

                    basePosition = world.Get<VisualTransform>(entry.Anchor).Position;
                }
                else
                {
                    basePosition = entry.WorldPosition;
                }

                float progress = 1f - (entry.TimeLeft / entry.Lifetime);
                float radius = Lerp(entry.StartRadius, entry.EndRadius, progress);
                Vector4 color = entry.Color;
                color.W *= 1f - progress;
                Vector3 position = basePosition
                    + entry.PositionOffset
                    + new Vector3(0f, entry.VerticalDrift * progress, 0f);

                TryAddSphere(primitives, position, radius, color, entry.StableId);
            }
        }

        private void EmitActiveEzrealProjectiles(World world, PrimitiveDrawBuffer? primitives)
        {
            if (primitives == null || _sphereMeshAssetId <= 0 || _projectilePrimitiveSpecs.Count == 0)
            {
                return;
            }

            world.Query(in ProjectileVisualQuery, (Entity entity, ref ProjectileState projectile, ref WorldPositionCm positionCm, ref VisualTransform visual) =>
            {
                if (!_projectilePrimitiveSpecs.TryGetValue(projectile.PresentationEffectTemplateId, out ProjectilePrimitiveSpec spec))
                {
                    return;
                }

                Vector2 direction2 = ResolveProjectileDirection(world, in projectile, in positionCm);
                Vector3 forward = new Vector3(direction2.X, 0f, direction2.Y);
                if (forward.LengthSquared() <= 0.0001f)
                {
                    forward = Vector3.UnitX;
                }
                else
                {
                    forward = Vector3.Normalize(forward);
                }

                Vector3 head = visual.Position + new Vector3(0f, spec.HeightOffset, 0f);
                for (int segmentIndex = 0; segmentIndex < spec.SegmentCount; segmentIndex++)
                {
                    float t = spec.SegmentCount <= 1
                        ? 0f
                        : segmentIndex / (float)(spec.SegmentCount - 1);
                    float radius = MathF.Max(0.04f, spec.HeadRadius - (spec.RadiusFalloff * segmentIndex));
                    Vector4 color = Lerp(spec.HeadColor, spec.TailColor, t);
                    Vector3 segmentPosition = head - (forward * (segmentIndex * spec.SegmentSpacing));
                    TryAddSphere(primitives, segmentPosition, radius, color, stableId: 0);
                }
            });
        }

        private void EmitEzrealMarks(World world, PrimitiveDrawBuffer? primitives)
        {
            if (primitives == null || _sphereMeshAssetId <= 0 || _ezrealWMarkTagId <= 0)
            {
                return;
            }

            world.Query(in EzrealMarkQuery, (Entity entity, ref GameplayTagContainer tags, ref VisualTransform visual) =>
            {
                if (!tags.HasTag(_ezrealWMarkTagId))
                {
                    return;
                }

                const int orbitCount = 5;
                float pulse = 0.5f + (0.5f * MathF.Sin(_feedbackClock * 7f));
                Vector3 center = visual.Position + new Vector3(0f, 1.02f, 0f);

                for (int i = 0; i < orbitCount; i++)
                {
                    float angle = _feedbackClock * 3.4f + ((MathF.PI * 2f) * i / orbitCount);
                    float orbitRadius = 0.24f + (0.03f * pulse);
                    Vector3 offset = new Vector3(
                        MathF.Cos(angle) * orbitRadius,
                        0.05f * MathF.Sin((_feedbackClock * 4f) + i),
                        MathF.Sin(angle) * orbitRadius);
                    TryAddSphere(primitives, center + offset, 0.075f, EzrealWColor, stableId: 0);
                }

                TryAddSphere(primitives, center + new Vector3(0f, 0.12f + (pulse * 0.04f), 0f), 0.09f, EzrealWTailColor, stableId: 0);
            });
        }

        private void EnsureIds(GameEngine engine)
        {
            if (_combatDeltaTokenId <= 0 &&
                engine.GetService(CoreServiceKeys.PresentationTextCatalog) is PresentationTextCatalog textCatalog)
            {
                _combatDeltaTokenId = textCatalog.GetTokenId(WellKnownHudTextKeys.CombatDelta);
            }

            if (!_directIdsInitialized)
            {
                _sphereMeshAssetId = engine.GetService(CoreServiceKeys.PresentationMeshAssetRegistry)?.GetId(WellKnownMeshKeys.Sphere) ?? 0;

                _ezrealMysticShotAbilityId = AbilityIdRegistry.GetId("Ability.Champion.Ezreal.MysticShot");
                _ezrealEssenceFluxAbilityId = AbilityIdRegistry.GetId("Ability.Champion.Ezreal.EssenceFlux");
                _ezrealArcaneShiftAbilityId = AbilityIdRegistry.GetId("Ability.Champion.Ezreal.ArcaneShift");
                _ezrealTrueshotBarrageAbilityId = AbilityIdRegistry.GetId("Ability.Champion.Ezreal.TrueshotBarrage");

                _ezrealMysticShotProjectileEffectId = EffectTemplateIdRegistry.GetId("Effect.Champion.Ezreal.MysticShot");
                _ezrealEssenceFluxProjectileEffectId = EffectTemplateIdRegistry.GetId("Effect.Champion.Ezreal.EssenceFlux");
                _ezrealArcaneShiftProjectileEffectId = EffectTemplateIdRegistry.GetId("Effect.Champion.Ezreal.ArcaneShiftBolt");
                _ezrealTrueshotProjectileEffectId = EffectTemplateIdRegistry.GetId("Effect.Champion.Ezreal.TrueshotBarrage");

                _ezrealMysticShotHitEffectId = EffectTemplateIdRegistry.GetId("Effect.Champion.Ezreal.MysticShotHit");
                _ezrealEssenceFluxHitEffectId = EffectTemplateIdRegistry.GetId("Effect.Champion.Ezreal.EssenceFluxHit");
                _ezrealEssenceFluxPopEffectId = EffectTemplateIdRegistry.GetId("Effect.Champion.Ezreal.EssenceFluxPop");
                _ezrealArcaneShiftHitEffectId = EffectTemplateIdRegistry.GetId("Effect.Champion.Ezreal.ArcaneShiftBoltHit");
                _ezrealTrueshotHitEffectId = EffectTemplateIdRegistry.GetId("Effect.Champion.Ezreal.TrueshotBarrageHit");
                _ezrealWMarkTagId = TagRegistry.GetId("State.Champion.Ezreal.WMark");
                _duelistActionContextAbilityId = AbilityIdRegistry.GetId("Ability.Champion.Duelist.ActionContext");
                _duelistComboStage1AbilityId = AbilityIdRegistry.GetId("Ability.Champion.Duelist.Combo.Stage1");
                _duelistComboStage2AbilityId = AbilityIdRegistry.GetId("Ability.Champion.Duelist.Combo.Stage2");
                _duelistStepInAbilityId = AbilityIdRegistry.GetId("Ability.Champion.Duelist.StepIn");
                _duelistCrowdSweepAbilityId = AbilityIdRegistry.GetId("Ability.Champion.Duelist.CrowdSweep");
                _duelistOpeningBreakerAbilityId = AbilityIdRegistry.GetId("Ability.Champion.Duelist.OpeningBreaker");

                _projectilePrimitiveSpecs.Clear();
                RegisterProjectilePrimitiveSpec(_ezrealMysticShotProjectileEffectId, new ProjectilePrimitiveSpec
                {
                    HeadColor = EzrealQColor,
                    TailColor = EzrealQTailColor,
                    HeadRadius = 0.12f,
                    SegmentCount = 7,
                    SegmentSpacing = 0.14f,
                    RadiusFalloff = 0.01f,
                    HeightOffset = 0.72f,
                });
                RegisterProjectilePrimitiveSpec(_ezrealEssenceFluxProjectileEffectId, new ProjectilePrimitiveSpec
                {
                    HeadColor = EzrealWColor,
                    TailColor = EzrealWTailColor,
                    HeadRadius = 0.14f,
                    SegmentCount = 6,
                    SegmentSpacing = 0.14f,
                    RadiusFalloff = 0.012f,
                    HeightOffset = 0.8f,
                });
                RegisterProjectilePrimitiveSpec(_ezrealArcaneShiftProjectileEffectId, new ProjectilePrimitiveSpec
                {
                    HeadColor = EzrealEColor,
                    TailColor = EzrealETailColor,
                    HeadRadius = 0.11f,
                    SegmentCount = 5,
                    SegmentSpacing = 0.13f,
                    RadiusFalloff = 0.012f,
                    HeightOffset = 0.74f,
                });
                RegisterProjectilePrimitiveSpec(_ezrealTrueshotProjectileEffectId, new ProjectilePrimitiveSpec
                {
                    HeadColor = EzrealRColor,
                    TailColor = EzrealRTailColor,
                    HeadRadius = 0.18f,
                    SegmentCount = 16,
                    SegmentSpacing = 0.18f,
                    RadiusFalloff = 0.006f,
                    HeightOffset = 0.94f,
                });

                _directIdsInitialized = true;
            }

            if (_cueIdsInitialized)
            {
                return;
            }

            if (engine.GetService(CoreServiceKeys.PerformerDefinitionRegistry) is not PerformerDefinitionRegistry performers)
            {
                return;
            }

            _castCueByAbility.Clear();
            _hitCueByEffect.Clear();

            RegisterAbilityCue(performers, "Ability.Champion.Garen.DecisiveStrike", "champion_skill_sandbox.cue.garen_decisive_strike");
            RegisterAbilityCue(performers, "Ability.Champion.Garen.Courage", "champion_skill_sandbox.cue.garen_courage");
            RegisterAbilityCue(performers, "Ability.Champion.Garen.Judgment", "champion_skill_sandbox.cue.garen_judgment");
            RegisterAbilityCue(performers, "Ability.Champion.Garen.DemacianJustice", "champion_skill_sandbox.cue.garen_demacian_justice_cast");

            RegisterAbilityCue(performers, "Ability.Champion.Geomancer.RunicBeacon", "champion_skill_sandbox.cue.geomancer_runic_beacon");
            RegisterAbilityCue(performers, "Ability.Champion.Geomancer.RuneField", "champion_skill_sandbox.cue.geomancer_rune_field_cast");
            RegisterAbilityCue(performers, "Ability.Champion.Geomancer.StonePillar", "champion_skill_sandbox.cue.geomancer_stone_pillar");
            RegisterAbilityCue(performers, "Ability.Champion.Geomancer.PrismaticBeam", "champion_skill_sandbox.cue.geomancer_prismatic_beam_cast");

            RegisterAbilityCue(performers, "Ability.Champion.Jayce.Cannon.AccelerationGate", "champion_skill_sandbox.cue.jayce_acceleration_gate");
            RegisterAbilityCue(performers, "Ability.Champion.Jayce.Cannon.HyperCharge", "champion_skill_sandbox.cue.jayce_hyper_charge");
            RegisterAbilityCue(performers, "Ability.Champion.Jayce.Cannon.ShockBlast", "champion_skill_sandbox.cue.jayce_shock_blast_cast");
            RegisterAbilityCue(performers, "Ability.Champion.Jayce.Hammer.LightningField", "champion_skill_sandbox.cue.jayce_hammer_lightning_field");
            RegisterAbilityCue(performers, "Ability.Champion.Jayce.Hammer.ThunderingBlow", "champion_skill_sandbox.cue.jayce_hammer_thundering_blow_cast");
            RegisterAbilityCue(performers, "Ability.Champion.Jayce.Hammer.ToTheSkies", "champion_skill_sandbox.cue.jayce_hammer_to_the_skies_cast");
            RegisterAbilityCue(performers, "Ability.Champion.Jayce.Transform.Cannon", "champion_skill_sandbox.cue.jayce_transform_cannon");
            RegisterAbilityCue(performers, "Ability.Champion.Jayce.Transform.Hammer", "champion_skill_sandbox.cue.jayce_transform_hammer");
            RegisterAbilityCue(performers, "Ability.Champion.Duelist.Combo.Stage1", "champion_skill_sandbox.cue.duelist_chain_jab_1");
            RegisterAbilityCue(performers, "Ability.Champion.Duelist.Combo.Stage2", "champion_skill_sandbox.cue.duelist_chain_jab_2");
            RegisterAbilityCue(performers, "Ability.Champion.Duelist.Combo.Stage3", "champion_skill_sandbox.cue.duelist_chain_finish");
            RegisterAbilityCue(performers, "Ability.Champion.Duelist.StepIn", "champion_skill_sandbox.cue.duelist_step_in");
            RegisterAbilityCue(performers, "Ability.Champion.Duelist.CrowdSweep", "champion_skill_sandbox.cue.duelist_crowd_sweep");
            RegisterAbilityCue(performers, "Ability.Champion.Duelist.OpeningBreaker", "champion_skill_sandbox.cue.duelist_opening_breaker_cast");
            RegisterAbilityCue(performers, "Ability.ChampionStress.Warrior.Cleave", "champion_skill_sandbox.cue.stress_warrior_cleave");
            RegisterAbilityCue(performers, "Ability.ChampionStress.FireMage.Fireball", "champion_skill_sandbox.cue.stress_fireball_cast");
            RegisterAbilityCue(performers, "Ability.ChampionStress.LaserMage.Laser", "champion_skill_sandbox.cue.stress_laser_cast");
            RegisterAbilityCue(performers, "Ability.ChampionStress.Priest.Heal", "champion_skill_sandbox.cue.stress_priest_heal_cast");
            RegisterAbilityCue(performers, "Ability.Champion.SpellEngineer.SpellBeacon", "champion_skill_sandbox.cue.spell_engineer_spell_beacon_cast");
            RegisterAbilityCue(performers, "Ability.Champion.SpellEngineer.GravityWell", "champion_skill_sandbox.cue.spell_engineer_gravity_well_cast");
            RegisterAbilityCue(performers, "Ability.Champion.SpellEngineer.CataclysmRing", "champion_skill_sandbox.cue.spell_engineer_cataclysm_ring_cast");
            RegisterAbilityCue(performers, "Ability.Champion.SpellEngineer.GuidedLaser", "champion_skill_sandbox.cue.spell_engineer_guided_laser_cast");

            RegisterEffectCue(performers, "Effect.Champion.Garen.JudgmentHit", "champion_skill_sandbox.cue.garen_judgment_hit");
            RegisterEffectCue(performers, "Effect.Champion.Garen.DemacianJusticeHit", "champion_skill_sandbox.cue.garen_demacian_justice_hit");
            RegisterEffectCue(performers, "Effect.Champion.Geomancer.RuneFieldHit", "champion_skill_sandbox.cue.geomancer_rune_field_hit");
            RegisterEffectCue(performers, "Effect.Champion.Geomancer.PrismaticBeamHit", "champion_skill_sandbox.cue.geomancer_prismatic_beam_hit");
            RegisterEffectCue(performers, "Effect.Champion.Jayce.Cannon.ShockBlastHit", "champion_skill_sandbox.cue.jayce_shock_blast_hit");
            RegisterEffectCue(performers, "Effect.Champion.Jayce.Hammer.LightningFieldHit", "champion_skill_sandbox.cue.jayce_hammer_lightning_field_hit");
            RegisterEffectCue(performers, "Effect.Champion.Jayce.Hammer.ThunderingBlowHit", "champion_skill_sandbox.cue.jayce_hammer_thundering_blow_hit");
            RegisterEffectCue(performers, "Effect.Champion.Jayce.Hammer.ToTheSkiesHit", "champion_skill_sandbox.cue.jayce_hammer_to_the_skies_hit");
            RegisterEffectCue(performers, "Effect.Champion.Duelist.Combo.Stage1", "champion_skill_sandbox.cue.duelist_combo_hit");
            RegisterEffectCue(performers, "Effect.Champion.Duelist.Combo.Stage2", "champion_skill_sandbox.cue.duelist_combo_hit");
            RegisterEffectCue(performers, "Effect.Champion.Duelist.Combo.Stage3", "champion_skill_sandbox.cue.duelist_opening_breaker_hit");
            RegisterEffectCue(performers, "Effect.Champion.Duelist.StepInStrike", "champion_skill_sandbox.cue.duelist_step_in_hit");
            RegisterEffectCue(performers, "Effect.Champion.Duelist.CrowdSweepHit", "champion_skill_sandbox.cue.duelist_crowd_sweep_hit");
            RegisterEffectCue(performers, "Effect.Champion.Duelist.OpeningBreaker", "champion_skill_sandbox.cue.duelist_opening_breaker_hit");
            RegisterEffectCue(performers, "Effect.ChampionStress.Warrior.CleaveHit", "champion_skill_sandbox.cue.stress_warrior_cleave_hit");
            RegisterEffectCue(performers, "Effect.ChampionStress.FireMage.FireballHit", "champion_skill_sandbox.cue.stress_fireball_hit");
            RegisterEffectCue(performers, "Effect.ChampionStress.LaserMage.LaserHit", "champion_skill_sandbox.cue.stress_laser_hit");
            RegisterEffectCue(performers, "Effect.ChampionStress.Priest.Heal", "champion_skill_sandbox.cue.stress_priest_heal_hit");
            RegisterEffectCue(performers, "Effect.Champion.SpellEngineer.GravityWellHit", "champion_skill_sandbox.cue.spell_engineer_gravity_well_hit");
            RegisterEffectCue(performers, "Effect.Champion.SpellEngineer.GuidedLaserHit", "champion_skill_sandbox.cue.spell_engineer_guided_laser_hit");

            _cueIdsInitialized = true;
        }

        private void RegisterAbilityCue(PerformerDefinitionRegistry performers, string abilityKey, string performerKey)
        {
            int abilityId = AbilityIdRegistry.GetId(abilityKey);
            if (abilityId <= 0)
            {
                throw new InvalidOperationException($"ChampionSkillSandboxMod requires ability id '{abilityKey}' to be registered.");
            }

            _castCueByAbility[abilityId] = ResolvePerformerId(performers, performerKey);
        }

        private void RegisterEffectCue(PerformerDefinitionRegistry performers, string effectKey, string performerKey)
        {
            int effectId = EffectTemplateIdRegistry.GetId(effectKey);
            if (effectId <= 0)
            {
                throw new InvalidOperationException($"ChampionSkillSandboxMod requires effect id '{effectKey}' to be registered.");
            }

            _hitCueByEffect[effectId] = ResolvePerformerId(performers, performerKey);
        }

        private static int ResolvePerformerId(PerformerDefinitionRegistry performers, string performerKey)
        {
            int performerId = performers.GetId(performerKey);
            if (performerId <= 0)
            {
                throw new InvalidOperationException($"ChampionSkillSandboxMod requires performer '{performerKey}'.");
            }

            return performerId;
        }

        private int ResolveCastCue(int abilityId)
        {
            return _castCueByAbility.TryGetValue(abilityId, out int performerId) ? performerId : 0;
        }

        private int ResolveHitCue(int effectTemplateId)
        {
            return _hitCueByEffect.TryGetValue(effectTemplateId, out int performerId) ? performerId : 0;
        }

        private void RegisterProjectilePrimitiveSpec(int effectTemplateId, in ProjectilePrimitiveSpec spec)
        {
            if (effectTemplateId > 0)
            {
                _projectilePrimitiveSpecs[effectTemplateId] = spec;
            }
        }

        private bool TryQueueEzrealCastCue(in GasPresentationEvent evt)
        {
            if (evt.Actor == Entity.Null)
            {
                return false;
            }

            if (evt.AbilityId == _ezrealMysticShotAbilityId)
            {
                QueueAnchoredPulse(evt.Actor, EzrealQColor, lifetime: 0.18f, startRadius: 0.12f, endRadius: 0.28f, new Vector3(0f, 0.72f, 0f));
                return true;
            }

            if (evt.AbilityId == _ezrealEssenceFluxAbilityId)
            {
                QueueAnchoredPulse(evt.Actor, EzrealWColor, lifetime: 0.22f, startRadius: 0.16f, endRadius: 0.34f, new Vector3(0f, 0.78f, 0f));
                return true;
            }

            if (evt.AbilityId == _ezrealArcaneShiftAbilityId)
            {
                QueueAnchoredPulse(evt.Actor, EzrealEColor, lifetime: 0.26f, startRadius: 0.18f, endRadius: 0.4f, new Vector3(0f, 0.74f, 0f));
                return true;
            }

            if (evt.AbilityId == _ezrealTrueshotBarrageAbilityId)
            {
                QueueAnchoredPulse(evt.Actor, EzrealRColor, lifetime: 0.32f, startRadius: 0.24f, endRadius: 0.58f, new Vector3(0f, 0.84f, 0f));
                return true;
            }

            return false;
        }

        private bool TryQueueEzrealHitCue(Entity anchor, int effectTemplateId)
        {
            if (effectTemplateId == _ezrealMysticShotHitEffectId)
            {
                QueueAnchoredPulse(anchor, EzrealQColor, lifetime: 0.18f, startRadius: 0.16f, endRadius: 0.34f, new Vector3(0f, 0.84f, 0f));
                return true;
            }

            if (effectTemplateId == _ezrealEssenceFluxHitEffectId)
            {
                QueueAnchoredPulse(anchor, EzrealWColor, lifetime: 0.22f, startRadius: 0.18f, endRadius: 0.38f, new Vector3(0f, 0.86f, 0f));
                return true;
            }

            if (effectTemplateId == _ezrealEssenceFluxPopEffectId)
            {
                QueueAnchoredPulse(anchor, EzrealWColor, lifetime: 0.22f, startRadius: 0.2f, endRadius: 0.46f, new Vector3(0f, 0.9f, 0f));
                return true;
            }

            if (effectTemplateId == _ezrealArcaneShiftHitEffectId)
            {
                QueueAnchoredPulse(anchor, EzrealEColor, lifetime: 0.2f, startRadius: 0.16f, endRadius: 0.36f, new Vector3(0f, 0.84f, 0f));
                return true;
            }

            if (effectTemplateId == _ezrealTrueshotHitEffectId)
            {
                QueueAnchoredPulse(anchor, EzrealRColor, lifetime: 0.24f, startRadius: 0.22f, endRadius: 0.52f, new Vector3(0f, 0.9f, 0f));
                return true;
            }

            return false;
        }

        private void QueueAnchoredPulse(Entity anchor, in Vector4 color, float lifetime, float startRadius, float endRadius, in Vector3 offset)
        {
            if (anchor == Entity.Null || _transientPrimitiveCount >= _transientPrimitiveEntries.Length)
            {
                return;
            }

            _transientPrimitiveEntries[_transientPrimitiveCount++] = new TransientPrimitiveEntry
            {
                StableId = _nextTransientPrimitiveStableId++,
                Anchor = anchor,
                WorldPosition = Vector3.Zero,
                PositionOffset = offset,
                Color = color,
                Lifetime = lifetime,
                TimeLeft = lifetime,
                StartRadius = startRadius,
                EndRadius = endRadius,
                VerticalDrift = 0.08f,
                FollowAnchor = 1,
            };
        }

        private void TryAddSphere(PrimitiveDrawBuffer primitives, Vector3 position, float radius, in Vector4 color, int stableId)
        {
            primitives.TryAdd(new PrimitiveDrawItem
            {
                MeshAssetId = _sphereMeshAssetId,
                Position = position,
                Rotation = Quaternion.Identity,
                Scale = new Vector3(radius * 2f, radius * 2f, radius * 2f),
                Color = color,
                StableId = stableId,
                RenderPath = VisualRenderPath.None,
                Mobility = VisualMobility.Movable,
                Flags = VisualRuntimeFlags.Visible,
                Visibility = VisualVisibility.Visible,
            });
        }

        private static Vector2 ResolveProjectileDirection(World world, in ProjectileState projectile, in WorldPositionCm positionCm)
        {
            if (projectile.HasDirection != 0)
            {
                return NormalizeOrFallback(new Vector2(
                    projectile.Direction.X.ToFloat(),
                    projectile.Direction.Y.ToFloat()));
            }

            if (world.IsAlive(projectile.Target) && world.Has<WorldPositionCm>(projectile.Target))
            {
                Vector2 delta = world.Get<WorldPositionCm>(projectile.Target).Value.ToVector2() - positionCm.Value.ToVector2();
                return NormalizeOrFallback(delta);
            }

            if (world.IsAlive(projectile.Source) && world.Has<WorldPositionCm>(projectile.Source))
            {
                Vector2 delta = positionCm.Value.ToVector2() - world.Get<WorldPositionCm>(projectile.Source).Value.ToVector2();
                return NormalizeOrFallback(delta);
            }

            return Vector2.UnitX;
        }

        private static Vector2 NormalizeOrFallback(Vector2 value)
        {
            float lengthSquared = value.LengthSquared();
            if (lengthSquared <= 0.0001f)
            {
                return Vector2.UnitX;
            }

            return value / MathF.Sqrt(lengthSquared);
        }

        private static float Lerp(float start, float end, float t)
        {
            return start + ((end - start) * t);
        }

        private static Vector4 Lerp(in Vector4 start, in Vector4 end, float t)
        {
            return new Vector4(
                Lerp(start.X, end.X, t),
                Lerp(start.Y, end.Y, t),
                Lerp(start.Z, end.Z, t),
                Lerp(start.W, end.W, t));
        }

        private static void TryQueueCue(World world, PresentationCommandBuffer commands, Entity anchor, int performerId)
        {
            if (performerId <= 0 || anchor == Entity.Null || !world.IsAlive(anchor))
            {
                return;
            }

            commands.TryAdd(new PresentationCommand
            {
                Kind = PresentationCommandKind.CreatePerformer,
                AnchorKind = PresentationAnchorKind.Entity,
                IdA = performerId,
                IdB = 0,
                Source = anchor,
            });
        }

        private static Entity ResolveFeedbackAnchor(World world, in GasPresentationEvent evt)
        {
            if (world.IsAlive(evt.Target))
            {
                return evt.Target;
            }

            if (world.IsAlive(evt.Actor))
            {
                return evt.Actor;
            }

            return Entity.Null;
        }

        private void EmitDuelistContextDebug(GameEngine engine, DebugDrawCommandBuffer? debugDraw, bool allowDebugDraw)
        {
            if (!allowDebugDraw ||
                debugDraw == null ||
                _duelistActionContextAbilityId <= 0)
            {
                return;
            }

            Span<ContextScoredCandidateProbe> probes = stackalloc ContextScoredCandidateProbe[8];
            if (!ChampionSkillSandboxDuelistContextInspector.TryInspect(
                    engine,
                    _duelistActionContextAbilityId,
                    probes,
                    out Entity actor,
                    out Entity hovered,
                    out ContextGroupDefinition group,
                    out int probeCount,
                    out ContextScoredOrderResolution resolution) ||
                !engine.World.TryGet(actor, out WorldPositionCm actorPosition))
            {
                return;
            }

            Vector2 actorWorld = ToDebugWorld(actorPosition.Value.ToVector2());
            float searchRadius = group.SearchRadiusCm / 100f;
            debugDraw.AddCircle(actorWorld, searchRadius, new DebugDrawColor(84, 198, 255, 210), thickness: 0.05f);

            if (hovered != Entity.Null && TryGetDebugWorld(engine.World, hovered, out Vector2 hoveredWorld))
            {
                debugDraw.AddCircle(hoveredWorld, 0.34f, DebugDrawColor.Yellow, thickness: 0.04f);
            }

            for (int i = 0; i < probeCount; i++)
            {
                ContextScoredCandidateProbe probe = probes[i];
                DebugDrawColor color = ResolveCandidateColor(probe.AbilityId);
                float thickness = i == 0 ? 0.07f : 0.035f;

                if (probe.Target != Entity.Null && TryGetDebugWorld(engine.World, probe.Target, out Vector2 targetWorld))
                {
                    debugDraw.AddCapsule(actorWorld, targetWorld, i == 0 ? 0.18f : 0.1f, color, thickness, drawCenterLine: i == 0);
                    debugDraw.AddCircle(targetWorld, i == 0 ? 0.42f : 0.26f, color, thickness);
                }
            }

            int resolvedAbilityId = probeCount > 0 ? probes[0].AbilityId : 0;
            Vector2 resolvedTargetWorld = actorWorld;
            if (resolution.Target != Entity.Null && TryGetDebugWorld(engine.World, resolution.Target, out Vector2 resolvedTarget))
            {
                resolvedTargetWorld = resolvedTarget;
                debugDraw.AddCircle(resolvedTarget, 0.5f, DebugDrawColor.White, thickness: 0.05f);
            }

            float rotation = ResolveDebugRotation(actorWorld, resolvedTargetWorld, engine.World, actor);
            DebugDrawColor resolvedColor = ResolveCandidateColor(resolvedAbilityId);
            if (resolvedAbilityId == _duelistStepInAbilityId && resolution.Target != Entity.Null)
            {
                debugDraw.AddCapsule(actorWorld, resolvedTargetWorld, 0.36f, resolvedColor, thickness: 0.08f, drawCenterLine: true);
                return;
            }

            if (resolvedAbilityId == _duelistCrowdSweepAbilityId)
            {
                debugDraw.AddArc(actorWorld, 2.4f, rotation - 1.1f, rotation + 1.1f, resolvedColor, thickness: 0.08f);
                return;
            }

            float radius = resolvedAbilityId == _duelistOpeningBreakerAbilityId
                ? 1.95f
                : resolvedAbilityId == _duelistComboStage2AbilityId
                    ? 1.76f
                    : 1.58f;
            float span = resolvedAbilityId == _duelistOpeningBreakerAbilityId ? 0.96f : 0.68f;
            debugDraw.AddArc(actorWorld, radius, rotation - span, rotation + span, resolvedColor, thickness: 0.08f);
        }

        private DebugDrawColor ResolveCandidateColor(int abilityId)
        {
            if (abilityId == _duelistStepInAbilityId)
            {
                return new DebugDrawColor(84, 210, 255, 220);
            }

            if (abilityId == _duelistCrowdSweepAbilityId)
            {
                return new DebugDrawColor(130, 244, 126, 220);
            }

            if (abilityId == _duelistOpeningBreakerAbilityId)
            {
                return new DebugDrawColor(255, 114, 126, 235);
            }

            if (abilityId == _duelistComboStage2AbilityId)
            {
                return new DebugDrawColor(255, 170, 96, 228);
            }

            if (abilityId == _duelistComboStage1AbilityId)
            {
                return new DebugDrawColor(248, 206, 104, 228);
            }

            return DebugDrawColor.White;
        }

        private static bool TryGetDebugWorld(World world, Entity entity, out Vector2 worldPosition)
        {
            worldPosition = default;
            if (!world.IsAlive(entity) || !world.TryGet(entity, out WorldPositionCm position))
            {
                return false;
            }

            worldPosition = ToDebugWorld(position.Value.ToVector2());
            return true;
        }

        private static float ResolveDebugRotation(Vector2 actorWorld, Vector2 targetWorld, World world, Entity actor)
        {
            Vector2 delta = targetWorld - actorWorld;
            if (delta.LengthSquared() > 0.0001f)
            {
                return MathF.Atan2(delta.Y, delta.X);
            }

            if (world.TryGet(actor, out FacingDirection facing))
            {
                return facing.AngleRad;
            }

            return 0f;
        }

        private static Vector2 ToDebugWorld(Vector2 worldCm)
        {
            return worldCm / 100f;
        }
    }
}

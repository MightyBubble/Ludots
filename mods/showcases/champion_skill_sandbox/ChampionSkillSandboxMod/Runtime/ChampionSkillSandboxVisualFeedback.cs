using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Presentation;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Primitives;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Scripting;

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
        private static readonly Vector4 GenericCastCueColor = new(0.95f, 0.88f, 0.34f, 0.96f);
        private static readonly Vector4 GenericHitCueColor = new(1.0f, 0.44f, 0.28f, 0.96f);

        private readonly CombatTextEntry[] _combatTextEntries = new CombatTextEntry[32];
        private readonly TransientPrimitiveEntry[] _transientPrimitiveEntries = new TransientPrimitiveEntry[64];
        private readonly HashSet<int> _castCueAbilities = new();
        private readonly HashSet<int> _hitCueEffects = new();
        private readonly Dictionary<int, ProjectilePrimitiveSpec> _projectilePrimitiveSpecs = new();
        private int _combatTextCount;
        private int _transientPrimitiveCount;
        private int _nextCombatTextStableId = 1;
        private int _nextTransientPrimitiveStableId = 1000;
        private int _combatDeltaTokenId;
        private int _cueMarkerPrefabId;
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
            TransientMarkerBuffer? markers = engine.GetService(CoreServiceKeys.TransientMarkerBuffer);
            RenderDebugState? renderDebug = engine.GetService(CoreServiceKeys.RenderDebugState);
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
                        if (!TryQueueEzrealCastCue(evt) && markers != null)
                        {
                            TryQueueCueMarker(engine.World, markers, evt.Actor, HasCastCue(evt.AbilityId), GenericCastCueColor);
                        }
                        break;
                    case GasPresentationEventKind.EffectApplied:
                        EmitEffectAppliedFeedback(engine.World, worldHud, markers, renderDebug?.DrawCombatText != false, in evt);
                        break;
                    case GasPresentationEventKind.EffectActivated:
                        EmitEffectAppliedFeedback(engine.World, worldHud, markers, renderDebug?.DrawCombatText != false, in evt);
                        break;
                }
            }
        }

        private void EmitEffectAppliedFeedback(
            World world,
            WorldHudBatchBuffer? worldHud,
            TransientMarkerBuffer? markers,
            bool allowCombatText,
            in GasPresentationEvent evt)
        {
            Entity anchor = ResolveFeedbackAnchor(world, evt);
            if (anchor == Entity.Null)
            {
                return;
            }

            if (!TryQueueEzrealHitCue(anchor, evt.EffectTemplateId) && markers != null)
            {
                TryQueueCueMarker(world, markers, anchor, HasHitCue(evt.EffectTemplateId), GenericHitCueColor);
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
                        stringTableId: 0,
                        valueModeId: 0,
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

            if (engine.GetService(CoreServiceKeys.PresentationPrefabRegistry) is not PrefabRegistry prefabs)
            {
                return;
            }

            _cueMarkerPrefabId = prefabs.GetId(WellKnownPrefabKeys.CueMarker);
            if (_cueMarkerPrefabId <= 0)
            {
                throw new InvalidOperationException($"ChampionSkillSandboxMod requires prefab '{WellKnownPrefabKeys.CueMarker}'.");
            }

            _castCueAbilities.Clear();
            _hitCueEffects.Clear();

            RegisterAbilityCue("Ability.Champion.Garen.DecisiveStrike");
            RegisterAbilityCue("Ability.Champion.Garen.Courage");
            RegisterAbilityCue("Ability.Champion.Garen.Judgment");
            RegisterAbilityCue("Ability.Champion.Garen.DemacianJustice");

            RegisterAbilityCue("Ability.Champion.Geomancer.RunicBeacon");
            RegisterAbilityCue("Ability.Champion.Geomancer.RuneField");
            RegisterAbilityCue("Ability.Champion.Geomancer.StonePillar");
            RegisterAbilityCue("Ability.Champion.Geomancer.PrismaticBeam");

            RegisterAbilityCue("Ability.Champion.Jayce.Cannon.AccelerationGate");
            RegisterAbilityCue("Ability.Champion.Jayce.Cannon.HyperCharge");
            RegisterAbilityCue("Ability.Champion.Jayce.Cannon.ShockBlast");
            RegisterAbilityCue("Ability.Champion.Jayce.Hammer.LightningField");
            RegisterAbilityCue("Ability.Champion.Jayce.Hammer.ThunderingBlow");
            RegisterAbilityCue("Ability.Champion.Jayce.Hammer.ToTheSkies");
            RegisterAbilityCue("Ability.Champion.Jayce.Transform.Cannon");
            RegisterAbilityCue("Ability.Champion.Jayce.Transform.Hammer");
            RegisterAbilityCue("Ability.ChampionStress.Warrior.Cleave");
            RegisterAbilityCue("Ability.ChampionStress.FireMage.Fireball");
            RegisterAbilityCue("Ability.ChampionStress.LaserMage.Laser");
            RegisterAbilityCue("Ability.ChampionStress.Priest.Heal");
            RegisterAbilityCue("Ability.Champion.SpellEngineer.SpellBeacon");
            RegisterAbilityCue("Ability.Champion.SpellEngineer.GravityWell");
            RegisterAbilityCue("Ability.Champion.SpellEngineer.CataclysmRing");
            RegisterAbilityCue("Ability.Champion.SpellEngineer.GuidedLaser");

            RegisterEffectCue("Effect.Champion.Garen.JudgmentHit");
            RegisterEffectCue("Effect.Champion.Garen.DemacianJusticeHit");
            RegisterEffectCue("Effect.Champion.Geomancer.RuneFieldHit");
            RegisterEffectCue("Effect.Champion.Geomancer.PrismaticBeamHit");
            RegisterEffectCue("Effect.Champion.Jayce.Cannon.ShockBlastHit");
            RegisterEffectCue("Effect.Champion.Jayce.Hammer.LightningFieldHit");
            RegisterEffectCue("Effect.Champion.Jayce.Hammer.ThunderingBlowHit");
            RegisterEffectCue("Effect.Champion.Jayce.Hammer.ToTheSkiesHit");
            RegisterEffectCue("Effect.ChampionStress.Warrior.CleaveHit");
            RegisterEffectCue("Effect.ChampionStress.FireMage.FireballHit");
            RegisterEffectCue("Effect.ChampionStress.LaserMage.LaserHit");
            RegisterEffectCue("Effect.ChampionStress.Priest.Heal");
            RegisterEffectCue("Effect.Champion.SpellEngineer.GravityWellHit");
            RegisterEffectCue("Effect.Champion.SpellEngineer.GuidedLaserHit");

            _cueIdsInitialized = true;
        }

        private void RegisterAbilityCue(string abilityKey)
        {
            int abilityId = AbilityIdRegistry.GetId(abilityKey);
            if (abilityId <= 0)
            {
                throw new InvalidOperationException($"ChampionSkillSandboxMod requires ability id '{abilityKey}' to be registered.");
            }

            _castCueAbilities.Add(abilityId);
        }

        private void RegisterEffectCue(string effectKey)
        {
            int effectId = EffectTemplateIdRegistry.GetId(effectKey);
            if (effectId <= 0)
            {
                throw new InvalidOperationException($"ChampionSkillSandboxMod requires effect id '{effectKey}' to be registered.");
            }

            _hitCueEffects.Add(effectId);
        }

        private bool HasCastCue(int abilityId)
        {
            return _castCueAbilities.Contains(abilityId);
        }

        private bool HasHitCue(int effectTemplateId)
        {
            return _hitCueEffects.Contains(effectTemplateId);
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
                return NormalizeOrUnitX(new Vector2(
                    projectile.Direction.X.ToFloat(),
                    projectile.Direction.Y.ToFloat()));
            }

            if (world.IsAlive(projectile.Target) && world.Has<WorldPositionCm>(projectile.Target))
            {
                Vector2 delta = world.Get<WorldPositionCm>(projectile.Target).Value.ToVector2() - positionCm.Value.ToVector2();
                return NormalizeOrUnitX(delta);
            }

            if (world.IsAlive(projectile.Source) && world.Has<WorldPositionCm>(projectile.Source))
            {
                Vector2 delta = positionCm.Value.ToVector2() - world.Get<WorldPositionCm>(projectile.Source).Value.ToVector2();
                return NormalizeOrUnitX(delta);
            }

            return Vector2.UnitX;
        }

        private static Vector2 NormalizeOrUnitX(Vector2 value)
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

        private void TryQueueCueMarker(World world, TransientMarkerBuffer markers, Entity anchor, bool shouldEmit, in Vector4 color)
        {
            if (!shouldEmit || _cueMarkerPrefabId <= 0 || anchor == Entity.Null || !world.IsAlive(anchor))
            {
                return;
            }

            bool added = markers.TryAddAnchoredPrefab(
                _cueMarkerPrefabId,
                Vector3.One,
                color,
                0.35f,
                anchor,
                new Vector3(0f, 0.75f, 0f));
            if (!added)
            {
                throw new InvalidOperationException("TransientMarkerBuffer is full while emitting champion skill cue marker.");
            }
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
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Scripting;
using NarrativeFrontendMod;
using NarrativeFrontendMod.Runtime;
using RelationshipShowcaseMod.Runtime;

namespace RelationshipShowcaseMod.Systems
{
    internal sealed class RelationshipShowcasePresentationSystem : ISystem<float>
    {
        private readonly GameEngine _engine;
        private readonly World _world;
        private readonly RelationshipShowcaseScenarioState _state;

        private RelationshipShowcaseConfig Config => _state.Config;
        private RelationshipShowcaseFrontendConfig Frontend => _state.FrontendConfig;
        private RelationshipShowcaseScenarioContext? ScenarioContext => _state.ScenarioContext;

        public RelationshipShowcasePresentationSystem(GameEngine engine, RelationshipShowcaseScenarioState state)
        {
            _engine = engine;
            _world = engine.World;
            _state = state;
        }

        public void Initialize()
        {
        }

        public void BeforeUpdate(in float dt)
        {
        }

        public void Update(in float dt)
        {
            if (!IsShowcaseMap() || ScenarioContext == null)
            {
                ClearFrontend();
                return;
            }

            if (_engine.GetService(CoreServiceKeys.GroundOverlayBuffer) is not GroundOverlayBuffer ground)
            {
                return;
            }

            RefreshDerivedState();
            PublishFrontend();
            DrawWorldHighlights(ground);
        }

        public void AfterUpdate(in float dt)
        {
        }

        public void Dispose()
        {
        }

        private bool IsShowcaseMap()
        {
            return string.Equals(_engine.CurrentMapSession?.MapId.Value, Config.MapId, StringComparison.OrdinalIgnoreCase);
        }

        private void PublishFrontend()
        {
            if (_engine.GetService(NarrativeFrontendServiceKeys.Service) is not NarrativeFrontendService frontend ||
                ScenarioContext == null)
            {
                return;
            }

            RelationshipRuntime runtime = _engine.GetService(CoreServiceKeys.RelationshipRuntime)
                ?? throw new InvalidOperationException("RelationshipRuntime missing.");
            RelationshipMetricRegistry metrics = _engine.GetService(CoreServiceKeys.RelationshipMetricRegistry)
                ?? throw new InvalidOperationException("RelationshipMetricRegistry missing.");
            RelationshipTypeRegistry types = _engine.GetService(CoreServiceKeys.RelationshipTypeRegistry)
                ?? throw new InvalidOperationException("RelationshipTypeRegistry missing.");

            int socialBondTypeId = types.GetId(Config.Types.SocialBond);
            int hostilityTypeId = types.GetId(Config.Types.Hostility);
            int loyaltyId = metrics.GetId(Config.Metrics.Loyalty);
            int supportId = metrics.GetId(Config.Metrics.Support);
            int threatId = metrics.GetId(Config.Metrics.Threat);
            int healthId = AttributeRegistry.GetId("Health");

            RelationshipNamedPair loyaltyA = Config.Presentation.Metrics.LoyaltyPairs[0];
            RelationshipNamedPair loyaltyB = Config.Presentation.Metrics.LoyaltyPairs[1];
            RelationshipNamedPair supportPair = Config.Presentation.Metrics.SupportPairs[0];
            Entity selectedHero = GetSelectedHero();
            Entity threatSource = ScenarioContext.GetEntityByName(Config.Presentation.Metrics.ThreatSourceName);

            var surfaces = new List<NarrativeFrontendSurfaceModel>
            {
                CreateSurface(
                    Frontend.PromptRibbon,
                    NarrativeFrontendSurfaceKind.PromptRibbon,
                    Frontend.PromptRibbon.Title,
                    Config.Ui.ControlsLine,
                    Config.Ui.CoverageLine),
                CreateSurface(
                    Frontend.StatusPanel,
                    NarrativeFrontendSurfaceKind.StatusPanel,
                    Frontend.StatusPanel.Title,
                    string.Empty,
                    Frontend.StatusPanel.Footer,
                    new[]
                    {
                        new NarrativeFrontendSurfaceItem(Config.Presentation.SelectedHeroLabel, _state.SelectedName, Active: true, AccentHex: "#74D7FF"),
                        new NarrativeFrontendSurfaceItem(Config.Presentation.EnemyFocusLabel, _state.EnemyFocusName, AccentHex: "#FFAA55"),
                        new NarrativeFrontendSurfaceItem(Config.Presentation.TrustedLabel, FormatBool(_state.TrustedUnlocked), AccentHex: _state.TrustedUnlocked ? "#86EFAC" : "#FDBA74", Active: _state.TrustedUnlocked),
                        new NarrativeFrontendSurfaceItem(Config.Presentation.OathBondLabel, FormatBool(_state.OathBondUnlocked), AccentHex: _state.OathBondUnlocked ? "#86EFAC" : "#FDBA74", Active: _state.OathBondUnlocked),
                        new NarrativeFrontendSurfaceItem(Config.Presentation.SynergyLabel, FormatBool(_state.SynergyActive), AccentHex: _state.SynergyActive ? "#86EFAC" : "#FDBA74", Active: _state.SynergyActive),
                    }),
                CreateSurface(
                    Frontend.RelationshipNotebook,
                    NarrativeFrontendSurfaceKind.RelationshipNotebook,
                    Frontend.RelationshipNotebook.Title,
                    string.Empty,
                    Frontend.RelationshipNotebook.Footer,
                    new[]
                    {
                        BuildMetricItem(runtime, loyaltyA, socialBondTypeId, loyaltyId, "Loyalty A"),
                        BuildMetricItem(runtime, loyaltyB, socialBondTypeId, loyaltyId, "Loyalty B"),
                        BuildMetricItem(runtime, supportPair, socialBondTypeId, supportId, "Support"),
                        new NarrativeFrontendSurfaceItem(
                            Config.Presentation.EnemyFocusLabel,
                            ReadMetric(runtime, threatSource, selectedHero, hostilityTypeId, threatId).ToString(),
                            $"{Config.Presentation.Metrics.ThreatSourceName} -> {_state.SelectedName}",
                            AccentHex: "#FFAA55"),
                        new NarrativeFrontendSurfaceItem(
                            "HP",
                            ReadHealth(selectedHero, healthId).ToString("0"),
                            _state.SelectedName,
                            AccentHex: "#93C5FD"),
                    }),
                CreateSurface(
                    Frontend.NotificationStack,
                    NarrativeFrontendSurfaceKind.NotificationStack,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    new[]
                    {
                        new NarrativeFrontendSurfaceItem("Trusted", _state.TrustedUnlocked ? Config.Logs.TriggerTrustedUnlocked : Config.Logs.RallyDenied, AccentHex: _state.TrustedUnlocked ? "#86EFAC" : "#FDBA74"),
                        new NarrativeFrontendSurfaceItem("Oath", _state.OathBondUnlocked ? Config.Logs.TriggerOathBondUnlocked : Config.Logs.Drill, AccentHex: _state.OathBondUnlocked ? "#86EFAC" : "#FDE68A"),
                        new NarrativeFrontendSurfaceItem("Synergy", _state.SynergyActive ? Config.Logs.TriggerSynergyActivated : Config.Logs.Doctrine, AccentHex: _state.SynergyActive ? "#86EFAC" : "#93C5FD"),
                    }),
                CreateSurface(
                    Frontend.ThreatBanner,
                    NarrativeFrontendSurfaceKind.ThreatBanner,
                    Frontend.ThreatBanner.Title,
                    $"{Config.Presentation.EnemyFocusLabel}: {_state.EnemyFocusName}",
                    Frontend.ThreatBanner.Footer),
                CreateSurface(
                    Frontend.FlowReview,
                    NarrativeFrontendSurfaceKind.FlowReview,
                    Frontend.FlowReview.Title,
                    string.Empty,
                    Frontend.FlowReview.Footer,
                    BuildLogItems()),
            };

            frontend.Publish(new NarrativeFrontendPageState(
                Frontend.OwnerId,
                BuildSignature(runtime, socialBondTypeId, hostilityTypeId, loyaltyId, supportId, threatId, healthId),
                true,
                Frontend.BackdropHex,
                surfaces));
        }

        private NarrativeFrontendSurfaceItem BuildMetricItem(
            RelationshipRuntime runtime,
            RelationshipNamedPair pair,
            int typeId,
            int metricId,
            string shortcut)
        {
            if (ScenarioContext == null)
            {
                return new NarrativeFrontendSurfaceItem(shortcut, "0");
            }

            Entity source = ScenarioContext.GetEntityByName(pair.SourceName);
            Entity target = ScenarioContext.GetEntityByName(pair.TargetName);
            short value = ReadMetric(runtime, source, target, typeId, metricId);
            return new NarrativeFrontendSurfaceItem(
                pair.SourceName,
                value.ToString(),
                $"{pair.SourceName} -> {pair.TargetName}",
                AccentHex: "#8BE9FD",
                Shortcut: shortcut);
        }

        private IReadOnlyList<NarrativeFrontendSurfaceItem> BuildLogItems()
        {
            var items = new List<NarrativeFrontendSurfaceItem>(_state.Log.Count);
            for (int i = _state.Log.Count - 1; i >= Math.Max(0, _state.Log.Count - 6); i--)
            {
                items.Add(new NarrativeFrontendSurfaceItem(
                    Label: $"T{i + 1:00}",
                    Value: _state.Log[i]));
            }

            return items;
        }

        private string BuildSignature(
            RelationshipRuntime runtime,
            int socialBondTypeId,
            int hostilityTypeId,
            int loyaltyId,
            int supportId,
            int threatId,
            int healthId)
        {
            if (ScenarioContext == null)
            {
                return "inactive";
            }

            RelationshipNamedPair loyaltyA = Config.Presentation.Metrics.LoyaltyPairs[0];
            RelationshipNamedPair loyaltyB = Config.Presentation.Metrics.LoyaltyPairs[1];
            RelationshipNamedPair supportPair = Config.Presentation.Metrics.SupportPairs[0];
            Entity selectedHero = GetSelectedHero();
            Entity threatSource = ScenarioContext.GetEntityByName(Config.Presentation.Metrics.ThreatSourceName);
            return string.Join("||",
                _state.Frame,
                _state.SelectedName,
                _state.EnemyFocusName,
                _state.TrustedUnlocked,
                _state.OathBondUnlocked,
                _state.SynergyActive,
                ReadMetric(runtime, ScenarioContext.GetEntityByName(loyaltyA.SourceName), ScenarioContext.GetEntityByName(loyaltyA.TargetName), socialBondTypeId, loyaltyId),
                ReadMetric(runtime, ScenarioContext.GetEntityByName(loyaltyB.SourceName), ScenarioContext.GetEntityByName(loyaltyB.TargetName), socialBondTypeId, loyaltyId),
                ReadMetric(runtime, ScenarioContext.GetEntityByName(supportPair.SourceName), ScenarioContext.GetEntityByName(supportPair.TargetName), socialBondTypeId, supportId),
                ReadMetric(runtime, threatSource, selectedHero, hostilityTypeId, threatId),
                ReadHealth(selectedHero, healthId),
                _state.Log.Count == 0 ? string.Empty : _state.Log[^1]);
        }

        private NarrativeFrontendSurfaceModel CreateSurface(
            RelationshipShowcaseSurfaceConfig config,
            NarrativeFrontendSurfaceKind kind,
            string title,
            string body,
            string footer = "",
            IReadOnlyList<NarrativeFrontendSurfaceItem>? items = null)
        {
            return new NarrativeFrontendSurfaceModel(
                SurfaceId: $"{Frontend.OwnerId}.{kind}.{config.ResolveAnchor()}",
                Kind: kind,
                Anchor: config.ResolveAnchor(),
                Title: title,
                Subtitle: config.Eyebrow,
                Body: body,
                Footer: string.IsNullOrWhiteSpace(footer) ? config.Footer : footer,
                Items: items,
                Width: config.Width,
                OffsetX: config.OffsetX,
                OffsetY: config.OffsetY,
                ZIndex: config.ZIndex,
                AccentHex: config.AccentHex,
                BackgroundHex: config.BackgroundHex,
                BorderHex: config.BorderHex,
                ForegroundHex: config.ForegroundHex,
                MutedHex: config.MutedHex);
        }

        private void ClearFrontend()
        {
            if (_engine.GetService(NarrativeFrontendServiceKeys.Service) is NarrativeFrontendService frontend)
            {
                frontend.Clear(Frontend.OwnerId);
            }
        }

        private void RefreshDerivedState()
        {
            if (ScenarioContext == null)
            {
                return;
            }

            _state.SelectedName = Config.Scenario.Heroes[Math.Clamp(_state.SelectedHeroIndex, 0, ScenarioContext.HeroEntities.Count - 1)].Name;

            RelationshipRuntime runtime = _engine.GetService(CoreServiceKeys.RelationshipRuntime)
                ?? throw new InvalidOperationException("RelationshipRuntime missing.");
            RelationshipTypeRegistry types = _engine.GetService(CoreServiceKeys.RelationshipTypeRegistry)
                ?? throw new InvalidOperationException("RelationshipTypeRegistry missing.");
            RelationshipFlagRegistry flags = _engine.GetService(CoreServiceKeys.RelationshipFlagRegistry)
                ?? throw new InvalidOperationException("RelationshipFlagRegistry missing.");

            int trustedTypeId = types.GetId(Config.State.Trusted.Type);
            int trustedFlagId = flags.GetId(Config.Flags.Trusted);
            int oathTagId = TagRegistry.GetId(Config.Tags.OathBond);
            int synergyTagId = TagRegistry.GetId(Config.Tags.Synergy);

            Entity trustedSource = ScenarioContext.GetEntityByName(Config.State.Trusted.SourceName);
            int trustedCount = 0;
            foreach (string targetName in Config.State.Trusted.TargetNames)
            {
                Entity target = ScenarioContext.GetEntityByName(targetName);
                if (trustedSource != Entity.Null && target != Entity.Null && runtime.HasFlag(trustedSource, target, trustedTypeId, trustedFlagId))
                {
                    trustedCount++;
                }
            }

            _state.TrustedUnlocked = trustedCount >= Config.State.Trusted.MinimumMatches;
            _state.OathBondUnlocked = Config.State.OathBond.EntityNames.Count(name =>
                EntityHasTag(ScenarioContext.GetEntityByName(name), oathTagId)) >= Config.State.OathBond.MinimumTaggedCount;
            _state.SynergyActive = EntityHasTag(ScenarioContext.GetTeamById(Config.State.Synergy.TeamId), synergyTagId);

            if (string.IsNullOrWhiteSpace(_state.EnemyFocusName))
            {
                _state.EnemyFocusName = Config.Presentation.EnemyFocusPendingText;
            }
        }

        private void DrawWorldHighlights(GroundOverlayBuffer ground)
        {
            if (ScenarioContext == null)
            {
                return;
            }

            AddRing(ground, GetSelectedHero(), new Vector4(0.28f, 0.82f, 1f, 0.16f), new Vector4(0.28f, 0.82f, 1f, 0.94f), 2.6f, 2.05f);
            if (!string.IsNullOrWhiteSpace(_state.EnemyFocusName))
            {
                Entity focus = ScenarioContext.GetEntityByName(_state.EnemyFocusName);
                if (focus != Entity.Null)
                {
                    AddRing(ground, focus, new Vector4(1f, 0.4f, 0.25f, 0.14f), new Vector4(1f, 0.55f, 0.35f, 0.96f), 2.8f, 2.2f);
                }
            }
        }

        private Entity GetSelectedHero()
        {
            if (ScenarioContext == null || ScenarioContext.HeroEntities.Count == 0)
            {
                return Entity.Null;
            }

            return ScenarioContext.HeroEntities[Math.Clamp(_state.SelectedHeroIndex, 0, ScenarioContext.HeroEntities.Count - 1)];
        }

        private float ReadHealth(Entity entity, int healthId)
        {
            if (entity == Entity.Null || !_world.IsAlive(entity) || !_world.Has<AttributeBuffer>(entity) || healthId < 0)
            {
                return 0f;
            }

            return _world.Get<AttributeBuffer>(entity).GetCurrent(healthId);
        }

        private short ReadMetric(RelationshipRuntime runtime, Entity source, Entity target, int typeId, int metricId)
        {
            if (source == Entity.Null || target == Entity.Null)
            {
                return 0;
            }

            return runtime.GetMetric(source, target, typeId, metricId);
        }

        private void AddRing(GroundOverlayBuffer ground, Entity entity, Vector4 fill, Vector4 border, float radius, float innerRadius)
        {
            if (entity == Entity.Null || !_world.IsAlive(entity) || !_world.Has<VisualTransform>(entity))
            {
                return;
            }

            Vector3 center = _world.Get<VisualTransform>(entity).Position;
            center.Y = 0.08f;
            ground.TryAdd(new GroundOverlayItem
            {
                Shape = GroundOverlayShape.Ring,
                Center = center,
                Radius = radius,
                InnerRadius = innerRadius,
                FillColor = fill,
                BorderColor = border,
                BorderWidth = 0.06f,
            });
        }

        private bool EntityHasTag(Entity entity, int tagId)
        {
            return tagId > 0 &&
                   entity != Entity.Null &&
                   _world.IsAlive(entity) &&
                   _world.Has<GameplayTagContainer>(entity) &&
                   _world.Get<GameplayTagContainer>(entity).HasTag(tagId);
        }

        private string FormatBool(bool value) => value ? Config.Presentation.ReadyText : Config.Presentation.LockedText;
    }
}

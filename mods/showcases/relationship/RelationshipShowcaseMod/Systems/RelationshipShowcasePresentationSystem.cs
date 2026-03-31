using System;
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
using RelationshipShowcaseMod.Runtime;

namespace RelationshipShowcaseMod.Systems
{
    internal sealed class RelationshipShowcasePresentationSystem : ISystem<float>
    {
        private readonly GameEngine _engine;
        private readonly World _world;
        private readonly RelationshipShowcaseScenarioState _state;
        private readonly Vector4 _titleColor = new(0.96f, 0.92f, 0.7f, 1f);
        private readonly Vector4 _textColor = new(0.9f, 0.93f, 0.98f, 1f);
        private readonly Vector4 _hintColor = new(0.72f, 0.82f, 0.92f, 1f);
        private readonly Vector4 _okColor = new(0.62f, 0.92f, 0.68f, 1f);
        private readonly Vector4 _warnColor = new(0.96f, 0.68f, 0.42f, 1f);

        private RelationshipShowcaseConfig Config => _state.Config;
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
                return;
            }

            if (_engine.GetService(CoreServiceKeys.ScreenOverlayBuffer) is not ScreenOverlayBuffer overlay ||
                _engine.GetService(CoreServiceKeys.GroundOverlayBuffer) is not GroundOverlayBuffer ground)
            {
                return;
            }

            RefreshDerivedState();
            DrawPanel(overlay);
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

        private void RefreshDerivedState()
        {
            if (ScenarioContext == null)
            {
                return;
            }

            int heroCount = ScenarioContext.HeroEntities.Count;
            if (heroCount > 0)
            {
                _state.SelectedName = Config.Scenario.Heroes[Math.Clamp(_state.SelectedHeroIndex, 0, heroCount - 1)].Name;
            }

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

        private void DrawPanel(ScreenOverlayBuffer overlay)
        {
            overlay.AddRect(18, 18, 620, 300, new Vector4(0.05f, 0.06f, 0.09f, 0.82f), new Vector4(0.45f, 0.62f, 0.82f, 0.8f));
            overlay.AddText(34, 34, $"{Config.Presentation.TitlePrefix}: {Config.Scenario.Teams[0].Name}", 18, _titleColor);
            overlay.AddText(34, 62, $"{Config.Presentation.SelectedHeroLabel}: {_state.SelectedName}", 15, _textColor);
            overlay.AddText(34, 84, $"{Config.Presentation.EnemyFocusLabel}: {_state.EnemyFocusName}", 15, _textColor);
            overlay.AddText(34, 106, $"{Config.Presentation.TrustedLabel}: {FormatBool(_state.TrustedUnlocked)}", 15, _state.TrustedUnlocked ? _okColor : _warnColor);
            overlay.AddText(220, 106, $"{Config.Presentation.OathBondLabel}: {FormatBool(_state.OathBondUnlocked)}", 15, _state.OathBondUnlocked ? _okColor : _warnColor);
            overlay.AddText(432, 106, $"{Config.Presentation.SynergyLabel}: {FormatBool(_state.SynergyActive)}", 15, _state.SynergyActive ? _okColor : _warnColor);
            overlay.AddText(34, 134, Config.Ui.ControlsLine, 14, _hintColor);
            overlay.AddText(34, 156, BuildMetricLine(), 14, _textColor);
            overlay.AddText(34, 178, Config.Ui.CoverageLine, 13, _hintColor);
            overlay.AddText(34, 202, Config.Presentation.BattleLogTitle, 15, _titleColor);

            int logStart = Math.Max(0, _state.Log.Count - 5);
            int y = 226;
            for (int i = logStart; i < _state.Log.Count; i++)
            {
                overlay.AddText(34, y, _state.Log[i], 13, _textColor);
                y += 18;
            }
        }

        private string BuildMetricLine()
        {
            if (ScenarioContext == null)
            {
                return Config.Presentation.MetricsPendingText;
            }

            RelationshipMetricRegistry metrics = _engine.GetService(CoreServiceKeys.RelationshipMetricRegistry)
                ?? throw new InvalidOperationException("RelationshipMetricRegistry missing.");
            RelationshipRuntime runtime = _engine.GetService(CoreServiceKeys.RelationshipRuntime)
                ?? throw new InvalidOperationException("RelationshipRuntime missing.");
            RelationshipTypeRegistry types = _engine.GetService(CoreServiceKeys.RelationshipTypeRegistry)
                ?? throw new InvalidOperationException("RelationshipTypeRegistry missing.");

            int loyaltyId = metrics.GetId(Config.Metrics.Loyalty);
            int supportId = metrics.GetId(Config.Metrics.Support);
            int threatId = metrics.GetId(Config.Metrics.Threat);
            int socialBondTypeId = types.GetId(Config.Types.SocialBond);
            int threatTypeId = types.GetId(Config.Types.Hostility);
            int healthId = AttributeRegistry.GetId("Health");

            RelationshipNamedPair loyaltyA = Config.Presentation.Metrics.LoyaltyPairs[0];
            RelationshipNamedPair loyaltyB = Config.Presentation.Metrics.LoyaltyPairs[1];
            RelationshipNamedPair supportPair = Config.Presentation.Metrics.SupportPairs[0];
            Entity loyaltySourceA = ScenarioContext.GetEntityByName(loyaltyA.SourceName);
            Entity loyaltyTargetA = ScenarioContext.GetEntityByName(loyaltyA.TargetName);
            Entity loyaltySourceB = ScenarioContext.GetEntityByName(loyaltyB.SourceName);
            Entity loyaltyTargetB = ScenarioContext.GetEntityByName(loyaltyB.TargetName);
            Entity supportSource = ScenarioContext.GetEntityByName(supportPair.SourceName);
            Entity supportTarget = ScenarioContext.GetEntityByName(supportPair.TargetName);
            Entity threatSource = ScenarioContext.GetEntityByName(Config.Presentation.Metrics.ThreatSourceName);
            Entity selected = GetSelectedHero();

            short loyaltyValueA = ReadMetric(runtime, loyaltySourceA, loyaltyTargetA, socialBondTypeId, loyaltyId);
            short loyaltyValueB = ReadMetric(runtime, loyaltySourceB, loyaltyTargetB, socialBondTypeId, loyaltyId);
            short supportValue = ReadMetric(runtime, supportSource, supportTarget, socialBondTypeId, supportId);
            short threatValue = ReadMetric(runtime, threatSource, selected, threatTypeId, threatId);
            float selectedHealth = ReadHealth(selected, healthId);

            return $"Metrics: Loyalty({loyaltyA.SourceName}->{loyaltyA.TargetName}={loyaltyValueA}, {loyaltyB.SourceName}->{loyaltyB.TargetName}={loyaltyValueB}) | Support({supportPair.SourceName}<->{supportPair.TargetName}={supportValue}) | Threat(Selected={threatValue}) | HP(Selected={selectedHealth:0})";
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

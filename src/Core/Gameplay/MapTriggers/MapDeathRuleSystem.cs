using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Map;
using Ludots.Core.Diagnostics;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Presentation.Components;

namespace Ludots.Core.Gameplay.MapTriggers
{
    /// <summary>
    /// Data-declared death rule for one map (<c>DeathRule: { "attribute": "Health", "onZero": "destroy" }</c>).
    /// The engine has no built-in "attribute zero kills" policy — maps opt in. On opt-in,
    /// every map entity whose declared attribute current value reaches zero goes through the
    /// presentation-aware destroy pipeline (event published first, finalize destroys), which
    /// feeds the heartbeat death ring so EntityDied / EntityAliveCountChanged fire for
    /// TriggerGraphs. Without the declaration the system does zero work.
    /// </summary>
    public sealed class MapDeathRuleSystem : Arch.System.ISystem<float>
    {
        private readonly QueryDescription _query = new QueryDescription()
            .WithAll<MapEntity, AttributeBuffer>();

        private readonly World _world;
        private readonly Func<MapSession?> _currentSession;
        private readonly Dictionary<string, MapDeathRule> _rulesByMap = new(StringComparer.Ordinal);
        private readonly List<Entity> _doomed = new(64);

        public MapDeathRuleSystem(World world, Func<MapSession?> currentSession)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _currentSession = currentSession ?? throw new ArgumentNullException(nameof(currentSession));
        }

        public void Declare(MapId mapId, MapDeathRule rule)
        {
            ArgumentNullException.ThrowIfNull(rule);
            rule.Validate();
            _rulesByMap[mapId.Value ?? string.Empty] = rule;
        }

        public void Retract(MapId mapId)
        {
            _rulesByMap.Remove(mapId.Value ?? string.Empty);
        }

        public void Initialize() { }
        public void BeforeUpdate(in float dt) { }
        public void AfterUpdate(in float dt) { }
        public void Dispose() { }

        public void Update(in float dt)
        {
            if (_rulesByMap.Count == 0)
            {
                return;
            }
            MapSession? session = _currentSession();
            if (session == null ||
                !_rulesByMap.TryGetValue(session.MapId.Value ?? string.Empty, out MapDeathRule rule))
            {
                return;
            }

            MapId current = session.MapId;

            int attributeId = AttributeRegistry.GetId(rule.Attribute);
            if (attributeId < 0)
            {
                throw new InvalidOperationException(
                    $"Map death rule references unregistered attribute '{rule.Attribute}'.");
            }

            _doomed.Clear();
            _world.Query(in _query, (Entity entity, ref MapEntity mapEntity, ref AttributeBuffer attributes) =>
            {
                if (mapEntity.MapId == current &&
                    attributes.GetCurrent(attributeId) <= 0f &&
                    !_world.Has<PresentationDestroyPending>(entity))
                {
                    _doomed.Add(entity);
                }
            });

            // Direct authoritative destroy: the Arch EntityDestroyed callback feeds the
            // heartbeat death ring (EntityDied / EntityAliveCountChanged) immediately.
            // Presentation-layer presenter teardown for rule-killed entities is a tracked
            // follow-up; map death must never hinge on the presentation pipeline.
            for (int i = 0; i < _doomed.Count; i++)
            {
                _world.Destroy(_doomed[i]);
            }
        }
    }

    /// <summary>Strict-parsed <c>DeathRule</c> map JSON node.</summary>
    public sealed class MapDeathRule
    {
        public string Attribute { get; set; } = "Health";
        public string OnZero { get; set; } = "destroy";

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(Attribute))
            {
                throw new InvalidOperationException("Map DeathRule.attribute must be a non-empty attribute id.");
            }

            if (!string.Equals(OnZero, "destroy", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Map DeathRule.onZero '{OnZero}' is not a known policy. Known policies: destroy.");
            }
        }
    }
}

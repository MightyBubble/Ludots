using System;
using System.Collections.Generic;
using Arch.Core;
using Arch.System;
using CapabilityStandardCrowdPhysicsArenaMod.Runtime;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Layers;
using Ludots.Core.MassNavigation;
using Ludots.Core.Movement.Physics2DBridge;
using Ludots.Core.Physics2D;
using Ludots.Core.Physics2D.Components;

namespace CapabilityStandardCrowdPhysicsArenaMod.Systems;

/// <summary>
/// Pressure plate → door gameplay bridge (issue #734).
///
/// Consumes ContactBegin/ContactEnd events routed for the <c>arena.plate</c> layer by the
/// massnav→kinematic bridge's <see cref="ContactEventRouter2D"/> and counts distinct squad
/// agents. The plate is a dynamic body seated in a static socket, so a single crossing agent
/// produces flickering raw Begin/End pairs while position correction pushes the plate out of
/// penetration each step; counting unique agents keeps "N units cross → exactly N Begins"
/// exact. Begin/End are both exposed so "everyone who stepped on also stepped off" stays a
/// queryable invariant, and every authored door opens once the configured Begin threshold is
/// reached. Opening a door means zeroing its ManifestationObstacleIntent2D sink flags and
/// marking it dirty so <c>ManifestationObstacleBridge2DSystem</c> removes the physics collider
/// and the navigation obstacle projection.
/// </summary>
public sealed class CrowdPhysicsArenaPressurePlateDoorSystem : BaseSystem<World, float>, IContactEventConsumer2D
{
    private static readonly QueryDescription ClosedDoorQuery = new QueryDescription()
        .WithAll<CrowdPhysicsArenaDoor, ManifestationObstacleIntent2D>()
        .WithNone<ManifestationObstacleBridge2DDirty>();

    private static readonly QueryDescription PlateAnchorQuery = new QueryDescription()
        .WithAll<CrowdPhysicsArenaPlateAnchor, Position2D, Velocity2D>();

    private const int AgentTrackingCapacity = 256;

    private readonly List<Entity> _doorsToOpen = new(4);
    private readonly HashSet<Entity> _agentsEverOnPlate = new(AgentTrackingCapacity);
    private readonly Dictionary<Entity, int> _activePlatePairsByAgent = new(AgentTrackingCapacity);
    private readonly HashSet<Entity> _agentsOffPlate = new(AgentTrackingCapacity);
    private uint _plateCategoryBit;
    private uint _agentCategoryBit;
    private bool _layerBitsResolved;

    public CrowdPhysicsArenaPressurePlateDoorSystem(World world) : base(world)
    {
    }

    /// <summary>Distinct squad agents that have begun contact with the plate (one Begin per agent).</summary>
    public long AgentContactBeginCount { get; private set; }

    /// <summary>Distinct counted agents that have since fully separated from the plate.</summary>
    public long AgentContactEndCount { get; private set; }

    /// <summary>Agents currently standing on the plate (Begin − End reconciliation).</summary>
    public long ActiveAgentContacts => AgentContactBeginCount - AgentContactEndCount;

    /// <summary>Number of doors this system has opened.</summary>
    public int OpenedDoorCount { get; private set; }

    public void OnContactEvent(in ContactEvent2D contactEvent)
    {
        ResolveLayerBitsOnce();

        // Identify the plate's contact partner; only squad agents count toward the door.
        bool aIsPlate = (contactEvent.LayerA.Category & _plateCategoryBit) != 0u;
        bool bIsPlate = (contactEvent.LayerB.Category & _plateCategoryBit) != 0u;
        if (!aIsPlate && !bIsPlate)
        {
            throw new InvalidOperationException(
                "CrowdPhysicsArenaPressurePlateDoorSystem received a contact event without the arena.plate layer; " +
                "router layer dispatch is broken.");
        }

        uint partnerCategory = aIsPlate ? contactEvent.LayerB.Category : contactEvent.LayerA.Category;
        if ((partnerCategory & _agentCategoryBit) == 0u)
        {
            return;
        }

        Entity agent = aIsPlate ? contactEvent.EntityB : contactEvent.EntityA;
        if (contactEvent.Type == ContactEventType2D.Begin)
        {
            if (_agentsEverOnPlate.Add(agent))
            {
                AgentContactBeginCount++;
            }

            _activePlatePairsByAgent.TryGetValue(agent, out int activePairs);
            _activePlatePairsByAgent[agent] = activePairs + 1;
            if (_agentsOffPlate.Remove(agent))
            {
                AgentContactEndCount--;
            }

            return;
        }

        if (!_activePlatePairsByAgent.TryGetValue(agent, out int pairs) || pairs <= 0)
        {
            throw new InvalidOperationException(
                $"CrowdPhysicsArenaPressurePlateDoorSystem received ContactEnd for agent {agent.Id} without a matching Begin; " +
                "the bridge Begin/End edge contract is broken.");
        }

        if (pairs == 1)
        {
            _activePlatePairsByAgent.Remove(agent);
            _agentsOffPlate.Add(agent);
            AgentContactEndCount++;
        }
        else
        {
            _activePlatePairsByAgent[agent] = pairs - 1;
        }
    }

    public override void Update(in float dt)
    {
        ReseatAnchoredPlates();

        _doorsToOpen.Clear();
        long beginCount = AgentContactBeginCount;
        World.Query(in ClosedDoorQuery, (Entity entity, ref CrowdPhysicsArenaDoor door, ref ManifestationObstacleIntent2D intent) =>
        {
            if (intent.SinkPhysicsCollider == 0 && intent.SinkNavigationObstacle == 0)
            {
                return;
            }

            if (door.OpenThresholdContacts <= 0)
            {
                throw new InvalidOperationException(
                    $"CrowdPhysicsArena.Door on entity {entity.Id} requires openThresholdContacts > 0 (data-driven, no default).");
            }

            if (beginCount >= door.OpenThresholdContacts)
            {
                _doorsToOpen.Add(entity);
            }
        });

        for (int i = 0; i < _doorsToOpen.Count; i++)
        {
            Entity entity = _doorsToOpen[i];
            ref ManifestationObstacleIntent2D intent = ref World.Get<ManifestationObstacleIntent2D>(entity);
            intent.SinkPhysicsCollider = 0;
            intent.SinkNavigationObstacle = 0;
            World.Add(entity, new ManifestationObstacleBridge2DDirty());
            OpenedDoorCount++;
        }
    }

    /// <summary>
    /// Re-seats every anchored plate on its authored position with zero velocity (see
    /// <see cref="CrowdPhysicsArenaPlateAnchor"/>): the plate stays a bolted-down sensor while
    /// remaining a Dynamic emitter that pairs with kinematic agents in the broadphase.
    /// Per-step solver corrections still move it transiently within a physics step, but they
    /// can no longer accumulate into socket tunneling and ejection under full-squad pressure.
    /// </summary>
    private void ReseatAnchoredPlates()
    {
        World.Query(in PlateAnchorQuery, (
            ref CrowdPhysicsArenaPlateAnchor anchor,
            ref Position2D position,
            ref Velocity2D velocity) =>
        {
            if (anchor.Captured == 0)
            {
                anchor.AnchorCm = position.Value;
                anchor.Captured = 1;
                return;
            }

            position.Value = anchor.AnchorCm;
            velocity = Velocity2D.Zero;
        });
    }

    private void ResolveLayerBitsOnce()
    {
        if (_layerBitsResolved)
        {
            return;
        }

        _plateCategoryBit = 1u << LayerRegistry.GetIndex(CrowdPhysicsArenaLayerNames.Plate);
        _agentCategoryBit = 1u << LayerRegistry.GetIndex(MassNavigationLayerNames.Agent);
        _layerBitsResolved = true;
    }
}

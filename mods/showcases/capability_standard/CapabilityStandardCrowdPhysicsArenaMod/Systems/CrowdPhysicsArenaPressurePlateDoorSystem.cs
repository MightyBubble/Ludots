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

namespace CapabilityStandardCrowdPhysicsArenaMod.Systems;

/// <summary>
/// Pressure plate → door gameplay bridge (issue #734).
///
/// Consumes ContactBegin/ContactEnd events routed for the <c>arena.plate</c> layer by the
/// massnav→kinematic bridge's <see cref="ContactEventRouter2D"/>, counts squad-agent contacts
/// (Begin/End are both exposed so "everyone who stepped on also stepped off" is a queryable
/// invariant), and opens every authored door once the configured ContactBegin threshold is
/// reached. Opening a door means zeroing its ManifestationObstacleIntent2D sink flags and
/// marking it dirty so <c>ManifestationObstacleBridge2DSystem</c> removes the physics collider
/// and the navigation obstacle projection.
/// </summary>
public sealed class CrowdPhysicsArenaPressurePlateDoorSystem : BaseSystem<World, float>, IContactEventConsumer2D
{
    private static readonly QueryDescription ClosedDoorQuery = new QueryDescription()
        .WithAll<CrowdPhysicsArenaDoor, ManifestationObstacleIntent2D>()
        .WithNone<ManifestationObstacleBridge2DDirty>();

    private readonly List<Entity> _doorsToOpen = new(4);
    private uint _plateCategoryBit;
    private uint _agentCategoryBit;
    private bool _layerBitsResolved;

    public CrowdPhysicsArenaPressurePlateDoorSystem(World world) : base(world)
    {
    }

    /// <summary>Total agent ContactBegin events on the plate.</summary>
    public long AgentContactBeginCount { get; private set; }

    /// <summary>Total agent ContactEnd events on the plate.</summary>
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

        if (contactEvent.Type == ContactEventType2D.Begin)
        {
            AgentContactBeginCount++;
        }
        else
        {
            AgentContactEndCount++;
        }
    }

    public override void Update(in float dt)
    {
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

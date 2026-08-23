using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Map;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Physics2D.Components;
using Ludots.Core.Scripting;

namespace DynamicNavBakeShowcaseMod.Runtime;

internal sealed class DynamicNavBakeShowcaseWallPool
{
    private readonly Entity[] _entities;
    private readonly bool[] _deployed;
    private readonly int[] _slotIndices;
    private int _deployedCount;

    public DynamicNavBakeShowcaseWallPool(int capacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        _entities = new Entity[capacity];
        _deployed = new bool[capacity];
        _slotIndices = new int[capacity];
        for (int i = 0; i < capacity; i++)
        {
            _slotIndices[i] = -1;
        }
    }

    public int Capacity => _entities.Length;
    public int DeployedCount => _deployedCount;
    public ReadOnlySpan<Entity> Entities => _entities;
    public bool IsFullyBound
    {
        get
        {
            for (int i = 0; i < _entities.Length; i++)
            {
                if (_entities[i] == Entity.Null)
                {
                    return false;
                }
            }

            return true;
        }
    }

    public void ValidateBoundEntities(IReadOnlyList<Entity> expectedWalls)
    {
        if (expectedWalls.Count != _entities.Length)
        {
            throw new InvalidOperationException(
                $"Wall pool validate expected {_entities.Length} entities, got {expectedWalls.Count}.");
        }

        var expected = new HashSet<Entity>();
        for (int i = 0; i < expectedWalls.Count; i++)
        {
            expected.Add(expectedWalls[i]);
        }

        for (int i = 0; i < _entities.Length; i++)
        {
            if (!expected.Contains(_entities[i]))
            {
                throw new InvalidOperationException(
                    $"Wall pool slot {i} entity {_entities[i].Id} is not in the current spawned wall set.");
            }
        }
    }

    public void ClearBindings()
    {
        for (int i = 0; i < _entities.Length; i++)
        {
            _entities[i] = Entity.Null;
            _deployed[i] = false;
            _slotIndices[i] = -1;
        }

        _deployedCount = 0;
    }

    public void BindSpawnedEntity(int poolIndex, Entity entity)
    {
        if ((uint)poolIndex >= (uint)_entities.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(poolIndex));
        }

        if (entity == Entity.Null)
        {
            throw new ArgumentException("Wall pool cannot bind a null entity.", nameof(entity));
        }

        if (_entities[poolIndex] != Entity.Null && _entities[poolIndex] != entity)
        {
            throw new InvalidOperationException(
                $"Wall pool index {poolIndex} is already bound to entity {_entities[poolIndex].Id}, cannot bind {entity.Id}.");
        }

        _entities[poolIndex] = entity;
    }

    public bool TryDeploySlot(
        GameEngine engine,
        DynamicNavBakeShowcaseConfig config,
        int slotIndex,
        int centerXCm,
        int centerYCm,
        out string error)
    {
        error = string.Empty;
        World world = engine.World;
        if ((uint)slotIndex >= (uint)config.Gate.SegmentCount)
        {
            error = $"Gate slot {slotIndex} is out of range.";
            return false;
        }

        for (int i = 0; i < _entities.Length; i++)
        {
            if (_deployed[i] && _slotIndices[i] == slotIndex)
            {
                error = $"Gate slot {slotIndex} is already built.";
                return false;
            }
        }

        int poolIndex = FindFreePoolIndex();
        if (poolIndex < 0)
        {
            error = "Wall pool exhausted.";
            return false;
        }

        Entity entity = _entities[poolIndex];
        if (entity == Entity.Null || !world.IsAlive(entity))
        {
            error = $"Wall pool entity {poolIndex} is not alive.";
            return false;
        }

        ComputeGateSlotWorldCm(config, slotIndex, centerXCm, centerYCm, out int xCm, out int yCm);
        if (!TryApplySceneNavigationFootprint(world, entity, config, out error))
        {
            return false;
        }

        MoveEntityWorldPosition(engine, entity, xCm, yCm);
        _deployed[poolIndex] = true;
        _slotIndices[poolIndex] = slotIndex;
        _deployedCount++;
        return true;
    }

    public bool TryDemolishSlot(GameEngine engine, DynamicNavBakeShowcaseConfig config, int slotIndex, out string error)
    {
        error = string.Empty;
        World world = engine.World;
        int poolIndex = FindPoolIndexForSlot(slotIndex);
        if (poolIndex < 0)
        {
            error = $"Gate slot {slotIndex} is not built.";
            return false;
        }

        Entity entity = _entities[poolIndex];
        if (entity == Entity.Null || !world.IsAlive(entity))
        {
            error = $"Wall pool entity {poolIndex} is not alive.";
            return false;
        }

        MoveEntityWorldPosition(engine, entity, config.Parking.XCm, config.Parking.YCm);
        _deployed[poolIndex] = false;
        _slotIndices[poolIndex] = -1;
        _deployedCount--;
        return true;
    }

    public bool TryBuildAll(
        GameEngine engine,
        DynamicNavBakeShowcaseConfig config,
        int centerXCm,
        int centerYCm,
        out string error)
    {
        error = string.Empty;
        World world = engine.World;
        int segmentCount = config.Gate.SegmentCount;
        if (segmentCount > _entities.Length)
        {
            error = $"Gate segmentCount {segmentCount} exceeds wall pool capacity {_entities.Length}.";
            return false;
        }

        // Prevalidate: every slot must be free and every required pool entity must be alive.
        for (int slot = 0; slot < segmentCount; slot++)
        {
            if (FindPoolIndexForSlot(slot) >= 0)
            {
                error = $"Gate slot {slot} is already built.";
                return false;
            }
        }

        for (int poolIndex = 0; poolIndex < segmentCount; poolIndex++)
        {
            Entity entity = _entities[poolIndex];
            if (entity == Entity.Null || !world.IsAlive(entity))
            {
                error = $"Wall pool entity {poolIndex} is not alive.";
                return false;
            }

            if (!TryValidateSceneNavigationFootprint(world, entity, config, out error))
            {
                return false;
            }

            if (_deployed[poolIndex])
            {
                error = $"Wall pool index {poolIndex} is already deployed; BuildAll requires a free prefix pool.";
                return false;
            }
        }

        // Transaction: mutate only after validation. Failure is impossible past this point unless world dies mid-loop.
        for (int slot = 0; slot < segmentCount; slot++)
        {
            Entity entity = _entities[slot];
            if (entity == Entity.Null || !world.IsAlive(entity))
            {
                error = $"Wall pool entity {slot} became invalid during BuildAll transaction.";
                // Roll back any slots already moved in this transaction.
                for (int rollback = 0; rollback < slot; rollback++)
                {
                    MoveEntityWorldPosition(engine, _entities[rollback], config.Parking.XCm, config.Parking.YCm);
                    _deployed[rollback] = false;
                    _slotIndices[rollback] = -1;
                    _deployedCount--;
                }

                return false;
            }

            ComputeGateSlotWorldCm(config, slot, centerXCm, centerYCm, out int xCm, out int yCm);
            if (!TryApplySceneNavigationFootprint(world, entity, config, out error))
            {
                for (int rollback = 0; rollback < slot; rollback++)
                {
                    MoveEntityWorldPosition(engine, _entities[rollback], config.Parking.XCm, config.Parking.YCm);
                    _deployed[rollback] = false;
                    _slotIndices[rollback] = -1;
                    _deployedCount--;
                }

                return false;
            }

            MoveEntityWorldPosition(engine, entity, xCm, yCm);
            _deployed[slot] = true;
            _slotIndices[slot] = slot;
            _deployedCount++;
        }

        return true;
    }

    public bool TryDemolishAll(GameEngine engine, DynamicNavBakeShowcaseConfig config, out string error)
    {
        error = string.Empty;
        World world = engine.World;
        int segmentCount = config.Gate.SegmentCount;
        Span<int> poolIndices = stackalloc int[segmentCount];
        for (int slot = 0; slot < segmentCount; slot++)
        {
            int poolIndex = FindPoolIndexForSlot(slot);
            if (poolIndex < 0)
            {
                error = $"Gate slot {slot} is not built.";
                return false;
            }

            Entity entity = _entities[poolIndex];
            if (entity == Entity.Null || !world.IsAlive(entity))
            {
                error = $"Wall pool entity {poolIndex} is not alive.";
                return false;
            }

            poolIndices[slot] = poolIndex;
        }

        for (int slot = 0; slot < segmentCount; slot++)
        {
            int poolIndex = poolIndices[slot];
            MoveEntityWorldPosition(engine, _entities[poolIndex], config.Parking.XCm, config.Parking.YCm);
            _deployed[poolIndex] = false;
            _slotIndices[poolIndex] = -1;
            _deployedCount--;
        }

        return true;
    }

    public bool IsSlotDeployed(int slotIndex)
    {
        return FindPoolIndexForSlot(slotIndex) >= 0;
    }

    public IReadOnlyList<Entity> GetStableEntityIds()
    {
        var list = new List<Entity>(_entities.Length);
        for (int i = 0; i < _entities.Length; i++)
        {
            list.Add(_entities[i]);
        }

        return list;
    }

    private int FindFreePoolIndex()
    {
        for (int i = 0; i < _entities.Length; i++)
        {
            if (!_deployed[i])
            {
                return i;
            }
        }

        return -1;
    }

    private int FindPoolIndexForSlot(int slotIndex)
    {
        for (int i = 0; i < _entities.Length; i++)
        {
            if (_deployed[i] && _slotIndices[i] == slotIndex)
            {
                return i;
            }
        }

        return -1;
    }

    private static void ComputeGateSlotWorldCm(
        DynamicNavBakeShowcaseConfig config,
        int slotIndex,
        int centerXCm,
        int centerYCm,
        out int xCm,
        out int yCm)
    {
        int half = (config.Gate.SegmentCount - 1) / 2;
        int offset = (slotIndex - half) * config.Gate.SegmentSpacingCm;
        xCm = checked(centerXCm + offset);
        yCm = centerYCm;
    }

    private static bool TryValidateSceneNavigationFootprint(
        World world,
        Entity entity,
        DynamicNavBakeShowcaseConfig config,
        out string error)
    {
        error = string.Empty;
        if (!world.TryGet(entity, out ManifestationObstacleIntent2D intent))
        {
            error = $"Wall entity {entity.Id} is missing ManifestationObstacleIntent2D.";
            return false;
        }

        if (intent.Shape != ManifestationObstacleShape2D.Circle)
        {
            error = $"Wall entity {entity.Id} must use a circle ManifestationObstacleIntent2D.";
            return false;
        }

        if (intent.SinkNavigationObstacle == 0)
        {
            error = $"Wall entity {entity.Id} must sink a runtime navigation obstacle.";
            return false;
        }

        if (config.Gate.NavRadiusCm <= 0 || config.Gate.NavMaxYcm <= config.Gate.NavMinYcm)
        {
            error = "Dynamic NavBake gate navigation footprint is invalid.";
            return false;
        }

        return true;
    }

    private static bool TryApplySceneNavigationFootprint(
        World world,
        Entity entity,
        DynamicNavBakeShowcaseConfig config,
        out string error)
    {
        if (!TryValidateSceneNavigationFootprint(world, entity, config, out error))
        {
            return false;
        }

        ManifestationObstacleIntent2D intent = world.Get<ManifestationObstacleIntent2D>(entity);
        intent.RadiusCm = config.Gate.NavRadiusCm;
        intent.NavRadiusCm = config.Gate.NavRadiusCm;
        intent.NavMinYcm = config.Gate.NavMinYcm;
        intent.NavMaxYcm = config.Gate.NavMaxYcm;
        world.Set(entity, intent);
        MarkBridgeDirty(world, entity);
        return true;
    }

    private static void MarkBridgeDirty(World world, Entity entity)
    {
        if (world.Has<ManifestationObstacleBridge2DDirty>(entity))
        {
            return;
        }

        world.Add(entity, new ManifestationObstacleBridge2DDirty());
    }

    private static void MoveEntityWorldPosition(GameEngine engine, Entity entity, int xCm, int yCm)
    {
        World world = engine.World;
        Fix64Vec2 worldCm = Fix64Vec2.FromInt(xCm, yCm);

        // Mark bridge dirty before pose writes. Arch archetype moves on Add must not
        // invalidate the teleport writes below.
        MarkBridgeDirty(world, entity);

        // WorldPositionCm is logical pose; Position2D is physics/sync pose.
        // Physics2DToWorldPositionSyncSystem copies Position2D to WorldPositionCm each
        // PostMovement tick, so both must move together or the wall snaps back to parking.
        world.Set(entity, new WorldPositionCm { Value = worldCm });
        if (world.Has<PreviousWorldPositionCm>(entity))
        {
            world.Set(entity, new PreviousWorldPositionCm { Value = worldCm });
        }

        if (world.Has<Position2D>(entity))
        {
            world.Set(entity, new Position2D { Value = worldCm });
        }

        if (world.Has<PreviousPosition2D>(entity))
        {
            world.Set(entity, new PreviousPosition2D { Value = worldCm });
        }
    }

    public static int BuildSpawnRequestCount(DynamicNavBakeShowcaseConfig config)
    {
        int count = config.WallPoolCapacity;
        count += 1; // goal marker
        count += 2; // side route markers
        count += config.Squad.Count;
        return count;
    }

    public static int WriteSpawnRequests(
        GameEngine engine,
        DynamicNavBakeShowcaseConfig config,
        MapId mapId,
        Span<RuntimeEntitySpawnRequest> destination,
        DynamicNavBakeShowcaseWallPool pool)
    {
        int index = 0;
        DynamicNavBakeShowcaseGateConfig gate = config.Gate;
        for (int poolIndex = 0; poolIndex < config.WallPoolCapacity; poolIndex++)
        {
            destination[index++] = CreateTemplateSpawn(
                gate.WallTemplateId,
                mapId,
                config.Parking.XCm,
                config.Parking.YCm);
        }

        destination[index++] = CreateTemplateSpawn(
            config.Goal.TemplateId,
            mapId,
            config.Goal.XCm,
            config.Goal.YCm);
        destination[index++] = CreateTemplateSpawn(
            config.SideRouteWest.MarkerTemplateId,
            mapId,
            config.SideRouteWest.XCm,
            config.SideRouteWest.YCm);
        destination[index++] = CreateTemplateSpawn(
            config.SideRouteEast.MarkerTemplateId,
            mapId,
            config.SideRouteEast.XCm,
            config.SideRouteEast.YCm);

        // Formal Command / box-select authorize through ControlDomain Owns edges, not PlayerOwner mirrors.
        // Formation Capability and MassNavigation scenario bootstrap use the same OwnershipSource seam.
        PlayerEntityLookup players = engine.GetService(CoreServiceKeys.PlayerEntityLookup)
            ?? throw new InvalidOperationException(
                "DynamicNavBake squad spawn requires PlayerEntityLookup before OwnershipSource is authored.");
        int localPlayerId = ResolveLocalPlayerId(engine);
        if (!players.TryGet(localPlayerId, out Entity ownershipSource) ||
            ownershipSource == Entity.Null ||
            !engine.World.IsAlive(ownershipSource))
        {
            throw new InvalidOperationException(
                $"DynamicNavBake squad spawn requires a live player representative for local player {localPlayerId} before OwnershipSource is authored.");
        }

        TeamEntityLookup teams = engine.GetService(CoreServiceKeys.TeamEntityLookup)
            ?? throw new InvalidOperationException(
                "DynamicNavBake squad spawn requires TeamEntityLookup before MembershipTarget is authored.");
        const int localTeamId = 1;
        if (!teams.TryGet(localTeamId, out Entity membershipTarget) ||
            membershipTarget == Entity.Null ||
            !engine.World.IsAlive(membershipTarget))
        {
            throw new InvalidOperationException(
                $"DynamicNavBake squad spawn requires a live team representative for team {localTeamId} before MembershipTarget is authored.");
        }

        for (int slotIndex = 0; slotIndex < config.Squad.Count; slotIndex++)
        {
            ComputeSquadSlotOffsetCm(config.Squad, slotIndex, out int offsetXCm, out int offsetZCm);
            int x = checked(config.Squad.CenterXCm + offsetXCm);
            int y = checked(config.Squad.CenterYCm + offsetZCm);
            RuntimeEntitySpawnRequest spawn = CreateTemplateSpawn(config.Squad.TemplateId, mapId, x, y);
            spawn.OwnershipSource = ownershipSource;
            spawn.HasOwnershipSource = 1;
            spawn.MembershipTarget = membershipTarget;
            spawn.HasMembershipTarget = 1;
            destination[index++] = spawn;
        }

        return index;
    }

    private static int ResolveLocalPlayerId(GameEngine engine)
    {
        // Map authorship binds Players[{ PlayerId: 1 }]. Launch context may publish LocalPlayerId later;
        // squad OwnershipSource must still resolve against PlayerEntityLookup during MapLoaded spawn.
        const int authoredLocalPlayerId = 1;
        if (engine.GlobalContext.TryGetValue(CoreServiceKeys.LocalPlayerId.Name, out object? playerIdObj) &&
            playerIdObj is int playerId &&
            playerId > 0)
        {
            return playerId;
        }

        return authoredLocalPlayerId;
    }

    /// <summary>
    /// Deterministic integer offset of a squad grid slot relative to the shared formation center.
    /// Slot order is row-major and matches entity-id binding after spawn (slot 0 = first spawned member).
    /// </summary>
    public static void ComputeSquadSlotOffsetCm(
        DynamicNavBakeShowcaseSquadConfig squad,
        int slotIndex,
        out int offsetXCm,
        out int offsetZCm)
    {
        ArgumentNullException.ThrowIfNull(squad);
        if ((uint)slotIndex >= (uint)squad.Count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(slotIndex),
                slotIndex,
                $"Squad slot index must be in [0, {squad.Count}).");
        }

        int col = slotIndex % squad.Columns;
        int row = slotIndex / squad.Columns;
        if (row >= squad.Rows)
        {
            throw new InvalidOperationException(
                $"Squad slot {slotIndex} maps to row {row}, but authored rows is {squad.Rows}.");
        }

        ComputeSquadHalfExtentsCm(squad, out int halfWidth, out int halfDepth);
        offsetXCm = checked(col * squad.SpacingXCm - halfWidth);
        offsetZCm = checked(row * squad.SpacingYCm - halfDepth);
    }

    public static void ComputeSquadHalfExtentsCm(
        DynamicNavBakeShowcaseSquadConfig squad,
        out int halfWidthCm,
        out int halfDepthCm)
    {
        ArgumentNullException.ThrowIfNull(squad);
        halfWidthCm = checked((squad.Columns - 1) * squad.SpacingXCm / 2);
        halfDepthCm = checked((squad.Rows - 1) * squad.SpacingYCm / 2);
    }

    private static RuntimeEntitySpawnRequest CreateTemplateSpawn(string templateId, MapId mapId, int xCm, int yCm)
    {
        return new RuntimeEntitySpawnRequest
        {
            Kind = RuntimeEntitySpawnKind.Template,
            TemplateId = templateId,
            MapId = mapId,
            WorldPositionCm = Fix64Vec2.FromInt(xCm, yCm),
            HasWorldPosition = 1,
            FacingAngleRad = 0f,
            HasFacing = 1,
        };
    }
}

using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Presentation.Components;
using Schedulers;

namespace Ludots.Core.MassCrowd.Runtime;

public sealed class MassFlowSimulationState
{
    public const float VisualMetersPerCm = 0.01f;

    private readonly int _fieldWidthCm;
    private readonly int _fieldHeightCm;
    private readonly int _flowCellSizeCm;
    private readonly int _gridWidth;
    private readonly int _gridHeight;
    private readonly int _gridCellCount;
    private readonly int _maxObstacleCount;
    private readonly int _separationHashCellSizeCm;
    private readonly int _separationHashWidth;
    private readonly int _separationHashHeight;
    private readonly int _separationHashMinSearchRadiusCells;
    private readonly int _hardResolveHashCellSizeCm;
    private readonly int _hardResolveHashWidth;
    private readonly int _hardResolveHashHeight;
    private readonly int _hardResolveHashMinSearchRadiusCells;
    private readonly float _playAreaMinXCm;
    private readonly float _playAreaMaxXCm;
    private readonly float _playAreaMinYCm;
    private readonly float _playAreaMaxYCm;

    private readonly int _parallelWorkerCount;

    private readonly float[] _staticCost;
    private readonly float[] _cost;
    private readonly float[] _obsX;
    private readonly float[] _obsY;
    private readonly float[] _obsWorldX;
    private readonly float[] _obsWorldY;
    private readonly float[] _obsRadius;
    private readonly float[] _obsR2;
    private readonly float[] _obsPR;
    private readonly float[] _obsPR2;
    private readonly int[] _separationCellCounts;
    private readonly int[] _separationCellOffsets;
    private readonly int[] _separationCellCursor;
    private readonly int[] _hardResolveCellCounts;
    private readonly int[] _hardResolveCellOffsets;
    private readonly int[] _hardResolveCellCursor;

    private float[] _positionsCm = Array.Empty<float>();
    private float[] _velocitiesCm = Array.Empty<float>();
    private float[] _readPositionsCm = Array.Empty<float>();
    private float[] _readVelocitiesCm = Array.Empty<float>();
    private float[] _unitProgressAnchorCm = Array.Empty<float>();
    private float[] _unitSettledAnchorCm = Array.Empty<float>();
    private int[] _teams = Array.Empty<int>();
    private int[] _teamRuntimeIndices = Array.Empty<int>();
    private int[] _teamLocalIndices = Array.Empty<int>();
    private int[] _flowRuntimeIndices = Array.Empty<int>();
    private uint[] _layerCategoryMasks = Array.Empty<uint>();
    private uint[] _layerInteractionMasks = Array.Empty<uint>();
    private float[] _navMasses = Array.Empty<float>();
    private float[] _visualScales = Array.Empty<float>();
    private float[] _bodyRadiiCm = Array.Empty<float>();
    private float[] _speedsCmPerSecond = Array.Empty<float>();
    private int[] _separationHashSearchRadiusCellsByAgent = Array.Empty<int>();
    private int[] _hardResolveHashSearchRadiusCellsByAgent = Array.Empty<int>();
    private float[] _unitTargetsCm = Array.Empty<float>();
    private byte[] _hasUnitTarget = Array.Empty<byte>();
    private byte[] _selectedFlags = Array.Empty<byte>();
    private byte[] _hardResolveCandidates = Array.Empty<byte>();
    private byte[] _heavyProfileFlags = Array.Empty<byte>();
    private byte[] _entitySyncDirtyFlags = Array.Empty<byte>();
    private byte[] _unitSettledFlags = Array.Empty<byte>();
    private byte[] _unitRetryCounts = Array.Empty<byte>();
    private float[] _unitStuckSeconds = Array.Empty<float>();
    private int[] _separationAgents = Array.Empty<int>();
    private int[] _hardResolveAgents = Array.Empty<int>();
    private int[] _entitySyncDirtyAgents = Array.Empty<int>();
    private readonly UnitStepJob[] _stepJobs;
    private readonly JobHandle[] _stepHandles;

    private readonly List<TeamRuntimeState> _teamStates = new();
    private readonly List<FlowRuntimeState> _flowStates = new();
    private readonly Dictionary<int, int> _teamStateIndexById = new();
    private TeamRelationship[] _teamRelationshipMatrix = Array.Empty<TeamRelationship>();
    private int _teamRelationshipRevision = int.MinValue;
    private readonly float[] _maxBodyRadiusByCategoryBit = new float[32];
    private int _frameCount;
    private bool _useCandidateGating;
    private int _entitySyncDirtyCount;
    private bool _flowDirty = true;
    private float _maxBodyRadiusCm;
    private float[] _maxInteractingBodyRadiiCm = Array.Empty<float>();
    private bool _maxInteractingBodyRadiiDirty = true;
    private int _lastFlowStepFrame = int.MinValue;
    private int _lastCrowdStampFrame = int.MinValue;
    private int _lastObstacleStampFrame = int.MinValue;
    private int _crowdStampCursor;
    private float _worldOriginXCm;
    private float _worldOriginYCm;
    private float _worldMinXCm = float.NegativeInfinity;
    private float _worldMaxXCm = float.PositiveInfinity;
    private float _worldMinYCm = float.NegativeInfinity;
    private float _worldMaxYCm = float.PositiveInfinity;

    public int UnitCount { get; private set; }
    public int ObstacleCount { get; private set; }
    public int SettledUnitCount { get; private set; }
    public int PendingEntitySyncCount => _entitySyncDirtyCount;
    public float LastFlowFieldRebuildMs { get; private set; }
    public MassFlowArrivalTuning ArrivalTuning { get; } = new();
    public MassFlowAvoidanceTuning AvoidanceTuning { get; } = new();
    public MassNavigationCrowdSemantics Semantics { get; } = new();

    public int FieldWidthCm => _fieldWidthCm;
    public int FieldHeightCm => _fieldHeightCm;
    public int FlowCellSizeCm => _flowCellSizeCm;
    public int GridWidth => _gridWidth;
    public int GridHeight => _gridHeight;
    public int MaxObstacleCount => _maxObstacleCount;
    public int ParallelWorkerCount => _parallelWorkerCount;
    public int SeparationHashCellSizeCm => _separationHashCellSizeCm;
    public int SeparationHashWidth => _separationHashWidth;
    public int SeparationHashHeight => _separationHashHeight;
    public int HardResolveHashCellSizeCm => _hardResolveHashCellSizeCm;
    public int HardResolveHashWidth => _hardResolveHashWidth;
    public int HardResolveHashHeight => _hardResolveHashHeight;
    public float PlayAreaMinXCm => _playAreaMinXCm;
    public float PlayAreaMaxXCm => _playAreaMaxXCm;
    public float PlayAreaMinYCm => _playAreaMinYCm;
    public float PlayAreaMaxYCm => _playAreaMaxYCm;
    public ReadOnlySpan<float> PositionsCm => _positionsCm.AsSpan(0, UnitCount * 2);
    public ReadOnlySpan<int> Teams => _teams.AsSpan(0, UnitCount);
    public ReadOnlySpan<byte> SelectedFlags => _selectedFlags.AsSpan(0, UnitCount);
    public float WorldOriginXCm => _worldOriginXCm;
    public float WorldOriginYCm => _worldOriginYCm;

    public MassFlowSimulationState(MassFlowSolverConfig solver)
    {
        ArgumentNullException.ThrowIfNull(solver);
        solver.Validate();

        _fieldWidthCm = solver.FieldWidthCm;
        _fieldHeightCm = solver.FieldHeightCm;
        _flowCellSizeCm = solver.FlowCellSizeCm;
        _gridWidth = solver.FlowGridWidth;
        _gridHeight = solver.FlowGridHeight;
        _gridCellCount = _gridWidth * _gridHeight;
        _maxObstacleCount = solver.MaxObstacleCount;
        _parallelWorkerCount = solver.ParallelWorkerCount;
        _separationHashCellSizeCm = solver.SeparationHashCellSizeCm;
        _separationHashWidth = solver.SeparationHashWidth;
        _separationHashHeight = solver.SeparationHashHeight;
        _separationHashMinSearchRadiusCells = solver.SeparationHashMinSearchRadiusCells;
        _hardResolveHashCellSizeCm = solver.HardResolveHashCellSizeCm;
        _hardResolveHashWidth = solver.HardResolveHashWidth;
        _hardResolveHashHeight = solver.HardResolveHashHeight;
        _hardResolveHashMinSearchRadiusCells = solver.HardResolveHashMinSearchRadiusCells;
        _playAreaMinXCm = solver.PlayAreaMinXCm;
        _playAreaMaxXCm = solver.PlayAreaMaxXCm;
        _playAreaMinYCm = solver.PlayAreaMinYCm;
        _playAreaMaxYCm = solver.PlayAreaMaxYCm;

        _staticCost = new float[_gridCellCount];
        _cost = new float[_gridCellCount];
        _obsX = new float[_maxObstacleCount];
        _obsY = new float[_maxObstacleCount];
        _obsWorldX = new float[_maxObstacleCount];
        _obsWorldY = new float[_maxObstacleCount];
        _obsRadius = new float[_maxObstacleCount];
        _obsR2 = new float[_maxObstacleCount];
        _obsPR = new float[_maxObstacleCount];
        _obsPR2 = new float[_maxObstacleCount];
        int separationHashCellCount = _separationHashWidth * _separationHashHeight;
        _separationCellCounts = new int[separationHashCellCount];
        _separationCellOffsets = new int[separationHashCellCount];
        _separationCellCursor = new int[separationHashCellCount];
        int hardResolveHashCellCount = _hardResolveHashWidth * _hardResolveHashHeight;
        _hardResolveCellCounts = new int[hardResolveHashCellCount];
        _hardResolveCellOffsets = new int[hardResolveHashCellCount];
        _hardResolveCellCursor = new int[hardResolveHashCellCount];
        _stepJobs = CreateStepJobs(_parallelWorkerCount);
        _stepHandles = new JobHandle[_parallelWorkerCount];
    }

    public Vector2 WorldToLocalCm(Vector2 worldCm)
    {
        return new Vector2(worldCm.X - _worldOriginXCm, worldCm.Y - _worldOriginYCm);
    }

    public Vector2 LocalToWorldCm(Vector2 localCm)
    {
        return new Vector2(localCm.X + _worldOriginXCm, localCm.Y + _worldOriginYCm);
    }

    public float GetPositionX(int index) => _positionsCm[index << 1];
    public float GetPositionY(int index) => _positionsCm[(index << 1) + 1];
    public int GetTeam(int index) => _teams[index];
    public float GetNavMass(int index) => _navMasses[index];
    public float GetVisualScale(int index) => _visualScales[index];
    public float GetBodyRadiusCm(int index) => _bodyRadiiCm[index];
    public float GetSpeedCmPerSecond(int index) => _speedsCmPerSecond[index];
    public bool IsHeavyProfile(int index) => _heavyProfileFlags[index] != 0;
    public bool IsSelected(int index) => _selectedFlags[index] != 0;
    public bool HasUnitTarget(int index) => (uint)index < (uint)UnitCount && _hasUnitTarget[index] != 0;
    public bool IsUnitSettled(int index) => (uint)index < (uint)UnitCount && _unitSettledFlags[index] != 0;
    public float GetObstacleX(int index) => _obsX[index];
    public float GetObstacleY(int index) => _obsY[index];
    public float GetObstacleWorldX(int index) => _obsWorldX[index];
    public float GetObstacleWorldY(int index) => _obsWorldY[index];
    public float GetObstacleRadius(int index) => _obsRadius[index];
    public float GetObstacleSoftPushRadius(int index) => _obsPR[index];
    public bool IsObstaclePoint(float xCm, float yCm) => IsObstacle(xCm, yCm);
    public Vector2 GetVelocityCmPerSecond(int index)
    {
        if ((uint)index >= (uint)UnitCount)
        {
            return Vector2.Zero;
        }

        int offset = index << 1;
        return new Vector2(_velocitiesCm[offset], _velocitiesCm[offset + 1]);
    }

    public void SetWorldOrigin(float originXCm, float originYCm)
    {
        _worldOriginXCm = originXCm;
        _worldOriginYCm = originYCm;
        RefreshObstacleLocalFrame();
    }

    public void SetWorldBounds(float minXCm, float maxXCm, float minYCm, float maxYCm)
    {
        if (minXCm > maxXCm || minYCm > maxYCm)
        {
            throw new InvalidOperationException("MassFlowSimulationState requires ordered world bounds.");
        }

        _worldMinXCm = minXCm;
        _worldMaxXCm = maxXCm;
        _worldMinYCm = minYCm;
        _worldMaxYCm = maxYCm;
    }

    public void ShiftLocalFrame(float deltaXCm, float deltaYCm)
    {
        if (deltaXCm == 0f && deltaYCm == 0f)
        {
            return;
        }

        int scalarCount = UnitCount * 2;
        for (int offset = 0; offset < scalarCount; offset += 2)
        {
            _positionsCm[offset] -= deltaXCm;
            _positionsCm[offset + 1] -= deltaYCm;
            _readPositionsCm[offset] -= deltaXCm;
            _readPositionsCm[offset + 1] -= deltaYCm;
            _unitProgressAnchorCm[offset] -= deltaXCm;
            _unitProgressAnchorCm[offset + 1] -= deltaYCm;
            _unitSettledAnchorCm[offset] -= deltaXCm;
            _unitSettledAnchorCm[offset + 1] -= deltaYCm;
            if (_hasUnitTarget[offset >> 1] != 0)
            {
                _unitTargetsCm[offset] -= deltaXCm;
                _unitTargetsCm[offset + 1] -= deltaYCm;
            }
        }

        for (int teamIndex = 0; teamIndex < _teamStates.Count; teamIndex++)
        {
            TeamRuntimeState team = _teamStates[teamIndex];
            team.TargetX -= deltaXCm;
            team.TargetY -= deltaYCm;
        }

        MarkFlowDirty();
        MarkAllEntitiesDirty();
    }

    public void Reset(
        ReadOnlySpan<int> teamIds,
        int unitsPerTeam,
        ReadOnlySpan<MassNavigationObstacleConfig> obstacles,
        MassNavigationAgentProfileSetConfig profileSet,
        MassNavigationAgentLayer layer,
        MassNavigationScenarioSpawnLayoutConfig spawnLayout)
    {
        if (unitsPerTeam < 0)
        {
            throw new InvalidOperationException("MassFlowSimulationState scenario reset requires unitsPerTeam >= 0.");
        }

        if (teamIds.Length <= 0)
        {
            throw new InvalidOperationException("MassFlowSimulationState scenario reset requires at least one team id.");
        }

        if (spawnLayout == null)
        {
            throw new InvalidOperationException("MassFlowSimulationState scenario reset requires explicit spawn layout config.");
        }

        UnitCount = checked(unitsPerTeam * teamIds.Length);
        EnsureCapacity(UnitCount);
        InitializeTeams(teamIds, unitsPerTeam, spawnLayout);
        CacheAuthoredObstacles(obstacles);
        InitializeUnits(profileSet, layer, spawnLayout.RandomSeed);
        ForceFlowRebuild();
        MarkAllEntitiesDirty();
        _frameCount = 0;
        SettledUnitCount = 0;
    }

    public void ResetAuthoredAgents(
        ReadOnlySpan<MassNavigationAgentSeed> agentSeeds,
        ReadOnlySpan<MassNavigationObstacleConfig> obstacles)
    {
        UnitCount = agentSeeds.Length;
        EnsureCapacity(UnitCount);
        InitializeTeams(agentSeeds);
        CacheAuthoredObstacles(obstacles);
        InitializeUnits(agentSeeds);
        ForceFlowRebuild();
        MarkAllEntitiesDirty();
        _frameCount = 0;
        SettledUnitCount = 0;
    }

    public void SetTeamTarget(int teamId, Vector2 targetCm)
    {
        if (!TryGetTeamState(teamId, out TeamRuntimeState team))
        {
            return;
        }

        float hintX = targetCm.X - team.TargetX;
        float hintY = targetCm.Y - team.TargetY;
        Vector2 resolved = ResolveNavigableTarget(
            targetCm.X,
            targetCm.Y,
            hintX,
            hintY,
            Semantics.TargetProjection.TeamTargetClearanceCm);
        team.TargetX = resolved.X;
        team.TargetY = resolved.Y;
        ResetTeamArrivalState(teamId);
        MarkFlowDirty();
    }

    public bool SetUnitRuntimeProfile(
        int index,
        int teamId,
        float navMass,
        float visualScale,
        float bodyRadiusCm,
        float speedCmPerSecond,
        MassNavigationAgentLayer layer)
    {
        if ((uint)index >= (uint)UnitCount)
        {
            throw new InvalidOperationException(
                $"MassFlow runtime profile agent index {index} exceeds current unit count {UnitCount}.");
        }

        ValidateRuntimeProfile(index, navMass, visualScale, bodyRadiusCm, speedCmPerSecond);
        uint categoryMask = layer.CategoryMask;
        uint interactionMask = layer.InteractionMask;
        bool changed =
            _teams[index] != teamId ||
            MathF.Abs(_navMasses[index] - navMass) > float.Epsilon ||
            MathF.Abs(_visualScales[index] - visualScale) > float.Epsilon ||
            MathF.Abs(_bodyRadiiCm[index] - bodyRadiusCm) > float.Epsilon ||
            MathF.Abs(_speedsCmPerSecond[index] - speedCmPerSecond) > float.Epsilon ||
            _layerCategoryMasks[index] != categoryMask ||
            _layerInteractionMasks[index] != interactionMask;
        if (!changed)
        {
            return false;
        }

        float previousBodyRadiusCm = _bodyRadiiCm[index];
        bool teamOrLayerChanged =
            _teams[index] != teamId ||
            _layerCategoryMasks[index] != categoryMask ||
            _layerInteractionMasks[index] != interactionMask;
        _teams[index] = teamId;
        if (_teamStateIndexById.TryGetValue(teamId, out int teamStateIndex))
        {
            _teamRuntimeIndices[index] = teamStateIndex;
        }

        _navMasses[index] = navMass;
        _visualScales[index] = visualScale;
        _bodyRadiiCm[index] = bodyRadiusCm;
        _speedsCmPerSecond[index] = speedCmPerSecond;
        if (bodyRadiusCm >= _maxBodyRadiusCm)
        {
            _maxBodyRadiusCm = bodyRadiusCm;
        }
        else if (MathF.Abs(previousBodyRadiusCm - _maxBodyRadiusCm) <= float.Epsilon)
        {
            RecomputeMaxBodyRadiusCm();
        }

        _layerCategoryMasks[index] = categoryMask;
        _layerInteractionMasks[index] = interactionMask;
        _maxInteractingBodyRadiiDirty = true;
        if ((uint)_teamRuntimeIndices[index] < (uint)_teamStates.Count)
        {
            _flowRuntimeIndices[index] = ResolveFlowStateIndex(_teamRuntimeIndices[index], layer);
            if (teamOrLayerChanged || MathF.Abs(previousBodyRadiusCm - bodyRadiusCm) > float.Epsilon)
            {
                MarkFlowDirty();
            }
        }

        MarkEntityDirty(index);
        return true;
    }

    private void ValidateRuntimeProfile(
        int index,
        float navMass,
        float visualScale,
        float bodyRadiusCm,
        float speedCmPerSecond)
    {
        if (!(navMass >= Semantics.Solver.MinNavMass))
        {
            throw new InvalidOperationException(
                $"MassFlow runtime profile for agent index {index} requires navMass >= configured semantics.solver.minNavMass {Semantics.Solver.MinNavMass}.");
        }

        if (!(visualScale >= Semantics.Solver.MinVisualScale))
        {
            throw new InvalidOperationException(
                $"MassFlow runtime profile for agent index {index} requires visualScale >= configured semantics.solver.minVisualScale {Semantics.Solver.MinVisualScale}.");
        }

        if (!(bodyRadiusCm > 0f))
        {
            throw new InvalidOperationException(
                $"MassFlow runtime profile for agent index {index} requires bodyRadiusCm > 0.");
        }

        if (!(speedCmPerSecond > 0f))
        {
            throw new InvalidOperationException(
                $"MassFlow runtime profile for agent index {index} requires speedCmPerSecond > 0.");
        }
    }

    public bool SetUnitRuntimeProfile(
        int index,
        int teamId,
        bool heavy,
        float navMass,
        float visualScale,
        float bodyRadiusCm,
        float speedCmPerSecond,
        MassNavigationAgentLayer layer)
    {
        bool changed = SetUnitRuntimeProfile(index, teamId, navMass, visualScale, bodyRadiusCm, speedCmPerSecond, layer);
        if ((uint)index < (uint)UnitCount)
        {
            byte heavyFlag = heavy ? (byte)1 : (byte)0;
            if (_heavyProfileFlags[index] != heavyFlag)
            {
                _heavyProfileFlags[index] = heavyFlag;
                MarkEntityDirty(index);
                changed = true;
            }
        }

        return changed;
    }

    public void RequestFlowRebuild()
    {
        MarkFlowDirty();
    }

    public void ResetRuntimeObstaclesFromWorld(ReadOnlySpan<MassNavigationObstacleSnapshot> obstacles)
    {
        if (obstacles.Length > _maxObstacleCount)
        {
            throw new InvalidOperationException(
                $"MassFlowSimulationState runtime obstacle count {obstacles.Length} exceeds solver capacity {_maxObstacleCount}.");
        }

        int previousCount = ObstacleCount;
        ObstacleCount = obstacles.Length;
        for (int i = 0; i < obstacles.Length; i++)
        {
            MassNavigationObstacleSnapshot obstacle = obstacles[i];
            if (!(obstacle.RadiusCm > 0f))
            {
                throw new InvalidOperationException("MassFlowSimulationState runtime obstacle radius requires radiusCm > 0.");
            }

            Vector2 localCm = WorldToLocalCm(new Vector2(obstacle.WorldXCm, obstacle.WorldYCm));
            CacheObstacle(i, localCm.X, localCm.Y, obstacle.RadiusCm);
        }

        ClearStaleObstacles(ObstacleCount, previousCount);
        ForceFlowRebuild();
    }

    public bool SetUnitTarget(int index, float xCm, float yCm, bool resetRecovery = false)
    {
        if ((uint)index >= (uint)UnitCount)
        {
            throw new InvalidOperationException(
                $"MassFlow unit target index {index} exceeds current unit count {UnitCount}.");
        }

        int offset = index << 1;
        bool wasInactive = _hasUnitTarget[index] == 0;
        ClampLocalToWorldBounds(ref xCm, ref yCm, _bodyRadiiCm[index]);
        bool targetChanged = wasInactive || _unitTargetsCm[offset] != xCm || _unitTargetsCm[offset + 1] != yCm;
        if (!targetChanged && !resetRecovery)
        {
            return false;
        }

        _unitTargetsCm[offset] = xCm;
        _unitTargetsCm[offset + 1] = yCm;
        _hasUnitTarget[index] = 1;
        if (targetChanged)
        {
            MarkEntityDirty(index);
        }

        if (resetRecovery || wasInactive)
        {
            ResetUnitArrivalState(index, clearRetryCount: true);
        }

        return targetChanged;
    }

    public bool TryGetUnitTarget(int index, out float xCm, out float yCm)
    {
        if ((uint)index >= (uint)UnitCount)
        {
            throw new InvalidOperationException(
                $"MassFlow unit target index {index} exceeds current unit count {UnitCount}.");
        }

        if (_hasUnitTarget[index] == 0)
        {
            xCm = 0f;
            yCm = 0f;
            return false;
        }

        int offset = index << 1;
        xCm = _unitTargetsCm[offset];
        yCm = _unitTargetsCm[offset + 1];
        return true;
    }

    public void ApplyExternalDisplacementRange(
        int startIndex,
        int count,
        float deltaXCm,
        float deltaYCm)
    {
        if (startIndex < 0)
        {
            throw new InvalidOperationException("MassFlow external displacement requires startIndex >= 0.");
        }

        if (count < 0)
        {
            throw new InvalidOperationException("MassFlow external displacement requires count >= 0.");
        }

        if (count == 0 || (deltaXCm == 0f && deltaYCm == 0f))
        {
            return;
        }

        int endIndex = startIndex + count;
        if (endIndex < startIndex || endIndex > UnitCount)
        {
            throw new InvalidOperationException(
                $"MassFlow external displacement range [{startIndex}, {endIndex}) exceeds current unit count {UnitCount}.");
        }

        for (int index = startIndex; index < endIndex; index++)
        {
            int offset = index << 1;
            _positionsCm[offset] += deltaXCm;
            _positionsCm[offset + 1] += deltaYCm;
            _readPositionsCm[offset] += deltaXCm;
            _readPositionsCm[offset + 1] += deltaYCm;
            _unitProgressAnchorCm[offset] += deltaXCm;
            _unitProgressAnchorCm[offset + 1] += deltaYCm;
            _unitSettledAnchorCm[offset] += deltaXCm;
            _unitSettledAnchorCm[offset + 1] += deltaYCm;
            if (_hasUnitTarget[index] != 0)
            {
                _unitTargetsCm[offset] += deltaXCm;
                _unitTargetsCm[offset + 1] += deltaYCm;
            }

            ClampLocalToWorldBounds(ref _positionsCm[offset], ref _positionsCm[offset + 1], _bodyRadiiCm[index]);
            ClampLocalToWorldBounds(ref _readPositionsCm[offset], ref _readPositionsCm[offset + 1], _bodyRadiiCm[index]);
            MarkEntityDirty(index);
        }
    }

    public void ApplyExternalDisplacement(ReadOnlySpan<int> agentIndices, float deltaXCm, float deltaYCm)
    {
        if (agentIndices.Length == 0 || (deltaXCm == 0f && deltaYCm == 0f))
        {
            return;
        }

        for (int i = 0; i < agentIndices.Length; i++)
        {
            int index = agentIndices[i];
            if ((uint)index >= (uint)UnitCount)
            {
                throw new InvalidOperationException(
                    $"MassFlow external displacement agent index {index} exceeds current unit count {UnitCount}.");
            }

            int offset = index << 1;
            _positionsCm[offset] += deltaXCm;
            _positionsCm[offset + 1] += deltaYCm;
            _readPositionsCm[offset] += deltaXCm;
            _readPositionsCm[offset + 1] += deltaYCm;
            _unitProgressAnchorCm[offset] += deltaXCm;
            _unitProgressAnchorCm[offset + 1] += deltaYCm;
            _unitSettledAnchorCm[offset] += deltaXCm;
            _unitSettledAnchorCm[offset + 1] += deltaYCm;
            if (_hasUnitTarget[index] != 0)
            {
                _unitTargetsCm[offset] += deltaXCm;
                _unitTargetsCm[offset + 1] += deltaYCm;
            }

            ClampLocalToWorldBounds(ref _positionsCm[offset], ref _positionsCm[offset + 1], _bodyRadiiCm[index]);
            ClampLocalToWorldBounds(ref _readPositionsCm[offset], ref _readPositionsCm[offset + 1], _bodyRadiiCm[index]);
            MarkEntityDirty(index);
        }
    }

    public void ReleaseUnitToTeamTarget(int index)
    {
        if ((uint)index >= (uint)UnitCount)
        {
            throw new InvalidOperationException(
                $"MassFlow release target index {index} exceeds current unit count {UnitCount}.");
        }

        ResetUnitArrivalState(index, clearRetryCount: true);
        _hasUnitTarget[index] = 0;
        MarkEntityDirty(index);
    }

    public void HoldUnitAtCurrentPosition(int index)
    {
        if ((uint)index >= (uint)UnitCount)
        {
            throw new InvalidOperationException(
                $"MassFlow hold target index {index} exceeds current unit count {UnitCount}.");
        }

        int offset = index << 1;
        float x = _positionsCm[offset];
        float y = _positionsCm[offset + 1];
        _unitTargetsCm[offset] = x;
        _unitTargetsCm[offset + 1] = y;
        _hasUnitTarget[index] = 1;
        _unitRetryCounts[index] = 0;
        EnterSettledState(index, x, y);
        MarkEntityDirty(index);
    }

    public void SetSelectedFlags(MassNavigationAgentState agentState, ReadOnlySpan<Entity> selectedEntities)
    {
        if (UnitCount <= 0)
        {
            return;
        }

        Array.Clear(_selectedFlags, 0, UnitCount);
        for (int i = 0; i < selectedEntities.Length; i++)
        {
            if (agentState.TryGetControllableIndex(selectedEntities[i], out int index) &&
                (uint)index < (uint)UnitCount)
            {
                _selectedFlags[index] = 1;
            }
        }
    }

    public Vector2 ResolveUnitNavigableTarget(
        int index,
        float xCm,
        float yCm,
        float hintX,
        float hintY,
        float minimumClearanceCm)
    {
        if ((uint)index >= (uint)UnitCount)
        {
            throw new InvalidOperationException(
                $"MassFlow unit target projection index {index} exceeds current unit count {UnitCount}.");
        }

        float clearanceCm = MathF.Max(minimumClearanceCm, _bodyRadiiCm[index]);
        return ResolveNavigableTarget(xCm, yCm, hintX, hintY, clearanceCm);
    }

    public Vector2 ResolveNavigableTarget(float xCm, float yCm, float hintX, float hintY, float extraClearanceCm)
    {
        if (extraClearanceCm < 0f)
        {
            throw new InvalidOperationException("MassFlow navigable target projection requires extraClearanceCm >= 0.");
        }

        float resolvedX = xCm;
        float resolvedY = yCm;
        ClampLocalToWorldBounds(ref resolvedX, ref resolvedY, extraClearanceCm);
        if (!IsInsideTacticalField(resolvedX, resolvedY))
        {
            return new Vector2(resolvedX, resolvedY);
        }

        resolvedX = ClampXPosition(resolvedX);
        resolvedY = ClampYPosition(resolvedY);
        float minDistancePadding = extraClearanceCm;

        for (int pass = 0; pass < 2; pass++)
        {
            bool adjusted = false;
            for (int obstacleIndex = 0; obstacleIndex < ObstacleCount; obstacleIndex++)
            {
                float minDistance = _obsRadius[obstacleIndex] + minDistancePadding;
                float dx = resolvedX - _obsX[obstacleIndex];
                float dy = resolvedY - _obsY[obstacleIndex];
                float d2 = dx * dx + dy * dy;
                if (d2 >= minDistance * minDistance)
                {
                    continue;
                }

                float nx;
                float ny;
                if (d2 > Semantics.Solver.DirectionEpsilonSq)
                {
                    float invD = FastInvSqrt(d2);
                    nx = dx * invD;
                    ny = dy * invD;
                }
                else
                {
                    float hintLenSq = hintX * hintX + hintY * hintY;
                    if (hintLenSq > Semantics.Solver.DirectionEpsilonSq)
                    {
                        float invHint = FastInvSqrt(hintLenSq);
                        nx = hintX * invHint;
                        ny = hintY * invHint;
                    }
                    else
                    {
                        nx = 1f;
                        ny = 0f;
                    }
                }

                resolvedX = ClampXPosition(_obsX[obstacleIndex] + nx * minDistance);
                resolvedY = ClampYPosition(_obsY[obstacleIndex] + ny * minDistance);
                ClampLocalToWorldBounds(ref resolvedX, ref resolvedY, extraClearanceCm);
                adjusted = true;
            }

            if (!adjusted)
            {
                break;
            }
        }

        return new Vector2(resolvedX, resolvedY);
    }

    public void Step(
        float dt,
        World world,
        MassNavigationGroupRuntime navGroupRuntime,
        bool runHardResolve,
        int hardResolveCandidateThresholdAgents,
        Action<double>? observeStepPrep = null,
        Action<double>? observeLocalSteering = null,
        Action<double>? observeHardResolve = null)
    {
        if (UnitCount <= 0)
        {
            return;
        }

        long prepStart = System.Diagnostics.Stopwatch.GetTimestamp();
        RefreshTeamRelationshipMatrixIfStale();

        float clampedDt = Math.Clamp(dt, 0f, Semantics.Solver.MaxStepDtSeconds);
        _frameCount++;
        int scalarCount = UnitCount * 2;
        Array.Copy(_positionsCm, _readPositionsCm, scalarCount);
        Array.Copy(_velocitiesCm, _readVelocitiesCm, scalarCount);
        _useCandidateGating = UnitCount >= Math.Max(1, hardResolveCandidateThresholdAgents);
        if (_useCandidateGating)
        {
            Array.Clear(_hardResolveCandidates, 0, UnitCount);
        }

        float sepRadiusCm = Semantics.Steering.SeparationRadiusCm;
        float sepRadiusSq = sepRadiusCm * sepRadiusCm;
        float arrivalRadiusCm = Semantics.Steering.GoalArrivalRadiusCm;
        float arrivalRadiusSq = arrivalRadiusCm * arrivalRadiusCm;
        float unitTargetStopThresholdCm = Semantics.Group.UnitTargetStopThresholdCm;
        float unitTargetStopThresholdSq = unitTargetStopThresholdCm * unitTargetStopThresholdCm;

        RecomputeMaxInteractingBodyRadiiCmIfDirty();
        BuildSeparationHash(_readPositionsCm);
        observeStepPrep?.Invoke((System.Diagnostics.Stopwatch.GetTimestamp() - prepStart) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);

        int hwm1 = _separationHashWidth - 1;
        int hhm1 = _separationHashHeight - 1;
        float invHashCell = 1f / _separationHashCellSizeCm;
        int flowObstacleNeighborRadiusCells = Semantics.Solver.FlowObstacleNeighborRadiusCells;

        int workerCount = Math.Min(_parallelWorkerCount, UnitCount);
        if (workerCount <= 1 || UnitCount < Semantics.Solver.ParallelStepMinAgents)
        {
            long steeringStart = System.Diagnostics.Stopwatch.GetTimestamp();
            StepRange(0, UnitCount, clampedDt, navGroupRuntime, sepRadiusSq, sepRadiusCm, arrivalRadiusCm, arrivalRadiusSq, unitTargetStopThresholdSq, hwm1, hhm1, invHashCell, flowObstacleNeighborRadiusCells, _useCandidateGating);
            observeLocalSteering?.Invoke((System.Diagnostics.Stopwatch.GetTimestamp() - steeringStart) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
            ClampAllPositionsToWorldBounds();
            long resolveStart = System.Diagnostics.Stopwatch.GetTimestamp();
            if (runHardResolve)
            {
                ResolveHardPenetration();
                ClampAllPositionsToWorldBounds();
            }
            observeHardResolve?.Invoke((System.Diagnostics.Stopwatch.GetTimestamp() - resolveStart) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
            UpdateSettledUnitCount();
            MarkMovedEntitiesDirty();
            return;
        }

        var scheduler = World.SharedJobScheduler ?? throw new InvalidOperationException(
            "MassFlowSimulationState requires World.SharedJobScheduler when solver.parallelWorkerCount requests parallel stepping.");
        int baseCount = UnitCount / workerCount;
        int remainder = UnitCount % workerCount;
        int startIndex = 0;
        long threadedSteeringStart = System.Diagnostics.Stopwatch.GetTimestamp();
        for (int workerIndex = 0; workerIndex < workerCount; workerIndex++)
        {
            int length = baseCount + (workerIndex < remainder ? 1 : 0);
            var job = _stepJobs[workerIndex];
            job.Owner = this;
            job.StartIndex = startIndex;
            job.EndIndex = startIndex + length;
            job.Dt = clampedDt;
            job.NavGroupRuntime = navGroupRuntime;
            job.SepRadiusSq = sepRadiusSq;
            job.SepRadiusCm = sepRadiusCm;
            job.ArrivalRadiusCm = arrivalRadiusCm;
            job.ArrivalRadiusSq = arrivalRadiusSq;
            job.UnitTargetStopThresholdSq = unitTargetStopThresholdSq;
            job.HashWidthMinusOne = hwm1;
            job.HashHeightMinusOne = hhm1;
            job.InvHashCell = invHashCell;
            job.FlowObstacleNeighborRadiusCells = flowObstacleNeighborRadiusCells;
            job.UseCandidateGating = _useCandidateGating;
            _stepHandles[workerIndex] = scheduler.Schedule(job);
            startIndex += length;
        }

        scheduler.Flush();
        for (int workerIndex = 0; workerIndex < workerCount; workerIndex++)
        {
            _stepHandles[workerIndex].Complete();
        }
        observeLocalSteering?.Invoke((System.Diagnostics.Stopwatch.GetTimestamp() - threadedSteeringStart) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);

        ClampAllPositionsToWorldBounds();
        long hardResolveStart = System.Diagnostics.Stopwatch.GetTimestamp();
        if (runHardResolve)
        {
            ResolveHardPenetration();
            ClampAllPositionsToWorldBounds();
        }
        observeHardResolve?.Invoke((System.Diagnostics.Stopwatch.GetTimestamp() - hardResolveStart) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
        UpdateSettledUnitCount();
        MarkMovedEntitiesDirty();
    }

    public bool AdvanceFlowPipeline(
        MassFlowTuning tuning,
        bool refreshFlow,
        bool refreshCrowd,
        bool refreshObstacles,
        Action<double>? observeFlowFieldRebuild = null)
    {
        return AdvanceFlowPipelineCore(tuning, 0, useConfiguredIntervals: false, refreshFlow, refreshCrowd, refreshObstacles, observeFlowFieldRebuild);
    }

    public bool AdvanceFlowPipeline(MassFlowTuning tuning, int frameIndex, Action<double>? observeFlowFieldRebuild = null)
    {
        return AdvanceFlowPipelineCore(
            tuning,
            frameIndex,
            useConfiguredIntervals: true,
            tuning.ForceRefreshFlow,
            tuning.ForceRefreshCrowd,
            tuning.ForceRefreshObstacles,
            observeFlowFieldRebuild);
    }

    private bool AdvanceFlowPipelineCore(
        MassFlowTuning tuning,
        int frameIndex,
        bool useConfiguredIntervals,
        bool forceRefreshFlow,
        bool forceRefreshCrowd,
        bool forceRefreshObstacles,
        Action<double>? observeFlowFieldRebuild)
    {
        bool refreshObstacles = _flowDirty || forceRefreshObstacles || (useConfiguredIntervals && ShouldRun(frameIndex, ref _lastObstacleStampFrame, tuning.ObstacleStampIntervalTicks));
        bool refreshCrowd = _flowDirty || forceRefreshCrowd || (useConfiguredIntervals && ShouldRun(frameIndex, ref _lastCrowdStampFrame, tuning.CrowdStampIntervalTicks));
        bool refreshFlow = _flowDirty || forceRefreshFlow || (useConfiguredIntervals && ShouldRun(frameIndex, ref _lastFlowStepFrame, tuning.StepIntervalTicks));
        tuning.ForceRefreshFlow = false;
        tuning.ForceRefreshCrowd = false;
        tuning.ForceRefreshObstacles = false;
        if (!refreshObstacles && !refreshCrowd && !refreshFlow)
        {
            return false;
        }

        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        if (refreshObstacles)
        {
            RebuildStaticObstacleCost();
        }

        if (refreshCrowd || refreshObstacles)
        {
            refreshFlow = true;
        }

        if (refreshFlow)
        {
            ComputeFlowFields(tuning.Enabled ? tuning.IterationsPerStep : 0);
            _flowDirty = false;
        }

        LastFlowFieldRebuildMs = (System.Diagnostics.Stopwatch.GetTimestamp() - start) * 1000f / System.Diagnostics.Stopwatch.Frequency;
        observeFlowFieldRebuild?.Invoke(LastFlowFieldRebuildMs);
        return true;
    }

    public void SyncEntities(World world, MassNavigationAgentState agentState)
    {
        if (UnitCount <= 0 || _entitySyncDirtyCount <= 0)
        {
            return;
        }

        int dirtyCount = _entitySyncDirtyCount;
        for (int dirtyIndex = 0; dirtyIndex < dirtyCount; dirtyIndex++)
        {
            int i = _entitySyncDirtyAgents[dirtyIndex];
            if ((uint)i >= (uint)UnitCount)
            {
                throw new InvalidOperationException(
                    $"MassFlowSimulationState dirty agent index {i} exceeds unit count {UnitCount}.");
            }

            _entitySyncDirtyFlags[i] = 0;
            if (!agentState.TryGetAgentEntity(i, out Entity entity))
            {
                throw new InvalidOperationException(
                    $"MassFlowSimulationState cannot sync unit {i} because no tracked agent entity is registered.");
            }

            if (!world.IsAlive(entity))
            {
                throw new InvalidOperationException(
                    $"MassFlowSimulationState cannot sync unit {i} because tracked entity {entity.Id} is not alive.");
            }

            int i2 = i << 1;
            float xCm = _positionsCm[i2];
            float yCm = _positionsCm[i2 + 1];
            float worldXCm = _worldOriginXCm + xCm;
            float worldYCm = _worldOriginYCm + yCm;
            Fix64Vec2 worldValue = Fix64Vec2.FromInt((int)MathF.Round(worldXCm), (int)MathF.Round(worldYCm));
            ref WorldPositionCm worldPosition = ref world.Get<WorldPositionCm>(entity);
            worldPosition.Value = worldValue;

        }

        _entitySyncDirtyCount = 0;
    }

    public void SyncCullStates(
        World world,
        MassNavigationAgentState agentState,
        float localMinXCm,
        float localMaxXCm,
        float localMinYCm,
        float localMaxYCm)
    {
        int count = Math.Min(UnitCount, agentState.ControllableAgentSlotCount);
        for (int i = 0; i < count; i++)
        {
            if (!agentState.TryGetControllableEntity(i, out Entity entity))
            {
                continue;
            }

            if (!world.IsAlive(entity) || !world.Has<CullState>(entity))
            {
                continue;
            }

            int i2 = i << 1;
            float xCm = _positionsCm[i2];
            float yCm = _positionsCm[i2 + 1];
            bool visible = xCm >= localMinXCm &&
                xCm <= localMaxXCm &&
                yCm >= localMinYCm &&
                yCm <= localMaxYCm;
            ref CullState cull = ref world.Get<CullState>(entity);
            cull.IsVisible = visible;
            cull.LOD = visible ? LODLevel.High : LODLevel.Culled;
        }
    }

    private void EnsureCapacity(int unitCount)
    {
        int vectorLength = Math.Max(0, unitCount * 2);
        if (_positionsCm.Length < vectorLength)
        {
            Array.Resize(ref _positionsCm, vectorLength);
            Array.Resize(ref _velocitiesCm, vectorLength);
            Array.Resize(ref _readPositionsCm, vectorLength);
            Array.Resize(ref _readVelocitiesCm, vectorLength);
            Array.Resize(ref _unitProgressAnchorCm, vectorLength);
            Array.Resize(ref _unitSettledAnchorCm, vectorLength);
            Array.Resize(ref _unitTargetsCm, vectorLength);
        }

        if (_teams.Length < unitCount)
        {
            Array.Resize(ref _teams, unitCount);
            Array.Resize(ref _teamRuntimeIndices, unitCount);
            Array.Resize(ref _teamLocalIndices, unitCount);
            Array.Resize(ref _flowRuntimeIndices, unitCount);
            Array.Resize(ref _layerCategoryMasks, unitCount);
            Array.Resize(ref _layerInteractionMasks, unitCount);
            Array.Resize(ref _maxInteractingBodyRadiiCm, unitCount);
            Array.Resize(ref _navMasses, unitCount);
            Array.Resize(ref _visualScales, unitCount);
            Array.Resize(ref _bodyRadiiCm, unitCount);
            Array.Resize(ref _speedsCmPerSecond, unitCount);
            Array.Resize(ref _separationHashSearchRadiusCellsByAgent, unitCount);
            Array.Resize(ref _hardResolveHashSearchRadiusCellsByAgent, unitCount);
            Array.Resize(ref _hasUnitTarget, unitCount);
            Array.Resize(ref _selectedFlags, unitCount);
            Array.Resize(ref _hardResolveCandidates, unitCount);
            Array.Resize(ref _heavyProfileFlags, unitCount);
            Array.Resize(ref _entitySyncDirtyFlags, unitCount);
            Array.Resize(ref _unitSettledFlags, unitCount);
            Array.Resize(ref _unitRetryCounts, unitCount);
            Array.Resize(ref _unitStuckSeconds, unitCount);
            Array.Resize(ref _separationAgents, unitCount);
            Array.Resize(ref _hardResolveAgents, unitCount);
            Array.Resize(ref _entitySyncDirtyAgents, unitCount);
        }
    }

    private void InitializeTeams(
        ReadOnlySpan<int> teamIds,
        int unitsPerTeam,
        MassNavigationScenarioSpawnLayoutConfig spawnLayout)
    {
        _teamStates.Clear();
        _flowStates.Clear();
        _teamStateIndexById.Clear();
        if (teamIds.Length <= 0)
        {
            throw new InvalidOperationException("MassFlowSimulationState requires at least one team id for scenario team initialization.");
        }

        if (spawnLayout.ParsedKind != MassNavigationScenarioSpawnLayoutKind.OrbitOpposedTargets)
        {
            throw new InvalidOperationException(
                $"MassFlowSimulationState does not support scenario spawn layout '{spawnLayout.Kind}'.");
        }

        float centerX = _fieldWidthCm * 0.5f;
        float centerY = _fieldHeightCm * 0.5f;
        float orbitRadiusCm = spawnLayout.OrbitRadiusCm;
        for (int teamIndex = 0; teamIndex < teamIds.Length; teamIndex++)
        {
            int teamId = teamIds[teamIndex];
            float angle = MathF.PI + ((MathF.PI * 2f * teamIndex) / teamIds.Length);
            float dirX = MathF.Cos(angle);
            float dirY = MathF.Sin(angle);
            float tangentX = -dirY;
            float tangentY = dirX;
            float spawnCenterX = centerX + dirX * orbitRadiusCm;
            float spawnCenterY = centerY + dirY * orbitRadiusCm;
            Vector2 target = ResolveNavigableTarget(
                centerX - dirX * orbitRadiusCm,
                centerY - dirY * orbitRadiusCm,
                -dirX,
                -dirY,
                Semantics.TargetProjection.TeamTargetClearanceCm);
            var state = new TeamRuntimeState(teamId, unitsPerTeam, spawnCenterX, spawnCenterY, dirX, dirY, tangentX, tangentY)
            {
                TargetX = target.X,
                TargetY = target.Y,
            };
            _teamStateIndexById[teamId] = _teamStates.Count;
            _teamStates.Add(state);
        }

        _teamRelationshipMatrix = new TeamRelationship[_teamStates.Count * _teamStates.Count];
        _teamRelationshipRevision = int.MinValue;
    }

    private void InitializeTeams(ReadOnlySpan<MassNavigationAgentSeed> agentSeeds)
    {
        _teamStates.Clear();
        _flowStates.Clear();
        _teamStateIndexById.Clear();
        for (int i = 0; i < agentSeeds.Length; i++)
        {
            int teamId = agentSeeds[i].TeamId;
            if (_teamStateIndexById.TryGetValue(teamId, out int teamStateIndex))
            {
                _teamStates[teamStateIndex].UnitCount++;
                continue;
            }

            var state = new TeamRuntimeState(teamId)
            {
                UnitCount = 1,
                TargetX = agentSeeds[i].LocalPositionXCm,
                TargetY = agentSeeds[i].LocalPositionYCm,
            };
            _teamStateIndexById[teamId] = _teamStates.Count;
            _teamStates.Add(state);
        }

        _teamRelationshipMatrix = new TeamRelationship[_teamStates.Count * _teamStates.Count];
        _teamRelationshipRevision = int.MinValue;
    }

    private void InitializeUnits(MassNavigationAgentProfileSetConfig profileSet, MassNavigationAgentLayer layer, int spawnRandomSeed)
    {
        _maxBodyRadiusCm = 0f;
        Array.Clear(_velocitiesCm, 0, UnitCount * 2);
        Array.Clear(_unitTargetsCm, 0, UnitCount * 2);
        Array.Clear(_hasUnitTarget, 0, UnitCount);
        Array.Clear(_selectedFlags, 0, UnitCount);
        Array.Clear(_heavyProfileFlags, 0, UnitCount);
        Array.Clear(_bodyRadiiCm, 0, UnitCount);
        Array.Clear(_speedsCmPerSecond, 0, UnitCount);
        Array.Clear(_maxInteractingBodyRadiiCm, 0, UnitCount);
        Array.Clear(_separationHashSearchRadiusCellsByAgent, 0, UnitCount);
        Array.Clear(_hardResolveHashSearchRadiusCellsByAgent, 0, UnitCount);
        Array.Clear(_unitProgressAnchorCm, 0, UnitCount * 2);
        Array.Clear(_unitSettledAnchorCm, 0, UnitCount * 2);
        Array.Clear(_unitSettledFlags, 0, UnitCount);
        Array.Clear(_unitRetryCounts, 0, UnitCount);
        Array.Clear(_unitStuckSeconds, 0, UnitCount);

        int unitIndex = 0;
        float spacingCm = Semantics.Group.SpawnSpacingCm;
        for (int teamStateIndex = 0; teamStateIndex < _teamStates.Count; teamStateIndex++)
        {
            TeamRuntimeState team = _teamStates[teamStateIndex];
            int flowStateIndex = ResolveFlowStateIndex(teamStateIndex, layer);
            int cols = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(team.UnitCount)));
            int rows = Math.Max(1, (int)Math.Ceiling(team.UnitCount / (double)cols));
            float colCenter = (cols - 1) * 0.5f;
            float rowCenter = (rows - 1) * 0.5f;
            for (int localIndex = 0; localIndex < team.UnitCount; localIndex++, unitIndex++)
            {
                int row = localIndex / cols;
                int col = localIndex % cols;
                float lateralOffset = (col - colCenter) * spacingCm;
                float depthOffset = (row - rowCenter) * spacingCm;
                float jitterLateral = DeterministicSpawnJitterCm(spawnRandomSeed, team.TeamId, localIndex, 0u, Semantics.Group.SpawnJitterCm);
                float jitterDepth = DeterministicSpawnJitterCm(spawnRandomSeed, team.TeamId, localIndex, 1u, Semantics.Group.SpawnJitterCm);
                float xCm = team.SpawnCenterX + team.TangentX * (lateralOffset + jitterLateral) + team.DirectionX * (depthOffset + jitterDepth);
                float yCm = team.SpawnCenterY + team.TangentY * (lateralOffset + jitterLateral) + team.DirectionY * (depthOffset + jitterDepth);
                int i2 = unitIndex << 1;
                MassNavigationAgentProfileConfig profile = profileSet.ResolveForLocalIndex(localIndex);
                _teams[unitIndex] = team.TeamId;
                _teamRuntimeIndices[unitIndex] = teamStateIndex;
                _teamLocalIndices[unitIndex] = localIndex;
                _flowRuntimeIndices[unitIndex] = flowStateIndex;
                _layerCategoryMasks[unitIndex] = layer.CategoryMask;
                _layerInteractionMasks[unitIndex] = layer.InteractionMask;
                _navMasses[unitIndex] = profile.NavMass;
                _visualScales[unitIndex] = profile.VisualScale;
                _bodyRadiiCm[unitIndex] = profile.BodyRadiusCm;
                _speedsCmPerSecond[unitIndex] = profile.SpeedCmPerSecond;
                _maxBodyRadiusCm = MathF.Max(_maxBodyRadiusCm, profile.BodyRadiusCm);
                _heavyProfileFlags[unitIndex] = profile.Heavy ? (byte)1 : (byte)0;
                _positionsCm[i2] = ClampXPosition(xCm);
                _positionsCm[i2 + 1] = ClampYPosition(yCm);
                _unitProgressAnchorCm[i2] = _positionsCm[i2];
                _unitProgressAnchorCm[i2 + 1] = _positionsCm[i2 + 1];
                _unitSettledAnchorCm[i2] = _positionsCm[i2];
                _unitSettledAnchorCm[i2 + 1] = _positionsCm[i2 + 1];
            }
        }

        _maxInteractingBodyRadiiDirty = true;
    }

    private void CacheAuthoredObstacles(ReadOnlySpan<MassNavigationObstacleConfig> obstacles)
    {
        if (obstacles.Length <= 0)
        {
            throw new InvalidOperationException("MassFlowSimulationState requires authored obstacles.");
        }

        if (obstacles.Length > _maxObstacleCount)
        {
            throw new InvalidOperationException(
                $"MassFlowSimulationState obstacle count {obstacles.Length} exceeds solver capacity {_maxObstacleCount}.");
        }

        ObstacleCount = obstacles.Length;
        for (int i = 0; i < obstacles.Length; i++)
        {
            MassNavigationObstacleConfig obstacle = obstacles[i];
            CacheObstacle(i, obstacle.LocalXCm, obstacle.LocalYCm, obstacle.RadiusCm);
        }
    }

    private void CacheObstacle(int index, float xCm, float yCm, float radiusCm)
    {
        _obsWorldX[index] = _worldOriginXCm + xCm;
        _obsWorldY[index] = _worldOriginYCm + yCm;
        _obsX[index] = xCm;
        _obsY[index] = yCm;
        _obsRadius[index] = radiusCm;
        _obsR2[index] = radiusCm * radiusCm;
        float pushRadius = Semantics.Obstacle.ResolveSoftPushRadiusCm(radiusCm);
        _obsPR[index] = pushRadius;
        _obsPR2[index] = pushRadius * pushRadius;
    }

    private void ClearStaleObstacles(int startIndex, int previousCount)
    {
        int count = previousCount - startIndex;
        if (count <= 0)
        {
            return;
        }

        Array.Clear(_obsWorldX, startIndex, count);
        Array.Clear(_obsWorldY, startIndex, count);
        Array.Clear(_obsX, startIndex, count);
        Array.Clear(_obsY, startIndex, count);
        Array.Clear(_obsRadius, startIndex, count);
        Array.Clear(_obsR2, startIndex, count);
        Array.Clear(_obsPR, startIndex, count);
        Array.Clear(_obsPR2, startIndex, count);
    }

    private void RefreshObstacleLocalFrame()
    {
        for (int i = 0; i < ObstacleCount; i++)
        {
            _obsX[i] = _obsWorldX[i] - _worldOriginXCm;
            _obsY[i] = _obsWorldY[i] - _worldOriginYCm;
        }
    }

    private void ComputeFlowFields(int crowdStampBudgetUnits)
    {
        for (int i = 0; i < _flowStates.Count; i++)
        {
            FlowRuntimeState flowState = _flowStates[i];
            TeamRuntimeState team = _teamStates[flowState.TeamStateIndex];
            RebuildFlowCostForState(flowState, crowdStampBudgetUnits);
            ComputeFlow(flowState.Flow, team.TargetX, team.TargetY);
        }

        if (crowdStampBudgetUnits > 0 && UnitCount > 0)
        {
            int budget = Math.Min(UnitCount, crowdStampBudgetUnits);
            _crowdStampCursor = (_crowdStampCursor + budget) % UnitCount;
        }
    }

    private void ForceFlowRebuild()
    {
        RebuildStaticObstacleCost();
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        ComputeFlowFields(crowdStampBudgetUnits: 0);
        LastFlowFieldRebuildMs = (System.Diagnostics.Stopwatch.GetTimestamp() - start) * 1000f / System.Diagnostics.Stopwatch.Frequency;
        _flowDirty = false;
        _lastFlowStepFrame = 0;
        _lastCrowdStampFrame = 0;
        _lastObstacleStampFrame = 0;
    }

    private void RebuildStaticObstacleCost()
    {
        for (int y = 0; y < _gridHeight; y++)
        {
            for (int x = 0; x < _gridWidth; x++)
            {
                float wx = (x + 0.5f) * _flowCellSizeCm;
                float wy = (y + 0.5f) * _flowCellSizeCm;
                _staticCost[(y * _gridWidth) + x] = IsObstacle(wx, wy) ? Semantics.Solver.FlowBlockedCellCost : 1f;
            }
        }
    }

    private void RebuildFlowCostForState(FlowRuntimeState flowState, int crowdStampBudgetUnits)
    {
        Array.Copy(_staticCost, _cost, _staticCost.Length);
        if (crowdStampBudgetUnits <= 0 || UnitCount <= 0)
        {
            return;
        }

        int budget = Math.Min(UnitCount, crowdStampBudgetUnits);
        for (int sample = 0; sample < budget; sample++)
        {
            int unitIndex = (_crowdStampCursor + sample) % UnitCount;
            if (!CanFlowStateObserveAgent(flowState, unitIndex))
            {
                continue;
            }

            int offset = unitIndex << 1;
            StampCrowdCost(_positionsCm[offset], _positionsCm[offset + 1]);
        }
    }

    private bool CanFlowStateObserveAgent(FlowRuntimeState flowState, int agentIndex)
    {
        uint otherCategory = _layerCategoryMasks[agentIndex];
        return (flowState.InteractionMask & otherCategory) != 0u &&
            (_layerInteractionMasks[agentIndex] & flowState.CategoryMask) != 0u;
    }

    private void StampCrowdCost(float xCm, float yCm)
    {
        int gx = Math.Clamp((int)(xCm / _flowCellSizeCm), 0, _gridWidth - 1);
        int gy = Math.Clamp((int)(yCm / _flowCellSizeCm), 0, _gridHeight - 1);
        for (int oy = -1; oy <= 1; oy++)
        {
            int ny = gy + oy;
            if ((uint)ny >= (uint)_gridHeight)
            {
                continue;
            }

            int rowBase = ny * _gridWidth;
            for (int ox = -1; ox <= 1; ox++)
            {
                int nx = gx + ox;
                if ((uint)nx >= (uint)_gridWidth)
                {
                    continue;
                }

                int idx = rowBase + nx;
                if (_cost[idx] > Semantics.Solver.FlowBlockedCellThreshold)
                {
                    continue;
                }

                float penalty = (ox == 0 && oy == 0) ? 8f : 3f;
                _cost[idx] += penalty;
            }
        }
    }

    private void MarkFlowDirty()
    {
        _flowDirty = true;
    }

    private void MarkEntityDirty(int index)
    {
        if ((uint)index >= (uint)UnitCount || _entitySyncDirtyFlags[index] != 0)
        {
            return;
        }

        _entitySyncDirtyFlags[index] = 1;
        _entitySyncDirtyAgents[_entitySyncDirtyCount++] = index;
    }

    private void MarkAllEntitiesDirty()
    {
        _entitySyncDirtyCount = 0;
        if (UnitCount <= 0)
        {
            return;
        }

        Array.Clear(_entitySyncDirtyFlags, 0, UnitCount);
        for (int i = 0; i < UnitCount; i++)
        {
            _entitySyncDirtyFlags[i] = 1;
            _entitySyncDirtyAgents[_entitySyncDirtyCount++] = i;
        }
    }

    private void InitializeUnits(ReadOnlySpan<MassNavigationAgentSeed> agentSeeds)
    {
        _maxBodyRadiusCm = 0f;
        Array.Clear(_velocitiesCm, 0, UnitCount * 2);
        Array.Clear(_unitTargetsCm, 0, UnitCount * 2);
        Array.Clear(_hasUnitTarget, 0, UnitCount);
        Array.Clear(_selectedFlags, 0, UnitCount);
        Array.Clear(_heavyProfileFlags, 0, UnitCount);
        Array.Clear(_bodyRadiiCm, 0, UnitCount);
        Array.Clear(_speedsCmPerSecond, 0, UnitCount);
        Array.Clear(_maxInteractingBodyRadiiCm, 0, UnitCount);
        Array.Clear(_separationHashSearchRadiusCellsByAgent, 0, UnitCount);
        Array.Clear(_hardResolveHashSearchRadiusCellsByAgent, 0, UnitCount);
        Array.Clear(_unitProgressAnchorCm, 0, UnitCount * 2);
        Array.Clear(_unitSettledAnchorCm, 0, UnitCount * 2);
        Array.Clear(_unitSettledFlags, 0, UnitCount);
        Array.Clear(_unitRetryCounts, 0, UnitCount);
        Array.Clear(_unitStuckSeconds, 0, UnitCount);

        int[] teamLocalWriteCursor = new int[_teamStates.Count];
        for (int unitIndex = 0; unitIndex < agentSeeds.Length; unitIndex++)
        {
            MassNavigationAgentSeed seed = agentSeeds[unitIndex];
            if (!_teamStateIndexById.TryGetValue(seed.TeamId, out int teamStateIndex))
            {
                throw new InvalidOperationException($"MassFlow agent seed references unregistered team {seed.TeamId}.");
            }

            int i2 = unitIndex << 1;
            ValidateRuntimeProfile(unitIndex, seed.NavMass, seed.VisualScale, seed.BodyRadiusCm, seed.SpeedCmPerSecond);
            _teams[unitIndex] = seed.TeamId;
            _teamRuntimeIndices[unitIndex] = teamStateIndex;
            _teamLocalIndices[unitIndex] = teamLocalWriteCursor[teamStateIndex]++;
            _flowRuntimeIndices[unitIndex] = ResolveFlowStateIndex(teamStateIndex, seed.Layer);
            _layerCategoryMasks[unitIndex] = seed.Layer.CategoryMask;
            _layerInteractionMasks[unitIndex] = seed.Layer.InteractionMask;
            _navMasses[unitIndex] = seed.NavMass;
            _visualScales[unitIndex] = seed.VisualScale;
            _bodyRadiiCm[unitIndex] = seed.BodyRadiusCm;
            _speedsCmPerSecond[unitIndex] = seed.SpeedCmPerSecond;
            _maxBodyRadiusCm = MathF.Max(_maxBodyRadiusCm, seed.BodyRadiusCm);
            _heavyProfileFlags[unitIndex] = seed.Heavy ? (byte)1 : (byte)0;
            _positionsCm[i2] = ClampXPosition(seed.LocalPositionXCm);
            _positionsCm[i2 + 1] = ClampYPosition(seed.LocalPositionYCm);
            _unitProgressAnchorCm[i2] = _positionsCm[i2];
            _unitProgressAnchorCm[i2 + 1] = _positionsCm[i2 + 1];
            _unitSettledAnchorCm[i2] = _positionsCm[i2];
            _unitSettledAnchorCm[i2 + 1] = _positionsCm[i2 + 1];
        }

        _maxInteractingBodyRadiiDirty = true;
    }

    private void MarkMovedEntitiesDirty()
    {
        float positionEpsilonSq = Semantics.Solver.EntitySyncPositionEpsilonSq;
        float velocityEpsilonSq = Semantics.Solver.EntitySyncVelocityEpsilonSq;
        for (int i = 0; i < UnitCount; i++)
        {
            int i2 = i << 1;
            float dx = _positionsCm[i2] - _readPositionsCm[i2];
            float dy = _positionsCm[i2 + 1] - _readPositionsCm[i2 + 1];
            float dvx = _velocitiesCm[i2] - _readVelocitiesCm[i2];
            float dvy = _velocitiesCm[i2 + 1] - _readVelocitiesCm[i2 + 1];
            if ((dx * dx) + (dy * dy) > positionEpsilonSq ||
                (dvx * dvx) + (dvy * dvy) > velocityEpsilonSq)
            {
                MarkEntityDirty(i);
            }
        }
    }

    private static bool ShouldRun(int frameIndex, ref int lastFrame, int intervalTicks)
    {
        int safeInterval = Math.Max(1, intervalTicks);
        if (lastFrame == int.MinValue || frameIndex - lastFrame >= safeInterval)
        {
            lastFrame = frameIndex;
            return true;
        }

        return false;
    }

    private void ComputeFlow(float[] flow, float targetX, float targetY)
    {
        for (int y = 0; y < _gridHeight; y++)
        {
            for (int x = 0; x < _gridWidth; x++)
            {
                int idx = (y * _gridWidth) + x;
                int flowIndex = idx << 1;
                if (_cost[idx] > Semantics.Solver.FlowBlockedCellThreshold)
                {
                    flow[flowIndex] = 0f;
                    flow[flowIndex + 1] = 0f;
                    continue;
                }

                float wx = (x + 0.5f) * _flowCellSizeCm;
                float wy = (y + 0.5f) * _flowCellSizeCm;
                float dx = targetX - wx;
                float dy = targetY - wy;
                float distSq = dx * dx + dy * dy;
                if (distSq < Semantics.Solver.FlowTargetStopDistanceSq)
                {
                    flow[flowIndex] = 0f;
                    flow[flowIndex + 1] = 0f;
                    continue;
                }

                float invDist = FastInvSqrt(distSq);
                dx *= invDist;
                dy *= invDist;

                float avoidX = 0f;
                float avoidY = 0f;
                int obstacleNeighborRadiusCells = Semantics.Solver.FlowObstacleNeighborRadiusCells;
                for (int offsetY = -obstacleNeighborRadiusCells; offsetY <= obstacleNeighborRadiusCells; offsetY++)
                {
                    for (int offsetX = -obstacleNeighborRadiusCells; offsetX <= obstacleNeighborRadiusCells; offsetX++)
                    {
                        if (offsetX == 0 && offsetY == 0)
                        {
                            continue;
                        }

                        int nx = x + offsetX;
                        int ny = y + offsetY;
                        if ((uint)nx >= (uint)_gridWidth || (uint)ny >= (uint)_gridHeight)
                        {
                            continue;
                        }

                        if (_cost[(ny * _gridWidth) + nx] > Semantics.Solver.FlowBlockedCellThreshold)
                        {
                            float ovx = -offsetX;
                            float ovy = -offsetY;
                            float obstacleDistSq = (ovx * ovx) + (ovy * ovy);
                            if (obstacleDistSq > Semantics.Solver.EntitySyncVelocityEpsilonSq)
                            {
                                float invObstacleDist = FastInvSqrt(obstacleDistSq);
                                float obstacleDist = obstacleDistSq * invObstacleDist;
                                float obstacleWeight = Semantics.Solver.FlowObstacleNeighborWeight / (obstacleDist * obstacleDist);
                                avoidX += (ovx * invObstacleDist) * obstacleWeight;
                                avoidY += (ovy * invObstacleDist) * obstacleWeight;
                            }
                        }
                    }
                }

                float flowX = dx + (avoidX * Semantics.Solver.FlowObstacleAvoidanceWeight);
                float flowY = dy + (avoidY * Semantics.Solver.FlowObstacleAvoidanceWeight);
                float flowLengthSq = flowX * flowX + flowY * flowY;
                if (flowLengthSq < Semantics.Solver.NormalizationEpsilonSq)
                {
                    flow[flowIndex] = 0f;
                    flow[flowIndex + 1] = 0f;
                }
                else
                {
                    float invFlow = FastInvSqrt(flowLengthSq);
                    flow[flowIndex] = flowX * invFlow;
                    flow[flowIndex + 1] = flowY * invFlow;
                }
            }
        }
    }

    private void BuildSeparationHash(float[] positionsCm)
    {
        BuildBucketHash(
            positionsCm,
            _separationHashWidth,
            _separationHashHeight,
            1f / _separationHashCellSizeCm,
            _separationCellCounts,
            _separationCellOffsets,
            _separationCellCursor,
            _separationAgents);
    }

    private void BuildHardResolveHash(float[] positionsCm)
    {
        BuildBucketHash(
            positionsCm,
            _hardResolveHashWidth,
            _hardResolveHashHeight,
            1f / _hardResolveHashCellSizeCm,
            _hardResolveCellCounts,
            _hardResolveCellOffsets,
            _hardResolveCellCursor,
            _hardResolveAgents);
    }

    private void BuildBucketHash(
        float[] positionsCm,
        int hashWidth,
        int hashHeight,
        float invHashCell,
        int[] cellCounts,
        int[] cellOffsets,
        int[] cellCursor,
        int[] cellAgents)
    {
        Array.Clear(cellCounts, 0, cellCounts.Length);
        int hashWidthMinusOne = hashWidth - 1;
        int hashHeightMinusOne = hashHeight - 1;

        for (int i = 0; i < UnitCount; i++)
        {
            if (TryGetHashCell(positionsCm, i, invHashCell, hashWidthMinusOne, hashHeightMinusOne, hashWidth, out int cell))
            {
                cellCounts[cell]++;
            }
        }

        int offset = 0;
        for (int cell = 0; cell < cellCounts.Length; cell++)
        {
            cellOffsets[cell] = offset;
            cellCursor[cell] = offset;
            offset += cellCounts[cell];
        }

        for (int i = 0; i < UnitCount; i++)
        {
            if (TryGetHashCell(positionsCm, i, invHashCell, hashWidthMinusOne, hashHeightMinusOne, hashWidth, out int cell))
            {
                cellAgents[cellCursor[cell]++] = i;
            }
        }
    }

    private void RefreshTeamRelationshipMatrixIfStale()
    {
        int currentRevision = TeamManager.Revision;
        if (_teamRelationshipRevision == currentRevision)
        {
            return;
        }

        int teamCount = _teamStates.Count;
        if (teamCount <= 0)
        {
            _teamRelationshipRevision = currentRevision;
            return;
        }

        int required = teamCount * teamCount;
        if (_teamRelationshipMatrix.Length < required)
        {
            _teamRelationshipMatrix = new TeamRelationship[required];
        }

        for (int a = 0; a < teamCount; a++)
        {
            int teamA = _teamStates[a].TeamId;
            int rowOffset = a * teamCount;
            for (int b = 0; b < teamCount; b++)
            {
                int teamB = _teamStates[b].TeamId;
                _teamRelationshipMatrix[rowOffset + b] = TeamManager.GetRelationship(teamA, teamB);
            }
        }

        _teamRelationshipRevision = currentRevision;
    }

    private static bool TryGetHashCell(float[] positionsCm, int index, float invHashCell, int hashWidthMinusOne, int hashHeightMinusOne, int hashWidth, out int cell)
    {
        int i2 = index << 1;
        int cellX = (int)(positionsCm[i2] * invHashCell);
        int cellY = (int)(positionsCm[i2 + 1] * invHashCell);
        if ((uint)cellX > (uint)hashWidthMinusOne || (uint)cellY > (uint)hashHeightMinusOne)
        {
            cell = -1;
            return false;
        }

        cell = (cellY * hashWidth) + cellX;
        return true;
    }

    private void StepRange(
        int startIndex,
        int endIndex,
        float clampedDt,
        MassNavigationGroupRuntime navGroupRuntime,
        float sepRadiusSq,
        float sepRadiusCm,
        float arrivalRadiusCm,
        float arrivalRadiusSq,
        float unitTargetStopThresholdSq,
        int hashWidthMinusOne,
        int hashHeightMinusOne,
        float invHashCell,
        int flowObstacleNeighborRadiusCells,
        bool useCandidateGating)
    {
        _ = sepRadiusSq;
        for (int i = startIndex; i < endIndex; i++)
        {
            int i2 = i << 1;
            float px = _readPositionsCm[i2];
            float py = _readPositionsCm[i2 + 1];
            int teamStateIndex = _teamRuntimeIndices[i];
            TeamRuntimeState team = _teamStates[teamStateIndex];
            FlowRuntimeState flowState = _flowStates[_flowRuntimeIndices[i]];
            bool inFormation = navGroupRuntime.HasGroup(i);

            float desiredX = 0f;
            float desiredY = 0f;
            float targetX = px;
            float targetY = py;
            float unitArrivalFactor = 1f;
            bool suppressTargetMotion = false;
            bool hasGoalTarget = false;
            float speed = _speedsCmPerSecond[i];

            if (_hasUnitTarget[i] != 0)
            {
                hasGoalTarget = true;
                targetX = _unitTargetsCm[i2];
                targetY = _unitTargetsCm[i2 + 1];
                float toTargetX = targetX - px;
                float toTargetY = targetY - py;
                float targetDistSq = toTargetX * toTargetX + toTargetY * toTargetY;
                if (ArrivalTuning.Enabled && _unitSettledFlags[i] != 0)
                {
                    if (ShouldRetryTargetAfterPush(i, px, py, targetDistSq, unitTargetStopThresholdSq))
                    {
                        ExitSettledState(i, px, py);
                    }
                    else
                    {
                        suppressTargetMotion = true;
                    }
                }

                if (!suppressTargetMotion && ArrivalTuning.Enabled && targetDistSq > unitTargetStopThresholdSq)
                {
                    UpdateUnitStuckTimer(i, px, py, clampedDt);
                    if (_unitStuckSeconds[i] >= ArrivalTuning.TimeoutSeconds)
                    {
                        EnterSettledState(i, px, py);
                        suppressTargetMotion = true;
                    }
                }
                else if (!suppressTargetMotion)
                {
                    ResetUnitProgressAnchor(i, px, py);
                }

                if (!suppressTargetMotion && targetDistSq > unitTargetStopThresholdSq)
                {
                    float invDist = FastInvSqrt(targetDistSq);
                    desiredX = toTargetX * invDist;
                    desiredY = toTargetY * invDist;

                    int gx = (int)(px / _flowCellSizeCm);
                    int gy = (int)(py / _flowCellSizeCm);
                    float avoidX = 0f;
                    float avoidY = 0f;
                    for (int oy = -flowObstacleNeighborRadiusCells; oy <= flowObstacleNeighborRadiusCells; oy++)
                    {
                        int ny = gy + oy;
                        if ((uint)ny >= (uint)_gridHeight)
                        {
                            continue;
                        }

                        int rowOffset = ny * _gridWidth;
                        for (int ox = -flowObstacleNeighborRadiusCells; ox <= flowObstacleNeighborRadiusCells; ox++)
                        {
                            if (ox == 0 && oy == 0)
                            {
                                continue;
                            }

                            int nx = gx + ox;
                            if ((uint)nx >= (uint)_gridWidth)
                            {
                                continue;
                            }

                            if (_staticCost[rowOffset + nx] > Semantics.Solver.FlowBlockedCellThreshold)
                            {
                                float obstacleDistanceSq = ox * ox + oy * oy;
                                float invObstacleDistance = FastInvSqrt(obstacleDistanceSq);
                                float invObstacleDistanceSq = invObstacleDistance * invObstacleDistance;
                                avoidX += (-ox * invObstacleDistance) * (Semantics.Solver.FlowObstacleNeighborWeight * invObstacleDistanceSq);
                                avoidY += (-oy * invObstacleDistance) * (Semantics.Solver.FlowObstacleNeighborWeight * invObstacleDistanceSq);
                            }
                        }
                    }

                    desiredX += avoidX * Semantics.Steering.FlowObstacleAvoidanceScale;
                    desiredY += avoidY * Semantics.Steering.FlowObstacleAvoidanceScale;
                    float flowLengthSq = desiredX * desiredX + desiredY * desiredY;
                    if (flowLengthSq > Semantics.Solver.NormalizationEpsilonSq)
                    {
                        float invFlow = FastInvSqrt(flowLengthSq);
                        desiredX *= invFlow;
                        desiredY *= invFlow;
                    }
                }

                float targetDistance = MathF.Sqrt(targetDistSq);
                float arriveThreshold = inFormation
                    ? Semantics.Group.FormationArriveThresholdCm
                    : Semantics.Group.LooseArriveThresholdCm;
                if (targetDistance < arriveThreshold)
                {
                    unitArrivalFactor = targetDistance / arriveThreshold;
                }
                else if (suppressTargetMotion)
                {
                    unitArrivalFactor = 0f;
                }
            }
            else
            {
                hasGoalTarget = true;
                int gx = (int)(px / _flowCellSizeCm);
                int gy = (int)(py / _flowCellSizeCm);
                float flowX = 0f;
                float flowY = 0f;
                if ((uint)gx < (uint)_gridWidth && (uint)gy < (uint)_gridHeight)
                {
                    int flowOffset = ((gy * _gridWidth) + gx) << 1;
                    flowX = flowState.Flow[flowOffset];
                    flowY = flowState.Flow[flowOffset + 1];
                }

                ComputeTeamSlotTarget(i, team, out targetX, out targetY);
                float toSlotX = targetX - px;
                float toSlotY = targetY - py;
                float slotDistSq = toSlotX * toSlotX + toSlotY * toSlotY;
                if (ArrivalTuning.Enabled && _unitSettledFlags[i] != 0)
                {
                    if (ShouldRetryTargetAfterPush(i, px, py, slotDistSq, unitTargetStopThresholdSq))
                    {
                        ExitSettledState(i, px, py);
                    }
                    else
                    {
                        suppressTargetMotion = true;
                    }
                }

                if (!suppressTargetMotion && ArrivalTuning.Enabled && slotDistSq > unitTargetStopThresholdSq)
                {
                    UpdateUnitStuckTimer(i, px, py, clampedDt);
                    if (_unitStuckSeconds[i] >= ArrivalTuning.TimeoutSeconds)
                    {
                        EnterSettledState(i, px, py);
                        suppressTargetMotion = true;
                    }
                }
                else if (!suppressTargetMotion)
                {
                    ResetUnitProgressAnchor(i, px, py);
                }

                if (!suppressTargetMotion && slotDistSq > Semantics.Solver.DirectionEpsilonSq)
                {
                    float invSlot = FastInvSqrt(slotDistSq);
                    float slotDirX = toSlotX * invSlot;
                    float slotDirY = toSlotY * invSlot;
                    float slotBlend = slotDistSq < Semantics.Group.NearSlotBlendDistanceSq
                        ? Semantics.Group.NearSlotBlend
                        : Semantics.Group.FarSlotBlend;
                    desiredX = (flowX * (1f - slotBlend)) + (slotDirX * slotBlend);
                    desiredY = (flowY * (1f - slotBlend)) + (slotDirY * slotBlend);
                    float desiredLengthSq = desiredX * desiredX + desiredY * desiredY;
                    if (desiredLengthSq > Semantics.Solver.NormalizationEpsilonSq)
                    {
                        float invDesired = FastInvSqrt(desiredLengthSq);
                        desiredX *= invDesired;
                        desiredY *= invDesired;
                    }
                }
                else if (!suppressTargetMotion)
                {
                    desiredX = flowX;
                    desiredY = flowY;
                }
            }

            float directTargetX = hasGoalTarget ? targetX - px : 0f;
            float directTargetY = hasGoalTarget ? targetY - py : 0f;
            float directTargetSq = hasGoalTarget ? directTargetX * directTargetX + directTargetY * directTargetY : 0f;
            float flowScale = suppressTargetMotion ? 0f : 1f;
            float effectiveArrivalCm = inFormation
                ? Semantics.Group.FormationFlowSlowRadiusCm
                : arrivalRadiusCm;
            float effectiveArrivalSq = effectiveArrivalCm * effectiveArrivalCm;
            if (!suppressTargetMotion && hasGoalTarget && directTargetSq < effectiveArrivalSq)
            {
                flowScale = MathF.Sqrt(directTargetSq) / effectiveArrivalCm;
            }

            float separationX = 0f;
            float separationY = 0f;
            int cellX = (int)(px * invHashCell);
            int cellY = (int)(py * invHashCell);
            cellX = cellX < 0 ? 0 : (cellX > hashWidthMinusOne ? hashWidthMinusOne : cellX);
            cellY = cellY < 0 ? 0 : (cellY > hashHeightMinusOne ? hashHeightMinusOne : cellY);

            int separationHashSearchRadius = _separationHashSearchRadiusCellsByAgent[i];
            int minY = Math.Max(0, cellY - separationHashSearchRadius);
            int maxY = Math.Min(hashHeightMinusOne, cellY + separationHashSearchRadius);
            int minX = Math.Max(0, cellX - separationHashSearchRadius);
            int maxX = Math.Min(hashWidthMinusOne, cellX + separationHashSearchRadius);

            for (int neighborY = minY; neighborY <= maxY; neighborY++)
            {
                int rowBase = neighborY * _separationHashWidth;
                for (int neighborX = minX; neighborX <= maxX; neighborX++)
                {
                    int cell = rowBase + neighborX;
                    int start = _separationCellOffsets[cell];
                    int end = start + _separationCellCounts[cell];
                    for (int hashIndex = start; hashIndex < end; hashIndex++)
                    {
                        int j = _separationAgents[hashIndex];
                        if (j != i)
                        {
                            if (!CanAgentsInteract(i, j))
                            {
                                continue;
                            }

                            int j2 = j << 1;
                            float dx = px - _readPositionsCm[j2];
                            float dy = py - _readPositionsCm[j2 + 1];
                            float d2 = dx * dx + dy * dy;
                            float pairHardCandidateDistance = ResolvePairHardCandidateDistanceCm(i, j);
                            if (useCandidateGating && d2 < pairHardCandidateDistance * pairHardCandidateDistance)
                            {
                                _hardResolveCandidates[i] = 1;
                            }

                            float pairSeparationRadius = ResolvePairSeparationRadiusCm(i, j, sepRadiusCm);
                            if (d2 < pairSeparationRadius * pairSeparationRadius && d2 > Semantics.Solver.DirectionEpsilonSq)
                            {
                                float invD = FastInvSqrt(d2);
                                float d = d2 * invD;
                                float force = 1f - (d / pairSeparationRadius);
                                float response = ComputeSeparationResponse(teamStateIndex, _teamRuntimeIndices[j], i, j);
                                separationX += dx * invD * force * response;
                                separationY += dy * invD * force * response;
                            }
                        }
                    }
                }
            }

            float obstaclePushX = 0f;
            float obstaclePushY = 0f;
            for (int obstacleIndex = 0; obstacleIndex < ObstacleCount; obstacleIndex++)
            {
                float dx = px - _obsX[obstacleIndex];
                float dy = py - _obsY[obstacleIndex];
                float d2 = dx * dx + dy * dy;
                float minDistance = _obsRadius[obstacleIndex] + _bodyRadiiCm[i] + Semantics.Obstacle.HardResolveCandidateDistanceCm;
                if (useCandidateGating && d2 < minDistance * minDistance)
                {
                    _hardResolveCandidates[i] = 1;
                }

                float pushRadius = _obsRadius[obstacleIndex] + _bodyRadiiCm[i] + Semantics.Obstacle.SoftPushPaddingCm;
                if (d2 < pushRadius * pushRadius && d2 > Semantics.Solver.DirectionEpsilonSq)
                {
                    float invD = FastInvSqrt(d2);
                    float d = d2 * invD;
                    float pushStrength = (pushRadius - d) / pushRadius;
                    float pushForce = pushStrength * pushStrength * Semantics.Obstacle.SoftPushForceScale;
                    obstaclePushX += dx * invD * pushForce;
                    obstaclePushY += dy * invD * pushForce;
                }
            }

            float separationScale = (inFormation
                ? Semantics.Steering.FormationSeparationScale
                : Semantics.Steering.LooseSeparationScale) * unitArrivalFactor;
            float desiredVelocityX = desiredX * speed * flowScale + separationX * separationScale + obstaclePushX * speed;
            float desiredVelocityY = desiredY * speed * flowScale + separationY * separationScale + obstaclePushY * speed;
            float desiredVelocitySq = desiredVelocityX * desiredVelocityX + desiredVelocityY * desiredVelocityY;
            if (desiredVelocitySq > speed * speed)
            {
                float scale = speed * FastInvSqrt(desiredVelocitySq);
                desiredVelocityX *= scale;
                desiredVelocityY *= scale;
            }

            float mix = MathF.Min(clampedDt * Semantics.Steering.VelocityBlendPerSecond, 1f);
            float velocityX = _readVelocitiesCm[i2] + ((desiredVelocityX - _readVelocitiesCm[i2]) * mix);
            float velocityY = _readVelocitiesCm[i2 + 1] + ((desiredVelocityY - _readVelocitiesCm[i2 + 1]) * mix);
            _velocitiesCm[i2] = velocityX;
            _velocitiesCm[i2 + 1] = velocityY;

            float nextX = px + velocityX * clampedDt;
            float nextY = py + velocityY * clampedDt;
            _positionsCm[i2] = nextX;
            _positionsCm[i2 + 1] = nextY;
        }
    }

    private void ClampAllPositionsToWorldBounds()
    {
        if (!HasWorldBounds())
        {
            return;
        }

        for (int i = 0; i < UnitCount; i++)
        {
            int offset = i << 1;
            float xCm = _positionsCm[offset];
            float yCm = _positionsCm[offset + 1];
            float bodyRadiusCm = _bodyRadiiCm[i];
            bool clampedX = ClampLocalXToWorldBounds(ref xCm, bodyRadiusCm);
            bool clampedY = ClampLocalYToWorldBounds(ref yCm, bodyRadiusCm);
            if (!clampedX && !clampedY)
            {
                continue;
            }

            _positionsCm[offset] = xCm;
            _positionsCm[offset + 1] = yCm;
            if (clampedX)
            {
                _velocitiesCm[offset] = 0f;
            }

            if (clampedY)
            {
                _velocitiesCm[offset + 1] = 0f;
            }
        }
    }

    private void ComputeTeamSlotTarget(int index, TeamRuntimeState team, out float targetX, out float targetY)
    {
        int teamCount = Math.Max(1, team.UnitCount);
        int localIndex = _teamLocalIndices[index];
        int cols = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(teamCount)));
        int rows = Math.Max(1, (int)Math.Ceiling(teamCount / (double)cols));
        int row = localIndex / cols;
        int col = localIndex % cols;
        float rowCenter = (rows - 1) * 0.5f;
        float colCenter = (cols - 1) * 0.5f;
        float offsetX = (col - colCenter) * Semantics.Group.TeamSlotSpacingCm;
        float offsetY = (row - rowCenter) * Semantics.Group.TeamSlotSpacingCm;
        Vector2 resolved = ResolveNavigableTarget(
            team.TargetX + offsetX,
            team.TargetY + offsetY,
            offsetX,
            offsetY,
            Semantics.TargetProjection.TeamSlotClearanceCm);
        targetX = resolved.X;
        targetY = resolved.Y;
    }

    private bool TryGetTeamState(int teamId, out TeamRuntimeState team)
    {
        if (_teamStateIndexById.TryGetValue(teamId, out int index) &&
            (uint)index < (uint)_teamStates.Count)
        {
            team = _teamStates[index];
            return true;
        }

        team = null!;
        return false;
    }

    private float ComputeSeparationResponse(int selfTeamStateIndex, int otherTeamStateIndex, int selfUnitIndex, int otherUnitIndex)
    {
        TeamRelationship selfToOther = GetTeamRelationship(selfTeamStateIndex, otherTeamStateIndex);
        float selfMass = _navMasses[selfUnitIndex];
        float otherMass = _navMasses[otherUnitIndex];
        MassFlowPairAvoidancePolicy policy = ResolvePolicy(selfToOther, selfMass, otherMass);
        return policy switch
        {
            MassFlowPairAvoidancePolicy.FriendlyCooperativeYield => Math.Clamp(
                (selfMass / MathF.Max(Semantics.Solver.MinNavMass, otherMass)) * AvoidanceTuning.FriendlyResponseScale,
                AvoidanceTuning.FriendlyResponseMin,
                AvoidanceTuning.FriendlyResponseMax),
            MassFlowPairAvoidancePolicy.DominantPush => Math.Clamp(
                (otherMass / MathF.Max(Semantics.Solver.MinNavMass, selfMass)) * AvoidanceTuning.DominantPushResponseScale,
                AvoidanceTuning.DominantPushResponseMin,
                AvoidanceTuning.DominantPushResponseMax),
            _ => Math.Clamp(
                (otherMass / MathF.Max(Semantics.Solver.MinNavMass, selfMass)) * AvoidanceTuning.NonFriendlyResponseScale,
                AvoidanceTuning.NonFriendlyResponseMin,
                AvoidanceTuning.NonFriendlyResponseMax),
        };
    }

    private TeamRelationship GetTeamRelationship(int sourceTeamStateIndex, int targetTeamStateIndex)
    {
        int teamCount = _teamStates.Count;
        if ((uint)sourceTeamStateIndex >= (uint)teamCount || (uint)targetTeamStateIndex >= (uint)teamCount)
        {
            throw new InvalidOperationException(
                $"MassFlow team relationship index out of range: source={sourceTeamStateIndex}, target={targetTeamStateIndex}, teamCount={teamCount}.");
        }

        return _teamRelationshipMatrix[(sourceTeamStateIndex * teamCount) + targetTeamStateIndex];
    }

    private MassFlowPairAvoidancePolicy ResolveBidirectionalPolicy(int selfTeamStateIndex, int otherTeamStateIndex, int selfUnitIndex, int otherUnitIndex)
    {
        TeamRelationship selfToOther = GetTeamRelationship(selfTeamStateIndex, otherTeamStateIndex);
        TeamRelationship otherToSelf = GetTeamRelationship(otherTeamStateIndex, selfTeamStateIndex);
        if (selfToOther == TeamRelationship.Friendly && otherToSelf == TeamRelationship.Friendly)
        {
            return MassFlowPairAvoidancePolicy.FriendlyCooperativeYield;
        }

        float selfMass = _navMasses[selfUnitIndex];
        float otherMass = _navMasses[otherUnitIndex];
        float minMass = MathF.Max(Semantics.Solver.MinNavMass, MathF.Min(selfMass, otherMass));
        float maxMass = MathF.Max(selfMass, otherMass);
        return (maxMass / minMass) >= AvoidanceTuning.DominantMassRatio
            ? MassFlowPairAvoidancePolicy.DominantPush
            : MassFlowPairAvoidancePolicy.NonFriendlyBlocker;
    }

    private bool CanAgentsInteract(int selfUnitIndex, int otherUnitIndex)
    {
        uint selfCategory = _layerCategoryMasks[selfUnitIndex];
        uint otherCategory = _layerCategoryMasks[otherUnitIndex];
        return (_layerInteractionMasks[selfUnitIndex] & otherCategory) != 0u &&
            (_layerInteractionMasks[otherUnitIndex] & selfCategory) != 0u;
    }

    private float ResolvePairBodyRadiusSumCm(int selfUnitIndex, int otherUnitIndex)
    {
        return _bodyRadiiCm[selfUnitIndex] + _bodyRadiiCm[otherUnitIndex];
    }

    private float ResolvePairSeparationRadiusCm(int selfUnitIndex, int otherUnitIndex, float configuredSeparationRadiusCm)
    {
        return MathF.Max(configuredSeparationRadiusCm, ResolvePairBodyRadiusSumCm(selfUnitIndex, otherUnitIndex));
    }

    private float ResolvePairHardCandidateDistanceCm(int selfUnitIndex, int otherUnitIndex)
    {
        return ResolvePairBodyRadiusSumCm(selfUnitIndex, otherUnitIndex) + Semantics.Obstacle.HardResolveCandidateDistanceCm;
    }

    private int ResolveSeparationHashSearchRadiusCells(int selfUnitIndex)
    {
        float maxPairDistanceCm = MathF.Max(
            Semantics.Steering.SeparationRadiusCm,
            _bodyRadiiCm[selfUnitIndex] + ResolveMaxInteractingBodyRadiusCm(selfUnitIndex) + Semantics.Obstacle.HardResolveCandidateDistanceCm);
        return Math.Max(_separationHashMinSearchRadiusCells, (int)MathF.Ceiling(maxPairDistanceCm / _separationHashCellSizeCm));
    }

    private int ResolveHardResolveHashSearchRadiusCells(int selfUnitIndex)
    {
        float maxCandidateDistanceCm = _bodyRadiiCm[selfUnitIndex] + ResolveMaxInteractingBodyRadiusCm(selfUnitIndex) + Semantics.Obstacle.HardResolveCandidateDistanceCm;
        return Math.Max(_hardResolveHashMinSearchRadiusCells, (int)MathF.Ceiling(maxCandidateDistanceCm / _hardResolveHashCellSizeCm));
    }

    private MassFlowPairAvoidancePolicy ResolvePolicy(TeamRelationship relationship, float selfMass, float otherMass)
    {
        if (relationship == TeamRelationship.Friendly)
        {
            return MassFlowPairAvoidancePolicy.FriendlyCooperativeYield;
        }

        float minMass = MathF.Max(Semantics.Solver.MinNavMass, MathF.Min(selfMass, otherMass));
        float maxMass = MathF.Max(selfMass, otherMass);
        return (maxMass / minMass) >= AvoidanceTuning.DominantMassRatio
            ? MassFlowPairAvoidancePolicy.DominantPush
            : MassFlowPairAvoidancePolicy.NonFriendlyBlocker;
    }

    private float ComputeCorrectionShare(MassFlowPairAvoidancePolicy policy, float selfMass, float otherMass)
    {
        float safeSelf = MathF.Max(Semantics.Solver.MinNavMass, selfMass);
        float safeOther = MathF.Max(Semantics.Solver.MinNavMass, otherMass);
        float dominantOtherMass = safeOther * AvoidanceTuning.DominantCorrectionOtherMassWeight;
        float nonFriendlyOtherMass = safeOther * AvoidanceTuning.NonFriendlyCorrectionOtherMassWeight;
        return policy switch
        {
            MassFlowPairAvoidancePolicy.FriendlyCooperativeYield => Math.Clamp(
                safeSelf / (safeSelf + safeOther),
                AvoidanceTuning.FriendlyCorrectionShareMin,
                AvoidanceTuning.FriendlyCorrectionShareMax),
            MassFlowPairAvoidancePolicy.DominantPush => Math.Clamp(
                dominantOtherMass / (safeSelf + dominantOtherMass),
                AvoidanceTuning.DominantCorrectionShareMin,
                AvoidanceTuning.DominantCorrectionShareMax),
            _ => Math.Clamp(
                nonFriendlyOtherMass / (safeSelf + nonFriendlyOtherMass),
                AvoidanceTuning.NonFriendlyCorrectionShareMin,
                AvoidanceTuning.NonFriendlyCorrectionShareMax),
        };
    }

    private void UpdateUnitStuckTimer(int index, float px, float py, float dt)
    {
        int i2 = index << 1;
        float dx = px - _unitProgressAnchorCm[i2];
        float dy = py - _unitProgressAnchorCm[i2 + 1];
        float progressDistanceCm = ArrivalTuning.ProgressDistanceCm;
        if ((dx * dx) + (dy * dy) >= progressDistanceCm * progressDistanceCm)
        {
            _unitProgressAnchorCm[i2] = px;
            _unitProgressAnchorCm[i2 + 1] = py;
            _unitStuckSeconds[index] = 0f;
            return;
        }

        _unitStuckSeconds[index] += dt;
    }

    private bool ShouldRetryTargetAfterPush(int index, float px, float py, float targetDistSq, float unitTargetStopThresholdSq)
    {
        if (_unitRetryCounts[index] >= ArrivalTuning.MaxRetryCount || targetDistSq <= unitTargetStopThresholdSq)
        {
            return false;
        }

        int i2 = index << 1;
        float dx = px - _unitSettledAnchorCm[i2];
        float dy = py - _unitSettledAnchorCm[i2 + 1];
        float wakeDistanceCm = ArrivalTuning.WakePushDistanceCm;
        return (dx * dx) + (dy * dy) >= wakeDistanceCm * wakeDistanceCm;
    }

    private void EnterSettledState(int index, float px, float py)
    {
        int i2 = index << 1;
        _unitSettledFlags[index] = 1;
        _unitSettledAnchorCm[i2] = px;
        _unitSettledAnchorCm[i2 + 1] = py;
        _unitProgressAnchorCm[i2] = px;
        _unitProgressAnchorCm[i2 + 1] = py;
        _unitStuckSeconds[index] = 0f;
        _velocitiesCm[i2] = 0f;
        _velocitiesCm[i2 + 1] = 0f;
    }

    private void ExitSettledState(int index, float px, float py)
    {
        _unitSettledFlags[index] = 0;
        if (_unitRetryCounts[index] < byte.MaxValue)
        {
            _unitRetryCounts[index]++;
        }

        ResetUnitProgressAnchor(index, px, py);
    }

    private void ResetUnitArrivalState(int index, bool clearRetryCount)
    {
        if ((uint)index >= (uint)UnitCount)
        {
            return;
        }

        int i2 = index << 1;
        float px = _positionsCm.Length > i2 ? _positionsCm[i2] : 0f;
        float py = _positionsCm.Length > i2 + 1 ? _positionsCm[i2 + 1] : 0f;
        _unitStuckSeconds[index] = 0f;
        _unitSettledFlags[index] = 0;
        _unitProgressAnchorCm[i2] = px;
        _unitProgressAnchorCm[i2 + 1] = py;
        _unitSettledAnchorCm[i2] = px;
        _unitSettledAnchorCm[i2 + 1] = py;
        if (clearRetryCount)
        {
            _unitRetryCounts[index] = 0;
        }
    }

    private void ResetTeamArrivalState(int teamId)
    {
        for (int i = 0; i < UnitCount; i++)
        {
            if (_teams[i] == teamId)
            {
                ResetUnitArrivalState(i, clearRetryCount: true);
            }
        }
    }

    private void ResetUnitProgressAnchor(int index, float px, float py)
    {
        int i2 = index << 1;
        _unitProgressAnchorCm[i2] = px;
        _unitProgressAnchorCm[i2 + 1] = py;
        _unitStuckSeconds[index] = 0f;
    }

    private void RecomputeMaxBodyRadiusCm()
    {
        float max = 0f;
        for (int i = 0; i < UnitCount; i++)
        {
            max = MathF.Max(max, _bodyRadiiCm[i]);
        }

        _maxBodyRadiusCm = max;
    }

    private float ResolveMaxInteractingBodyRadiusCm(int selfUnitIndex)
    {
        float radius = _maxInteractingBodyRadiiCm[selfUnitIndex];
        return radius > 0f ? radius : _maxBodyRadiusCm;
    }

    private void RecomputeMaxInteractingBodyRadiiCmIfDirty()
    {
        if (!_maxInteractingBodyRadiiDirty)
        {
            return;
        }

        RecomputeMaxInteractingBodyRadiiCm();
    }

    private void RecomputeMaxInteractingBodyRadiiCm()
    {
        Array.Clear(_maxBodyRadiusByCategoryBit, 0, _maxBodyRadiusByCategoryBit.Length);
        for (int i = 0; i < UnitCount; i++)
        {
            uint categories = _layerCategoryMasks[i];
            while (categories != 0u)
            {
                int bitIndex = BitOperations.TrailingZeroCount(categories);
                _maxBodyRadiusByCategoryBit[bitIndex] = MathF.Max(
                    _maxBodyRadiusByCategoryBit[bitIndex],
                    _bodyRadiiCm[i]);
                categories &= categories - 1u;
            }
        }

        for (int i = 0; i < UnitCount; i++)
        {
            uint mask = _layerInteractionMasks[i];
            float maxInteractingRadiusCm = 0f;
            while (mask != 0u)
            {
                int bitIndex = BitOperations.TrailingZeroCount(mask);
                maxInteractingRadiusCm = MathF.Max(maxInteractingRadiusCm, _maxBodyRadiusByCategoryBit[bitIndex]);
                mask &= mask - 1u;
            }

            _maxInteractingBodyRadiiCm[i] = maxInteractingRadiusCm;
            _separationHashSearchRadiusCellsByAgent[i] = ResolveSeparationHashSearchRadiusCells(i);
            _hardResolveHashSearchRadiusCellsByAgent[i] = ResolveHardResolveHashSearchRadiusCells(i);
        }

        _maxInteractingBodyRadiiDirty = false;
    }

    private void UpdateSettledUnitCount()
    {
        int settled = 0;
        for (int i = 0; i < UnitCount; i++)
        {
            if (_unitSettledFlags[i] != 0)
            {
                settled++;
            }
        }

        SettledUnitCount = settled;
    }

    private void ResolveHardPenetration()
    {
        if (UnitCount <= 1)
        {
            ResolveObstaclePenetration();
            return;
        }

        BuildHardResolveHash(_positionsCm);
        float invHashCell = 1f / _hardResolveHashCellSizeCm;
        int hwm1 = _hardResolveHashWidth - 1;
        int hhm1 = _hardResolveHashHeight - 1;

        for (int i = 0; i < UnitCount; i++)
        {
            if (_useCandidateGating && _hardResolveCandidates[i] == 0)
            {
                continue;
            }

            int i2 = i << 1;
            float px = _positionsCm[i2];
            float py = _positionsCm[i2 + 1];
            if (!IsInsideTacticalField(px, py))
            {
                continue;
            }

            int cellX = (int)(px * invHashCell);
            int cellY = (int)(py * invHashCell);
            cellX = cellX < 0 ? 0 : (cellX > hwm1 ? hwm1 : cellX);
            cellY = cellY < 0 ? 0 : (cellY > hhm1 ? hhm1 : cellY);

            int hardResolveSearchRadius = _hardResolveHashSearchRadiusCellsByAgent[i];
            int minY = Math.Max(0, cellY - hardResolveSearchRadius);
            int maxY = Math.Min(hhm1, cellY + hardResolveSearchRadius);
            int minX = Math.Max(0, cellX - hardResolveSearchRadius);
            int maxX = Math.Min(hwm1, cellX + hardResolveSearchRadius);

            for (int neighborY = minY; neighborY <= maxY; neighborY++)
            {
                int rowBase = neighborY * _hardResolveHashWidth;
                for (int neighborX = minX; neighborX <= maxX; neighborX++)
                {
                    int cell = rowBase + neighborX;
                    int start = _hardResolveCellOffsets[cell];
                    int end = start + _hardResolveCellCounts[cell];
                    for (int hashIndex = start; hashIndex < end; hashIndex++)
                    {
                        int j = _hardResolveAgents[hashIndex];
                        if (j > i)
                        {
                            if (!CanAgentsInteract(i, j))
                            {
                                continue;
                            }

                            SeparateAgents(i, j);
                        }
                    }
                }
            }
        }

        ResolveObstaclePenetration();
    }

    private void SeparateAgents(int i, int j)
    {
        int i2 = i << 1;
        int j2 = j << 1;
        float dx = _positionsCm[i2] - _positionsCm[j2];
        float dy = _positionsCm[i2 + 1] - _positionsCm[j2 + 1];
        float d2 = dx * dx + dy * dy;
        float minDistance = ResolvePairBodyRadiusSumCm(i, j);
        if (d2 >= minDistance * minDistance)
        {
            return;
        }

        float nx;
        float ny;
        float overlap;
        if (d2 > Semantics.Solver.DirectionEpsilonSq)
        {
            float invD = FastInvSqrt(d2);
            float distance = d2 * invD;
            nx = dx * invD;
            ny = dy * invD;
            overlap = minDistance - distance;
        }
        else
        {
            int hashMask = Semantics.Solver.CoincidentPairHashBucketCount - 1;
            float angle = ((i * Semantics.Solver.CoincidentPairHashPrimeA) ^ (j * Semantics.Solver.CoincidentPairHashPrimeB)) & hashMask;
            float radians = angle * (MathF.PI * 2f / Semantics.Solver.CoincidentPairHashBucketCount);
            nx = MathF.Cos(radians);
            ny = MathF.Sin(radians);
            overlap = minDistance;
        }

        MassFlowPairAvoidancePolicy policy = ResolveBidirectionalPolicy(_teamRuntimeIndices[i], _teamRuntimeIndices[j], i, j);
        float shareI = ComputeCorrectionShare(policy, _navMasses[i], _navMasses[j]);
        float shareJ = 1f - shareI;
        float correctionI = overlap * shareI;
        float correctionJ = overlap * shareJ;
        _positionsCm[i2] += nx * correctionI;
        _positionsCm[i2 + 1] += ny * correctionI;
        _positionsCm[j2] -= nx * correctionJ;
        _positionsCm[j2 + 1] -= ny * correctionJ;

        float relVelX = _velocitiesCm[i2] - _velocitiesCm[j2];
        float relVelY = _velocitiesCm[i2 + 1] - _velocitiesCm[j2 + 1];
        float closingSpeed = relVelX * nx + relVelY * ny;
        if (closingSpeed < 0f)
        {
            float impulse = -closingSpeed;
            _velocitiesCm[i2] += nx * impulse * shareI;
            _velocitiesCm[i2 + 1] += ny * impulse * shareI;
            _velocitiesCm[j2] -= nx * impulse * shareJ;
            _velocitiesCm[j2 + 1] -= ny * impulse * shareJ;
        }
    }

    private void ResolveObstaclePenetration()
    {
        for (int i = 0; i < UnitCount; i++)
        {
            if (_useCandidateGating && _hardResolveCandidates[i] == 0)
            {
                continue;
            }

            int i2 = i << 1;
            float px = _positionsCm[i2];
            float py = _positionsCm[i2 + 1];
            if (!IsInsideTacticalField(px, py))
            {
                continue;
            }

            for (int obstacleIndex = 0; obstacleIndex < ObstacleCount; obstacleIndex++)
            {
                float dx = px - _obsX[obstacleIndex];
                float dy = py - _obsY[obstacleIndex];
                float minDistance = _obsRadius[obstacleIndex] + _bodyRadiiCm[i];
                float d2 = dx * dx + dy * dy;
                if (d2 >= minDistance * minDistance)
                {
                    continue;
                }

                float nx;
                float ny;
                if (d2 > Semantics.Solver.DirectionEpsilonSq)
                {
                    float invD = FastInvSqrt(d2);
                    nx = dx * invD;
                    ny = dy * invD;
                }
                else
                {
                    nx = 1f;
                    ny = 0f;
                }

                _positionsCm[i2] = _obsX[obstacleIndex] + nx * minDistance;
                _positionsCm[i2 + 1] = _obsY[obstacleIndex] + ny * minDistance;
                ClampLocalToWorldBounds(ref _positionsCm[i2], ref _positionsCm[i2 + 1], _bodyRadiiCm[i]);
                px = _positionsCm[i2];
                py = _positionsCm[i2 + 1];

                float inwardSpeed = _velocitiesCm[i2] * nx + _velocitiesCm[i2 + 1] * ny;
                if (inwardSpeed < 0f)
                {
                    _velocitiesCm[i2] -= nx * inwardSpeed;
                    _velocitiesCm[i2 + 1] -= ny * inwardSpeed;
                }
            }
        }
    }

    private int ResolveFlowStateIndex(int teamStateIndex, MassNavigationAgentLayer layer)
    {
        for (int i = 0; i < _flowStates.Count; i++)
        {
            FlowRuntimeState existing = _flowStates[i];
            if (existing.TeamStateIndex == teamStateIndex &&
                existing.CategoryMask == layer.CategoryMask &&
                existing.InteractionMask == layer.InteractionMask)
            {
                return i;
            }
        }

        _flowStates.Add(new FlowRuntimeState(teamStateIndex, layer.CategoryMask, layer.InteractionMask, _gridCellCount));
        return _flowStates.Count - 1;
    }

    private static UnitStepJob[] CreateStepJobs(int count)
    {
        var jobs = new UnitStepJob[count];
        for (int i = 0; i < count; i++)
        {
            jobs[i] = new UnitStepJob();
        }

        return jobs;
    }

    private static float DeterministicSpawnJitterCm(int seed, int teamId, int localIndex, uint axis, float jitterCm)
    {
        if (jitterCm == 0f)
        {
            return 0f;
        }

        uint hash = MixSpawnJitterHash(seed, teamId, localIndex, axis);
        float unit = ((hash >> 8) * (1f / 16_777_216f)) - 0.5f;
        return unit * jitterCm;
    }

    private static uint MixSpawnJitterHash(int seed, int teamId, int localIndex, uint axis)
    {
        unchecked
        {
            uint hash = 2_166_136_261u;
            hash = (hash ^ (uint)seed) * 16_777_619u;
            hash = (hash ^ (uint)teamId) * 16_777_619u;
            hash = (hash ^ (uint)localIndex) * 16_777_619u;
            hash = (hash ^ axis) * 16_777_619u;
            hash ^= hash >> 16;
            hash *= 2_246_822_519u;
            hash ^= hash >> 13;
            hash *= 3_266_489_917u;
            hash ^= hash >> 16;
            return hash;
        }
    }

    private bool IsObstacle(float wx, float wy)
    {
        for (int i = 0; i < ObstacleCount; i++)
        {
            float dx = wx - _obsX[i];
            float dy = wy - _obsY[i];
            if ((dx * dx) + (dy * dy) < _obsR2[i])
            {
                return true;
            }
        }

        return false;
    }

    private float FastInvSqrt(float value)
    {
        return value < Semantics.Solver.InverseSqrtMinValue ? 0f : 1f / MathF.Sqrt(value);
    }

    private float ClampXPosition(float value)
    {
        return Math.Clamp(value, _playAreaMinXCm, _playAreaMaxXCm);
    }

    private float ClampYPosition(float value)
    {
        return Math.Clamp(value, _playAreaMinYCm, _playAreaMaxYCm);
    }

    private bool HasWorldBounds()
    {
        return _worldMinXCm <= _worldMaxXCm &&
            _worldMinYCm <= _worldMaxYCm &&
            !float.IsInfinity(_worldMinXCm) &&
            !float.IsInfinity(_worldMaxXCm) &&
            !float.IsInfinity(_worldMinYCm) &&
            !float.IsInfinity(_worldMaxYCm);
    }

    private void ClampLocalToWorldBounds(ref float xCm, ref float yCm, float insetCm)
    {
        ClampLocalXToWorldBounds(ref xCm, insetCm);
        ClampLocalYToWorldBounds(ref yCm, insetCm);
    }

    private bool ClampLocalXToWorldBounds(ref float xCm, float insetCm)
    {
        if (!HasWorldBounds())
        {
            return false;
        }

        if (insetCm < 0f)
        {
            throw new InvalidOperationException("MassFlow world-bound clamp requires insetCm >= 0.");
        }

        float worldX = _worldOriginXCm + xCm;
        float min = _worldMinXCm + insetCm;
        float max = _worldMaxXCm - insetCm;
        if (min > max)
        {
            throw new InvalidOperationException(
                $"MassFlow world bounds cannot contain inset {insetCm:0.###} cm on X axis.");
        }

        float clampedWorldX = Math.Clamp(worldX, min, max);
        if (clampedWorldX == worldX)
        {
            return false;
        }

        xCm = clampedWorldX - _worldOriginXCm;
        return true;
    }

    private bool ClampLocalYToWorldBounds(ref float yCm, float insetCm)
    {
        if (!HasWorldBounds())
        {
            return false;
        }

        if (insetCm < 0f)
        {
            throw new InvalidOperationException("MassFlow world-bound clamp requires insetCm >= 0.");
        }

        float worldY = _worldOriginYCm + yCm;
        float min = _worldMinYCm + insetCm;
        float max = _worldMaxYCm - insetCm;
        if (min > max)
        {
            throw new InvalidOperationException(
                $"MassFlow world bounds cannot contain inset {insetCm:0.###} cm on Y axis.");
        }

        float clampedWorldY = Math.Clamp(worldY, min, max);
        if (clampedWorldY == worldY)
        {
            return false;
        }

        yCm = clampedWorldY - _worldOriginYCm;
        return true;
    }

    private bool IsInsideTacticalField(float xCm, float yCm)
    {
        return xCm >= _playAreaMinXCm &&
            xCm <= _playAreaMaxXCm &&
            yCm >= _playAreaMinYCm &&
            yCm <= _playAreaMaxYCm;
    }

    private sealed class UnitStepJob : IJob
    {
        public MassFlowSimulationState? Owner { get; set; }
        public int StartIndex { get; set; }
        public int EndIndex { get; set; }
        public float Dt { get; set; }
        public MassNavigationGroupRuntime? NavGroupRuntime { get; set; }
        public float SepRadiusSq { get; set; }
        public float SepRadiusCm { get; set; }
        public float ArrivalRadiusCm { get; set; }
        public float ArrivalRadiusSq { get; set; }
        public float UnitTargetStopThresholdSq { get; set; }
        public int HashWidthMinusOne { get; set; }
        public int HashHeightMinusOne { get; set; }
        public float InvHashCell { get; set; }
        public int FlowObstacleNeighborRadiusCells { get; set; }
        public bool UseCandidateGating { get; set; }

        public void Execute()
        {
            Owner!.StepRange(
                StartIndex,
                EndIndex,
                Dt,
                NavGroupRuntime!,
                SepRadiusSq,
                SepRadiusCm,
                ArrivalRadiusCm,
                ArrivalRadiusSq,
                UnitTargetStopThresholdSq,
                HashWidthMinusOne,
                HashHeightMinusOne,
                InvHashCell,
                FlowObstacleNeighborRadiusCells,
                UseCandidateGating);
        }
    }

    private sealed class TeamRuntimeState
    {
        public TeamRuntimeState(
            int teamId,
            int unitCount,
            float spawnCenterX,
            float spawnCenterY,
            float directionX,
            float directionY,
            float tangentX,
            float tangentY)
        {
            TeamId = teamId;
            UnitCount = unitCount;
            SpawnCenterX = spawnCenterX;
            SpawnCenterY = spawnCenterY;
            DirectionX = directionX;
            DirectionY = directionY;
            TangentX = tangentX;
            TangentY = tangentY;
        }

        public TeamRuntimeState(int teamId)
        {
            TeamId = teamId;
        }

        public int TeamId { get; }
        public int UnitCount { get; set; }
        public float SpawnCenterX { get; }
        public float SpawnCenterY { get; }
        public float DirectionX { get; }
        public float DirectionY { get; }
        public float TangentX { get; }
        public float TangentY { get; }
        public float TargetX { get; set; }
        public float TargetY { get; set; }
    }

    private sealed class FlowRuntimeState
    {
        public FlowRuntimeState(int teamStateIndex, uint categoryMask, uint interactionMask, int gridCellCount)
        {
            TeamStateIndex = teamStateIndex;
            CategoryMask = categoryMask;
            InteractionMask = interactionMask;
            Flow = new float[gridCellCount * 2];
        }

        public int TeamStateIndex { get; }
        public uint CategoryMask { get; }
        public uint InteractionMask { get; }
        public float[] Flow { get; }
    }
}

using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Presentation.Components;
using Schedulers;

namespace MassNavWebParityMod.Runtime;

public sealed class MassNavWebParitySimState
{
    public const int FieldWidthCm = 10_000;
    public const int FieldHeightCm = 10_000;
    public const int CellCm = 100;
    public const int GridWidth = FieldWidthCm / CellCm;
    public const int GridHeight = FieldHeightCm / CellCm;
    public const float VisualMetersPerCm = 0.01f;

    private const int MaxObstacles = 64;
    private const int SeparationHashCellSizeCm = 100;
    private const int SeparationHashWidth = FieldWidthCm / SeparationHashCellSizeCm;
    private const int SeparationHashHeight = FieldHeightCm / SeparationHashCellSizeCm;
    private const int SeparationHashSearchRadius = 2;
    private const int HardResolveHashCellSizeCm = 50;
    private const int HardResolveHashWidth = FieldWidthCm / HardResolveHashCellSizeCm;
    private const int HardResolveHashHeight = FieldHeightCm / HardResolveHashCellSizeCm;
    private const int HardResolveHashSearchRadius = 1;
    private const float AgentBodyRadiusCm = 20f;
    private const float AgentBodyDiameterCm = AgentBodyRadiusCm * 2f;
    private const float AgentBodyDiameterSq = AgentBodyDiameterCm * AgentBodyDiameterCm;
    private const float HardResolveCandidateDistanceCm = 100f;
    private const float HardResolveCandidateDistanceSq = HardResolveCandidateDistanceCm * HardResolveCandidateDistanceCm;
    private const float TeamSlotSpacingCm = 90f;
    private const float MinPositionCm = 50f;
    private const float MaxPositionCm = 9_950f;

    private readonly Random _random = new();

    private readonly float[] _cost = new float[GridWidth * GridHeight];
    private readonly float[] _obsX = new float[MaxObstacles];
    private readonly float[] _obsY = new float[MaxObstacles];
    private readonly float[] _obsRadius = new float[MaxObstacles];
    private readonly float[] _obsR2 = new float[MaxObstacles];
    private readonly float[] _obsPR = new float[MaxObstacles];
    private readonly float[] _obsPR2 = new float[MaxObstacles];
    private readonly int[] _separationCellCounts = new int[SeparationHashWidth * SeparationHashHeight];
    private readonly int[] _separationCellOffsets = new int[SeparationHashWidth * SeparationHashHeight];
    private readonly int[] _separationCellCursor = new int[SeparationHashWidth * SeparationHashHeight];
    private readonly int[] _hardResolveCellCounts = new int[HardResolveHashWidth * HardResolveHashHeight];
    private readonly int[] _hardResolveCellOffsets = new int[HardResolveHashWidth * HardResolveHashHeight];
    private readonly int[] _hardResolveCellCursor = new int[HardResolveHashWidth * HardResolveHashHeight];

    private float[] _positionsCm = Array.Empty<float>();
    private float[] _velocitiesCm = Array.Empty<float>();
    private float[] _readPositionsCm = Array.Empty<float>();
    private float[] _readVelocitiesCm = Array.Empty<float>();
    private float[] _unitProgressAnchorCm = Array.Empty<float>();
    private float[] _unitSettledAnchorCm = Array.Empty<float>();
    private int[] _teams = Array.Empty<int>();
    private int[] _teamRuntimeIndices = Array.Empty<int>();
    private int[] _teamLocalIndices = Array.Empty<int>();
    private float[] _navMasses = Array.Empty<float>();
    private float[] _visualScales = Array.Empty<float>();
    private float[] _unitTargetsCm = Array.Empty<float>();
    private byte[] _hasUnitTarget = Array.Empty<byte>();
    private byte[] _selectedFlags = Array.Empty<byte>();
    private byte[] _hardResolveCandidates = Array.Empty<byte>();
    private byte[] _unitSettledFlags = Array.Empty<byte>();
    private byte[] _unitRetryCounts = Array.Empty<byte>();
    private float[] _unitStuckSeconds = Array.Empty<float>();
    private int[] _separationAgents = Array.Empty<int>();
    private int[] _hardResolveAgents = Array.Empty<int>();
    private readonly UnitStepJob[] _stepJobs = CreateStepJobs();
    private JobHandle[] _stepHandles = Array.Empty<JobHandle>();

    private readonly List<TeamRuntimeState> _teamStates = new();
    private readonly Dictionary<int, int> _teamStateIndexById = new();
    private TeamRelationship[] _teamRelationshipMatrix = Array.Empty<TeamRelationship>();
    private int _frameCount;
    private bool _useCandidateGating;

    public int UnitCount { get; private set; }
    public int ObstacleCount { get; private set; }
    public int SettledUnitCount { get; private set; }
    public MassNavArrivalTuning ArrivalTuning { get; } = new();
    public MassNavAvoidanceTuning AvoidanceTuning { get; } = new();

    public ReadOnlySpan<float> PositionsCm => _positionsCm.AsSpan(0, UnitCount * 2);
    public ReadOnlySpan<int> Teams => _teams.AsSpan(0, UnitCount);
    public ReadOnlySpan<byte> SelectedFlags => _selectedFlags.AsSpan(0, UnitCount);

    public float GetPositionX(int index) => _positionsCm[index << 1];
    public float GetPositionY(int index) => _positionsCm[(index << 1) + 1];
    public int GetTeam(int index) => _teams[index];
    public float GetNavMass(int index) => _navMasses[index];
    public float GetVisualScale(int index) => _visualScales[index];
    public bool IsSelected(int index) => _selectedFlags[index] != 0;
    public float GetObstacleX(int index) => _obsX[index];
    public float GetObstacleY(int index) => _obsY[index];
    public float GetObstacleRadius(int index) => _obsRadius[index];
    public bool IsObstaclePoint(float xCm, float yCm) => IsObstacle(xCm, yCm);

    public void Reset(ReadOnlySpan<int> teamIds, int unitsPerTeam)
    {
        int safeUnitsPerTeam = Math.Max(0, unitsPerTeam);
        int safeTeamCount = Math.Max(0, teamIds.Length);
        UnitCount = safeUnitsPerTeam * safeTeamCount;
        EnsureCapacity(UnitCount);
        InitializeTeams(teamIds, safeUnitsPerTeam);
        CacheDefaultObstacles();
        InitializeUnits();
        ComputeFlowFields();
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
        Vector2 resolved = ResolveNavigableTarget(targetCm.X, targetCm.Y, hintX, hintY, 60f);
        team.TargetX = resolved.X;
        team.TargetY = resolved.Y;
        ResetTeamArrivalState(teamId);
        ComputeFlowFields();
    }

    public void SetUnitTarget(int index, float xCm, float yCm, bool resetRecovery = false)
    {
        if ((uint)index >= (uint)UnitCount)
        {
            return;
        }

        int offset = index << 1;
        bool wasInactive = _hasUnitTarget[index] == 0;
        _unitTargetsCm[offset] = ClampPosition(xCm);
        _unitTargetsCm[offset + 1] = ClampPosition(yCm);
        _hasUnitTarget[index] = 1;
        if (resetRecovery || wasInactive)
        {
            ResetUnitArrivalState(index, clearRetryCount: true);
        }
    }

    public void ClearUnitTarget(int index)
    {
        if ((uint)index >= (uint)UnitCount)
        {
            return;
        }

        ResetUnitArrivalState(index, clearRetryCount: true);
        _hasUnitTarget[index] = 0;
    }

    public void SetSelectedFlags(MassNavAgentState agentState, ReadOnlySpan<Entity> selectedEntities)
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

    public Vector2 ResolveNavigableTarget(float xCm, float yCm, float hintX, float hintY, float extraClearanceCm = 0f)
    {
        float resolvedX = ClampPosition(xCm);
        float resolvedY = ClampPosition(yCm);
        float minDistancePadding = AgentBodyRadiusCm + MathF.Max(0f, extraClearanceCm);

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
                if (d2 > 0.0001f)
                {
                    float invD = FastInvSqrt(d2);
                    nx = dx * invD;
                    ny = dy * invD;
                }
                else
                {
                    float hintLenSq = hintX * hintX + hintY * hintY;
                    if (hintLenSq > 0.0001f)
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

                resolvedX = ClampPosition(_obsX[obstacleIndex] + nx * minDistance);
                resolvedY = ClampPosition(_obsY[obstacleIndex] + ny * minDistance);
                adjusted = true;
            }

            if (!adjusted)
            {
                break;
            }
        }

        return new Vector2(resolvedX, resolvedY);
    }

    public void Step(float dt, MassNavGroupRuntime navGroupRuntime, Action<double>? observeHardResolve = null)
    {
        if (UnitCount <= 0)
        {
            return;
        }

        RefreshTeamRelationshipMatrix();

        float clampedDt = Math.Clamp(dt, 0f, 0.05f);
        _frameCount++;
        int scalarCount = UnitCount * 2;
        Array.Copy(_positionsCm, _readPositionsCm, scalarCount);
        Array.Copy(_velocitiesCm, _readVelocitiesCm, scalarCount);
        _useCandidateGating = UnitCount >= 16_000;
        if (_useCandidateGating)
        {
            Array.Clear(_hardResolveCandidates, 0, UnitCount);
        }

        const float speed = 800f;
        const float sepRadiusCm = 200f;
        const float sepRadiusSq = sepRadiusCm * sepRadiusCm;
        const float invSepRadius = 1f / sepRadiusCm;
        const float arrivalRadiusCm = 1_200f;
        const float arrivalRadiusSq = arrivalRadiusCm * arrivalRadiusCm;
        const float unitTargetStopThresholdSq = 2_500f;

        BuildSeparationHash(_readPositionsCm);

        int hwm1 = SeparationHashWidth - 1;
        int hhm1 = SeparationHashHeight - 1;
        float invHashCell = 1f / SeparationHashCellSizeCm;

        if (World.SharedJobScheduler == null || UnitCount < 2048)
        {
            StepRange(0, UnitCount, clampedDt, navGroupRuntime, speed, sepRadiusSq, invSepRadius, arrivalRadiusCm, arrivalRadiusSq, unitTargetStopThresholdSq, hwm1, hhm1, invHashCell, SeparationHashSearchRadius, _useCandidateGating);
            long resolveStart = System.Diagnostics.Stopwatch.GetTimestamp();
            ResolveHardPenetration();
            observeHardResolve?.Invoke((System.Diagnostics.Stopwatch.GetTimestamp() - resolveStart) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
            UpdateSettledUnitCount();
            return;
        }

        EnsureJobHandleCapacity(_stepJobs.Length);
        int workerCount = Math.Min(_stepJobs.Length, UnitCount);
        int baseCount = UnitCount / workerCount;
        int remainder = UnitCount % workerCount;
        int startIndex = 0;
        for (int workerIndex = 0; workerIndex < workerCount; workerIndex++)
        {
            int length = baseCount + (workerIndex < remainder ? 1 : 0);
            var job = _stepJobs[workerIndex];
            job.Owner = this;
            job.StartIndex = startIndex;
            job.EndIndex = startIndex + length;
            job.Dt = clampedDt;
            job.NavGroupRuntime = navGroupRuntime;
            job.Speed = speed;
            job.SepRadiusSq = sepRadiusSq;
            job.InvSepRadius = invSepRadius;
            job.ArrivalRadiusCm = arrivalRadiusCm;
            job.ArrivalRadiusSq = arrivalRadiusSq;
            job.UnitTargetStopThresholdSq = unitTargetStopThresholdSq;
            job.HashWidthMinusOne = hwm1;
            job.HashHeightMinusOne = hhm1;
            job.InvHashCell = invHashCell;
            job.HashSearchRadius = SeparationHashSearchRadius;
            job.UseCandidateGating = _useCandidateGating;
            _stepHandles[workerIndex] = World.SharedJobScheduler!.Schedule(job);
            startIndex += length;
        }

        World.SharedJobScheduler!.Flush();
        for (int workerIndex = 0; workerIndex < workerCount; workerIndex++)
        {
            _stepHandles[workerIndex].Complete();
        }

        long hardResolveStart = System.Diagnostics.Stopwatch.GetTimestamp();
        ResolveHardPenetration();
        observeHardResolve?.Invoke((System.Diagnostics.Stopwatch.GetTimestamp() - hardResolveStart) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
        UpdateSettledUnitCount();
    }

    public void SyncEntities(World world, MassNavAgentState agentState)
    {
        int count = Math.Min(UnitCount, agentState.ControllableCount);
        for (int i = 0; i < count; i++)
        {
            Entity entity = agentState.ControllableAgents[i];
            if (!world.IsAlive(entity))
            {
                continue;
            }

            int i2 = i << 1;
            float xCm = _positionsCm[i2];
            float yCm = _positionsCm[i2 + 1];
            ref VisualTransform transform = ref world.Get<VisualTransform>(entity);
            transform.Position = new Vector3(xCm * VisualMetersPerCm, 0.25f, yCm * VisualMetersPerCm);

            Fix64Vec2 worldValue = Fix64Vec2.FromInt((int)MathF.Round(xCm), (int)MathF.Round(yCm));
            ref WorldPositionCm worldPosition = ref world.Get<WorldPositionCm>(entity);
            worldPosition.Value = worldValue;

            ref PreviousWorldPositionCm previousPosition = ref world.Get<PreviousWorldPositionCm>(entity);
            previousPosition.Value = worldValue;
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
            Array.Resize(ref _navMasses, unitCount);
            Array.Resize(ref _visualScales, unitCount);
            Array.Resize(ref _hasUnitTarget, unitCount);
            Array.Resize(ref _selectedFlags, unitCount);
            Array.Resize(ref _hardResolveCandidates, unitCount);
            Array.Resize(ref _unitSettledFlags, unitCount);
            Array.Resize(ref _unitRetryCounts, unitCount);
            Array.Resize(ref _unitStuckSeconds, unitCount);
            Array.Resize(ref _separationAgents, unitCount);
            Array.Resize(ref _hardResolveAgents, unitCount);
        }
    }

    private void InitializeTeams(ReadOnlySpan<int> teamIds, int unitsPerTeam)
    {
        _teamStates.Clear();
        _teamStateIndexById.Clear();
        if (teamIds.Length <= 0)
        {
            return;
        }

        const float centerX = FieldWidthCm * 0.5f;
        const float centerY = FieldHeightCm * 0.5f;
        const float orbitRadiusCm = 3_650f;
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
                60f);
            var state = new TeamRuntimeState(teamId, unitsPerTeam, spawnCenterX, spawnCenterY, dirX, dirY, tangentX, tangentY)
            {
                TargetX = target.X,
                TargetY = target.Y,
            };
            _teamStateIndexById[teamId] = _teamStates.Count;
            _teamStates.Add(state);
        }

        _teamRelationshipMatrix = new TeamRelationship[_teamStates.Count * _teamStates.Count];
    }

    private void InitializeUnits()
    {
        Array.Clear(_velocitiesCm, 0, UnitCount * 2);
        Array.Clear(_unitTargetsCm, 0, UnitCount * 2);
        Array.Clear(_hasUnitTarget, 0, UnitCount);
        Array.Clear(_selectedFlags, 0, UnitCount);
        Array.Clear(_unitProgressAnchorCm, 0, UnitCount * 2);
        Array.Clear(_unitSettledAnchorCm, 0, UnitCount * 2);
        Array.Clear(_unitSettledFlags, 0, UnitCount);
        Array.Clear(_unitRetryCounts, 0, UnitCount);
        Array.Clear(_unitStuckSeconds, 0, UnitCount);

        int unitIndex = 0;
        const float spacingCm = 46f;
        for (int teamStateIndex = 0; teamStateIndex < _teamStates.Count; teamStateIndex++)
        {
            TeamRuntimeState team = _teamStates[teamStateIndex];
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
                float jitterLateral = (_random.NextSingle() - 0.5f) * 12f;
                float jitterDepth = (_random.NextSingle() - 0.5f) * 12f;
                float xCm = team.SpawnCenterX + team.TangentX * (lateralOffset + jitterLateral) + team.DirectionX * (depthOffset + jitterDepth);
                float yCm = team.SpawnCenterY + team.TangentY * (lateralOffset + jitterLateral) + team.DirectionY * (depthOffset + jitterDepth);
                int i2 = unitIndex << 1;
                bool heavy = (localIndex % 7) == 0;
                _teams[unitIndex] = team.TeamId;
                _teamRuntimeIndices[unitIndex] = teamStateIndex;
                _teamLocalIndices[unitIndex] = localIndex;
                _navMasses[unitIndex] = heavy ? AvoidanceTuning.HeavyNavMass : AvoidanceTuning.LightNavMass;
                _visualScales[unitIndex] = heavy ? AvoidanceTuning.HeavyVisualScale : AvoidanceTuning.LightVisualScale;
                _positionsCm[i2] = ClampPosition(xCm);
                _positionsCm[i2 + 1] = ClampPosition(yCm);
                _unitProgressAnchorCm[i2] = _positionsCm[i2];
                _unitProgressAnchorCm[i2 + 1] = _positionsCm[i2 + 1];
                _unitSettledAnchorCm[i2] = _positionsCm[i2];
                _unitSettledAnchorCm[i2 + 1] = _positionsCm[i2 + 1];
            }
        }
    }

    private void CacheDefaultObstacles()
    {
        ObstacleCount = 5;
        CacheObstacle(0, 3_000f, 5_000f, 500f);
        CacheObstacle(1, 5_000f, 3_000f, 400f);
        CacheObstacle(2, 5_000f, 7_000f, 400f);
        CacheObstacle(3, 7_000f, 5_000f, 500f);
        CacheObstacle(4, 5_000f, 5_000f, 300f);
    }

    private void CacheObstacle(int index, float xCm, float yCm, float radiusCm)
    {
        _obsX[index] = xCm;
        _obsY[index] = yCm;
        _obsRadius[index] = radiusCm;
        _obsR2[index] = radiusCm * radiusCm;
        float pushRadius = radiusCm + 350f;
        _obsPR[index] = pushRadius;
        _obsPR2[index] = pushRadius * pushRadius;
    }

    private void ComputeFlowFields()
    {
        for (int y = 0; y < GridHeight; y++)
        {
            for (int x = 0; x < GridWidth; x++)
            {
                float wx = (x + 0.5f) * CellCm;
                float wy = (y + 0.5f) * CellCm;
                _cost[(y * GridWidth) + x] = IsObstacle(wx, wy) ? 99_999f : 1f;
            }
        }

        for (int i = 0; i < _teamStates.Count; i++)
        {
            TeamRuntimeState team = _teamStates[i];
            ComputeFlow(team.Flow, team.TargetX, team.TargetY);
        }
    }

    private void ComputeFlow(float[] flow, float targetX, float targetY)
    {
        for (int y = 0; y < GridHeight; y++)
        {
            for (int x = 0; x < GridWidth; x++)
            {
                int idx = (y * GridWidth) + x;
                int flowIndex = idx << 1;
                if (_cost[idx] > 9_999f)
                {
                    flow[flowIndex] = 0f;
                    flow[flowIndex + 1] = 0f;
                    continue;
                }

                float wx = (x + 0.5f) * CellCm;
                float wy = (y + 0.5f) * CellCm;
                float dx = targetX - wx;
                float dy = targetY - wy;
                float distSq = dx * dx + dy * dy;
                if (distSq < 1f)
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
                for (int offsetY = -4; offsetY <= 4; offsetY++)
                {
                    for (int offsetX = -4; offsetX <= 4; offsetX++)
                    {
                        if (offsetX == 0 && offsetY == 0)
                        {
                            continue;
                        }

                        int nx = x + offsetX;
                        int ny = y + offsetY;
                        if ((uint)nx >= (uint)GridWidth || (uint)ny >= (uint)GridHeight)
                        {
                            continue;
                        }

                        if (_cost[(ny * GridWidth) + nx] > 9_999f)
                        {
                            float ovx = -offsetX;
                            float ovy = -offsetY;
                            float obstacleDistSq = (ovx * ovx) + (ovy * ovy);
                            if (obstacleDistSq > 0.01f)
                            {
                                float invObstacleDist = FastInvSqrt(obstacleDistSq);
                                float obstacleDist = obstacleDistSq * invObstacleDist;
                                avoidX += (ovx * invObstacleDist) * (5f / (obstacleDist * obstacleDist));
                                avoidY += (ovy * invObstacleDist) * (5f / (obstacleDist * obstacleDist));
                            }
                        }
                    }
                }

                float flowX = dx + (avoidX * 1.5f);
                float flowY = dy + (avoidY * 1.5f);
                float flowLengthSq = flowX * flowX + flowY * flowY;
                if (flowLengthSq < 0.000001f)
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
            SeparationHashWidth,
            SeparationHashHeight,
            1f / SeparationHashCellSizeCm,
            _separationCellCounts,
            _separationCellOffsets,
            _separationCellCursor,
            _separationAgents);
    }

    private void BuildHardResolveHash(float[] positionsCm)
    {
        BuildBucketHash(
            positionsCm,
            HardResolveHashWidth,
            HardResolveHashHeight,
            1f / HardResolveHashCellSizeCm,
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
            int cell = GetHashCell(positionsCm, i, invHashCell, hashWidthMinusOne, hashHeightMinusOne, hashWidth);
            cellCounts[cell]++;
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
            int cell = GetHashCell(positionsCm, i, invHashCell, hashWidthMinusOne, hashHeightMinusOne, hashWidth);
            cellAgents[cellCursor[cell]++] = i;
        }
    }

    private void RefreshTeamRelationshipMatrix()
    {
        int teamCount = _teamStates.Count;
        if (teamCount <= 0)
        {
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
    }

    private static int GetHashCell(float[] positionsCm, int index, float invHashCell, int hashWidthMinusOne, int hashHeightMinusOne, int hashWidth)
    {
        int i2 = index << 1;
        int cellX = (int)(positionsCm[i2] * invHashCell);
        int cellY = (int)(positionsCm[i2 + 1] * invHashCell);
        cellX = cellX < 0 ? 0 : (cellX > hashWidthMinusOne ? hashWidthMinusOne : cellX);
        cellY = cellY < 0 ? 0 : (cellY > hashHeightMinusOne ? hashHeightMinusOne : cellY);
        return (cellY * hashWidth) + cellX;
    }

    private void StepRange(
        int startIndex,
        int endIndex,
        float clampedDt,
        MassNavGroupRuntime navGroupRuntime,
        float speed,
        float sepRadiusSq,
        float invSepRadius,
        float arrivalRadiusCm,
        float arrivalRadiusSq,
        float unitTargetStopThresholdSq,
        int hashWidthMinusOne,
        int hashHeightMinusOne,
        float invHashCell,
        int hashSearchRadius,
        bool useCandidateGating)
    {
        for (int i = startIndex; i < endIndex; i++)
        {
            int i2 = i << 1;
            float px = _readPositionsCm[i2];
            float py = _readPositionsCm[i2 + 1];
            int teamStateIndex = _teamRuntimeIndices[i];
            TeamRuntimeState team = _teamStates[teamStateIndex];
            bool inFormation = navGroupRuntime.HasGroup(i);

            float desiredX = 0f;
            float desiredY = 0f;
            float targetX = px;
            float targetY = py;
            float unitArrivalFactor = 1f;
            bool suppressTargetMotion = false;
            bool hasGoalTarget = false;

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

                    int gx = (int)(px / CellCm);
                    int gy = (int)(py / CellCm);
                    float avoidX = 0f;
                    float avoidY = 0f;
                    for (int oy = -2; oy <= 2; oy++)
                    {
                        int ny = gy + oy;
                        if ((uint)ny >= (uint)GridHeight)
                        {
                            continue;
                        }

                        int rowOffset = ny * GridWidth;
                        for (int ox = -2; ox <= 2; ox++)
                        {
                            if (ox == 0 && oy == 0)
                            {
                                continue;
                            }

                            int nx = gx + ox;
                            if ((uint)nx >= (uint)GridWidth)
                            {
                                continue;
                            }

                            if (_cost[rowOffset + nx] > 9_999f)
                            {
                                float obstacleDistanceSq = ox * ox + oy * oy;
                                float invObstacleDistance = FastInvSqrt(obstacleDistanceSq);
                                float invObstacleDistanceSq = invObstacleDistance * invObstacleDistance;
                                avoidX += (-ox * invObstacleDistance) * (4f * invObstacleDistanceSq);
                                avoidY += (-oy * invObstacleDistance) * (4f * invObstacleDistanceSq);
                            }
                        }
                    }

                    desiredX += avoidX * 1.2f;
                    desiredY += avoidY * 1.2f;
                    float flowLengthSq = desiredX * desiredX + desiredY * desiredY;
                    if (flowLengthSq > 0.000001f)
                    {
                        float invFlow = FastInvSqrt(flowLengthSq);
                        desiredX *= invFlow;
                        desiredY *= invFlow;
                    }
                }

                float targetDistance = MathF.Sqrt(targetDistSq);
                float arriveThreshold = inFormation ? 200f : 300f;
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
                int gx = (int)(px / CellCm);
                int gy = (int)(py / CellCm);
                float flowX = 0f;
                float flowY = 0f;
                if ((uint)gx < (uint)GridWidth && (uint)gy < (uint)GridHeight)
                {
                    int flowOffset = ((gy * GridWidth) + gx) << 1;
                    flowX = team.Flow[flowOffset];
                    flowY = team.Flow[flowOffset + 1];
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

                if (!suppressTargetMotion && slotDistSq > 0.0001f)
                {
                    float invSlot = FastInvSqrt(slotDistSq);
                    float slotDirX = toSlotX * invSlot;
                    float slotDirY = toSlotY * invSlot;
                    float slotBlend = slotDistSq < 4_000_000f ? 0.82f : 0.38f;
                    desiredX = (flowX * (1f - slotBlend)) + (slotDirX * slotBlend);
                    desiredY = (flowY * (1f - slotBlend)) + (slotDirY * slotBlend);
                    float desiredLengthSq = desiredX * desiredX + desiredY * desiredY;
                    if (desiredLengthSq > 0.000001f)
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
            float effectiveArrivalCm = inFormation ? 400f : arrivalRadiusCm;
            float effectiveArrivalSq = inFormation ? 160_000f : arrivalRadiusSq;
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

            int minY = Math.Max(0, cellY - hashSearchRadius);
            int maxY = Math.Min(hashHeightMinusOne, cellY + hashSearchRadius);
            int minX = Math.Max(0, cellX - hashSearchRadius);
            int maxX = Math.Min(hashWidthMinusOne, cellX + hashSearchRadius);

            for (int neighborY = minY; neighborY <= maxY; neighborY++)
            {
                int rowBase = neighborY * SeparationHashWidth;
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
                                int j2 = j << 1;
                            float dx = px - _readPositionsCm[j2];
                            float dy = py - _readPositionsCm[j2 + 1];
                            float d2 = dx * dx + dy * dy;
                            if (useCandidateGating && d2 < HardResolveCandidateDistanceSq)
                            {
                                _hardResolveCandidates[i] = 1;
                            }

                            if (d2 < sepRadiusSq && d2 > 0.0001f)
                            {
                                float invD = FastInvSqrt(d2);
                                float d = d2 * invD;
                                float force = 1f - (d * invSepRadius);
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
                float minDistance = _obsRadius[obstacleIndex] + HardResolveCandidateDistanceCm;
                if (useCandidateGating && d2 < minDistance * minDistance)
                {
                    _hardResolveCandidates[i] = 1;
                }

                if (d2 < _obsPR2[obstacleIndex] && d2 > 0.0001f)
                {
                    float invD = FastInvSqrt(d2);
                    float d = d2 * invD;
                    float pushRadius = _obsPR[obstacleIndex];
                    float pushStrength = (pushRadius - d) / pushRadius;
                    float pushForce = pushStrength * pushStrength * 8f;
                    obstaclePushX += dx * invD * pushForce;
                    obstaclePushY += dy * invD * pushForce;
                }
            }

            float separationScale = (inFormation ? 2f : 4f) * unitArrivalFactor;
            float desiredVelocityX = desiredX * speed * flowScale + separationX * separationScale + obstaclePushX * speed;
            float desiredVelocityY = desiredY * speed * flowScale + separationY * separationScale + obstaclePushY * speed;
            float desiredVelocitySq = desiredVelocityX * desiredVelocityX + desiredVelocityY * desiredVelocityY;
            if (desiredVelocitySq > 640_000f)
            {
                float scale = speed * FastInvSqrt(desiredVelocitySq);
                desiredVelocityX *= scale;
                desiredVelocityY *= scale;
            }

            float mix = MathF.Min(clampedDt * 5f, 1f);
            float velocityX = _readVelocitiesCm[i2] + ((desiredVelocityX - _readVelocitiesCm[i2]) * mix);
            float velocityY = _readVelocitiesCm[i2 + 1] + ((desiredVelocityY - _readVelocitiesCm[i2 + 1]) * mix);
            _velocitiesCm[i2] = velocityX;
            _velocitiesCm[i2 + 1] = velocityY;

            float nextX = px + velocityX * clampedDt;
            float nextY = py + velocityY * clampedDt;
            _positionsCm[i2] = ClampPosition(nextX);
            _positionsCm[i2 + 1] = ClampPosition(nextY);
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
        float offsetX = (col - colCenter) * TeamSlotSpacingCm;
        float offsetY = (row - rowCenter) * TeamSlotSpacingCm;
        Vector2 resolved = ResolveNavigableTarget(
            team.TargetX + offsetX,
            team.TargetY + offsetY,
            offsetX,
            offsetY,
            TeamSlotSpacingCm * 0.5f);
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
        MassNavPairAvoidancePolicy policy = ResolvePolicy(selfToOther, selfMass, otherMass);
        return policy switch
        {
            MassNavPairAvoidancePolicy.FriendlyCooperativeYield => Math.Clamp((selfMass / MathF.Max(0.001f, otherMass)) * AvoidanceTuning.FriendlyResponseScale, 0.35f, 2.75f),
            MassNavPairAvoidancePolicy.DominantPush => Math.Clamp((otherMass / MathF.Max(0.001f, selfMass)) * AvoidanceTuning.DominantPushResponseScale, 0.15f, 4.5f),
            _ => Math.Clamp((otherMass / MathF.Max(0.001f, selfMass)) * AvoidanceTuning.NonFriendlyResponseScale, 0.25f, 3.25f),
        };
    }

    private TeamRelationship GetTeamRelationship(int sourceTeamStateIndex, int targetTeamStateIndex)
    {
        int teamCount = _teamStates.Count;
        if ((uint)sourceTeamStateIndex >= (uint)teamCount || (uint)targetTeamStateIndex >= (uint)teamCount)
        {
            return TeamRelationship.Hostile;
        }

        return _teamRelationshipMatrix[(sourceTeamStateIndex * teamCount) + targetTeamStateIndex];
    }

    private MassNavPairAvoidancePolicy ResolveBidirectionalPolicy(int selfTeamStateIndex, int otherTeamStateIndex, int selfUnitIndex, int otherUnitIndex)
    {
        TeamRelationship selfToOther = GetTeamRelationship(selfTeamStateIndex, otherTeamStateIndex);
        TeamRelationship otherToSelf = GetTeamRelationship(otherTeamStateIndex, selfTeamStateIndex);
        if (selfToOther == TeamRelationship.Friendly && otherToSelf == TeamRelationship.Friendly)
        {
            return MassNavPairAvoidancePolicy.FriendlyCooperativeYield;
        }

        float selfMass = _navMasses[selfUnitIndex];
        float otherMass = _navMasses[otherUnitIndex];
        float minMass = MathF.Max(0.001f, MathF.Min(selfMass, otherMass));
        float maxMass = MathF.Max(selfMass, otherMass);
        return (maxMass / minMass) >= AvoidanceTuning.DominantMassRatio
            ? MassNavPairAvoidancePolicy.DominantPush
            : MassNavPairAvoidancePolicy.NonFriendlyBlocker;
    }

    private MassNavPairAvoidancePolicy ResolvePolicy(TeamRelationship relationship, float selfMass, float otherMass)
    {
        if (relationship == TeamRelationship.Friendly)
        {
            return MassNavPairAvoidancePolicy.FriendlyCooperativeYield;
        }

        float minMass = MathF.Max(0.001f, MathF.Min(selfMass, otherMass));
        float maxMass = MathF.Max(selfMass, otherMass);
        return (maxMass / minMass) >= AvoidanceTuning.DominantMassRatio
            ? MassNavPairAvoidancePolicy.DominantPush
            : MassNavPairAvoidancePolicy.NonFriendlyBlocker;
    }

    private float ComputeCorrectionShare(MassNavPairAvoidancePolicy policy, float selfMass, float otherMass)
    {
        float safeSelf = MathF.Max(0.001f, selfMass);
        float safeOther = MathF.Max(0.001f, otherMass);
        return policy switch
        {
            MassNavPairAvoidancePolicy.FriendlyCooperativeYield => Math.Clamp(safeSelf / (safeSelf + safeOther), 0.18f, 0.82f),
            MassNavPairAvoidancePolicy.DominantPush => Math.Clamp((safeOther * 1.8f) / (safeSelf + (safeOther * 1.8f)), 0.05f, 0.95f),
            _ => Math.Clamp((safeOther * 1.2f) / (safeSelf + (safeOther * 1.2f)), 0.08f, 0.92f),
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
        float invHashCell = 1f / HardResolveHashCellSizeCm;
        int hwm1 = HardResolveHashWidth - 1;
        int hhm1 = HardResolveHashHeight - 1;

        for (int i = 0; i < UnitCount; i++)
        {
            if (_useCandidateGating && _hardResolveCandidates[i] == 0)
            {
                continue;
            }

            int i2 = i << 1;
            float px = _positionsCm[i2];
            float py = _positionsCm[i2 + 1];
            int cellX = (int)(px * invHashCell);
            int cellY = (int)(py * invHashCell);
            cellX = cellX < 0 ? 0 : (cellX > hwm1 ? hwm1 : cellX);
            cellY = cellY < 0 ? 0 : (cellY > hhm1 ? hhm1 : cellY);

            int minY = Math.Max(0, cellY - HardResolveHashSearchRadius);
            int maxY = Math.Min(hhm1, cellY + HardResolveHashSearchRadius);
            int minX = Math.Max(0, cellX - HardResolveHashSearchRadius);
            int maxX = Math.Min(hwm1, cellX + HardResolveHashSearchRadius);

            for (int neighborY = minY; neighborY <= maxY; neighborY++)
            {
                int rowBase = neighborY * HardResolveHashWidth;
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
        if (d2 >= AgentBodyDiameterSq)
        {
            return;
        }

        float nx;
        float ny;
        float overlap;
        if (d2 > 0.0001f)
        {
            float invD = FastInvSqrt(d2);
            float distance = d2 * invD;
            nx = dx * invD;
            ny = dy * invD;
            overlap = AgentBodyDiameterCm - distance;
        }
        else
        {
            float angle = ((i * 73856093) ^ (j * 19349663)) & 1023;
            float radians = angle * (MathF.PI * 2f / 1024f);
            nx = MathF.Cos(radians);
            ny = MathF.Sin(radians);
            overlap = AgentBodyDiameterCm;
        }

        MassNavPairAvoidancePolicy policy = ResolveBidirectionalPolicy(_teamRuntimeIndices[i], _teamRuntimeIndices[j], i, j);
        float shareI = ComputeCorrectionShare(policy, _navMasses[i], _navMasses[j]);
        float shareJ = 1f - shareI;
        float correctionI = overlap * shareI;
        float correctionJ = overlap * shareJ;
        _positionsCm[i2] = ClampPosition(_positionsCm[i2] + nx * correctionI);
        _positionsCm[i2 + 1] = ClampPosition(_positionsCm[i2 + 1] + ny * correctionI);
        _positionsCm[j2] = ClampPosition(_positionsCm[j2] - nx * correctionJ);
        _positionsCm[j2 + 1] = ClampPosition(_positionsCm[j2 + 1] - ny * correctionJ);

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
            for (int obstacleIndex = 0; obstacleIndex < ObstacleCount; obstacleIndex++)
            {
                float dx = px - _obsX[obstacleIndex];
                float dy = py - _obsY[obstacleIndex];
                float minDistance = _obsRadius[obstacleIndex] + AgentBodyRadiusCm;
                float d2 = dx * dx + dy * dy;
                if (d2 >= minDistance * minDistance)
                {
                    continue;
                }

                float nx;
                float ny;
                if (d2 > 0.0001f)
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

                _positionsCm[i2] = ClampPosition(_obsX[obstacleIndex] + nx * minDistance);
                _positionsCm[i2 + 1] = ClampPosition(_obsY[obstacleIndex] + ny * minDistance);
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

    private void EnsureJobHandleCapacity(int required)
    {
        if (_stepHandles.Length < required)
        {
            Array.Resize(ref _stepHandles, required);
        }
    }

    private static UnitStepJob[] CreateStepJobs()
    {
        int count = Math.Max(1, Environment.ProcessorCount);
        var jobs = new UnitStepJob[count];
        for (int i = 0; i < count; i++)
        {
            jobs[i] = new UnitStepJob();
        }

        return jobs;
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

    private static float FastInvSqrt(float value)
    {
        return value < 0.00000001f ? 0f : 1f / MathF.Sqrt(value);
    }

    private static float ClampPosition(float value)
    {
        return Math.Clamp(value, MinPositionCm, MaxPositionCm);
    }

    private sealed class UnitStepJob : IJob
    {
        public MassNavWebParitySimState? Owner { get; set; }
        public int StartIndex { get; set; }
        public int EndIndex { get; set; }
        public float Dt { get; set; }
        public MassNavGroupRuntime? NavGroupRuntime { get; set; }
        public float Speed { get; set; }
        public float SepRadiusSq { get; set; }
        public float InvSepRadius { get; set; }
        public float ArrivalRadiusCm { get; set; }
        public float ArrivalRadiusSq { get; set; }
        public float UnitTargetStopThresholdSq { get; set; }
        public int HashWidthMinusOne { get; set; }
        public int HashHeightMinusOne { get; set; }
        public float InvHashCell { get; set; }
        public int HashSearchRadius { get; set; }
        public bool UseCandidateGating { get; set; }

        public void Execute()
        {
            Owner!.StepRange(
                StartIndex,
                EndIndex,
                Dt,
                NavGroupRuntime!,
                Speed,
                SepRadiusSq,
                InvSepRadius,
                ArrivalRadiusCm,
                ArrivalRadiusSq,
                UnitTargetStopThresholdSq,
                HashWidthMinusOne,
                HashHeightMinusOne,
                InvHashCell,
                HashSearchRadius,
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
            Flow = new float[GridWidth * GridHeight * 2];
        }

        public int TeamId { get; }
        public int UnitCount { get; }
        public float SpawnCenterX { get; }
        public float SpawnCenterY { get; }
        public float DirectionX { get; }
        public float DirectionY { get; }
        public float TangentX { get; }
        public float TangentY { get; }
        public float TargetX { get; set; }
        public float TargetY { get; set; }
        public float[] Flow { get; }
    }
}

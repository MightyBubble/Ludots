using System;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Components;
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
    private const float HashCellSizeCm = 250f;
    private const int HashWidth = (int)(FieldWidthCm / HashCellSizeCm);
    private const int HashHeight = (int)(FieldHeightCm / HashCellSizeCm);
    private const float AgentBodyRadiusCm = 20f;
    private const float AgentBodyDiameterCm = AgentBodyRadiusCm * 2f;
    private const float AgentBodyDiameterSq = AgentBodyDiameterCm * AgentBodyDiameterCm;
    private const float MinPositionCm = 50f;
    private const float MaxPositionCm = 9_950f;

    private readonly Random _random = new();

    private readonly float[] _flow0 = new float[GridWidth * GridHeight * 2];
    private readonly float[] _flow1 = new float[GridWidth * GridHeight * 2];
    private readonly float[] _cost = new float[GridWidth * GridHeight];
    private readonly float[] _obsX = new float[MaxObstacles];
    private readonly float[] _obsY = new float[MaxObstacles];
    private readonly float[] _obsRadius = new float[MaxObstacles];
    private readonly float[] _obsR2 = new float[MaxObstacles];
    private readonly float[] _obsPR = new float[MaxObstacles];
    private readonly float[] _obsPR2 = new float[MaxObstacles];
    private readonly int[] _hashHead = new int[HashWidth * HashHeight];

    private float[] _positionsCm = Array.Empty<float>();
    private float[] _velocitiesCm = Array.Empty<float>();
    private float[] _readPositionsCm = Array.Empty<float>();
    private float[] _readVelocitiesCm = Array.Empty<float>();
    private byte[] _teams = Array.Empty<byte>();
    private float[] _unitTargetsCm = Array.Empty<float>();
    private byte[] _hasUnitTarget = Array.Empty<byte>();
    private byte[] _selectedFlags = Array.Empty<byte>();
    private int[] _hashNext = Array.Empty<int>();
    private readonly UnitStepJob[] _stepJobs = CreateStepJobs();
    private JobHandle[] _stepHandles = Array.Empty<JobHandle>();

    private readonly float[] _teamTargetX = new float[2];
    private readonly float[] _teamTargetY = new float[2];
    private int _frameCount;

    public int UnitCount { get; private set; }
    public int ObstacleCount { get; private set; }

    public ReadOnlySpan<float> PositionsCm => _positionsCm.AsSpan(0, UnitCount * 2);
    public ReadOnlySpan<byte> Teams => _teams.AsSpan(0, UnitCount);
    public ReadOnlySpan<byte> SelectedFlags => _selectedFlags.AsSpan(0, UnitCount);

    public float Team0TargetX => _teamTargetX[0];
    public float Team0TargetY => _teamTargetY[0];
    public float Team1TargetX => _teamTargetX[1];
    public float Team1TargetY => _teamTargetY[1];

    public float GetPositionX(int index) => _positionsCm[index << 1];
    public float GetPositionY(int index) => _positionsCm[(index << 1) + 1];
    public byte GetTeam(int index) => _teams[index];
    public bool IsSelected(int index) => _selectedFlags[index] != 0;
    public float GetObstacleX(int index) => _obsX[index];
    public float GetObstacleY(int index) => _obsY[index];
    public float GetObstacleRadius(int index) => _obsRadius[index];

    public void Reset(int totalUnits)
    {
        UnitCount = Math.Max(0, totalUnits);
        EnsureCapacity(UnitCount);
        InitializeTargets();
        CacheDefaultObstacles();
        InitializeUnits();
        ComputeFlowFields();
        _frameCount = 0;
    }

    public void SetTeamTarget(int teamId, Vector2 targetCm)
    {
        int team = teamId <= 0 ? 0 : 1;
        _teamTargetX[team] = ClampPosition(targetCm.X);
        _teamTargetY[team] = ClampPosition(targetCm.Y);
        ComputeFlowFields();
    }

    public void SetUnitTarget(int index, float xCm, float yCm)
    {
        if ((uint)index >= (uint)UnitCount)
        {
            return;
        }

        int offset = index << 1;
        _unitTargetsCm[offset] = ClampPosition(xCm);
        _unitTargetsCm[offset + 1] = ClampPosition(yCm);
        _hasUnitTarget[index] = 1;
    }

    public void ClearUnitTarget(int index)
    {
        if ((uint)index >= (uint)UnitCount)
        {
            return;
        }

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

    public void Step(float dt, MassNavFormationRuntime formationRuntime, Action<double>? observeHardResolve = null)
    {
        if (UnitCount <= 0)
        {
            return;
        }

        float clampedDt = Math.Clamp(dt, 0f, 0.05f);
        _frameCount++;
        int scalarCount = UnitCount * 2;
        Array.Copy(_positionsCm, _readPositionsCm, scalarCount);
        Array.Copy(_velocitiesCm, _readVelocitiesCm, scalarCount);
        BuildSpatialHash(_readPositionsCm);

        const float speed = 800f;
        const float sepRadiusCm = 200f;
        const float sepRadiusSq = sepRadiusCm * sepRadiusCm;
        const float invSepRadius = 1f / sepRadiusCm;
        const float arrivalRadiusCm = 1_200f;
        const float arrivalRadiusSq = arrivalRadiusCm * arrivalRadiusCm;
        const float unitTargetStopThresholdSq = 2_500f;

        float tx0 = _teamTargetX[0];
        float ty0 = _teamTargetY[0];
        float tx1 = _teamTargetX[1];
        float ty1 = _teamTargetY[1];
        int hwm1 = HashWidth - 1;
        int hhm1 = HashHeight - 1;
        float invHashCell = 1f / HashCellSizeCm;

        if (World.SharedJobScheduler == null || UnitCount < 2048)
        {
            StepRange(0, UnitCount, clampedDt, formationRuntime, speed, sepRadiusSq, invSepRadius, arrivalRadiusCm, arrivalRadiusSq, unitTargetStopThresholdSq, tx0, ty0, tx1, ty1, hwm1, hhm1, invHashCell);
            long resolveStart = System.Diagnostics.Stopwatch.GetTimestamp();
            ResolveHardPenetration();
            observeHardResolve?.Invoke((System.Diagnostics.Stopwatch.GetTimestamp() - resolveStart) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
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
            job.FormationRuntime = formationRuntime;
            job.Speed = speed;
            job.SepRadiusSq = sepRadiusSq;
            job.InvSepRadius = invSepRadius;
            job.ArrivalRadiusCm = arrivalRadiusCm;
            job.ArrivalRadiusSq = arrivalRadiusSq;
            job.UnitTargetStopThresholdSq = unitTargetStopThresholdSq;
            job.Team0TargetX = tx0;
            job.Team0TargetY = ty0;
            job.Team1TargetX = tx1;
            job.Team1TargetY = ty1;
            job.HashWidthMinusOne = hwm1;
            job.HashHeightMinusOne = hhm1;
            job.InvHashCell = invHashCell;
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
            Array.Resize(ref _unitTargetsCm, vectorLength);
        }

        if (_teams.Length < unitCount)
        {
            Array.Resize(ref _teams, unitCount);
            Array.Resize(ref _hasUnitTarget, unitCount);
            Array.Resize(ref _selectedFlags, unitCount);
            Array.Resize(ref _hashNext, unitCount);
        }
    }

    private void InitializeTargets()
    {
        _teamTargetX[0] = 9_000f;
        _teamTargetY[0] = 5_000f;
        _teamTargetX[1] = 1_000f;
        _teamTargetY[1] = 5_000f;
    }

    private void InitializeUnits()
    {
        Array.Clear(_velocitiesCm, 0, UnitCount * 2);
        Array.Clear(_unitTargetsCm, 0, UnitCount * 2);
        Array.Clear(_hasUnitTarget, 0, UnitCount);
        Array.Clear(_selectedFlags, 0, UnitCount);

        int half = UnitCount >> 1;
        for (int i = 0; i < UnitCount; i++)
        {
            bool team0 = i < half;
            _teams[i] = team0 ? (byte)0 : (byte)1;
            int i2 = i << 1;
            if (team0)
            {
                _positionsCm[i2] = 500f + (_random.NextSingle() * 1_500f);
                _positionsCm[i2 + 1] = 1_000f + (_random.NextSingle() * 8_000f);
            }
            else
            {
                _positionsCm[i2] = 8_000f + (_random.NextSingle() * 1_500f);
                _positionsCm[i2 + 1] = 1_000f + (_random.NextSingle() * 8_000f);
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

        ComputeFlow(_flow0, _teamTargetX[0], _teamTargetY[0]);
        ComputeFlow(_flow1, _teamTargetX[1], _teamTargetY[1]);
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

    private void BuildSpatialHash(float[] positionsCm)
    {
        Array.Fill(_hashHead, -1);
        float invCell = 1f / HashCellSizeCm;
        int hwm1 = HashWidth - 1;
        int hhm1 = HashHeight - 1;
        for (int i = 0; i < UnitCount; i++)
        {
            int i2 = i << 1;
            int cellX = (int)(positionsCm[i2] * invCell);
            int cellY = (int)(positionsCm[i2 + 1] * invCell);
            cellX = cellX < 0 ? 0 : (cellX > hwm1 ? hwm1 : cellX);
            cellY = cellY < 0 ? 0 : (cellY > hhm1 ? hhm1 : cellY);
            int cell = (cellY * HashWidth) + cellX;
            _hashNext[i] = _hashHead[cell];
            _hashHead[cell] = i;
        }
    }

    private void StepRange(
        int startIndex,
        int endIndex,
        float clampedDt,
        MassNavFormationRuntime formationRuntime,
        float speed,
        float sepRadiusSq,
        float invSepRadius,
        float arrivalRadiusCm,
        float arrivalRadiusSq,
        float unitTargetStopThresholdSq,
        float tx0,
        float ty0,
        float tx1,
        float ty1,
        int hashWidthMinusOne,
        int hashHeightMinusOne,
        float invHashCell)
    {
        for (int i = startIndex; i < endIndex; i++)
        {
            int i2 = i << 1;
            float px = _readPositionsCm[i2];
            float py = _readPositionsCm[i2 + 1];
            byte team = _teams[i];
            bool inFormation = formationRuntime.IsInFormation(i);

            float desiredX = 0f;
            float desiredY = 0f;
            float targetX;
            float targetY;
            float unitArrivalFactor = 1f;

            if (_hasUnitTarget[i] != 0)
            {
                targetX = _unitTargetsCm[i2];
                targetY = _unitTargetsCm[i2 + 1];
                float toTargetX = targetX - px;
                float toTargetY = targetY - py;
                float targetDistSq = toTargetX * toTargetX + toTargetY * toTargetY;
                if (targetDistSq > unitTargetStopThresholdSq)
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
            }
            else
            {
                int gx = (int)(px / CellCm);
                int gy = (int)(py / CellCm);
                if ((uint)gx < (uint)GridWidth && (uint)gy < (uint)GridHeight)
                {
                    int flowOffset = ((gy * GridWidth) + gx) << 1;
                    if (team == 0)
                    {
                        desiredX = _flow0[flowOffset];
                        desiredY = _flow0[flowOffset + 1];
                    }
                    else
                    {
                        desiredX = _flow1[flowOffset];
                        desiredY = _flow1[flowOffset + 1];
                    }
                }

                targetX = team == 0 ? tx0 : tx1;
                targetY = team == 0 ? ty0 : ty1;
            }

            float directTargetX = targetX - px;
            float directTargetY = targetY - py;
            float directTargetSq = directTargetX * directTargetX + directTargetY * directTargetY;
            float flowScale = 1f;
            float effectiveArrivalCm = inFormation ? 400f : arrivalRadiusCm;
            float effectiveArrivalSq = inFormation ? 160_000f : arrivalRadiusSq;
            if (directTargetSq < effectiveArrivalSq)
            {
                flowScale = MathF.Sqrt(directTargetSq) / effectiveArrivalCm;
            }

            float separationX = 0f;
            float separationY = 0f;
            int cellX = (int)(px * invHashCell);
            int cellY = (int)(py * invHashCell);
            cellX = cellX < 0 ? 0 : (cellX > hashWidthMinusOne ? hashWidthMinusOne : cellX);
            cellY = cellY < 0 ? 0 : (cellY > hashHeightMinusOne ? hashHeightMinusOne : cellY);

            int minY = cellY > 0 ? cellY - 1 : 0;
            int maxY = cellY < hashHeightMinusOne ? cellY + 1 : hashHeightMinusOne;
            int minX = cellX > 0 ? cellX - 1 : 0;
            int maxX = cellX < hashWidthMinusOne ? cellX + 1 : hashWidthMinusOne;

            for (int neighborY = minY; neighborY <= maxY; neighborY++)
            {
                int rowBase = neighborY * HashWidth;
                for (int neighborX = minX; neighborX <= maxX; neighborX++)
                {
                    int j = _hashHead[rowBase + neighborX];
                    while (j >= 0)
                    {
                        if (j != i)
                        {
                            int j2 = j << 1;
                            float dx = px - _readPositionsCm[j2];
                            float dy = py - _readPositionsCm[j2 + 1];
                            float d2 = dx * dx + dy * dy;
                            if (d2 < sepRadiusSq && d2 > 0.0001f)
                            {
                                float invD = FastInvSqrt(d2);
                                float d = d2 * invD;
                                float force = 1f - (d * invSepRadius);
                                separationX += dx * invD * force;
                                separationY += dy * invD * force;
                            }
                        }

                        j = _hashNext[j];
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

    private void ResolveHardPenetration()
    {
        if (UnitCount <= 1)
        {
            ResolveObstaclePenetration();
            return;
        }

        BuildSpatialHash(_positionsCm);
        float invHashCell = 1f / HashCellSizeCm;
        int hwm1 = HashWidth - 1;
        int hhm1 = HashHeight - 1;

        for (int i = 0; i < UnitCount; i++)
        {
            int i2 = i << 1;
            float px = _positionsCm[i2];
            float py = _positionsCm[i2 + 1];
            int cellX = (int)(px * invHashCell);
            int cellY = (int)(py * invHashCell);
            cellX = cellX < 0 ? 0 : (cellX > hwm1 ? hwm1 : cellX);
            cellY = cellY < 0 ? 0 : (cellY > hhm1 ? hhm1 : cellY);

            int minY = cellY > 0 ? cellY - 1 : 0;
            int maxY = cellY < hhm1 ? cellY + 1 : hhm1;
            int minX = cellX > 0 ? cellX - 1 : 0;
            int maxX = cellX < hwm1 ? cellX + 1 : hwm1;

            for (int neighborY = minY; neighborY <= maxY; neighborY++)
            {
                int rowBase = neighborY * HashWidth;
                for (int neighborX = minX; neighborX <= maxX; neighborX++)
                {
                    int j = _hashHead[rowBase + neighborX];
                    while (j >= 0)
                    {
                        if (j > i)
                        {
                            SeparateAgents(i, j);
                        }

                        j = _hashNext[j];
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

        float correction = overlap * 0.5f;
        _positionsCm[i2] = ClampPosition(_positionsCm[i2] + nx * correction);
        _positionsCm[i2 + 1] = ClampPosition(_positionsCm[i2 + 1] + ny * correction);
        _positionsCm[j2] = ClampPosition(_positionsCm[j2] - nx * correction);
        _positionsCm[j2 + 1] = ClampPosition(_positionsCm[j2 + 1] - ny * correction);

        float relVelX = _velocitiesCm[i2] - _velocitiesCm[j2];
        float relVelY = _velocitiesCm[i2 + 1] - _velocitiesCm[j2 + 1];
        float closingSpeed = relVelX * nx + relVelY * ny;
        if (closingSpeed < 0f)
        {
            float impulse = closingSpeed * 0.5f;
            _velocitiesCm[i2] -= nx * impulse;
            _velocitiesCm[i2 + 1] -= ny * impulse;
            _velocitiesCm[j2] += nx * impulse;
            _velocitiesCm[j2 + 1] += ny * impulse;
        }
    }

    private void ResolveObstaclePenetration()
    {
        for (int i = 0; i < UnitCount; i++)
        {
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
        public MassNavFormationRuntime? FormationRuntime { get; set; }
        public float Speed { get; set; }
        public float SepRadiusSq { get; set; }
        public float InvSepRadius { get; set; }
        public float ArrivalRadiusCm { get; set; }
        public float ArrivalRadiusSq { get; set; }
        public float UnitTargetStopThresholdSq { get; set; }
        public float Team0TargetX { get; set; }
        public float Team0TargetY { get; set; }
        public float Team1TargetX { get; set; }
        public float Team1TargetY { get; set; }
        public int HashWidthMinusOne { get; set; }
        public int HashHeightMinusOne { get; set; }
        public float InvHashCell { get; set; }

        public void Execute()
        {
            Owner!.StepRange(
                StartIndex,
                EndIndex,
                Dt,
                FormationRuntime!,
                Speed,
                SepRadiusSq,
                InvSepRadius,
                ArrivalRadiusCm,
                ArrivalRadiusSq,
                UnitTargetStopThresholdSq,
                Team0TargetX,
                Team0TargetY,
                Team1TargetX,
                Team1TargetY,
                HashWidthMinusOne,
                HashHeightMinusOne,
                InvHashCell);
        }
    }
}

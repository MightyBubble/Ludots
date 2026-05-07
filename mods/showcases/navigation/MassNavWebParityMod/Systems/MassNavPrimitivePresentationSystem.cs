using System.Diagnostics;
using System.Numerics;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Scripting;
using Ludots.Core.Presentation.Camera;
using MassNavWebParityMod.Runtime;

namespace MassNavWebParityMod.Systems;

internal sealed class MassNavPrimitivePresentationSystem : ISystem<float>
{
    private const float StrategicAtlasScale = 0.0025f;
    private const float StrategicAtlasYOffset = 0.04f;
    private const float TacticalGridStepCm = 1000f;
    private const float TacticalMajorGridStepCm = 5000f;
    private const float TacticalGridThickness = 0.06f;
    private const float TacticalBoundaryThickness = 0.22f;
    private const float WorldInspectGridHalfExtentCm = 20000f;

    private readonly GameEngine _engine;
    private readonly MassNavSimulationRuntime _simulation;
    private int _sphereMeshId;
    private int _cubeMeshId;

    public MassNavPrimitivePresentationSystem(GameEngine engine, MassNavSimulationRuntime simulation, MeshAssetRegistry meshes)
    {
        _engine = engine;
        _simulation = simulation;
        _sphereMeshId = meshes.GetId(WellKnownMeshKeys.Sphere);
        _cubeMeshId = meshes.GetId(WellKnownMeshKeys.Cube);
    }

    public void Initialize()
    {
        var registry = _engine.GetService(CoreServiceKeys.PresentationMeshAssetRegistry);
        _sphereMeshId = registry?.GetId(WellKnownMeshKeys.Sphere) ?? _sphereMeshId;
        _cubeMeshId = registry?.GetId(WellKnownMeshKeys.Cube) ?? _cubeMeshId;
    }

    public void BeforeUpdate(in float dt) { }
    public void AfterUpdate(in float dt) { }
    public void Dispose() { }

    public void Update(in float dt)
    {
        if (!MassNavWebParityIds.IsCurrentPlaygroundMap(_engine))
        {
            return;
        }

        _simulation.ObservePrimitiveTick();

        if (_engine.GetService(CoreServiceKeys.PresentationPrimitiveDrawBuffer) is not PrimitiveDrawBuffer draw)
        {
            return;
        }

        long start = Stopwatch.GetTimestamp();
        bool strategicWorldView = MassNavWebParityRuntime.IsStrategicWorldCameraActive(_engine);
        if (strategicWorldView)
        {
            EmitStrategicWorldAtlas(draw);
            _simulation.WebParity.SyncCullStates(
                _engine.World,
                _simulation.AgentState,
                float.MaxValue,
                float.MinValue,
                float.MaxValue,
                float.MinValue);
            _simulation.ObservePrimitiveCoverage(0, 0, 0, draw.DroppedSinceClear);
            _simulation.ObservePrimitiveEmit((Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency);
            return;
        }

        EmitWorldInspectGrid(draw);
        EmitFlowWorkAreaGuides(draw);
        ResolveViewportBounds(out float minX, out float maxX, out float minY, out float maxY);
        float localMinX = _simulation.ToLocalXCm(minX);
        float localMaxX = _simulation.ToLocalXCm(maxX);
        float localMinY = _simulation.ToLocalYCm(minY);
        float localMaxY = _simulation.ToLocalYCm(maxY);
        _simulation.WebParity.SyncCullStates(_engine.World, _simulation.AgentState, localMinX, localMaxX, localMinY, localMaxY);
        ReadOnlySpan<float> positions = _simulation.WebParity.PositionsCm;
        ReadOnlySpan<int> teams = _simulation.WebParity.Teams;
        ReadOnlySpan<byte> selectedFlags = _simulation.WebParity.SelectedFlags;
        int unitCount = _simulation.WebParity.UnitCount;
        int crowdInViewCount = 0;
        int crowdSubmittedCount = 0;
        for (int i = 0; i < unitCount; i++)
        {
            int offset = i << 1;
            float localXCm = positions[offset];
            float localYCm = positions[offset + 1];
            if (localXCm < localMinX || localXCm > localMaxX || localYCm < localMinY || localYCm > localMaxY)
            {
                continue;
            }

            crowdInViewCount++;
            float xCm = _simulation.ToWorldXCm(localXCm);
            float yCm = _simulation.ToWorldYCm(localYCm);
            float x = xCm * MassNavWebParitySimState.VisualMetersPerCm;
            float z = yCm * MassNavWebParitySimState.VisualMetersPerCm;
            bool selected = selectedFlags[i] != 0;
            float visualScale = _simulation.WebParity.GetVisualScale(i);
            Vector4 color;
            if (selected)
            {
                color = new Vector4(0.40f, 1.0f, 0.20f, 0.95f);
            }
            else
            {
                color = ResolveTeamColor(teams[i]);
            }
            if (draw.TryAdd(new PrimitiveDrawItem
            {
                MeshAssetId = _sphereMeshId,
                Position = new Vector3(x, 0.18f, z),
                Scale = selected
                    ? new Vector3(visualScale + 0.06f, visualScale + 0.06f, visualScale + 0.06f)
                    : new Vector3(visualScale, visualScale, visualScale),
                Color = color
            }))
            {
                crowdSubmittedCount++;
            }
        }

        int obstacleSubmittedCount = 0;
        for (int i = 0; i < _simulation.WebParity.ObstacleCount; i++)
        {
            float localXCm = _simulation.WebParity.GetObstacleX(i);
            float localYCm = _simulation.WebParity.GetObstacleY(i);
            float radiusCm = _simulation.WebParity.GetObstacleRadius(i);
            if (localXCm + radiusCm < localMinX || localXCm - radiusCm > localMaxX || localYCm + radiusCm < localMinY || localYCm - radiusCm > localMaxY)
            {
                continue;
            }

            float xCm = _simulation.ToWorldXCm(localXCm);
            float yCm = _simulation.ToWorldYCm(localYCm);
            float x = xCm * MassNavWebParitySimState.VisualMetersPerCm;
            float z = yCm * MassNavWebParitySimState.VisualMetersPerCm;
            float radius = radiusCm * MassNavWebParitySimState.VisualMetersPerCm;
            if (draw.TryAdd(new PrimitiveDrawItem
            {
                MeshAssetId = _sphereMeshId,
                Position = new Vector3(x, 0.15f, z),
                Scale = new Vector3(radius * 2f, 0.3f, radius * 2f),
                Color = new Vector4(0.72f, 0.22f, 0.18f, 0.9f)
            }))
            {
                obstacleSubmittedCount++;
            }
        }

        _simulation.ObservePrimitiveCoverage(crowdInViewCount, crowdSubmittedCount, obstacleSubmittedCount, draw.DroppedSinceClear);
        _simulation.ObservePrimitiveEmit((Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency);
    }

    private void EmitStrategicWorldAtlas(PrimitiveDrawBuffer draw)
    {
        MassNavWorldConfig world = _simulation.WorldConfig;
        float worldWidth = world.WorldWidthCm * StrategicAtlasScale * MassNavWebParitySimState.VisualMetersPerCm;
        float worldHeight = world.WorldHeightCm * StrategicAtlasScale * MassNavWebParitySimState.VisualMetersPerCm;
        EmitBox(
            draw,
            0f,
            0f,
            worldWidth,
            worldHeight,
            StrategicAtlasYOffset,
            new Vector4(0.05f, 0.10f, 0.15f, 0.52f));

        float stripeThickness = MathF.Max(worldWidth, worldHeight) * 0.008f;
        EmitBox(draw, 0f, -worldHeight * 0.5f, worldWidth, stripeThickness, 0.08f, new Vector4(0.30f, 0.58f, 0.78f, 0.88f));
        EmitBox(draw, 0f, worldHeight * 0.5f, worldWidth, stripeThickness, 0.08f, new Vector4(0.30f, 0.58f, 0.78f, 0.88f));
        EmitBox(draw, -worldWidth * 0.5f, 0f, stripeThickness, worldHeight, 0.08f, new Vector4(0.30f, 0.58f, 0.78f, 0.88f));
        EmitBox(draw, worldWidth * 0.5f, 0f, stripeThickness, worldHeight, 0.08f, new Vector4(0.30f, 0.58f, 0.78f, 0.88f));

        float axisThickness = MathF.Max(worldWidth, worldHeight) * 0.003f;
        EmitBox(draw, 0f, 0f, worldWidth, axisThickness, 0.10f, new Vector4(0.18f, 0.31f, 0.40f, 0.70f));
        EmitBox(draw, 0f, 0f, axisThickness, worldHeight, 0.10f, new Vector4(0.18f, 0.31f, 0.40f, 0.70f));

        ReadOnlySpan<MassNavHotZoneConfig> hotZones = _simulation.HotZones;
        for (int i = 0; i < hotZones.Length; i++)
        {
            MassNavHotZoneConfig zone = hotZones[i];
            float centerX = ToStrategicVisual(zone.CenterXCm);
            float centerZ = ToStrategicVisual(zone.CenterYCm);
            float zoneWidth = MathF.Max(1.8f, zone.WidthCm * StrategicAtlasScale * MassNavWebParitySimState.VisualMetersPerCm);
            float zoneHeight = MathF.Max(1.8f, zone.HeightCm * StrategicAtlasScale * MassNavWebParitySimState.VisualMetersPerCm);
            EmitBox(
                draw,
                centerX,
                centerZ,
                zoneWidth,
                zoneHeight,
                0.20f,
                new Vector4(0.92f, 0.66f, 0.20f, 0.82f));
            EmitSphere(
                draw,
                centerX,
                centerZ,
                2.0f,
                0.48f,
                new Vector4(0.92f, 0.72f, 0.35f, 0.95f));
        }

        float activeCenterX = ToStrategicVisual(_simulation.SolverWindowCenterXCm);
        float activeCenterZ = ToStrategicVisual(_simulation.SolverWindowCenterYCm);
        float activeRadius = MathF.Max(4f, MathF.Sqrt(MathF.Max(1, _simulation.AgentState.TotalAgents)) * 0.05f);
        EmitSphere(draw, activeCenterX, activeCenterZ, activeRadius, 1.0f, new Vector4(0.42f, 0.86f, 1f, 0.88f));

        float workCenterX = ToStrategicVisual(_simulation.FlowWorkAreaCenterXCm);
        float workCenterZ = ToStrategicVisual(_simulation.FlowWorkAreaCenterYCm);
        float workWidth = MathF.Max(2.4f, _simulation.FlowWorkAreaWidthCm * StrategicAtlasScale * MassNavWebParitySimState.VisualMetersPerCm);
        float workHeight = MathF.Max(2.4f, _simulation.FlowWorkAreaHeightCm * StrategicAtlasScale * MassNavWebParitySimState.VisualMetersPerCm);
        EmitBox(draw, workCenterX, workCenterZ, workWidth, workHeight, 0.18f, new Vector4(0.42f, 0.92f, 0.30f, 0.32f));

        float streamingRadius = world.StreamingRadiusCm * StrategicAtlasScale * MassNavWebParitySimState.VisualMetersPerCm;
        float streamingSize = MathF.Max(2.4f, streamingRadius * 2f);
        EmitBox(draw, activeCenterX, activeCenterZ, streamingSize, streamingSize, 0.16f, new Vector4(0.24f, 0.70f, 1f, 0.32f));
    }

    private void EmitFlowWorkAreaGuides(PrimitiveDrawBuffer draw)
    {
        EmitGuideRect(
            draw,
            _simulation.FlowWorkAreaMinXCm,
            _simulation.FlowWorkAreaMaxXCm,
            _simulation.FlowWorkAreaMinYCm,
            _simulation.FlowWorkAreaMaxYCm,
            new Vector4(0.48f, 0.90f, 0.35f, 0.70f),
            drawGrid: false);
        EmitGuideRect(
            draw,
            _simulation.SolverWindowMinXCm,
            _simulation.SolverWindowMaxXCm,
            _simulation.SolverWindowMinYCm,
            _simulation.SolverWindowMaxYCm,
            new Vector4(0.24f, 0.88f, 1f, 0.90f),
            drawGrid: true);
    }

    private void EmitGuideRect(PrimitiveDrawBuffer draw, float minX, float maxX, float minY, float maxY, Vector4 boundary, bool drawGrid)
    {
        float centerX = (minX + maxX) * 0.5f * MassNavWebParitySimState.VisualMetersPerCm;
        float centerZ = (minY + maxY) * 0.5f * MassNavWebParitySimState.VisualMetersPerCm;
        float width = (maxX - minX) * MassNavWebParitySimState.VisualMetersPerCm;
        float depth = (maxY - minY) * MassNavWebParitySimState.VisualMetersPerCm;
        EmitBox(draw, centerX, minY * MassNavWebParitySimState.VisualMetersPerCm, width, TacticalBoundaryThickness, 0.03f, boundary);
        EmitBox(draw, centerX, maxY * MassNavWebParitySimState.VisualMetersPerCm, width, TacticalBoundaryThickness, 0.03f, boundary);
        EmitBox(draw, minX * MassNavWebParitySimState.VisualMetersPerCm, centerZ, TacticalBoundaryThickness, depth, 0.03f, boundary);
        EmitBox(draw, maxX * MassNavWebParitySimState.VisualMetersPerCm, centerZ, TacticalBoundaryThickness, depth, 0.03f, boundary);
        if (!drawGrid)
        {
            return;
        }

        int firstGridX = (int)MathF.Ceiling(minX / TacticalGridStepCm);
        int lastGridX = (int)MathF.Floor(maxX / TacticalGridStepCm);
        for (int gx = firstGridX; gx <= lastGridX; gx++)
        {
            float worldX = gx * TacticalGridStepCm;
            bool major = IsMajorGridLine(worldX);
            EmitBox(
                draw,
                worldX * MassNavWebParitySimState.VisualMetersPerCm,
                centerZ,
                major ? TacticalGridThickness * 1.8f : TacticalGridThickness,
                depth,
                major ? 0.025f : 0.02f,
                major
                    ? new Vector4(0.42f, 0.75f, 1f, 0.36f)
                    : new Vector4(0.24f, 0.46f, 0.60f, 0.20f));
        }

        int firstGridY = (int)MathF.Ceiling(minY / TacticalGridStepCm);
        int lastGridY = (int)MathF.Floor(maxY / TacticalGridStepCm);
        for (int gy = firstGridY; gy <= lastGridY; gy++)
        {
            float worldY = gy * TacticalGridStepCm;
            bool major = IsMajorGridLine(worldY);
            EmitBox(
                draw,
                centerX,
                worldY * MassNavWebParitySimState.VisualMetersPerCm,
                width,
                major ? TacticalGridThickness * 1.8f : TacticalGridThickness,
                major ? 0.025f : 0.02f,
                major
                    ? new Vector4(0.42f, 0.75f, 1f, 0.36f)
                    : new Vector4(0.24f, 0.46f, 0.60f, 0.20f));
        }
    }

    private void EmitWorldInspectGrid(PrimitiveDrawBuffer draw)
    {
        var cameraState = _engine.GameSession.Camera.State;
        float centerWorldX = cameraState.TargetCm.X;
        float centerWorldY = cameraState.TargetCm.Y;
        float minX = MathF.Max(_simulation.WorldWidthCm * -0.5f, centerWorldX - WorldInspectGridHalfExtentCm);
        float maxX = MathF.Min(_simulation.WorldWidthCm * 0.5f, centerWorldX + WorldInspectGridHalfExtentCm);
        float minY = MathF.Max(_simulation.WorldHeightCm * -0.5f, centerWorldY - WorldInspectGridHalfExtentCm);
        float maxY = MathF.Min(_simulation.WorldHeightCm * 0.5f, centerWorldY + WorldInspectGridHalfExtentCm);
        float centerX = (minX + maxX) * 0.5f * MassNavWebParitySimState.VisualMetersPerCm;
        float centerZ = (minY + maxY) * 0.5f * MassNavWebParitySimState.VisualMetersPerCm;
        float width = MathF.Max(1f, (maxX - minX) * MassNavWebParitySimState.VisualMetersPerCm);
        float depth = MathF.Max(1f, (maxY - minY) * MassNavWebParitySimState.VisualMetersPerCm);

        int firstGridX = (int)MathF.Ceiling(minX / TacticalGridStepCm);
        int lastGridX = (int)MathF.Floor(maxX / TacticalGridStepCm);
        for (int gx = firstGridX; gx <= lastGridX; gx++)
        {
            float worldX = gx * TacticalGridStepCm;
            bool major = IsMajorGridLine(worldX);
            EmitBox(
                draw,
                worldX * MassNavWebParitySimState.VisualMetersPerCm,
                centerZ,
                major ? TacticalGridThickness * 1.5f : TacticalGridThickness,
                depth,
                major ? 0.015f : 0.012f,
                major
                    ? new Vector4(0.26f, 0.58f, 0.78f, 0.24f)
                    : new Vector4(0.16f, 0.34f, 0.44f, 0.15f));
        }

        int firstGridY = (int)MathF.Ceiling(minY / TacticalGridStepCm);
        int lastGridY = (int)MathF.Floor(maxY / TacticalGridStepCm);
        for (int gy = firstGridY; gy <= lastGridY; gy++)
        {
            float worldY = gy * TacticalGridStepCm;
            bool major = IsMajorGridLine(worldY);
            EmitBox(
                draw,
                centerX,
                worldY * MassNavWebParitySimState.VisualMetersPerCm,
                width,
                major ? TacticalGridThickness * 1.5f : TacticalGridThickness,
                major ? 0.015f : 0.012f,
                major
                    ? new Vector4(0.26f, 0.58f, 0.78f, 0.24f)
                    : new Vector4(0.16f, 0.34f, 0.44f, 0.15f));
        }

        EmitBox(
            draw,
            centerWorldX * MassNavWebParitySimState.VisualMetersPerCm,
            centerWorldY * MassNavWebParitySimState.VisualMetersPerCm,
            2.4f,
            0.18f,
            0.07f,
            new Vector4(0.48f, 0.95f, 1f, 0.88f));
        EmitBox(
            draw,
            centerWorldX * MassNavWebParitySimState.VisualMetersPerCm,
            centerWorldY * MassNavWebParitySimState.VisualMetersPerCm,
            0.18f,
            2.4f,
            0.07f,
            new Vector4(0.48f, 0.95f, 1f, 0.88f));
    }

    private void EmitBox(PrimitiveDrawBuffer draw, float centerX, float centerZ, float width, float depth, float y, Vector4 color)
    {
        draw.TryAdd(new PrimitiveDrawItem
        {
            MeshAssetId = _cubeMeshId,
            Position = new Vector3(centerX, y, centerZ),
            Scale = new Vector3(width, 0.05f, depth),
            Color = color
        });
    }

    private void EmitSphere(PrimitiveDrawBuffer draw, float centerX, float centerZ, float radius, float y, Vector4 color)
    {
        draw.TryAdd(new PrimitiveDrawItem
        {
            MeshAssetId = _sphereMeshId,
            Position = new Vector3(centerX, y, centerZ),
            Scale = new Vector3(radius, radius, radius),
            Color = color
        });
    }

    private static float ToStrategicVisual(float worldCm)
    {
        return worldCm * StrategicAtlasScale * MassNavWebParitySimState.VisualMetersPerCm;
    }

    private static bool IsMajorGridLine(float worldCm)
    {
        return Math.Abs(worldCm % TacticalMajorGridStepCm) < 0.5f;
    }

    private void ResolveViewportBounds(out float minX, out float maxX, out float minY, out float maxY)
    {
        if (_engine.GetService(CoreServiceKeys.ViewController) is not IViewController view)
        {
            minX = _simulation.FlowWorkAreaMinXCm;
            minY = _simulation.FlowWorkAreaMinYCm;
            maxX = _simulation.FlowWorkAreaMaxXCm;
            maxY = _simulation.FlowWorkAreaMaxYCm;
            return;
        }

        var cameraState = _engine.GameSession.Camera.State;
        float fovY = cameraState.FovYDeg * (float)(Math.PI / 180.0f);
        float pitchRad = cameraState.Pitch * (float)(Math.PI / 180.0f);
        float logicHeight = 2.0f * cameraState.DistanceCm * (float)Math.Tan(fovY / 2.0f);
        float pitchScale = 1.0f / (float)Math.Max(Math.Sin(pitchRad), 0.1f);
        logicHeight *= pitchScale;
        float logicWidth = logicHeight * view.AspectRatio;
        logicWidth *= 1.5f;
        logicHeight *= 1.5f;

        minX = cameraState.TargetCm.X - logicWidth / 2f;
        maxX = cameraState.TargetCm.X + logicWidth / 2f;
        minY = cameraState.TargetCm.Y - logicHeight / 2f;
        maxY = cameraState.TargetCm.Y + logicHeight / 2f;
    }

    private static Vector4 ResolveTeamColor(int teamId)
    {
        return (Math.Abs(teamId) % 4) switch
        {
            0 => new Vector4(0.12f, 0.82f, 0.94f, 0.85f),
            1 => new Vector4(1.0f, 0.55f, 0.16f, 0.85f),
            2 => new Vector4(0.92f, 0.30f, 0.86f, 0.85f),
            _ => new Vector4(0.95f, 0.88f, 0.22f, 0.85f),
        };
    }
}

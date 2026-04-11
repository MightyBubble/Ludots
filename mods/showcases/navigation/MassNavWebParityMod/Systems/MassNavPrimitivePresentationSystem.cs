using System.Diagnostics;
using System.Numerics;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Scripting;
using MassNavWebParityMod.Runtime;

namespace MassNavWebParityMod.Systems;

internal sealed class MassNavPrimitivePresentationSystem : ISystem<float>
{
    private readonly GameEngine _engine;
    private readonly MassNavSimulationRuntime _simulation;
    private int _sphereMeshId;

    public MassNavPrimitivePresentationSystem(GameEngine engine, MassNavSimulationRuntime simulation, MeshAssetRegistry meshes)
    {
        _engine = engine;
        _simulation = simulation;
        _sphereMeshId = meshes.GetId(WellKnownMeshKeys.Sphere);
    }

    public void Initialize()
    {
        var registry = _engine.GetService(CoreServiceKeys.PresentationMeshAssetRegistry);
        _sphereMeshId = registry?.GetId(WellKnownMeshKeys.Sphere) ?? _sphereMeshId;
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

        if (_engine.GetService(CoreServiceKeys.PresentationPrimitiveDrawBuffer) is not PrimitiveDrawBuffer draw)
        {
            return;
        }

        long start = Stopwatch.GetTimestamp();
        ReadOnlySpan<float> positions = _simulation.WebParity.PositionsCm;
        ReadOnlySpan<int> teams = _simulation.WebParity.Teams;
        ReadOnlySpan<byte> selectedFlags = _simulation.WebParity.SelectedFlags;
        int unitCount = _simulation.WebParity.UnitCount;
        for (int i = 0; i < unitCount; i++)
        {
            int offset = i << 1;
            float x = positions[offset] * MassNavWebParitySimState.VisualMetersPerCm;
            float z = positions[offset + 1] * MassNavWebParitySimState.VisualMetersPerCm;
            bool selected = selectedFlags[i] != 0;
            Vector4 color;
            if (selected)
            {
                color = new Vector4(0.40f, 1.0f, 0.20f, 0.95f);
            }
            else
            {
                color = ResolveTeamColor(teams[i]);
            }
            draw.TryAdd(new PrimitiveDrawItem
            {
                MeshAssetId = _sphereMeshId,
                Position = new Vector3(x, 0.18f, z),
                Scale = selected ? new Vector3(0.28f, 0.28f, 0.28f) : new Vector3(0.22f, 0.22f, 0.22f),
                Color = color
            });
        }

        for (int i = 0; i < _simulation.WebParity.ObstacleCount; i++)
        {
            float x = _simulation.WebParity.GetObstacleX(i) * MassNavWebParitySimState.VisualMetersPerCm;
            float z = _simulation.WebParity.GetObstacleY(i) * MassNavWebParitySimState.VisualMetersPerCm;
            float radius = _simulation.WebParity.GetObstacleRadius(i) * MassNavWebParitySimState.VisualMetersPerCm;
            draw.TryAdd(new PrimitiveDrawItem
            {
                MeshAssetId = _sphereMeshId,
                Position = new Vector3(x, 0.15f, z),
                Scale = new Vector3(radius * 2f, 0.3f, radius * 2f),
                Color = new Vector4(0.72f, 0.22f, 0.18f, 0.9f)
            });
        }

        _simulation.ObservePrimitiveEmit((Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency);
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

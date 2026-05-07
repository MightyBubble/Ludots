using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Navigation.GraphWorld;
using Ludots.Core.Scripting;
using MassNavWebParityMod.Runtime;
using MinimapControlMod;
using MinimapControlMod.Runtime;

namespace MassNavWebParityMod.Systems;

internal sealed class MassNavMinimapDebugSyncSystem : ISystem<float>
{
    private readonly GameEngine _engine;
    private readonly MassNavSimulationRuntime _simulation;

    public MassNavMinimapDebugSyncSystem(GameEngine engine, MassNavSimulationRuntime simulation)
    {
        _engine = engine;
        _simulation = simulation;
    }

    public void Initialize() { }
    public void BeforeUpdate(in float dt) { }
    public void AfterUpdate(in float dt) { }
    public void Dispose() { }

    public void Update(in float dt)
    {
        if (!MassNavWebParityIds.IsCurrentPlaygroundMap(_engine))
        {
            return;
        }

        if (_engine.GetService(MinimapControlServiceKeys.Runtime) is not { } minimap)
        {
            throw new InvalidOperationException("MassNavWebParityMod requires MinimapControlMod runtime because it is declared as a dependency.");
        }

        minimap.ClearDebugOverlay();
        minimap.ConfigureDebugChunks(_simulation.StreamingChunkSizeCm, _simulation.LoadedChunkCount);
        foreach (long chunkKey in _simulation.LoadedChunks.ActiveChunkKeys)
        {
            (int x, int y) = GraphChunkKey.Unpack(chunkKey);
            minimap.AddDebugChunk(x, y);
        }

        ReadOnlySpan<MassNavHotZoneConfig> hotZones = _simulation.HotZones;
        for (int i = 0; i < hotZones.Length; i++)
        {
            MassNavHotZoneConfig zone = hotZones[i];
            minimap.AddDebugRect(
                zone.Label,
                MinimapDebugRectKind.HotZone,
                zone.CenterXCm,
                zone.CenterYCm,
                zone.WidthCm,
                zone.HeightCm);
        }

        minimap.AddDebugRect(
            "WORK",
            MinimapDebugRectKind.FlowWorkArea,
            _simulation.FlowWorkAreaCenterXCm,
            _simulation.FlowWorkAreaCenterYCm,
            _simulation.FlowWorkAreaWidthCm,
            _simulation.FlowWorkAreaHeightCm);
        minimap.AddDebugRect(
            "SOLVER",
            MinimapDebugRectKind.SolverCache,
            _simulation.SolverWindowCenterXCm,
            _simulation.SolverWindowCenterYCm,
            _simulation.SolverWindowWidthCm,
            _simulation.SolverWindowHeightCm);
    }
}

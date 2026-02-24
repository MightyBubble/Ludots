using System;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Navigation2D.FlowField;
using Ludots.Core.Navigation2D.Spatial;
using Ludots.Core.Spatial;

namespace Ludots.Core.Navigation2D.Runtime
{
    public sealed class Navigation2DRuntime : IDisposable
    {
        public readonly Navigation2DWorld AgentSoA;
        public readonly Nav2DCellMap CellMap;
        public readonly CrowdSurface2D Surface;
        public readonly CrowdFlow2D[] Flows;
        private readonly CrowdFlowChunkStreaming? _streaming;

        public bool FlowEnabled { get; set; } = false;
        public bool FlowDebugEnabled { get; set; } = false;
        public int FlowDebugMode { get; set; } = 0;

        public int FlowIterationsPerTick { get; set; } = 1024;

        public Navigation2DRuntime(int maxAgents, int gridCellSizeCm, ILoadedChunks? loadedChunks)
        {
            var cellSize = Fix64.FromInt(gridCellSizeCm);
            AgentSoA = new Navigation2DWorld(new Navigation2DWorldSettings(maxAgents, cellSize));
            CellMap = new Nav2DCellMap(cellSize, initialAgentCapacity: maxAgents, initialCellCapacity: Math.Max(128, maxAgents / 2));

            Surface = new CrowdSurface2D(cellSize, tileSizeCells: 64, initialTileCapacity: 256);
            Flows = new[]
            {
                new CrowdFlow2D(Surface, initialTileCapacity: 256),
                new CrowdFlow2D(Surface, initialTileCapacity: 256),
            };

            if (loadedChunks != null)
            {
                _streaming = new CrowdFlowChunkStreaming(loadedChunks, Flows);
            }
        }

        public int FlowCount => Flows.Length;

        public CrowdFlow2D? TryGetFlow(int flowId)
        {
            if ((uint)flowId >= (uint)Flows.Length) return null;
            return Flows[flowId];
        }

        public void Dispose()
        {
            _streaming?.Dispose();
            for (int i = 0; i < Flows.Length; i++)
            {
                Flows[i].Dispose();
            }
            CellMap.Dispose();
            AgentSoA.Dispose();
        }
    }
}

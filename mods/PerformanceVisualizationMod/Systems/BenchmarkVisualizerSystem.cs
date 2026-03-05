using System.Numerics;
using Arch.Core;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Gameplay.Cue;
using Ludots.Core.Engine;
using Arch.System;

namespace PerformanceVisualizationMod.Systems
{
    public class BenchmarkVisualizerSystem : BaseSystem<World, float>
    {
        private readonly CueEventBuffer _cueBuffer;
        private readonly QueryDescription _cueQuery = new QueryDescription().WithAll<GameplayEvent>();

        public BenchmarkVisualizerSystem(World world, CueEventBuffer cueBuffer) : base(world)
        {
            _cueBuffer = cueBuffer;
        }

        public override void Update(in float dt)
        {
        }
    }
}

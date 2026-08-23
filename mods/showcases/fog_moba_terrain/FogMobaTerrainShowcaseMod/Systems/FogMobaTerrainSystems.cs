using Arch.Core;
using Arch.System;
using FogMobaTerrainShowcaseMod.Runtime;
using Ludots.Core.Engine;

namespace FogMobaTerrainShowcaseMod.Systems;

internal sealed class FogMobaTerrainSimulationSystem : BaseSystem<World, float>
{
    private readonly GameEngine _engine;
    private readonly FogMobaTerrainRuntime _runtime;
    public FogMobaTerrainSimulationSystem(GameEngine engine, FogMobaTerrainRuntime runtime) : base(engine.World) { _engine = engine; _runtime = runtime; }
    public override void Update(in float dt) => _runtime.Update(_engine);
}

internal sealed class FogMobaTerrainPresentationSystem : BaseSystem<World, float>
{
    private readonly GameEngine _engine;
    private readonly FogMobaTerrainRuntime _runtime;
    public FogMobaTerrainPresentationSystem(GameEngine engine, FogMobaTerrainRuntime runtime) : base(engine.World) { _engine = engine; _runtime = runtime; }
    public override void Update(in float dt) => _runtime.Present(_engine);
}

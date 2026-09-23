using Arch.Core;
using Arch.System;
using Ludots.Core.Engine;

namespace RtsProductionCapabilityMod.Runtime;

internal sealed class RtsProductionSimulationSystem : BaseSystem<World, float>
{
	private readonly GameEngine _engine;
	private readonly RtsProductionRuntime _runtime;

	public RtsProductionSimulationSystem(GameEngine engine, RtsProductionRuntime runtime)
		: base(engine.World)
	{
		_engine = engine;
		_runtime = runtime;
	}

	public override void Update(in float t)
	{
		_runtime.Update(_engine, t);
	}
}

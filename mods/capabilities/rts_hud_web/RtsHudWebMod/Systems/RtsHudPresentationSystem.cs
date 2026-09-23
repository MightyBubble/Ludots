using Arch.Core;
using Arch.System;
using Ludots.Core.Engine;
using RtsHudWebMod.UI;

namespace RtsHudWebMod.Systems;

internal sealed class RtsHudPresentationSystem : BaseSystem<World, float>
{
	private readonly GameEngine _engine;
	private readonly RtsHudPanelController _controller;

	public RtsHudPresentationSystem(GameEngine engine, RtsHudPanelController controller)
		: base(engine.World)
	{
		_engine = engine;
		_controller = controller;
	}

	public override void Update(in float t)
	{
		_controller.MountOrRefresh(_engine);
	}
}

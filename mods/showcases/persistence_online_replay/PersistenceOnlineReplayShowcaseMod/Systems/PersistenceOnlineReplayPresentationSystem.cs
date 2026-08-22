using Arch.Core;
using Arch.System;
using Ludots.Core.Engine;
using PersistenceOnlineReplayShowcaseMod.Runtime;

namespace PersistenceOnlineReplayShowcaseMod.Systems;

internal sealed class PersistenceOnlineReplayPresentationSystem : BaseSystem<World, float>
{
    private readonly GameEngine _engine;
    private readonly PersistenceOnlineReplayRuntime _runtime;
    public PersistenceOnlineReplayPresentationSystem(GameEngine engine, PersistenceOnlineReplayRuntime runtime) : base(engine.World) { _engine = engine; _runtime = runtime; }
    public override void Update(in float dt) => _runtime.RefreshPanel(_engine);
}

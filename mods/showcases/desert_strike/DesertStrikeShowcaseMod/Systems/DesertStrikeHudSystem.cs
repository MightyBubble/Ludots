using Arch.Core;
using Arch.System;
using Ludots.Core.Engine;
using DesertStrikeShowcaseMod.Runtime;

namespace DesertStrikeShowcaseMod.Systems
{
    public sealed class DesertStrikeHudSystem : BaseSystem<World, float>
    {
        private readonly DesertStrikeHudPanelRuntime _runtime;

        public DesertStrikeHudSystem(GameEngine engine, DesertStrikeHudPanelRuntime runtime)
            : base(engine.World)
        {
            _runtime = runtime;
        }

        public override void Update(in float dt)
        {
            _runtime.Update();
        }
    }
}

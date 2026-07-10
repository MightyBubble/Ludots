using Arch.System;
using Ludots.Core.Engine;
using SuperweaponContextShowcaseMod.Runtime;

namespace SuperweaponContextShowcaseMod.Systems
{
    internal sealed class SuperweaponContextShowcaseSystem : BaseSystem<Arch.Core.World, float>
    {
        private readonly GameEngine _engine;
        private readonly SuperweaponContextShowcaseRuntime _runtime;

        public SuperweaponContextShowcaseSystem(GameEngine engine, SuperweaponContextShowcaseRuntime runtime)
            : base(engine.World)
        {
            _engine = engine;
            _runtime = runtime;
        }

        public override void Update(in float dt)
        {
            _runtime.Update(_engine);
        }
    }
}

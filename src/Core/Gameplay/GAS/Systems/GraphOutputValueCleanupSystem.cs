using System;
using Arch.Core;
using Arch.System;
using Ludots.Core.NodeLibraries.GASGraph;

namespace Ludots.Core.Gameplay.GAS.Systems
{
    public sealed class GraphOutputValueCleanupSystem : ISystem<float>
    {
        private readonly World _world;
        private readonly GraphOutputValueStore _values;

        public GraphOutputValueCleanupSystem(World world, GraphOutputValueStore values)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _values = values ?? throw new ArgumentNullException(nameof(values));
        }

        public int ReleasedLastUpdate { get; private set; }

        public void Initialize() { }
        public void BeforeUpdate(in float dt) { }

        public void Update(in float dt)
        {
            ReleasedLastUpdate = _values.ReleaseDeadOwners(_world);
        }

        public void AfterUpdate(in float dt) { }
        public void Dispose() { }
    }
}

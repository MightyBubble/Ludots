using Arch.Core;
using Arch.System;
using Ludots.Core.Gameplay.GAS.Bindings;

namespace Ludots.Core.Gameplay.GAS.Systems
{
    public sealed class AttributeBindingSystem : BaseSystem<World, float>
    {
        private readonly AttributeSinkRegistry _sinks;
        private readonly AttributeBindingRegistry _bindings;

        public AttributeBindingSystem(World world, AttributeSinkRegistry sinks, AttributeBindingRegistry bindings) : base(world)
        {
            _sinks = sinks;
            _bindings = bindings;
        }

        public override void Update(in float dt)
        {
            var groups = _bindings.Groups;
            var entries = _bindings.Entries;
            for (int i = 0; i < groups.Length; i++)
            {
                var g = groups[i];
                var sink = _sinks.GetSink(g.SinkId);
                sink.Apply(World, entries, g.Start, g.Count);
            }
        }
    }
}

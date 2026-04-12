using System;
using Arch.Core;
using Arch.System;
using Ludots.Core.Engine.Physics2D;
using Ludots.Core.Physics2D.Components;

namespace Ludots.Core.Navigation2D.Systems
{
    public sealed class NavPhysicsModeActivationSystem : BaseSystem<World, float>
    {
        private static readonly QueryDescription DynamicMassQuery = new QueryDescription().WithAll<Mass2D>();

        private readonly Physics2DController _controller;

        public NavPhysicsModeActivationSystem(World world, Physics2DController controller) : base(world)
        {
            _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        }

        public override void Update(in float dt)
        {
            bool hasDynamicMass = false;
            foreach (ref var chunk in World.Query(in DynamicMassQuery))
            {
                Span<Mass2D> masses = chunk.GetSpan<Mass2D>();
                foreach (int index in chunk)
                {
                    if (masses[index].IsDynamic)
                    {
                        hasDynamicMass = true;
                        break;
                    }
                }

                if (hasDynamicMass)
                {
                    break;
                }
            }

            if (hasDynamicMass)
            {
                if (!_controller.IsEnabled)
                {
                    _controller.Enable();
                }
            }
            else if (_controller.IsEnabled)
            {
                _controller.Disable();
            }
        }
    }
}

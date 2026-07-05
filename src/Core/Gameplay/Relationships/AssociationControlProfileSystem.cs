using System;
using Arch.Core;
using Arch.System;

namespace Ludots.Core.Gameplay.Relationships
{
    /// <summary>
    /// Drives <see cref="AssociationControlProfileRuntime"/> once per tick (RFC-0065 CTRL-4b).
    /// The runtime itself gates on relationship-revision / tag-bit changes, so unchanged ticks do no work.
    /// </summary>
    public sealed class AssociationControlProfileSystem : BaseSystem<World, float>
    {
        private readonly AssociationControlProfileRuntime _runtime;

        public AssociationControlProfileSystem(World world, AssociationControlProfileRuntime runtime)
            : base(world)
        {
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        }

        public override void Update(in float dt)
        {
            _runtime.Update();
        }
    }
}
